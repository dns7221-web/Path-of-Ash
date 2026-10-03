using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

/// <summary>
/// 추가 생성(2026-10-02) — 석상이 깨어나는 Timeline을 재생하고 연출이 빌린 상태를 돌려준다.
/// 시각은 에셋에서만 읽고, 정상 종료와 스킵은 같은 Finish를 거쳐 신호 유실로 전투가 막히지 않게 한다.
/// 2페이즈 Director와 그래프가 충돌하지 않도록 보스의 Intro 자식에 별도로 붙인다.
/// </summary>
// 수정(2026-10-03) — ISkippableCutscene을 붙였다. 2페이즈 전환과 같은 스킵 확인 창을 쓰기 위해서다.
// CanSkip과 Skip은 이미 있던 public 멤버라 동작은 바뀌지 않는다.
[DisallowMultipleComponent, RequireComponent(typeof(PlayableDirector))]
public sealed class BossIntroSequence : MonoBehaviour, INotificationReceiver, ISkippableCutscene
{
    public const string SeenPreferenceKey = "Ash.BossIntro.Seen.v1";

    [Header("보스와 화면")]
    [SerializeField] private SpriteRenderer bossRenderer;
    [SerializeField] private Sprite dormantSprite;
    [SerializeField] private Material ashMaterial;
    [SerializeField] private GameObject nameCard;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private CanvasGroup cardGroup;
    [SerializeField] private GameObject[] effects;
    [SerializeField] private Light2D bossLight;

    [Header("시그널")]
    [SerializeField] private SignalAsset doorSlamSignal;
    [SerializeField] private SignalAsset eyesIgniteSignal;
    [SerializeField] private SignalAsset swordPlantSignal;
    [SerializeField] private SignalAsset nameCardSignal;

    [Header("관람 설정")]
    [SerializeField] private bool shortenRepeat = true;

    // 추가 생성(2026-10-02) — 이 값들은 Intro 자식 Animator의 Timeline 곡선이 쓴다.
    // 머티리얼 인스턴스를 만들지 않고 MPB로 전달하고, 외부 광원은 입장 시 밝기를 기준으로 곱한다.
    [Range(0f, 1f)] public float ash = 1f;
    [Range(0f, 1f)] public float roomLightMultiplier = 1f;
    [Min(0f)] public float bossLightIntensity;
    [Range(0f, 1f)] public float healthFill;
    [Range(0f, 1f)] public float cardAlpha;

    private static readonly int AshId = Shader.PropertyToID("_Ash");
    private readonly List<(Light2D light, float intensity)> roomLights = new();
    private PlayableDirector director;
    private EnemyBoss boss;
    private PlayerController player;
    private Health playerHealth;
    private BossHealthBar healthBar;
    private Animator bossAnimator;
    private Material originalMaterial;
    private Sprite originalSprite;
    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock workingProperties;
    private CameraShake cameraShake;
    private bool running;
    private bool finished;
    private bool ownsPlayerLock;
    private bool originalPlayerInvulnerability;
    private BossIntroSkipDialog skipDialog;
    private bool firedDoor;
    private bool firedEyes;
    private bool firedSword;
    private bool firedName;
    private float originalBossLightIntensity;
    private bool originalBossLightEnabled;

    public event Action Finished;
    public static BossIntroSequence Active { get; private set; }
    public bool IsPlaying => running;
    public bool CanSkip => running;

    /// <summary>추가 생성(2026-10-02) — 도메인 리로드를 끈 에디터에서도 지난 보스 참조를 지운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    /// <summary>추가 생성(2026-10-02) — 길이를 코드에 복제하지 않고 저장된 Timeline에서 가져온다.</summary>
    public double TotalSeconds => director != null && director.playableAsset != null
        ? director.playableAsset.duration : 0d;

    /// <summary>추가 생성(2026-10-02) — 인스턴스마다 자기 Director의 종료만 구독한다.</summary>
    private void Awake()
    {
        director = GetComponent<PlayableDirector>();
        director.playOnAwake = false;
        director.stopped += OnStopped;
        if (Camera.main != null) cameraShake = Camera.main.GetComponent<CameraShake>();
    }

    /// <summary>
    /// 추가 생성(2026-10-02) — 방이 만든 보스와 플레이어를 받는다. HUD나 Timeline 누락도 전투를 가두지 않는다.
    /// </summary>
    public void Begin(EnemyBoss activeBoss, PlayerController activePlayer, BossHealthBar bar)
    {
        if (running || finished) return;
        if (director == null) Awake();
        boss = activeBoss;
        player = activePlayer;
        healthBar = bar;
        running = true;
        Active = this;
        boss?.BeginIntro();
        firedDoor = firedEyes = firedSword = firedName = false;

        if (player != null && !player.IsScripted)
        {
            Vector2 face = boss != null ? (Vector2)(boss.transform.position - player.transform.position) : Vector2.up;
            ownsPlayerLock = player.BeginScripted(face, null);
            playerHealth = player.GetComponent<Health>();
            if (ownsPlayerLock && playerHealth != null)
            {
                // Player.Update 전 같은 프레임에 남은 투사체가 닿아도 무적이어야 한다.
                originalPlayerInvulnerability = playerHealth.IsInvulnerableExternally;
                playerHealth.IsInvulnerableExternally = true;
            }
        }

        CapturePresentation();
        SoundPlayer.StopMusic(0f);
        if (nameLabel != null) nameLabel.text = healthBar != null ? healthBar.Phase1Name : string.Empty;
        if (director.playableAsset == null || TotalSeconds <= 0d)
        {
            Debug.LogWarning("[보스 등장] Timeline이 없어 전투를 바로 시작한다.", this);
            Finish(false);
            return;
        }

        director.timeUpdateMode = DirectorUpdateMode.GameTime;
        director.extrapolationMode = DirectorWrapMode.None;
        director.time = 0d;
        director.Play();
        if (shortenRepeat && PlayerPrefs.GetInt(SeenPreferenceKey, 0) != 0)
        {
            double repeatDuration = RepeatDuration();
            if (repeatDuration > 0d && director.playableGraph.IsValid())
                for (int i = 0; i < director.playableGraph.GetRootPlayableCount(); i++)
                    director.playableGraph.GetRootPlayable(i).SetSpeed(TotalSeconds / repeatDuration);
        }
        director.Evaluate();
        ApplyPresentation();
    }

    /// <summary>추가 생성(2026-10-02) — 재생 전 상태를 보관해서 중도 퇴장에도 빛과 머티리얼이 남지 않게 한다.</summary>
    private void CapturePresentation()
    {
        if (bossRenderer == null && boss != null) bossRenderer = boss.GetComponent<SpriteRenderer>();
        if (bossRenderer != null)
        {
            originalMaterial = bossRenderer.sharedMaterial;
            originalSprite = bossRenderer.sprite;
            bossAnimator = bossRenderer.GetComponent<Animator>();
            originalProperties = new MaterialPropertyBlock();
            workingProperties = new MaterialPropertyBlock();
            bossRenderer.GetPropertyBlock(originalProperties);
            if (ashMaterial != null) bossRenderer.sharedMaterial = ashMaterial;
            if (dormantSprite != null) bossRenderer.sprite = dormantSprite;
        }
        roomLights.Clear();
        foreach (Light2D light in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            if (light.lightType == Light2D.LightType.Global && light != bossLight)
                roomLights.Add((light, light.intensity));
        if (bossLight != null)
        {
            originalBossLightIntensity = bossLight.intensity;
            originalBossLightEnabled = bossLight.enabled;
            bossLight.enabled = true;
        }
    }

    /// <summary>추가 생성(2026-10-02) — 입력과 UI Update 뒤에 검사해 창을 닫는 키를 스킵으로 중복 소비하지 않는다.</summary>
    private void LateUpdate()
    {
        if (!running) return;
        ApplyPresentation();
        if (PauseGate.IsPaused || Time.timeScale <= 0f) return;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if ((escape || InputBindings.SettingsAction.WasPressedThisFrame())
            && !BossIntroSkipDialog.InputConsumedThisFrame) RequestSkipConfirmation();
    }

    /// <summary>추가 생성(2026-10-02) — ESC만으로 건너뛰지 않고 사용자가 스킵 버튼을 누를 기회를 준다.</summary>
    public void RequestSkipConfirmation()
    {
        if (!running || PauseGate.IsPaused || skipDialog != null) return;
        skipDialog = BossIntroSkipDialog.Show(this);
    }

    /// <summary>추가 생성(2026-10-02) — 반복 길이도 Timeline 마커에서 읽어 편집 시 숫자 불일치를 막는다.</summary>
    private double RepeatDuration()
    {
        if (director.playableAsset is not TimelineAsset timeline) return TotalSeconds;
        foreach (TrackAsset track in timeline.GetOutputTracks())
            foreach (IMarker marker in track.GetMarkers())
                if (marker is BossIntroRepeatMarker && marker.time > 0d) return marker.time;
        return TotalSeconds;
    }

    /// <summary>추가 생성(2026-10-02) — 곡선은 시간표가, 외부 객체 접근과 복구는 이 컴포넌트가 맡는다.</summary>
    private void ApplyPresentation()
    {
        if (bossRenderer != null && workingProperties != null)
        {
            bossRenderer.GetPropertyBlock(workingProperties);
            workingProperties.SetFloat(AshId, Mathf.Clamp01(ash));
            bossRenderer.SetPropertyBlock(workingProperties);
        }
        foreach (var entry in roomLights)
            if (entry.light != null) entry.light.intensity = entry.intensity * Mathf.Clamp01(roomLightMultiplier);
        if (bossLight != null) bossLight.intensity = bossLightIntensity;
        if (cardGroup != null) cardGroup.alpha = Mathf.Clamp01(cardAlpha);
        if (firedName && healthBar != null) healthBar.SetIntroFill(healthFill);
    }

    /// <summary>추가 생성(2026-10-02) — 문 자체는 RoomController가 닫는다. 이 신호는 닫힘의 소리와 충격만 맡는다.</summary>
    public void RaiseDoorSlam()
    {
        if (!running || firedDoor) return;
        firedDoor = true;
        SoundPlayer.Play(SfxId.BossGateOpen);
        cameraShake?.Shake(0.45f, 0.2f);
    }

    /// <summary>추가 생성(2026-10-02) — 눈이 켜지는 그림과 같은 신호에서 기존 낮은 보스 울림을 재사용한다.</summary>
    public void RaiseEyesIgnite()
    {
        if (!running || firedEyes) return;
        firedEyes = true;
        SoundPlayer.Play(SfxId.BossReturn);
    }

    /// <summary>추가 생성(2026-10-02) — 공격 판정을 만들지 않고 칼의 무게만 전달한다.</summary>
    public void RaiseSwordPlant()
    {
        if (!running || firedSword) return;
        firedSword = true;
        SoundPlayer.Play(SfxId.BossSlam);
        cameraShake?.Shake(0.7f, 0.25f);
    }

    /// <summary>추가 생성(2026-10-02) — 이름 등장부터 채움을 Timeline에 맡긴다. 끝에서는 신호와 무관하게 다시 보장한다.</summary>
    public void RaiseNameCard()
    {
        if (!running || firedName) return;
        firedName = true;
        if (healthBar != null && boss != null)
        {
            healthBar.Bind(boss.GetComponent<Health>(), boss.Phase2HealthRatio);
            healthBar.SetIntroFill(healthFill);
        }
        SoundPlayer.PlayMusic(MusicId.Boss);
    }

    /// <summary>추가 생성(2026-10-02) — Timeline 끝으로 이동한 뒤 Finish를 직접 호출하므로 건너뛴 신호에 의존하지 않는다.</summary>
    public void Skip()
    {
        if (!CanSkip || PauseGate.IsPaused) return;
        if (director != null && director.playableAsset != null) director.time = TotalSeconds;
        Finish(true);
    }

    /// <summary>추가 생성(2026-10-02) — Director.Stop도 stopped를 발생시키므로 먼저 running을 내려 재진입을 막는다.</summary>
    private void Finish(bool rememberSeen)
    {
        if (!running || finished) return;
        finished = true;
        running = false;
        if (Active == this) Active = null;
        RestorePresentation();
        if (healthBar != null && boss != null)
        {
            if (!healthBar.IsBoundTo(boss.GetComponent<Health>()))
                healthBar.Bind(boss.GetComponent<Health>(), boss.Phase2HealthRatio);
            healthBar.CompleteIntroFill();
        }
        if (rememberSeen)
        {
            PlayerPrefs.SetInt(SeenPreferenceKey, 1);
            PlayerPrefs.Save();
        }
        Finished?.Invoke();
    }

    /// <summary>추가 생성(2026-10-02) — 정상 종료는 관람 완료로 기록하고 전투 시작 이벤트를 한 번 보낸다.</summary>
    private void OnStopped(PlayableDirector stoppedDirector)
    {
        // 추가 생성(2026-10-02) — 부모가 꺼지면 Director의 stopped가 OnDisable보다 먼저 올 수도 있다.
        // 콜백 순서 대신 활성 상태로 구분해야 퇴장을 관람 완료·전투 시작으로 기록하지 않는다.
        if (!isActiveAndEnabled || !stoppedDirector.isActiveAndEnabled)
        {
            Abort();
            return;
        }
        if (running) Finish(true);
    }

    /// <summary>추가 생성(2026-10-02) — 방 비활성화는 전투 시작이 아니므로 완료 이벤트와 관람 기록 없이 복구한다.</summary>
    public void Abort()
    {
        if (!running) return;
        running = false;
        if (Active == this) Active = null;
        RestorePresentation();
        healthBar?.Unbind();
        SoundPlayer.StopMusic(0f);
    }

    /// <summary>추가 생성(2026-10-02) — 정상 종료·스킵·중단 모두 동일한 역순으로 빌린 상태를 돌려준다.</summary>
    private void RestorePresentation()
    {
        if (skipDialog != null) skipDialog.Close();
        skipDialog = null;
        if (director != null) director.Stop();
        if (effects != null) foreach (GameObject effect in effects) if (effect != null) effect.SetActive(false);
        if (nameCard != null) nameCard.SetActive(false);
        if (cardGroup != null) cardGroup.alpha = 0f;
        foreach (var entry in roomLights) if (entry.light != null) entry.light.intensity = entry.intensity;
        roomLights.Clear();
        if (bossLight != null)
        {
            bossLight.intensity = originalBossLightIntensity;
            bossLight.enabled = originalBossLightEnabled;
        }
        if (bossRenderer != null)
        {
            bossRenderer.sharedMaterial = originalMaterial;
            bossRenderer.SetPropertyBlock(originalProperties);
            // 추가 생성(2026-10-02) — 강제 처치의 Die 트리거를 Rebind로 지우지 않는다.
            bool bossAlive = boss != null && !boss.GetComponent<Health>().IsDead;
            if (bossAlive && bossAnimator != null && bossAnimator.isActiveAndEnabled && bossAnimator.runtimeAnimatorController != null)
            {
                bossAnimator.Rebind();
                bossAnimator.Update(0f);
            }
            else bossRenderer.sprite = originalSprite;
        }
        cameraShake?.StopShake();
        if (ownsPlayerLock && player != null)
        {
            player.EndScripted();
            if (playerHealth != null) playerHealth.IsInvulnerableExternally = originalPlayerInvulnerability;
        }
        ownsPlayerLock = false;
        boss?.EndIntro();
    }

    /// <summary>추가 생성(2026-10-02) — 표준 SignalReceiver와 알림 경로가 함께 와도 각 처리의 플래그로 중복을 막는다.</summary>
    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (notification is not SignalEmitter emitter || emitter.asset == null) return;
        if (emitter.asset == doorSlamSignal) RaiseDoorSlam();
        else if (emitter.asset == eyesIgniteSignal) RaiseEyesIgnite();
        else if (emitter.asset == swordPlantSignal) RaiseSwordPlant();
        else if (emitter.asset == nameCardSignal) RaiseNameCard();
    }

    /// <summary>추가 생성(2026-10-02) — 컴포넌트만 꺼지는 경로에서도 플레이어 잠금을 남기지 않는다.</summary>
    private void OnDisable() => Abort();

    /// <summary>추가 생성(2026-10-02) — 파괴 시 Director 구독을 끊어 남은 종료 콜백을 막는다.</summary>
    private void OnDestroy()
    {
        if (director != null) director.stopped -= OnStopped;
    }
}
