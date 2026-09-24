using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-21) — 보스 궁극기 "왕관 의식"의 연출. 보스 방의 EnemyEncounter에 붙는다.
///
/// 순서:
/// 1. 플레이어를 붙잡고(<see cref="PlayerController.BeginScripted"/>) "유물 뽑힘" 모션을 튼다.
/// 2. 모션의 5번째 장(몸을 젖히는 장면)에서 애니메이션 이벤트가 울리면, <b>바로 그 프레임에</b>
///    가슴에서 유물 네 개가 튀어나와 방 네 귀퉁이로 날아간다(사용자 요청 — 반드시 그 장면).
/// 3. 주저앉은 장을 다 보여주면 조작이 돌아온다.
/// 4. 시전 바에 "왕관 의식"이 차오르는 동안 유물을 부술 수 있다(부수기·결과는 다음 단계).
/// 5. 시간이 끝나면 유물을 거두고 <see cref="Finished"/>를 울린다. 방이 받아 보스를 풀어 준다.
///
/// 수정(2026-09-22) — 4·5에 부수기와 결과를 붙였다.
/// 4. 유물은 공격 3대로 부서진다(<see cref="FlyingRelic"/>). <b>넷을 다 부수면 시간이 남아도 곧바로 끝난다</b> —
///    기획의 "의식 중 때려서 저지"다.
/// 5. 부순 개수로 결과를 낸다(기획 메모 09-19): 하나도 못 부수면 빈사(체력 1, 이미 1이면 사망),
///    1개면 체력 2칸만, 2개면 3칸만 남긴다. 넷을 다 부수면 피해 없이 보스가 그로기에 빠진다.
///    숫자는 인스펙터 결과표에서 조정한다. 궁극기 영상(다음 단계)은 결과 바로 앞에 들어온다.
///
/// 수정(2026-09-23, 사용자 기획) — 시간과 결과를 바꿨다.
/// 4. 시전 바는 유물마다 새로 찬다: 첫 바 10초, 하나 부술 때마다 처음으로 돌아가 1.5배·2배로 빨라진다.
///    <b>3개를 부수면 막는다</b> — 곧바로 끝나고 남은 하나는 사라진다.
/// 5. 막으면 피해 없이 보스가 그로기. 못 막으면(2개 이하) 체력을 2칸까지만 남긴다(궁극기 영상은 이 앞에).
///    위 09-22의 개수별 결과표와 "이미 1이면 사망"은 걷어냈다.
///
/// <b>왜 보스가 아니라 방에 붙나.</b> 의식에 필요한 것 — 네 귀퉁이, 유물 목록, 시전 바, 플레이어 —
/// 이 전부 방 쪽 사정이다. 보스는 "의식을 시작했다"는 신호만 내고(<see cref="EnemyBoss.CrownRitualStarted"/>),
/// 이 컴포넌트는 보스를 모른다. 전환 연출(<see cref="BossTransitionSequence"/>)이 보스 몸에서 일어나는
/// 일이라 보스 쪽에 붙은 것과 반대 경우다.
///
/// <b>왜 타이머가 아니라 애니메이션 이벤트로 기다리나.</b> <see cref="PlayerAnimationEvents"/> 주석 참고.
/// 모션의 프레임 길이를 고쳐도 유물은 늘 그 장면에 나온다. 이벤트가 끝내 안 오면(애니메이션을 아직
/// 안 만들었을 때) 정해 둔 시간 뒤에 그냥 진행한다 — 연출이 멈춘 채 판이 막히는 것보다 낫다.
///
/// <b>스케일 시간으로 센다.</b> 플레이어 모션(Animator)이 스케일 시간으로 돌기 때문이다. 인벤토리를
/// 열어 시간이 멈추면 의식도 같이 멈춰야 유물과 모션의 박자가 안 어긋난다.
/// </summary>
[DisallowMultipleComponent]
public class CrownRitual : MonoBehaviour
{
    // 플레이어 애니메이터의 트리거 이름. AshPlayerDirectionalAnimationBuilder의 동작 표와 같아야 한다.
    private const string RelicTornTrigger = "RelicTorn";

    [Header("유물")]
    [Tooltip("뽑혀 나갈 보스 진입 유물 네 개. 같은 순서의 자리로 날아간다.")]
    [SerializeField] private RelicData[] relics = new RelicData[4];
    [Tooltip("유물이 내려앉을 바닥 자리. 위 유물과 같은 순서(왼쪽 위, 오른쪽 위, 왼쪽 아래, 오른쪽 아래).")]
    [SerializeField] private Transform[] landingPoints = new Transform[4];
    [Tooltip("날아가는 유물 프리팹.")]
    [SerializeField] private FlyingRelic relicPrefab;

    [Header("뽑히는 순간")]
    [Tooltip("유물이 튀어나오는 높이(플레이어 발밑 기준, 유닛). \"뽑힘\" 장의 가슴 섬광이 발밑에서 약 66px(2.06유닛) 위다.")]
    [SerializeField, Min(0f)] private float chestHeight = 2f;
    // 수정(2026-09-22) — 한 번이 아니라 유물마다 날아가는 쪽으로 돌려서 터뜨린다(TearOutRelics 주석 참고).
    [Tooltip("튀어나오는 순간 가슴에서 터뜨릴 불꽃. 오른쪽(+X)으로 튀는 프리팹을 유물마다 날아가는 쪽으로 " +
             "돌려서 하나씩 터뜨린다. 비우면 안 터뜨린다.")]
    [SerializeField] private GameObject burstPrefab;
    [Tooltip("튀어나오는 순간의 화면 흔들림 세기(0~1).")]
    [SerializeField, Range(0f, 1f)] private float burstShake = 0.6f;
    [SerializeField, Min(0f)] private float burstShakeSeconds = 0.4f;
    [Tooltip("튀어나오는 순간 시간을 멈추는 길이(실시간 초). 젖힌 자세가 한 박자 박힌다.")]
    [SerializeField, Min(0f)] private float burstHitStop = 0.08f;

    [Header("의식")]
    [Tooltip("시전 바에 뜰 이름.")]
    [SerializeField] private string ritualName = "왕관 의식";
    // 수정(2026-09-23) — ritualSeconds(의식 전체 10초)를 유물마다 주는 시간으로 바꿨다(사용자 기획: 하나 부술 때마다
    // 바가 처음으로 돌아가고 점점 빨리 찬다). 첫 바는 25초로 시작했다가, 3개로 막게 바꾸면서 영상이 나오도록 10초로 줄였다.
    // 이름을 두 번 바꾼 까닭은 같다 — 씬에 저장됐거나 열린 씬이 들고 있는 옛 값(10, 25)이 새 기본값을 덮지 않게.
    [Tooltip("첫 유물을 부술 때까지 주는 시간(초). 하나 부술 때마다 시전 바가 처음으로 돌아가 다시 찬다.")]
    [SerializeField, Min(1f)] private float firstBarSeconds = 10f;
    [Tooltip("부순 개수(0·1·2…개)마다 시전 바가 차는 빠르기(배). 1·1.5·2면 10초 → 6.7초 → 5초. 막는 개수(Broken To Block)에 " +
             "닿으면 끝나므로 그 뒤 칸은 막는 개수를 올릴 때만 쓴다. 부순 개수가 칸 수보다 많으면 마지막 칸을 쓴다.")]
    [SerializeField, Min(0.1f)] private float[] barSpeedByBroken = { 1f, 1.5f, 2f, 3f };
    [Tooltip("의식이 끝날 때 남은 유물이 사라지는 시간(초).")]
    [SerializeField, Min(0f)] private float relicVanishSeconds = 0.4f;
    [Tooltip("유물이 부서질 때의 화면 흔들림 세기(0~1)와 시간(초). 공격 자체의 흔들림 위에 한 번 더 얹는다.")]
    [SerializeField, Range(0f, 1f)] private float breakShake = 0.35f;
    [SerializeField, Min(0f)] private float breakShakeSeconds = 0.25f;

    // 추가 생성(2026-09-22) — 결과표. 기획 메모(09-19)의 숫자를 기본값으로 넣었다. 플레이하며 조정한다.
    // 수정(2026-09-23) — 부순 개수별 결과표(healthLeftByBroken)와 "이미 1이면 사망"(killIfNoneBrokenAtOne)을 걷어내고
    // "막았다/못 막았다" 둘로 줄였다(사용자 기획: 2개까지만 부수면 영상 + 체력 2칸까지만 남김, 3개 부수면 그로기).
    [Header("결과")]
    [Tooltip("이만큼 부수면 의식을 막는다 — 곧바로 끝나고, 남은 유물은 사라지고, 보스는 그로기. 뿌린 유물 수보다 크면 전부 부숴야 한다.")]
    [SerializeField, Min(1)] private int brokenToBlock = 3;
    [Tooltip("못 막았을 때 플레이어에게 남길 체력. 지금 체력이 더 많으면 이 값까지 깎고, 같거나 적으면 그대로 둔다. 0이면 깎지 않는다.")]
    [SerializeField, Min(0)] private int failHealthLeft = 2;
    [Tooltip("결과가 들어가는 순간의 화면 흔들림 세기(0~1)와 시간(초).")]
    [SerializeField, Range(0f, 1f)] private float resultShake = 0.9f;
    [SerializeField, Min(0f)] private float resultShakeSeconds = 0.5f;
    [Tooltip("결과가 들어가는 순간 시간을 멈추는 길이(실시간 초).")]
    [SerializeField, Min(0f)] private float resultHitStop = 0.12f;

    [Header("안전장치")]
    [Tooltip("\"뽑힘\" 이벤트를 기다리는 최대 시간(초). 모션 기준 1.1초에 온다.")]
    [SerializeField, Min(0.1f)] private float tornOutTimeout = 2f;
    [Tooltip("뽑힌 뒤 모션 끝 이벤트를 기다리는 최대 시간(초). 모션 기준 1초 뒤에 온다.")]
    [SerializeField, Min(0.1f)] private float poseEndTimeout = 2.5f;

    /// <summary>
    /// 의식이 끝났다(시간이 다 됐거나 끊겼다). 방이 받아 보스를 싸움으로 돌려보낸다.
    /// 수정(2026-09-22) — 값을 하나 넘긴다: 유물을 다 부숴 의식을 막았는가(true면 보스가 그로기에 빠진다).
    /// 보스를 그로기로 보낼지는 의식의 결과지만, 보스를 모르는 이 컴포넌트 대신 방이 보스에게 전한다.
    /// </summary>
    public event Action<bool> Finished;

    /// <summary>
    /// 추가 생성(2026-09-22, 보스 파티클 1부) — 유물이 뽑혀 나간 순간. 방이 받아 보스에게 전한다(손 뻗기 → 의식 자세).
    /// </summary>
    public event Action RelicsTorn;

    /// <summary>추가 생성(2026-09-22) — 유물 하나가 깨진 순간. 방이 받아 보스를 움찔하게 한다.</summary>
    public event Action RelicBroken;

    /// <summary>의식이 도는 중인가.</summary>
    public bool IsRunning => routine != null;

    private Coroutine routine;
    private PlayerController player;
    private BossCastBar castBar;
    private CameraShake cameraShake;

    // 이번 의식에서 만든 유물. 다음 단계(부수기)에서 남은 수를 센다.
    private readonly List<FlyingRelic> spawned = new List<FlyingRelic>();

    // 추가 생성(2026-09-22) — 이번 의식에서 부서진 유물 수. 유물이 부서질 때 알려 온다(FlyingRelic.Broken).
    // 남은 오브젝트를 세지 않는 이유: 부서진 유물은 흩어지는 0.2초 동안 아직 살아 있어서, 세는 순간에 따라 수가 달라진다.
    private int brokenCount;

    // 추가 생성(2026-09-22) — 유물 실이 닿고 남은 유물이 빨려 들어갈 곳(보스 가슴). 방이 넘긴다.
    private Vector3? ritualCenter;

    [Tooltip("추가 생성(2026-09-22, 의식-7) — 못 막았을 때 남은 유물이 보스에게 빨려 들어가는 시간(초). 이 뒤에 결과가 들어간다.")]
    [SerializeField, Min(0f)] private float absorbSeconds = 0.5f;

    /// <summary>
    /// 추가 생성(2026-09-22) — 의식을 막았는가. 하나도 못 뿌렸으면 거짓이다.
    /// 수정(2026-09-23) — AllBroken(전부 부숨)에서 바꿨다. 이제 <see cref="brokenToBlock"/>개면 막는다(유물이 더 적으면 전부).
    /// </summary>
    private bool Blocked => spawned.Count > 0 && brokenCount >= Mathf.Min(brokenToBlock, spawned.Count);

    // 플레이어 모션이 보낸 신호. 코루틴이 이 값을 보고 다음 단계로 넘어간다.
    private bool tornOut;
    private bool poseEnded;

    /// <summary>
    /// 의식을 시작한다. 이미 돌고 있으면 무시한다.
    /// </summary>
    /// <param name="target">유물을 뽑힐 플레이어. 없으면 연출 없이 곧바로 끝낸다.</param>
    /// <param name="bar">의식 시간을 보여 줄 시전 바. 없으면 바 없이 시간만 흐른다.</param>
    /// <param name="threadTarget">추가 생성(2026-09-22) — 유물 실이 닿을 곳(보스 가슴). 못 막았을 때 유물이 빨려 들어가는 곳이기도 하다.
    /// 없으면 실 없이, 남은 유물은 예전처럼 제자리에서 사라진다.</param>
    public void Begin(PlayerController target, BossCastBar bar, Vector3? threadTarget = null)
    {
        if (routine != null) return;

        player = target;
        castBar = bar;
        ritualCenter = threadTarget;
        if (cameraShake == null && Camera.main != null) cameraShake = Camera.main.GetComponent<CameraShake>();

        routine = StartCoroutine(Run());
    }

    /// <summary>
    /// 의식을 중간에 끊는다(방이 정리될 때). 유물을 즉시 지우고 플레이어를 놓아준다.
    /// <see cref="Finished"/>는 울리지 않는다 — 끊는 쪽이 보스까지 치우는 중이다.
    /// </summary>
    public void Abort()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        ReleasePlayer();
        player = null;

        // 수정(2026-09-22) — castBar?.HideImmediate() → 유니티 null 검사. 이 함수는 방이 꺼질 때(씬을 닫을 때
        // 포함) 불린다. 그때는 시전 바가 먼저 파괴됐을 수 있는데, ?.는 파괴된 오브젝트를 null로 보지 않아서
        // 그대로 호출하다 예외가 난다. 유니티 오브젝트는 != null로 봐야 파괴된 것까지 걸러진다.
        if (castBar != null) castBar.HideImmediate();
        castBar = null;

        foreach (FlyingRelic relic in spawned)
            if (relic != null) Destroy(relic.gameObject);
        spawned.Clear();
    }

    private void OnDisable()
    {
        // 방이 꺼지면 코루틴은 어차피 멈춘다. 붙잡힌 플레이어와 떠 있는 유물이 남지 않게 정리한다.
        Abort();
    }

    private IEnumerator Run()
    {
        tornOut = false;
        poseEnded = false;

        // 추가 생성(2026-09-22) — 이번 의식의 부순 수를 처음으로 돌린다.
        brokenCount = 0;
        spawned.Clear();

        // 1. 붙잡는다. 이미 죽었으면 뽑을 몸이 없다 — 곧바로 끝낸다.
        if (player == null || !player.BeginScripted(Vector2.down, RelicTornTrigger))
        {
            Finish(false);
            yield break;
        }

        player.RelicsTornOut += OnRelicsTornOut;
        player.ScriptedPoseEnded += OnScriptedPoseEnded;

        // 2. "뽑힘" 장을 기다린다.
        float waited = 0f;
        while (!tornOut && waited < tornOutTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!tornOut)
        {
            Debug.LogWarning("[왕관 의식] \"뽑힘\" 애니메이션 이벤트가 안 왔다. 시간 기준으로 유물을 띄운다. " +
                             "Tools → 재의 길 → 애니메이션 → 8방향 플레이어 애니메이션 생성 을 실행했는지 확인해라.", this);
        }

        TearOutRelics();

        // 3. 주저앉은 장이 끝나 조작이 돌아오기를 기다린다.
        waited = 0f;
        while (!poseEnded && waited < poseEndTimeout)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!poseEnded)
        {
            Debug.LogWarning("[왕관 의식] 모션 끝 이벤트가 안 왔다. 플레이어를 강제로 놓아준다.", this);
        }

        ReleasePlayer();

        // 4. 의식. 시전 바가 차는 동안 유물을 부술 수 있다.
        // 수정(2026-09-23) — 하나 부술 때마다 바가 처음으로 돌아가고 다음 바는 더 빨리 찬다(WindowSeconds).
        // 바가 다 차면 못 막은 것이다. 10초를 한 번에 주던 때는 네 모서리를 다 돌 수 없었다.
        // 수정(2026-09-22) — 막으면 시간이 남아도 곧바로 끝낸다(기획 "의식 중 때려서 저지"). 09-23부터 막는 기준은 넷 전부가 아니라 brokenToBlock개다.
        // 끝까지 기다리게 하면 다 부순 뒤 몇 초 동안 아무 일도 없는 빈 시간이 생기고, 막았다는 느낌이 흐려진다.
        int windowFor = -1;
        float window = 0f;
        float elapsed = 0f;
        while (!Blocked)
        {
            if (brokenCount != windowFor)
            {
                windowFor = brokenCount;
                window = WindowSeconds(brokenCount);
                elapsed = 0f;
                castBar?.Show(ritualName, window);
            }

            if (elapsed >= window) break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        castBar?.Hide();

        // 5. 결과.
        // 수정(2026-09-22) — 결과표를 붙였다. 남은 유물만 거두고(부서진 것은 스스로 흩어지는 중이다) 결과를 넣는다.
        // 궁극기 영상(다음 단계)은 못 막았을 때 여기, 결과를 넣기 바로 앞에 들어온다.
        bool bossGroggy = Blocked;

        // 수정(2026-09-22, 의식-7) — 못 막았으면 남은 유물이 보스에게 빨려 들어간 뒤 결과가 들어간다.
        // 실이 닿을 곳을 못 받았으면(방이 안 넘김) 예전처럼 제자리에서 사라진다.
        bool absorbed = false;
        foreach (FlyingRelic relic in spawned)
        {
            if (relic == null || relic.IsBroken) continue;

            if (!bossGroggy && ritualCenter.HasValue)
            {
                relic.AbsorbInto(ritualCenter.Value, absorbSeconds);
                absorbed = true;
            }
            else
            {
                relic.Vanish(relicVanishSeconds);
            }
        }

        if (absorbed) yield return new WaitForSeconds(absorbSeconds);

        // 추가 생성(2026-09-24) — 못 막았으면 궁극기 영상을 게임을 멈춘 채 틀고, 끝난 뒤에 결과를 넣는다(위 "다음 단계" 자리).
        // 막았으면(그로기) 영상 없이 바로 결과로 간다.
        if (!bossGroggy) yield return PlayUltimateVideo();

        ApplyResult(bossGroggy);
        spawned.Clear();

        Finish(bossGroggy);
    }

    /// <summary>
    /// 추가 생성(2026-09-24) — 궁극기 영상(CutsceneVideo)을 게임을 멈춘 채 재생하고 끝날 때까지 기다린다.
    ///
    /// 게임 시간은 PauseGate만 만진다(규칙) — 여기서도 Open/Close로 멈춘다. 컷인은 실제 시간으로 돈다.
    /// 씬에 컷인이 없으면 경고만 남기고 넘어간다 — 영상이 없어도 결과(피해)는 들어가야 한다.
    /// 컷인이 끝났다는 신호(Finished)가 안 오면 게임이 멈춘 채 갇히므로 실제 시간 15초에서 끊는다(안전망).
    /// </summary>
    private IEnumerator PlayUltimateVideo()
    {
        var cutscene = FindFirstObjectByType<CutsceneVideo>(FindObjectsInactive.Include);
        if (cutscene == null)
        {
            Debug.LogWarning("[왕관 의식] 씬에 궁극기 컷인이 없다 — 영상 없이 결과만 넣는다. " +
                             "Tools → 재의 길 → 씬·세팅 → 궁극기 컷인 게임 씬에 추가 를 실행해라.", this);
            yield break;
        }

        bool finished = false;
        void OnFinished() => finished = true;

        cutscene.gameObject.SetActive(true);
        cutscene.Finished += OnFinished;

        PauseGate.Open(this);
        cutscene.Play();

        float giveUpAt = Time.unscaledTime + 15f;
        while (!finished && Time.unscaledTime < giveUpAt) yield return null;
        if (!finished) Debug.LogError("[왕관 의식] 컷인이 끝났다는 신호가 안 왔다 — 15초에서 끊고 넘어간다.", this);

        cutscene.Finished -= OnFinished;
        PauseGate.Close(this);
    }

    /// <summary>
    /// 유물 네 개를 가슴에서 뽑아 네 귀퉁이로 날린다. 화면 흔들림과 짧은 히트스톱을 같이 건다.
    ///
    /// 수정(2026-09-22) — 불꽃을 가슴에서 한 번 터뜨리던 것을 <b>유물마다 날아가는 쪽으로 하나씩</b> 터뜨린다.
    /// 구성 도구가 넣는 불꽃(명중 불똥 HitSparks)은 오른쪽(+X)으로 튀게 만들어져 있고, 쓰는 쪽이 방향을
    /// 돌려 놓는 프리팹이다(<see cref="HitSparkSpawner"/>가 공격 방향으로 돌리는 것과 같다). 돌리지 않고 한 번만
    /// 터뜨리면 유물은 사방으로 날아가는데 불꽃만 오른쪽으로 뿜는다. 날아가는 쪽으로 뿜어야 몸에서
    /// "뜯겨 나간다"가 읽힌다.
    /// </summary>
    private void TearOutRelics()
    {
        Vector3 feet = player.transform.position;
        Vector3 chest = feet + Vector3.up * chestHeight;

        cameraShake?.Shake(burstShake, burstShakeSeconds);
        PauseGate.HitStop(burstHitStop);

        if (relicPrefab == null)
        {
            Debug.LogWarning("[왕관 의식] 유물 프리팹이 비어 있다. Tools → 재의 길 → 씬·세팅 → 왕관 의식 구성 을 실행해라.", this);
            return;
        }

        int count = Mathf.Min(relics.Length, landingPoints.Length);
        for (int i = 0; i < count; i++)
        {
            if (landingPoints[i] == null) continue;

            // 방 아래에 둔다. 방이 꺼지거나 지워질 때 유물도 같이 사라진다.
            FlyingRelic relic = Instantiate(relicPrefab, feet, Quaternion.identity, transform);
            relic.name = relics[i] != null ? $"FlyingRelic_{relics[i].name}" : $"FlyingRelic_{i}";
            relic.Launch(relics[i], feet, chestHeight, landingPoints[i].position);
            spawned.Add(relic);

            // 추가 생성(2026-09-22) — 부서지면 알려 오게 한다. 유물은 부서진 뒤 스스로 지워지므로 구독을 따로 끊지 않는다
            // (지워지는 쪽이 이 컴포넌트를 들고 있을 뿐, 반대는 아니다).
            relic.Broken += OnRelicBroken;

            // 추가 생성(2026-09-22, 의식-4) — 내려앉은 뒤 보스 가슴으로 실을 흘린다.
            if (ritualCenter.HasValue) relic.SetThreadTarget(ritualCenter.Value);

            // 추가 생성(2026-09-22) — 이 유물이 날아가는 쪽으로 불꽃을 뿜는다. 이유는 위 요약 주석.
            if (burstPrefab != null)
            {
                Vector3 toPoint = landingPoints[i].position - feet;
                float angle = Mathf.Atan2(toPoint.y, toPoint.x) * Mathf.Rad2Deg;
                GameObject burst = Instantiate(burstPrefab, chest, Quaternion.Euler(0f, 0f, angle));

                // 불꽃 프리팹이 스스로 지워지게(Stop Action) 만들어져 있어도, 아닌 것을 꽂았을 때 씬에 쌓이지 않게 한다.
                Destroy(burst, 3f);
            }
        }

        // 추가 생성(2026-09-22) — 뽑혔다고 알린다. 방이 받아 보스를 손 뻗기에서 의식 자세로 넘긴다.
        RelicsTorn?.Invoke();
    }

    // 수정(2026-09-22) — CountRemaining(남은 유물 수)을 지웠다. 쓰던 곳이 시간 끝 로그 하나였고, 이제 부서진 수를
    // brokenCount로 바로 센다. 남은 오브젝트로 세면 흩어지는 중인 유물 때문에 순간마다 수가 달라진다.

    /// <summary>
    /// 추가 생성(2026-09-22) — 유물 하나가 부서졌다. 수를 세고 화면을 한 번 더 흔든다.
    /// 공격 자체의 히트스톱·흔들림은 이미 들어갔다. 그 위에 조금 더 얹어 "한 대 더"가 아니라 "부쉈다"로 읽히게 한다.
    /// </summary>
    private void OnRelicBroken(FlyingRelic relic)
    {
        brokenCount++;
        cameraShake?.Shake(breakShake, breakShakeSeconds);

        // 추가 생성(2026-09-22) — 보스가 움찔하게 알린다(방이 전한다).
        RelicBroken?.Invoke();

        Debug.Log($"[왕관 의식] 유물을 부쉈다 — {brokenCount}/{spawned.Count}" +
                  (relic.Relic != null ? $" ({relic.Relic.DisplayName})" : "") +
                  (!Blocked ? $", 다음 바 {WindowSeconds(brokenCount):0.#}초" : ""), this);
    }

    /// <summary>추가 생성(2026-09-23) — 유물을 broken개 부순 뒤 시전 바가 차는 시간(초).</summary>
    private float WindowSeconds(int broken)
    {
        float speed = 1f;
        if (barSpeedByBroken != null && barSpeedByBroken.Length > 0)
            speed = barSpeedByBroken[Mathf.Clamp(broken, 0, barSpeedByBroken.Length - 1)];

        return firstBarSeconds / Mathf.Max(0.1f, speed);
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 결과를 넣는다. 못 막았으면 플레이어 체력을 <see cref="failHealthLeft"/>까지 깎는다.
    /// 수정(2026-09-23) — 부순 개수별 결과표를 "막았다/못 막았다" 둘로 줄였다(필드 주석 참고).
    ///
    /// <b>피해량이 아니라 남길 체력으로 적은 이유.</b> 기획이 "2칸까지만 남게"처럼 남길 양으로 적혀 있고,
    /// 유물로 최대 체력이 늘어난 플레이어에게도 같은 무게("2칸만 남기고 깎인다")로 들어간다.
    ///
    /// 피해는 <see cref="Health.TakeUnavoidableDamage"/>로 넣는다. 막는 법은 부수기였고 그 시간이 끝났으니,
    /// 이 순간 마침 대시 중이라고 결과를 피하면 안 된다(자세한 이유는 그 함수 주석).
    /// </summary>
    /// <param name="bossGroggy">막았는가. 로그에만 쓴다 — 그로기는 방이 보스에게 전한다.</param>
    private void ApplyResult(bool bossGroggy)
    {
        // 유물을 하나도 못 뿌렸으면(프리팹이 비었거나 자리가 없을 때) 부술 기회 자체가 없었다. 그걸 "하나도 못 부숨"으로
        // 치면 설정 실수 하나로 플레이어가 크게 다친다. 경고는 뿌릴 때 이미 남겼다.
        if (spawned.Count == 0)
        {
            Debug.LogWarning("[왕관 의식] 뿌린 유물이 없어 결과를 건너뛴다.", this);
            return;
        }

        Health health = player != null ? player.GetComponent<Health>() : null;
        if (health == null || health.IsDead)
        {
            Debug.Log($"[왕관 의식] 결과 — 부순 유물 {brokenCount}/{spawned.Count}. 플레이어가 없거나 이미 쓰러져 피해는 건너뛴다.", this);
            return;
        }

        int before = health.Current;
        int damage = !bossGroggy && failHealthLeft > 0 && before > failHealthLeft ? before - failHealthLeft : 0;

        if (damage > 0 && health.TakeUnavoidableDamage(damage, null))
        {
            cameraShake?.Shake(resultShake, resultShakeSeconds);
            PauseGate.HitStop(resultHitStop);
        }

        Debug.Log($"[왕관 의식] 결과 — 부순 유물 {brokenCount}/{spawned.Count}, 체력 {before} → {health.Current}" +
                  (bossGroggy ? ", 의식을 막았다(보스 그로기)" : ""), this);
    }

    private void OnRelicsTornOut() => tornOut = true;

    private void OnScriptedPoseEnded() => poseEnded = true;

    /// <summary>
    /// 플레이어 신호 구독을 끊고, 아직 붙잡혀 있으면 놓아준다. 여러 번 불려도 안전하다.
    /// </summary>
    private void ReleasePlayer()
    {
        if (player == null) return;

        player.RelicsTornOut -= OnRelicsTornOut;
        player.ScriptedPoseEnded -= OnScriptedPoseEnded;

        // EndScripted는 붙잡힌 상태가 아니면(이미 풀렸거나 죽었으면) 아무것도 안 한다.
        player.EndScripted();
    }

    /// <param name="bossGroggy">수정(2026-09-22) — 유물을 다 부숴 의식을 막았는가. <see cref="Finished"/>로 넘긴다.</param>
    private void Finish(bool bossGroggy)
    {
        ReleasePlayer();
        player = null;

        // 추가 생성(2026-09-22) — 다 쓴 시전 바 참조도 비운다. 남겨 두면 나중에 방이 꺼질 때
        // Abort가 이미 끝난 의식의 바를 한 번 더 감추러 간다.
        castBar = null;

        routine = null;
        Finished?.Invoke(bossGroggy);
    }
}
