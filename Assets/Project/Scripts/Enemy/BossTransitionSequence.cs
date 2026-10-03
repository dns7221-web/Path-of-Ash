using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

/// <summary>
/// 추가 생성 — 재의 왕 2페이즈 전환 연출의 <b>바깥쪽 손잡이</b>.
///
/// <b>이 클래스는 시간표를 들고 있지 않다.</b> 3.125초 계획표(무엇을 언제 켜고 끄는지)는
/// 전부 <c>BossTransition.playable</c> 에셋 안에 있고, 여기서는 그걸 재생하고 몇 개의
/// 시그널을 받아 넘길 뿐이다.
///
/// <b>왜 코루틴으로 안 짰는가:</b> 2026-09-01 기록에 "3.125초 타임라인은 결국 그림을 보면서
/// 조정할 값"이라고 적어뒀다. 코루틴이면 0.875를 0.95로 바꿔보는 데 코드 수정 → 컴파일 →
/// Play가 매번 필요하다. 타임라인이면 클립 끝을 끌고 스크럽해서 바로 본다. 이 연출은
/// 보스전의 유일한 절정이라 조정 횟수가 확실히 많다.
///
/// 덤으로 얻는 것: <b>연출 길이가 한 곳에만 적힌다.</b> 예전에는 클립이 0.875초인데 코드가
/// 0.75초를 기다려서 마지막 프레임을 아무도 못 봤다. 이제 보스는 <see cref="TotalSeconds"/>를
/// 물어보므로 두 숫자가 갈라질 자리가 없다.
///
/// <b>왜 SignalReceiver 컴포넌트를 안 쓰는가:</b> 시그널을 받는 표준 방법은
/// <c>SignalReceiver</c> + UnityEvent 연결이지만, 그러면 "시그널이 오면 무엇을 하는가"가
/// 인스펙터의 UnityEvent 목록에 적힌다. 그건 <b>코드에서 안 보이는 연결</b>이고, 보스
/// 프리팹을 다시 만들 때마다 손으로 다시 이어야 한다. <c>INotificationReceiver</c>를 직접
/// 구현하면 같은 내장 기능을 쓰면서 분기가 코드에 남는다.
///
/// 수정(2026-10-03, 전환 연출 ③ "느리게 + 1페이즈 등장의 장치") — 2.5초를 4.0초로 늘리고,
/// 1페이즈 등장(<see cref="BossIntroSequence"/>)이 쓰는 장치를 여기에도 붙였다.
/// <list type="bullet">
/// <item>플레이어 조작 잠금과 무적 (연출 내내)</item>
/// <item>보스 곡을 끄고 이름 카드에서 다시 튼다</item>
/// <item>방을 어둡게 하고 보스 빛만 남긴다. 알 구간에 빛이 두 번 뛰고, 알이 깨질 때 방이 번쩍인다</item>
/// <item>무릎 꿇은 왕이 잿빛으로 굳은 뒤 무너진다 (1페이즈 등장의 잿빛 셰이더를 거꾸로 쓴다)</item>
/// <item>이름 카드와 체력바 차오름</item>
/// <item>ESC로 스킵 확인 창</item>
/// </list>
/// 곡선 값(<see cref="ash"/>, <see cref="roomLightMultiplier"/> 등)은 Timeline의 Animation Track이 쓰고,
/// 이 컴포넌트는 그 값을 실제 렌더러·조명·UI에 옮기고 끝날 때 되돌리는 일만 한다.
/// 1페이즈 등장과 같은 나눔이다(<c>BossIntroSequence.ApplyPresentation</c> 참고).
///
/// <b>왜 보스 루트가 아니라 자식(TransitionCinematic)에 붙는가:</b> Animation Track은 Animator에
/// 붙는다. 보스 본체의 Animator에 연결하면 타임라인이 도는 동안 보스의 Animator Controller 출력
/// (무릎 꿇는 모션)을 덮어쓴다. 1페이즈 등장이 Intro 자식에 컨트롤러 없는 Animator를 따로 둔
/// 것과 같은 이유다. 그래서 보스는 <c>GetComponentInChildren</c>으로 이 컴포넌트를 찾는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayableDirector))]
public class BossTransitionSequence : MonoBehaviour, INotificationReceiver, ISkippableCutscene
{
    [Header("시그널")]
    [Tooltip("갑옷이 다 무너진 순간(계획표 0.875). 보스 스프라이트를 숨긴다.")]
    [SerializeField] private SignalAsset armorBrokenSignal;

    [Tooltip("껍질이 깨지는 순간(계획표 2.375). 이름이 바뀌고 체력바가 다시 찬다.")]
    [SerializeField] private SignalAsset revealedSignal;

    [Tooltip("2페이즈 보스가 나타나는 순간(계획표 2.875). 컨트롤러를 갈아 끼우고 다시 보인다.")]
    [SerializeField] private SignalAsset bossReturnsSignal;

    // 추가 생성(2026-10-03) — 이름 카드가 뜨는 순간. 보스 곡을 다시 튼다.
    [Tooltip("이름 카드가 뜨는 순간(계획표 3.2). 보스 곡을 다시 튼다.")]
    [SerializeField] private SignalAsset nameCardSignal;

    [Header("화면 흔들림")]
    [Tooltip("연출이 시작될 때(갑옷 붕괴) 흔들 세기와 시간.")]
    [SerializeField, Range(0f, 1f)] private float startShakeStrength = 1f;
    [SerializeField, Min(0f)] private float startShakeSeconds = 0.9f;

    [Tooltip("껍질이 깨질 때 흔들 세기와 시간. 시작보다 짧고 날카롭게.")]
    [SerializeField, Range(0f, 1f)] private float revealShakeStrength = 0.75f;
    [SerializeField, Min(0f)] private float revealShakeSeconds = 0.35f;

    // 추가 생성(2026-10-03) — 1페이즈 등장과 같은 연출 장치. 빌더(AshBossTransitionTimelineBuilder)가 채운다.
    // 하나라도 비어 있으면 그 장치만 빠지고 연출과 전투는 그대로 돈다(예전 프리팹과의 호환).
    [Header("연출 장치")]
    [Tooltip("잿빛으로 굳힐 보스 렌더러. 비어 있으면 보스 루트에서 찾는다.")]
    [SerializeField] private SpriteRenderer bossRenderer;

    [Tooltip("1페이즈 등장의 잿빛 머티리얼(BossIntroAsh). _Ash 값으로 색을 잿빛과 섞는다.")]
    [SerializeField] private Material ashMaterial;

    [Tooltip("보스 쪽만 비추는 빛. 알 구간에 심장처럼 뛴다.")]
    [SerializeField] private Light2D bossLight;

    [Tooltip("2페이즈 이름 카드(화면 UI). 이름은 체력바의 2페이즈 이름을 그대로 쓴다.")]
    [SerializeField] private GameObject nameCard;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private CanvasGroup cardGroup;

    [Header("음악")]
    [Tooltip("연출이 시작될 때 보스 곡이 사라지는 시간(초).")]
    [SerializeField, Min(0f)] private float musicFadeOutSeconds = 0.6f;

    [Tooltip("이름 카드에서 보스 곡이 다시 들어오는 시간(초). 짧을수록 2페이즈 시작이 세게 들린다.")]
    [SerializeField, Min(0f)] private float musicFadeInSeconds = 0.5f;

    // 추가 생성(2026-10-03) — 이 값들은 TransitionCinematic 자식 Animator의 Timeline 곡선이 쓴다.
    // public인 이유: Animation Track은 직렬화된 필드만 곡선으로 움직일 수 있다(1페이즈 등장과 같은 방식).
    // 기본값은 "아무것도 안 바꾸는 값"이다. 곡선이 없는 예전 타임라인으로 돌아도 화면이 망가지지 않는다.
    [Tooltip("0 = 원래 색, 1 = 잿빛. 무릎 꿇은 왕이 굳어 가는 정도.")]
    [Range(0f, 1f)] public float ash;

    [Tooltip("방 전체 빛의 배율. 1보다 크면 번쩍인다(알이 깨지는 순간).")]
    [Range(0f, 3f)] public float roomLightMultiplier = 1f;

    [Tooltip("보스 빛의 세기.")]
    [Min(0f)] public float bossLightIntensity;

    [Tooltip("이름 카드 투명도.")]
    [Range(0f, 1f)] public float cardAlpha;

    [Tooltip("2페이즈 체력바가 보이는 만큼(0~1). 껍질이 깨진 뒤부터 적용된다.")]
    [Range(0f, 1f)] public float healthFill = 1f;

    private PlayableDirector director;
    private CameraShake cameraShake;

    // 추가 생성(시그널 유실 수정) — 이번 재생에서 각 신호가 이미 울렸는지.
    //
    // <b>왜 필요한가:</b> 신호가 두 경로로 들어올 수 있다. SignalReceiver의 UnityEvent와
    // 아래 <see cref="OnNotify"/>다. 둘 다 살려둔 이유는 <b>어느 쪽이 실제로 도착하는지가
    // 유니티 버전과 바인딩 상태에 달려 있기 때문</b>이다. 한쪽만 남기면 그쪽이 안 올 때
    // 연출이 통째로 죽는데, 실제로 그렇게 죽었다 — 전환이 끝나도 체력바가 안 차고
    // 이름이 "???"로 남았다.
    //
    // 둘 다 살리면 이번엔 <b>두 번 울릴</b> 위험이 생긴다. 이 세 값이 그것을 막는다.
    private bool firedArmorBroken;
    private bool firedRevealed;
    private bool firedBossReturns;

    // 추가 생성(2026-10-03) — 이름 카드 신호도 같은 규칙으로 한 번만 받는다.
    private bool firedNameCard;

    // 추가 생성(2026-10-03) — 재생 상태와 스킵.
    private bool running;

    // 추가 생성(2026-10-03) — 스킵이나 시그널 유실을 메우느라 못 울린 순간을 한꺼번에 처리하는 중인가.
    // 이때는 소리와 흔들림을 내지 않는다. 안 막으면 스킵 버튼 한 번에 갑옷·껍질·숨소리가 동시에 터진다.
    private bool applyingQuietly;
    private BossIntroSkipDialog skipDialog;

    // 추가 생성(2026-10-03) — 보스 방(BossEncounter)이 넘겨주는 플레이어와 체력바.
    // 보스가 HUD를 직접 찾지 않게 하려는 기존 규칙(BossEncounter.OnBossEnteredPhase2 주석)을 따른다.
    private PlayerController player;
    private BossHealthBar healthBar;
    private Health playerHealth;
    private bool ownsPlayerLock;
    private bool originalPlayerInvulnerability;
    private Transform bossBody;

    // 추가 생성(2026-10-03) — 연출이 잠시 빌리는 것들의 원래 값. 끝·스킵·중단 어디로 나가도 되돌린다.
    private static readonly int AshId = Shader.PropertyToID("_Ash");
    private readonly List<(Light2D light, float intensity)> roomLights = new();
    private float originalBossLightIntensity;
    private bool originalBossLightEnabled;
    private Material originalMaterial;
    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock workingProperties;
    private bool materialBorrowed;

    // 추가 생성(2026-10-03) — 2페이즈 모습이 나온 뒤에는 잿빛 머티리얼을 다시 빌리지 않는다.
    // ash 곡선은 끝까지 1로 남아 있으므로, 이 표시가 없으면 다음 프레임에 2페이즈 보스가 잿빛이 된다.
    private bool materialFinished;

    /// <summary>
    /// 갑옷이 다 무너진 순간. 보스가 받아서 스프라이트를 숨긴다.
    ///
    /// 숨기는 일을 이 클래스가 직접 안 하는 이유: 스프라이트는 <b>보스의 몸</b>이다.
    /// 연출이 남의 몸을 직접 끄면, 연출이 중간에 끊겼을 때 누가 다시 켜주는지가 흐려진다.
    /// </summary>
    public event Action ArmorBroken;

    /// <summary>껍질이 깨진 순간. 보스가 받아서 밖(체력바)에 전환을 알린다.</summary>
    public event Action Revealed;

    /// <summary>2페이즈 보스가 나타나는 순간. 보스가 받아서 컨트롤러를 갈아 끼운다.</summary>
    public event Action BossReturns;

    /// <summary>
    /// 추가 생성(2026-10-03) — 연출이 끝났다(정상 종료와 스킵 모두). 한 판에 한 번만 온다.
    /// 중단(<see cref="Abort"/>)은 전투 재개가 아니므로 이 신호를 보내지 않는다.
    /// </summary>
    public event Action Finished;

    /// <summary>
    /// 추가 생성(2026-10-03) — 지금 재생 중인 전환 연출. 설정 창이 ESC를 양보할지 판단할 때 쓴다.
    /// 1페이즈 등장의 <see cref="BossIntroSequence.Active"/>와 같은 역할이다.
    /// </summary>
    public static BossTransitionSequence Active { get; private set; }

    /// <summary>추가 생성(2026-10-03) — 연출이 진행 중인가. 보스는 이 값이 false가 될 때까지 기다린다.</summary>
    public bool IsPlaying => running;

    /// <summary>추가 생성(2026-10-03) — 스킵 확인 창이 묻는 값. 재생 중일 때만 건너뛸 수 있다.</summary>
    public bool CanSkip => running;

    /// <summary>
    /// 연출 전체 길이(초). 보스는 이 값만큼 기다린다.
    ///
    /// 타임라인 에셋이 곧 이 길이의 주인이다. 인스펙터에 같은 숫자를 또 적어두지 않는다.
    ///
    /// <c>director.duration</c>이 아니라 <c>playableAsset.duration</c>을 읽는 이유:
    /// 앞의 것은 <b>재생 그래프가 만들어진 뒤</b>에야 값이 찬다. 보스는 재생을 시작하기
    /// 전에 이 값을 물어보므로, 그때는 0이 돌아와서 <b>기다리지 않고 곧장 넘어간다.</b>
    /// 에셋 쪽은 클립 배치에서 바로 계산되는 값이라 언제 물어봐도 같다.
    /// </summary>
    public float TotalSeconds => director != null && director.playableAsset != null
        ? (float)director.playableAsset.duration
        : 0f;

    /// <summary>추가 생성(2026-10-03) — 도메인 리로드를 끈 에디터에서도 지난 판의 연출 참조를 지운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    private void Awake()
    {
        director = GetComponent<PlayableDirector>();

        // 추가 생성(2026-10-03) — 방에 놓이는 순간 재생되면 안 된다. 끝나면 stopped로 Finish를 부른다.
        director.playOnAwake = false;
        director.stopped += OnStopped;

        // 추가 생성(2026-10-03) — 플레이어가 바라볼 방향을 계산할 보스 몸. 이 컴포넌트는 보스의 자식에 붙는다.
        EnemyBoss boss = GetComponentInParent<EnemyBoss>();
        bossBody = boss != null ? boss.transform : transform;
        if (bossRenderer == null && boss != null) bossRenderer = boss.GetComponent<SpriteRenderer>();

        // 카메라는 프리팹 밖에 있어서 타임라인이 바인딩할 수 없다. 그래서 흔들림만
        // 시그널을 받아 코드가 건다. 못 찾아도 연출은 그대로 돌아간다 — 흔들림이 없을 뿐이다.
        if (Camera.main != null) cameraShake = Camera.main.GetComponent<CameraShake>();

        if (cameraShake == null)
            Debug.LogWarning("[보스 전환] Main Camera에 CameraShake가 없다. 화면이 안 흔들린다.", this);
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 보스 방이 전투를 시작할 때 플레이어와 체력바를 넘긴다.
    ///
    /// 안 불려도(방 없이 보스만 놓은 테스트 씬) 연출은 돈다. 조작 잠금과 체력바 채움만 빠진다.
    /// </summary>
    public void Prepare(PlayerController activePlayer, BossHealthBar bar)
    {
        player = activePlayer;
        healthBar = bar;
    }

    /// <summary>
    /// 연출을 시작한다. 시작 흔들림은 여기서 건다 — 계획표의 0.000 지점이라
    /// 시그널을 하나 더 만들 것 없이 재생과 같은 순간이다.
    /// </summary>
    public void Play()
    {
        // 추가 생성(2026-10-03) — 이미 재생 중이면 다시 시작하지 않는다. 처음부터 다시 돌면 빌린 상태를 두 번 저장한다.
        if (running) return;
        if (director == null) Awake();

        // 수정(시그널 유실 추적) — 이번 재생에서 아직 아무 신호도 안 왔다.
        firedArmorBroken = false;
        firedRevealed = false;
        firedBossReturns = false;

        // 추가 생성(2026-10-03) — 1페이즈 등장과 같은 준비: 잠금 → 빌릴 상태 저장 → 음악.
        firedNameCard = false;
        materialFinished = false;
        running = true;
        Active = this;
        LockPlayer();
        CapturePresentation();
        SoundPlayer.StopMusic(musicFadeOutSeconds);
        if (nameLabel != null && healthBar != null) nameLabel.text = healthBar.Phase2Name;

        cameraShake?.Shake(startShakeStrength, startShakeSeconds);

        // 추가 생성(2026-10-03) — 타임라인이 없으면 연출 없이 결과만 적용하고 끝낸다. 전투가 무적 상태로 갇히지 않게.
        if (director.playableAsset == null || TotalSeconds <= 0f)
        {
            Debug.LogWarning("[보스 전환] 타임라인이 없어 연출 없이 2페이즈로 넘어간다.", this);
            Finish();
            return;
        }

        // 추가 생성(2026-10-03) — 매번 0초부터, 게임 시간으로(인벤토리 등으로 멈추면 연출도 멈춘다).
        director.timeUpdateMode = DirectorUpdateMode.GameTime;
        director.extrapolationMode = DirectorWrapMode.None;
        director.time = 0d;
        director.Play();

        // 추가 생성(2026-10-03) — 첫 프레임 곡선 값을 바로 반영한다. 안 하면 한 프레임 동안 방이 원래 밝기로 보인다.
        director.Evaluate();
        ApplyPresentation();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 매 프레임 곡선 값을 화면에 옮기고 ESC를 확인한다.
    ///
    /// Update가 아니라 LateUpdate인 이유: 타임라인(GameTime)은 Update 뒤에 곡선을 계산한다.
    /// LateUpdate에서 읽어야 이번 프레임의 값이 이번 프레임에 화면에 나간다. 1페이즈 등장과 같다.
    /// </summary>
    private void LateUpdate()
    {
        if (!running) return;
        ApplyPresentation();

        if (PauseGate.IsPaused || Time.timeScale <= 0f) return;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if ((escape || InputBindings.SettingsAction.WasPressedThisFrame())
            && !BossIntroSkipDialog.InputConsumedThisFrame) RequestSkipConfirmation();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — ESC로 바로 건너뛰지 않고 확인 창을 띄운다.
    /// 1페이즈 등장과 같은 창을 쓴다. 창이 열려 있는 동안 PauseGate가 시간을 멈추므로 연출도 멈춘다.
    /// </summary>
    public void RequestSkipConfirmation()
    {
        if (!running || PauseGate.IsPaused || skipDialog != null) return;
        skipDialog = BossIntroSkipDialog.Show(this, "연출을 스킵하시겠습니까?");
    }

    /// <summary>계획표 <c>0.875</c>. <see cref="SignalReceiver"/>의 UnityEvent가 부른다.
    ///
    /// public인 이유는 하나뿐이다 — UnityEvent는 public 메서드만 연결할 수 있다.
    /// 배선은 빌더가 하므로 손으로 이을 일은 없다.
    /// </summary>
    public void RaiseArmorBroken()
    {
        // 수정(2026-10-03) — 연출이 끝난 뒤 늦게 온 신호는 무시한다.
        if (!running || firedArmorBroken) return;
        firedArmorBroken = true;

        // 추가 생성(2026-10-01, 소리 3차) — 소리도 시그널에 붙인다. 시간을 코드에 따로 적지 않으니 타임라인 시간표를 고쳐도
        // 그림·흔들림·소리가 함께 움직인다(0.75 대 0.875 사고를 구조로 막은 것과 같은 이유).
        // 수정(2026-10-03) — 스킵으로 몰아서 처리할 때는 소리를 내지 않는다.
        if (!applyingQuietly) SoundPlayer.Play(SfxId.BossArmorBreak);

        ArmorBroken?.Invoke();
    }

    /// <summary>계획표 <c>2.375</c>. 껍질이 깨진다.</summary>
    public void RaiseRevealed()
    {
        // 수정(2026-10-03) — 연출이 끝난 뒤 늦게 온 신호는 무시한다.
        if (!running || firedRevealed) return;
        firedRevealed = true;

        // 수정(2026-10-03) — 스킵으로 몰아서 처리할 때는 흔들림과 소리를 내지 않는다.
        if (!applyingQuietly)
        {
            cameraShake?.Shake(revealShakeStrength, revealShakeSeconds);
            // 추가 생성(2026-10-01, 소리 3차) — 화면이 흔들리는 순간과 껍질 깨지는 소리를 같은 줄에 묶는다.
            SoundPlayer.Play(SfxId.BossShellBreak);
        }
        Revealed?.Invoke();

        // 추가 생성(2026-10-03) — 체력바가 2페이즈로 바뀐 직후에 채움을 곡선 값으로 덮는다.
        // Revealed를 받은 보스 방이 MarkPhase2로 "차오르기"를 켜는데, 이 연출에서는 차오르는 시점을
        // 이름 카드에 맞춰야 하므로 곡선(healthFill)이 이긴다.
        ApplyHealthFill();
    }

    /// <summary>계획표 <c>2.875</c>. 2페이즈 보스가 나타난다.</summary>
    public void RaiseBossReturns()
    {
        // 수정(2026-10-03) — 연출이 끝난 뒤 늦게 온 신호는 무시한다.
        if (!running || firedBossReturns) return;
        firedBossReturns = true;

        // 추가 생성(2026-10-03) — 2페이즈 모습이 보이기 전에 잿빛 머티리얼을 돌려준다.
        // 보스가 이 신호를 받아 페이드인을 시작하므로, 이벤트보다 먼저 와야 잿빛 2페이즈가 한 프레임도 안 보인다.
        RestoreBossMaterial();
        materialFinished = true;

        // 추가 생성(2026-10-01, 소리 3차) — 진체가 나타나는 순간의 숨소리. 이유는 RaiseArmorBroken의 소리 설명과 같다.
        // 수정(2026-10-03) — 스킵으로 몰아서 처리할 때는 소리를 내지 않는다.
        if (!applyingQuietly) SoundPlayer.Play(SfxId.BossReturn);

        BossReturns?.Invoke();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 계획표 <c>3.2</c>. 이름 카드가 뜨고 보스 곡이 다시 들어온다.
    /// 카드의 투명도와 체력바 채움은 곡선이 맡는다. 여기서는 카드를 켜고 음악만 튼다.
    /// </summary>
    public void RaiseNameCard()
    {
        if (!running || firedNameCard) return;
        firedNameCard = true;

        if (nameCard != null) nameCard.SetActive(true);
        if (!applyingQuietly) SoundPlayer.PlayMusic(MusicId.Boss, musicFadeInSeconds);
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 확인 창의 "스킵"이 부른다. 타임라인 끝으로 옮긴 뒤 바로 끝낸다.
    /// 건너뛴 시그널은 <see cref="Finish"/>가 직접 채우므로 2페이즈가 안 된 채 싸움이 시작되는 일이 없다.
    /// </summary>
    public void Skip()
    {
        if (!CanSkip || PauseGate.IsPaused) return;
        if (director != null && director.playableAsset != null) director.time = TotalSeconds;
        Finish();
    }

    /// <summary>
    /// 연출을 즉시 멈추고 켜둔 것을 전부 되돌린다.
    ///
    /// <c>Stop</c>이 Activation Track이 켜둔 오브젝트까지 원래 상태로 돌려놓는다
    /// (<c>ActivationTrack.postPlaybackState</c>). 그래서 여기서 이펙트를 하나씩 끌 필요가 없다.
    ///
    /// 수정(2026-10-03) — 조명·잠금·머티리얼도 돌려줘야 해서 <see cref="Abort"/>로 넘긴다.
    /// 보스가 죽을 때(EnemyBoss.OnDied) 부르는 이름은 그대로 둔다.
    /// </summary>
    public void StopAndReset()
    {
        Abort();
        if (director != null) director.Stop();
        cameraShake?.StopShake();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 정상 종료와 스킵이 함께 지나는 출구. 한 번만 돈다.
    ///
    /// 순서가 중요하다:
    /// 1) 못 울린 시그널을 조용히 채운다 (스킵·유실 대비).
    /// 2) running을 내린다 — 아래 Director.Stop이 stopped를 다시 불러도 재진입하지 않게.
    /// 3) 빌린 상태를 돌려준다.
    /// 4) 체력바를 실제 값으로 확정하고 보스 곡을 보장한다.
    /// 5) 마지막에 Finished를 보낸다. 듣는 쪽(보스)이 깨어날 때는 모든 정리가 끝나 있어야 한다.
    /// </summary>
    private void Finish()
    {
        if (!running) return;

        applyingQuietly = true;
        RaiseArmorBroken();
        RaiseRevealed();
        RaiseBossReturns();
        applyingQuietly = false;

        running = false;
        if (Active == this) Active = null;
        RestorePresentation();

        // 건너뛴 채움 곡선의 끝값을 확정한다. 1페이즈 등장의 Finish와 같은 처리다.
        // ?. 대신 != null인 이유: 파괴된 유니티 오브젝트는 C# null 검사를 통과한다(유니티의 == 만 잡는다).
        if (healthBar != null) healthBar.CompleteIntroFill();

        // 이름 카드 신호를 건너뛰었어도 2페이즈는 보스 곡과 함께 시작한다. 이미 틀고 있으면 아무 일도 없다.
        SoundPlayer.PlayMusic(MusicId.Boss, musicFadeInSeconds);

        Finished?.Invoke();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 방이 사라지거나 보스가 꺼질 때. 전투 재개가 아니므로 Finished를 보내지 않고 복구만 한다.
    /// 음악은 건드리지 않는다. 방이 끝나면 방이 음악을 정한다.
    /// </summary>
    public void Abort()
    {
        if (!running) return;
        running = false;
        if (Active == this) Active = null;
        RestorePresentation();

        // 껍질이 깨진 뒤에 끊겼다면 체력바가 곡선 값에 묶인 채(introFilling) 남는다. 평소 동작으로 풀어 준다.
        if (firedRevealed && healthBar != null) healthBar.CompleteIntroFill();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 타임라인이 끝까지 가서 멈췄을 때 부른다.
    ///
    /// 부모가 꺼지면서 멈춘 것인지 끝까지 간 것인지는 활성 상태로 가른다. 1페이즈 등장의
    /// OnStopped와 같은 이유로 콜백 순서를 믿지 않는다.
    /// </summary>
    private void OnStopped(PlayableDirector stoppedDirector)
    {
        if (!isActiveAndEnabled || !stoppedDirector.isActiveAndEnabled)
        {
            Abort();
            return;
        }
        if (running) Finish();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 연출 동안 플레이어를 멈추고 무적으로 만든다.
    /// 이미 다른 연출이 플레이어를 잡고 있으면 손대지 않는다(풀어 줄 책임도 그쪽에 있다).
    /// </summary>
    private void LockPlayer()
    {
        ownsPlayerLock = false;
        if (player == null || player.IsScripted) return;

        Vector2 face = bossBody != null ? (Vector2)(bossBody.position - player.transform.position) : Vector2.zero;
        ownsPlayerLock = player.BeginScripted(face, null);
        if (!ownsPlayerLock) return;

        // 같은 프레임에 날아오던 투사체에 맞지 않도록 잠그는 즉시 무적으로 만든다.
        playerHealth = player.GetComponent<Health>();
        if (playerHealth != null)
        {
            originalPlayerInvulnerability = playerHealth.IsInvulnerableExternally;
            playerHealth.IsInvulnerableExternally = true;
        }
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 연출이 바꿀 조명의 원래 값을 저장한다. 머티리얼은 실제로 잿빛이 시작될 때 빌린다.
    /// 방의 Global Light2D를 전부 모으는 이유: 방마다 빛 구성이 달라도 "지금 밝기 × 배율"로 어둡게 할 수 있다.
    /// </summary>
    private void CapturePresentation()
    {
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

        if (cardGroup != null) cardGroup.alpha = 0f;
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 곡선 값을 실제 화면에 옮긴다. 시간은 타임라인이, 외부 객체 접근은 이 함수가 맡는다.
    /// </summary>
    private void ApplyPresentation()
    {
        ApplyAsh();

        float room = Mathf.Clamp(roomLightMultiplier, 0f, 3f);
        foreach (var entry in roomLights)
            if (entry.light != null) entry.light.intensity = entry.intensity * room;

        if (bossLight != null) bossLight.intensity = bossLightIntensity;
        if (cardGroup != null) cardGroup.alpha = Mathf.Clamp01(cardAlpha);
        ApplyHealthFill();
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 잿빛 값을 보스 렌더러에 MaterialPropertyBlock으로 넘긴다.
    ///
    /// 머티리얼은 ash가 0보다 커지는 순간에 처음 빌린다. 0인 동안 원래 머티리얼을 그대로 두면
    /// 두 셰이더의 미세한 차이가 연출 시작 순간에 튀어 보일 일이 없다.
    /// <c>renderer.material</c> 대신 MPB를 쓰는 이유: material은 몰래 복제본을 만들어 메모리와 배칭을 깬다.
    /// </summary>
    private void ApplyAsh()
    {
        if (bossRenderer == null || ashMaterial == null || materialFinished) return;
        if (!materialBorrowed)
        {
            if (ash <= 0.001f) return;
            originalMaterial = bossRenderer.sharedMaterial;
            originalProperties ??= new MaterialPropertyBlock();
            workingProperties ??= new MaterialPropertyBlock();
            bossRenderer.GetPropertyBlock(originalProperties);
            bossRenderer.sharedMaterial = ashMaterial;
            materialBorrowed = true;
        }

        bossRenderer.GetPropertyBlock(workingProperties);
        workingProperties.SetFloat(AshId, Mathf.Clamp01(ash));
        bossRenderer.SetPropertyBlock(workingProperties);
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 껍질이 깨진 뒤에만 체력바 채움을 곡선 값으로 정한다.
    /// 그 전에는 1페이즈 체력바(전환 비율에서 이미 비어 있다)를 그대로 둔다.
    /// </summary>
    private void ApplyHealthFill()
    {
        if (firedRevealed && healthBar != null) healthBar.SetIntroFill(healthFill);
    }

    /// <summary>추가 생성(2026-10-03) — 빌린 잿빛 머티리얼을 원래대로 돌려준다. 여러 번 불려도 안전하다.</summary>
    private void RestoreBossMaterial()
    {
        if (!materialBorrowed) return;
        materialBorrowed = false;
        if (bossRenderer == null) return;
        bossRenderer.sharedMaterial = originalMaterial;
        bossRenderer.SetPropertyBlock(originalProperties);
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 정상 종료·스킵·중단 모두 같은 순서로 빌린 상태를 돌려준다.
    /// 하나라도 빠지면 방이 계속 어둡거나 플레이어가 못 움직이는 버그가 남는다.
    /// </summary>
    private void RestorePresentation()
    {
        if (skipDialog != null) skipDialog.Close();
        skipDialog = null;

        // Stop은 stopped를 다시 부르지만 running이 이미 false라 아무 일도 안 일어난다.
        if (director != null) director.Stop();

        if (nameCard != null) nameCard.SetActive(false);
        if (cardGroup != null) cardGroup.alpha = 0f;

        foreach (var entry in roomLights)
            if (entry.light != null) entry.light.intensity = entry.intensity;
        roomLights.Clear();

        if (bossLight != null)
        {
            bossLight.intensity = originalBossLightIntensity;
            bossLight.enabled = originalBossLightEnabled;
        }

        RestoreBossMaterial();

        if (ownsPlayerLock && player != null)
        {
            player.EndScripted();
            if (playerHealth != null) playerHealth.IsInvulnerableExternally = originalPlayerInvulnerability;
        }
        ownsPlayerLock = false;
    }

    /// <summary>
    /// 타임라인의 시그널이 여기로 들어온다.
    ///
    /// 시그널 트랙에 바인딩을 안 걸어두면 알림이 <c>PlayableDirector</c>가 붙은
    /// 오브젝트로 오고, 그 오브젝트의 <c>INotificationReceiver</c>들이 전부 받는다.
    ///
    /// 이름이 아니라 <b>에셋 참조로 비교</b>하는 이유: 이름 비교는 시그널 파일 이름을
    /// 바꾸는 순간 조용히 안 맞게 된다. 에셋 참조는 파일을 옮기거나 이름을 바꿔도 살아 있고,
    /// 참조가 비면 인스펙터에서 눈으로 보인다.
    /// </summary>
    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (notification is not SignalEmitter emitter) return;

        SignalAsset asset = emitter.asset;

        if (asset == armorBrokenSignal)
        {
            RaiseArmorBroken();
        }
        else if (asset == revealedSignal)
        {
            RaiseRevealed();
        }
        else if (asset == bossReturnsSignal)
        {
            RaiseBossReturns();
        }
        else if (asset == nameCardSignal)
        {
            // 추가 생성(2026-10-03) — 이름 카드 신호.
            RaiseNameCard();
        }
        else
        {
            // 조용히 넘기지 않는 이유: 시그널을 새로 추가하고 여기 분기를 안 달면
            // <b>아무 일도 안 일어나는데 에러도 없다.</b> 그건 찾는 데 제일 오래 걸리는 종류다.
            Debug.LogWarning($"[보스 전환] 받는 데가 없는 시그널이다: {asset?.name}", this);
        }
    }

    /// <summary>추가 생성(2026-10-03) — 컴포넌트만 꺼지는 경로에서도 플레이어 잠금과 어두운 방을 남기지 않는다.</summary>
    private void OnDisable() => Abort();

    /// <summary>추가 생성(2026-10-03) — 파괴될 때 Director 구독을 끊어 남은 종료 콜백을 막는다.</summary>
    private void OnDestroy()
    {
        if (director != null) director.stopped -= OnStopped;
    }
}
