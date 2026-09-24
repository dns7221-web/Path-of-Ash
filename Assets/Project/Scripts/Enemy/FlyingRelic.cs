using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 추가 생성(2026-09-21, 보스 궁극기 "왕관 의식") — 플레이어 가슴에서 뽑혀 방 귀퉁이까지 날아가
/// 떠 있는 유물 하나. <see cref="CrownRitual"/>이 네 개를 만든다.
///
/// <b>좌표를 "바닥 자리 + 높이"로 나눈 이유.</b> 이 게임은 35도로 내려다보는 화면이라, 화면 위쪽이
/// "멀리"이기도 하고 "높이"이기도 하다. 유물을 포물선으로 날리면서 화면 좌표만 쓰면, 솟아오르는 것과
/// 방 안쪽으로 멀어지는 것이 구별되지 않는다. 그래서 루트는 늘 <b>바닥 위의 자리</b>에 두고(그림자도
/// 여기 있다), 그림(Body)만 그 위로 높이만큼 띄운다. 그림자가 바닥을 직선으로 미끄러지고 그림이
/// 그 위로 솟았다 내려앉으니, 눈이 "공중으로 던졌다"로 읽는다.
///
/// <b>정렬은 루트의 SortingGroup이 맡는다.</b> 캐릭터는 발밑(피벗)으로 앞뒤를 가린다. 유물 그림은
/// 1유닛 넘게 떠 있어서 그림 자리로 가리면 실제로 선 자리보다 뒤에 있는 것처럼 정렬된다. 그룹이
/// 루트(바닥 자리)로 정렬하므로 플레이어가 유물 앞뒤를 돌아다녀도 캐릭터와 같은 규칙으로 가려진다.
/// 날아가는 동안에는 VFX 층에 두어 모든 것 위로 보이게 하고, 내려앉으면 Entity 층으로 내린다.
///
/// 수정(2026-09-22) — 부수기를 붙였다. 결과 계산은 <see cref="CrownRitual"/>이 부서진 수를 세서 한다.
///
/// <b>맞는 방식은 튜토리얼 허수아비와 같다.</b> 플레이어의 공격 다섯(기본·W·E·Q·R)은 전부 "Enemy 레이어에 있고
/// <see cref="Health"/>가 달린 것"을 때린다. 유물도 같은 조건을 갖추면 전투 코드를 한 줄도 안 고치고 모든 공격에
/// 맞는다 — 히트스톱·화면 흔들림·명중 불똥까지 같이 따라온다.
///
/// <b>피해량이 아니라 맞은 횟수로 센다(사용자 선택).</b> 피해량은 기본 공격 2, E 4, Q 7, R 8에 유물 보정까지 붙어서
/// 체력으로 세면 "3대"가 스킬·빌드마다 달라진다. 그래서 Health는 "맞았다"는 신호를 받는 통로로만 쓰고
/// (체력은 허수아비처럼 999라 줄어도 상관없다), 들어온 횟수를 여기서 센다. 피격 무적(0.35초)은 적과 같게 남겨서
/// 한 번의 공격이 여러 판정으로 겹쳐 두 대가 되는 일을 막는다. Q는 1단이 무적을 안 걸어서 1단·2단이 다 맞으면 2대다.
/// </summary>
[DisallowMultipleComponent]
public class FlyingRelic : MonoBehaviour
{
    [Header("구성 (빌더가 채운다)")]
    [Tooltip("높이만큼 떠오르는 부분. 아이콘·꼬리·빛이 이 밑에 있다.")]
    [SerializeField] private Transform body;
    [Tooltip("유물 아이콘을 그릴 렌더러.")]
    [SerializeField] private SpriteRenderer iconRenderer;
    [Tooltip("바닥 그림자. 비워도 된다.")]
    [SerializeField] private SpriteRenderer shadowRenderer;
    [Tooltip("날아가는 동안 남는 꼬리. 비워도 된다.")]
    [SerializeField] private TrailRenderer trail;
    [Tooltip("정렬 그룹. 날 때 VFX, 내려앉으면 Entity 층.")]
    [SerializeField] private SortingGroup sortingGroup;
    [Tooltip("내려앉는 순간 터뜨릴 불꽃. 비우면 안 터뜨린다.")]
    [SerializeField] private GameObject landBurstPrefab;

    // 추가 생성(2026-09-22) — 부수기에 쓰는 구성. 빌더가 채운다.
    [Tooltip("맞았다는 신호를 받는 통로. 체력 숫자는 안 쓰고 맞은 횟수만 센다. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private Health health;
    [Tooltip("공격이 닿는 판정(트리거). 내려앉은 뒤에만 켠다. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private Collider2D hitCollider;
    [Tooltip("부서지는 순간 터뜨릴 불꽃. 맞은 방향(때린 쪽 반대)으로 돌려서 터뜨린다. 비우면 안 터뜨린다.")]
    [SerializeField] private GameObject breakBurstPrefab;

    // 추가 생성(2026-09-22, 보스 파티클 의식-4·5) — 보스로 이어지는 실. 보스 파티클 만들기가 채운다.
    [Tooltip("의식-4 유물 실. 코드가 유물에서 보스 가슴으로 불티를 직접 뿌린다(월드 공간). 비우면 실이 없다.")]
    [SerializeField] private ParticleSystem thread;
    [Tooltip("실이 초당 뿌리는 수(기획 20 × 화려하게 1.5).")]
    [SerializeField, Min(0f)] private float threadRate = 30f;
    [Tooltip("실 불티가 보스에게 닿기까지의 시간(초).")]
    [SerializeField, Min(0.1f)] private float threadSeconds = 1.2f;

    [Header("크기")]
    [Tooltip("아이콘의 화면 가로 폭(유닛). 아이콘 원본 크기와 상관없이 이 폭으로 맞춘다.")]
    [SerializeField, Min(0.1f)] private float worldSize = 1.8f;

    [Header("비행")]
    [Tooltip("귀퉁이까지 날아가는 시간(초). 플레이어의 주저앉은 장(0.8초)과 같게 잡았다.")]
    [SerializeField, Min(0.05f)] private float flightSeconds = 0.8f;
    [Tooltip("포물선이 시작·끝 높이를 잇는 선보다 얼마나 더 솟는가(유닛).")]
    [SerializeField, Min(0f)] private float arcHeight = 3f;
    [Tooltip("날아가는 동안 도는 바퀴 수. 내려앉을 때는 똑바로 선다.")]
    [SerializeField] private float spinTurns = 2f;
    [Tooltip("뽑혀 나오는 순간의 크기 비율. 가슴에서 작게 나와 날면서 제 크기가 된다.")]
    [SerializeField, Range(0.05f, 1f)] private float startScale = 0.35f;

    [Header("떠 있기")]
    [Tooltip("내려앉은 뒤 바닥에서 떠 있는 높이(유닛).")]
    [SerializeField, Min(0f)] private float hoverHeight = 1.2f;
    [Tooltip("위아래로 흔들리는 폭(유닛).")]
    [SerializeField, Min(0f)] private float bobAmplitude = 0.15f;
    [Tooltip("위아래로 흔들리는 빠르기(초당 라디안).")]
    [SerializeField, Min(0f)] private float bobSpeed = 2.2f;
    [Tooltip("그림자의 가장 진한 투명도(내려앉았을 때).")]
    [SerializeField, Range(0f, 1f)] private float shadowAlpha = 0.42f;

    // 추가 생성(2026-09-22) — 부수기 수치. 플레이하며 조정한다.
    [Header("부수기")]
    [Tooltip("부서지기까지 맞아야 하는 횟수. 피해량과 상관없이 공격이 한 번 들어가면 1이다.")]
    [SerializeField, Min(1)] private int hitsToBreak = 3;
    [Tooltip("한 대 맞을 때마다 가라앉는 높이(유닛). 남은 횟수가 높이로 읽힌다 — 숫자를 띄우지 않고도 \"한 대 남았다\"가 보인다.")]
    [SerializeField, Min(0f)] private float sinkPerHit = 0.3f;
    [Tooltip("맞았을 때 아이콘에 곱할 색. 이 재질은 원래 색보다 밝게는 못 그려서, 흰 번쩍임 대신 붉게 달아오르게 한다.")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.45f, 0.25f, 1f);
    [Tooltip("맞았을 때 번쩍이는 시간(초).")]
    [SerializeField, Min(0.01f)] private float hitFlashSeconds = 0.12f;
    [Tooltip("맞는 순간 아이콘이 커지는 비율. 번쩍이는 동안 제 크기로 돌아온다.")]
    [SerializeField, Min(1f)] private float hitPunchScale = 1.25f;
    [Tooltip("부서진 뒤 사라지는 시간(초). 의식이 끝나 거둘 때보다 짧게 — 깨진 것은 순식간에 흩어져야 한다.")]
    [SerializeField, Min(0.01f)] private float breakVanishSeconds = 0.2f;

    [Header("정렬 층")]
    [SerializeField] private string flyingSortingLayer = "VFX";
    [SerializeField] private string landedSortingLayer = "Entity";

    /// <summary>
    /// 추가 생성(2026-09-22) — 부서진 순간에 울린다. <see cref="CrownRitual"/>이 받아 부서진 수를 센다.
    /// </summary>
    public event Action<FlyingRelic> Broken;

    /// <summary>이 유물의 데이터. 결과 계산(다음 단계)에서 어느 유물이 남았는지 알려 준다.</summary>
    public RelicData Relic { get; private set; }

    /// <summary>귀퉁이에 내려앉았는가.</summary>
    public bool Landed { get; private set; }

    /// <summary>추가 생성(2026-09-22) — 부서졌는가.</summary>
    public bool IsBroken { get; private set; }

    /// <summary>추가 생성(2026-09-22) — 지금까지 맞은 횟수.</summary>
    public int HitsTaken { get; private set; }

    // 아이콘을 worldSize에 맞춘 배율. 뽑혀 나오는 동안 이 값까지 커진다.
    private float iconScale = 1f;

    // 네 개가 같은 박자로 흔들리면 기계처럼 보여서 유물마다 시작 위상을 다르게 준다.
    private float bobPhase;

    private Coroutine motion;

    // 추가 생성(2026-09-22) — 맞았을 때의 번쩍임. 떠 있는 흔들림(Update)과 따로 돈다.
    private Coroutine flash;

    // 추가 생성(2026-09-22) — 실이 닿을 곳(보스 가슴)과 방출의 소수점 누적.
    private Vector3? threadTarget;
    private float threadCarry;

    /// <summary>
    /// 추가 생성(2026-09-22) — 실이 닿을 곳을 정한다. 왕관 의식이 유물을 뿌릴 때 보스 가슴 자리를 넘긴다.
    /// 보스는 의식 동안 제자리라 위치 하나면 된다.
    /// </summary>
    public void SetThreadTarget(Vector3 target)
    {
        threadTarget = target;
    }

    /// <summary>
    /// 추가 생성(2026-09-22, 의식-7) — 못 막았을 때 남은 유물이 작아지며 보스에게 빨려 들어간다. 끝나면 스스로 지운다.
    /// 왜 깎였는지가 보이게 하려는 연출이다 — 조용히 사라지면 결과와 유물이 이어지지 않는다.
    /// </summary>
    public void AbsorbInto(Vector3 target, float seconds)
    {
        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(Absorb(target, Mathf.Max(0.05f, seconds)));
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 지금 떠 있는 높이. 맞을수록 가라앉되, 바닥에 붙지는 않게 막는다.
    /// </summary>
    private float CurrentHover => Mathf.Max(0.3f, hoverHeight - sinkPerHit * HitsTaken);

    /// <summary>추가 생성(2026-09-22) — 비워 둔 구성을 같은 오브젝트에서 찾고, 판정은 끈 채로 시작한다.</summary>
    private void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (hitCollider == null) hitCollider = GetComponent<Collider2D>();

        // 날아가는 도중에는 맞으면 안 된다. 가슴에서 튀어나오는 순간 플레이어 검에 겹쳐 있어서,
        // 켜 둔 채로 시작하면 휘두르던 공격에 뽑히자마자 한 대가 들어간다.
        SetHittable(false);
    }

    private void OnEnable()
    {
        // 추가 생성(2026-09-22)
        if (health != null) health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        // 추가 생성(2026-09-22)
        if (health != null) health.Damaged -= OnDamaged;
    }

    /// <summary>
    /// 날린다. 루트는 <paramref name="groundFrom"/>(플레이어 발밑)에서 <paramref name="groundTo"/>(귀퉁이 바닥)로
    /// 미끄러지고, 그림은 <paramref name="startHeight"/>(가슴 높이)에서 포물선을 그리며 떠 있는 높이로 내려앉는다.
    /// </summary>
    public void Launch(RelicData relic, Vector3 groundFrom, float startHeight, Vector3 groundTo)
    {
        Relic = relic;
        Landed = false;
        bobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        // 추가 생성(2026-09-22) — 부수기 상태를 처음으로 돌린다.
        IsBroken = false;
        HitsTaken = 0;
        SetHittable(false);
        if (iconRenderer != null) iconRenderer.color = Color.white;

        if (iconRenderer != null && relic != null && relic.Icon != null)
        {
            iconRenderer.sprite = relic.Icon;

            // 아이콘마다 여백이 달라도 화면에서 같은 크기로 보이게 폭 기준으로 맞춘다.
            float width = relic.Icon.bounds.size.x;
            iconScale = width > 0.0001f ? worldSize / width : 1f;
        }

        if (sortingGroup != null) sortingGroup.sortingLayerName = flyingSortingLayer;

        transform.position = groundFrom;
        SetHeight(startHeight);
        SetIcon(startScale, 0f);
        SetShadow(0f);

        if (trail != null)
        {
            // 풀에서 꺼낸 것처럼 옛 궤적이 남아 있으면 첫 프레임에 선이 튄다. 새로 시작한다.
            trail.Clear();
            trail.emitting = true;
        }

        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(Fly(groundFrom, startHeight, groundTo));
    }

    /// <summary>
    /// 사라진다. 제자리에서 조금 커지며 투명해진 뒤 스스로 지운다.
    /// 의식이 끝났을 때, 부서졌을 때 부른다.
    /// </summary>
    public void Vanish(float seconds)
    {
        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(FadeOut(Mathf.Max(0.01f, seconds)));
    }

    private IEnumerator Fly(Vector3 from, float startHeight, Vector3 to)
    {
        float elapsed = 0f;

        while (elapsed < flightSeconds)
        {
            // 스케일 시간을 쓴다. 뽑히는 순간의 히트스톱과 일시정지(인벤토리)에 같이 멈춰야
            // 플레이어 모션과 박자가 맞는다.
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightSeconds);

            // 처음에 세게 튀어나가고 귀퉁이에서 느려진다(가슴에서 "뽑혀 나가는" 힘이 읽히게).
            float eased = 1f - (1f - t) * (1f - t);

            transform.position = Vector3.LerpUnclamped(from, to, eased);
            SetHeight(Mathf.Lerp(startHeight, hoverHeight, eased) + arcHeight * 4f * eased * (1f - eased));

            // 처음 25% 동안 제 크기로 자란다. 도는 각도는 끝에서 0이 되게 남은 양으로 센다.
            float grow = Mathf.Clamp01(t / 0.25f);
            SetIcon(Mathf.Lerp(startScale, 1f, grow), spinTurns * 360f * (1f - eased));

            // 그림자는 발밑에서 시작하면 플레이어 다리 위에 검은 얼룩으로 얹힌다. 날아가며 서서히 드러낸다.
            SetShadow(eased);

            yield return null;
        }

        Land(to);
    }

    private void Land(Vector3 ground)
    {
        transform.position = ground;
        SetHeight(hoverHeight);
        SetIcon(1f, 0f);
        SetShadow(1f);

        if (trail != null) trail.emitting = false;
        if (sortingGroup != null) sortingGroup.sortingLayerName = landedSortingLayer;

        if (landBurstPrefab != null)
        {
            GameObject burst = Instantiate(landBurstPrefab, body != null ? body.position : ground, Quaternion.identity);

            // 불꽃 프리팹이 스스로 지워지게 만들어져 있어도, 루트가 남는 구성이면 씬에 쌓인다. 넉넉히 뒤에 지운다.
            Destroy(burst, 3f);
        }

        Landed = true;
        motion = null;

        // 추가 생성(2026-09-22) — 내려앉은 뒤부터 맞는다.
        SetHittable(true);
    }

    private void Update()
    {
        // 추가 생성(2026-09-22) — 의식-4 실. 내려앉아 떠 있는 동안 보스 가슴으로 불티를 흘린다.
        if (Landed && motion == null && !IsBroken) EmitThread();

        if (!Landed || motion != null) return;

        // 떠 있는 동안 천천히 위아래로 흔들린다. "아직 살아 있는 저주"라는 신호이자, 부술 대상이라는 표시다.
        // 수정(2026-09-22) — 기준 높이를 hoverHeight에서 CurrentHover로. 맞을수록 낮게 흔들린다.
        SetHeight(CurrentHover + Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobAmplitude);
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 공격이 들어왔다. 피해량은 보지 않고 한 대로 센다.
    /// </summary>
    private void OnDamaged(int current, int max)
    {
        // 판정은 내려앉은 뒤에만 켜지지만, 부서지는 한 대와 같은 프레임에 다른 공격이 겹쳐 들어올 수 있다.
        if (IsBroken || !Landed) return;

        HitsTaken++;

        if (HitsTaken >= hitsToBreak)
        {
            Break();
            return;
        }

        if (flash != null) StopCoroutine(flash);
        flash = StartCoroutine(HitFlash());
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 맞은 표시. 붉게 달아오르며 커졌다가 돌아온다. 가라앉는 것은 Update가 CurrentHover로 한다.
    ///
    /// 스케일 시간을 쓰는 이유: 공격이 걸어 둔 히트스톱 동안 번쩍인 채로 멈춰 있다가 시간이 풀리며 돌아와야,
    /// 멈춤과 번쩍임이 한 박자로 읽힌다(명중 섬광 이펙트가 쓰는 방식과 같다).
    /// </summary>
    private IEnumerator HitFlash()
    {
        for (float t = 0f; t < hitFlashSeconds; t += Time.deltaTime)
        {
            float k = t / hitFlashSeconds;
            if (iconRenderer != null) iconRenderer.color = Color.Lerp(hitFlashColor, Color.white, k);
            SetIcon(Mathf.Lerp(hitPunchScale, 1f, k), 0f);
            yield return null;
        }

        if (iconRenderer != null) iconRenderer.color = Color.white;
        SetIcon(1f, 0f);
        flash = null;
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 부서진다. 맞은 방향으로 불꽃을 뿜고, 짧게 흩어지며 사라진다.
    /// 화면 흔들림은 <see cref="CrownRitual"/>이 <see cref="Broken"/>을 받아서 건다(카메라는 의식이 이미 들고 있다).
    /// </summary>
    private void Break()
    {
        IsBroken = true;
        SetHittable(false);

        if (flash != null)
        {
            StopCoroutine(flash);
            flash = null;
        }

        if (iconRenderer != null) iconRenderer.color = Color.white;

        if (breakBurstPrefab != null && health != null)
        {
            // LastHitDirection은 때린 쪽에서 유물로 향한다. 불꽃이 공격이 밀고 간 쪽으로 튄다.
            Vector2 direction = health.LastHitDirection;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            Vector3 at = body != null ? body.position : transform.position;

            GameObject burst = Instantiate(breakBurstPrefab, at, Quaternion.Euler(0f, 0f, angle));
            Destroy(burst, 3f);
        }

        // 추가 생성(2026-09-22) — 의식-5 실 끊김. 실을 따라 불티가 흩어진다.
        SnapThread();

        Broken?.Invoke(this);
        Vanish(breakVanishSeconds);
    }

    /// <summary>추가 생성(2026-09-22) — 실 불티를 한 프레임 몫만큼 뿌린다. 유물 → 보스 가슴으로 threadSeconds에 닿는다.</summary>
    private void EmitThread()
    {
        if (thread == null || !threadTarget.HasValue) return;

        threadCarry += threadRate * Time.deltaTime;
        int count = (int)threadCarry;
        threadCarry -= count;

        Vector3 from = body != null ? body.position : transform.position;
        Vector3 velocity = (threadTarget.Value - from) / threadSeconds;
        for (int i = 0; i < count; i++)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = from + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.25f),
                velocity = velocity + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.25f),
                startLifetime = threadSeconds,
                applyShapeToPosition = false,
            };
            thread.Emit(emit, 1);
        }
    }

    /// <summary>추가 생성(2026-09-22) — 실이 끊길 때 실을 따라 불티를 흩뿌린다(기획 12개 × 화려하게 1.5).</summary>
    private void SnapThread()
    {
        if (thread == null || !threadTarget.HasValue) return;

        Vector3 from = body != null ? body.position : transform.position;
        for (int i = 0; i < 18; i++)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = Vector3.Lerp(from, threadTarget.Value, UnityEngine.Random.Range(0.05f, 0.95f)),
                velocity = (Vector3)(UnityEngine.Random.insideUnitCircle * 2f) + Vector3.up,
                startLifetime = UnityEngine.Random.Range(0.25f, 0.45f),
                applyShapeToPosition = false,
            };
            thread.Emit(emit, 1);
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 실 파티클을 유물에서 떼어 낸다. 유물이 지워질 때 같이 지우면 날아가던 불티가 한꺼번에 사라진다.
    /// </summary>
    private void DetachThread()
    {
        if (thread == null) return;

        thread.transform.SetParent(null, true);
        Destroy(thread.gameObject, threadSeconds + 0.5f);
        thread = null;
    }

    private IEnumerator Absorb(Vector3 target, float seconds)
    {
        Landed = false;
        SetHittable(false);
        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }

        Vector3 start = transform.position;
        float startHeight = body != null ? body.localPosition.y : hoverHeight;

        // 루트(바닥 자리)는 보스 발밑으로, 그림은 가슴 높이로 — 떠오른 채 빨려 들어가는 것처럼 보이게.
        Vector3 ground = new Vector3(target.x, target.y - 4.2f, start.z);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float u = t / seconds;
            float e = u * u;
            transform.position = Vector3.Lerp(start, ground, e);
            SetHeight(Mathf.Lerp(startHeight, 4.2f, e));
            SetIcon(1f - 0.6f * u, 0f);
            SetShadow(1f - u);
            yield return null;
        }

        DetachThread();
        Destroy(gameObject);
    }

    private IEnumerator FadeOut(float seconds)
    {
        Landed = false;
        if (trail != null) trail.emitting = false;

        // 추가 생성(2026-09-22) — 사라지는 동안에는 맞지 않는다. 번쩍임이 돌고 있으면 색을 놓고 끝낸다.
        SetHittable(false);
        if (flash != null)
        {
            StopCoroutine(flash);
            flash = null;
            if (iconRenderer != null) iconRenderer.color = Color.white;
        }

        Color icon = iconRenderer != null ? iconRenderer.color : Color.white;
        Color shadow = shadowRenderer != null ? shadowRenderer.color : Color.black;
        float iconAlpha = icon.a;
        float shadowStart = shadow.a;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);

            if (iconRenderer != null)
            {
                icon.a = iconAlpha * (1f - t);
                iconRenderer.color = icon;
            }

            if (shadowRenderer != null)
            {
                shadow.a = shadowStart * (1f - t);
                shadowRenderer.color = shadow;
            }

            SetIcon(1f + 0.3f * t, 0f);
            yield return null;
        }

        // 추가 생성(2026-09-22) — 실 불티(끊길 때 흩어진 것 포함)가 끝까지 날아가게 떼어 낸 뒤 지운다.
        DetachThread();
        Destroy(gameObject);
    }

    /// <summary>추가 생성(2026-09-22) — 공격 판정을 켜고 끈다.</summary>
    private void SetHittable(bool on)
    {
        if (hitCollider != null) hitCollider.enabled = on;
    }

    private void SetHeight(float height)
    {
        if (body != null) body.localPosition = new Vector3(0f, height, 0f);
    }

    /// <summary>아이콘 크기(worldSize 기준 비율)와 회전(도).</summary>
    private void SetIcon(float scale, float angle)
    {
        if (iconRenderer == null) return;

        Transform icon = iconRenderer.transform;
        icon.localScale = Vector3.one * (iconScale * scale);
        icon.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>그림자 진하기(0~1). 1이면 shadowAlpha.</summary>
    private void SetShadow(float amount)
    {
        if (shadowRenderer == null) return;

        Color color = shadowRenderer.color;
        color.a = shadowAlpha * Mathf.Clamp01(amount);
        shadowRenderer.color = color;
    }
}
