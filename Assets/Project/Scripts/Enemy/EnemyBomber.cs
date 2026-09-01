using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 자폭병. <b>답은 "거리를 벌린다"</b>다.
///
/// <b>왜 이 적을 만들었나.</b> 망령의 답은 "옆으로 피한다", 사수의 답은 "다가간다"다.
/// 셋째 적이 또 같은 답을 요구하면 적을 늘려도 플레이어가 하는 일은 안 늘어난다.
/// 이 적은 붙어서 터지므로 답이 <b>"멀어진다"</b>가 되고, 사수와는 아예 반대다.
/// 한 방에 사수와 같이 두면 "누구에게 붙고 누구에게서 떨어질지"라는 판단이 새로 생긴다.
///
/// <b>느린 것이 이 설계의 전부다.</b> 추격 속도가 플레이어(14)보다 한참 느려서 그냥 걸어가면
/// 언제든 벗어난다. 그래서 이 적은 <b>가만히 있는 것</b>과 <b>공격 모션에 묶인 것</b>만 처벌한다.
/// 다른 적과 싸우느라 발이 묶였을 때 뒤에서 다가오는 것이 이 적의 자리다.
/// 빠르게 만들면 "무조건 맞는 적"이 되어 회피가 아니라 체력 관리 문제로 바뀐다.
///
/// 상태 흐름:
///   Roam ──(탐지)──> Chase ──(점화 거리)──> Fuse ──(끝)──> 폭발 + 자기 사망
///   Chase ──(놓침)──> Roam
///   점화 중이 아닐 때 ──(피격)──> Hit ──> Chase
///   어디서든 ──(사망)──> Dead
///
/// <b>점화는 맞아도 안 멈춘다.</b> 경직으로 점화가 늦춰지면 "때리면 안전"이 되어 이 적의
/// 유일한 위협이 사라진다. 대신 체력이 낮아 점화 중에도 <b>죽일 수는</b> 있고, 죽으면 안 터진다.
/// 그래서 플레이어에게 답이 둘 남는다 — 터지기 전에 벗어나거나, 그 자리에서 끝내거나.
///
/// 탐지·넉백·피격·사망·풀 복귀는 <see cref="EnemyBase"/>가 갖는다. 여기 있는 것은
/// 이 적이 어떻게 싸우는지뿐이다.
/// </summary>
public class EnemyBomber : EnemyBase
{
    private enum State { Roam, Chase, Fuse, Hit, Dead }

    [Header("점화")]
    [Tooltip("이 거리 안으로 들어오면 점화를 시작한다. <b>폭발 반경보다 작아야 한다</b> — " +
             "크면 점화 시점에 플레이어가 이미 반경 밖이라 영영 못 맞힌다.")]
    [SerializeField, Min(0.1f)] private float fuseRange = 3f;

    [Tooltip("점화 시간(초). fuse 클립 4프레임 / 6fps = 0.667초에 맞췄다. " +
             "이 시간이 곧 플레이어가 폭발 반경 밖으로 나갈 수 있는 시간이다.")]
    [SerializeField, Min(0f)] private float fuseSeconds = 0.667f;

    [Header("폭발")]
    [Tooltip("폭발 판정 반경(유닛). 점화 거리(3)에서 여기까지 벌어져야 피한다.")]
    [SerializeField, Min(0.1f)] private float explosionRadius = 6f;

    [Tooltip("폭발 피해량. 플레이어 최대 체력이 3이라 1도 가볍지 않다. " +
             "이 적은 한 번 터지고 사라지므로 2로 올리면 거래가 자폭병 쪽으로 크게 기운다.")]
    [SerializeField, Min(0)] private int explosionDamage = 1;

    [Tooltip("터질 때 띄울 이펙트. 시트에는 폭발 그림이 없으므로 이것이 유일한 폭발 연출이고, " +
             "동시에 판정 반경을 플레이어에게 알려주는 유일한 수단이다.")]
    [SerializeField] private GameObject explosionEffectPrefab;

    [Tooltip("이펙트 크기 배율. 프리팹에 들어 있는 크기에 곱한다. " +
             "그림의 불투명 영역이 판정 반경과 맞도록 재서 넣는다 — 스프라이트 크기가 아니라 " +
             "실제로 그려진 부분을 기준으로 재야 한다.")]
    [SerializeField, Min(0.05f)] private float explosionEffectScale = 1f;

    [Header("이동 속도")]
    [Tooltip("배회 속도. 플레이어를 못 찾은 상태라 느긋해도 된다.")]
    [SerializeField, Min(0f)] private float roamSpeed = 5f;

    [Tooltip("추격 속도. <b>플레이어 이동(14)보다 한참 느려야 한다</b> — 걸어서 벗어날 수 " +
             "있어야 이 적이 '가만히 있는 것'만 처벌하는 적이 된다.")]
    [SerializeField, Min(0f)] private float chaseSpeed = 7f;

    [Tooltip("배회 방향을 바꾸는 주기(초).")]
    [SerializeField, Min(0.1f)] private float roamDirectionSeconds = 1.2f;

    private State state;
    private Vector2 roamDirection;
    private float stateTimer;
    private float roamTimer;

    // 터져서 죽는 중인가. 사망 처리에서 그림을 감출지 정하는 데 쓴다.
    private bool exploded;

    // ── 수명 ──────────────────────────────────────────────────────────────

    protected override void OnSpawned()
    {
        exploded = false;

        // 자폭한 적도 풀에 돌아갔다가 다시 나온다. 감춰둔 그림을 여기서 되돌리지 않으면
        // 다음 방에 <b>안 보이는 적</b>이 돌아다닌다 — 전투가 안 끝나는데 화면에는 아무것도
        // 없는 상태라, 원인을 찾기 가장 어려운 종류의 버그가 된다.
        if (Renderer != null) Renderer.enabled = true;

        EnterRoam();
    }

    private void Update()
    {
        if (state == State.Dead) return;

        switch (state)
        {
            case State.Roam:
                UpdateRoam();
                break;

            case State.Chase:
                UpdateChase();
                break;

            default:
                UpdateTimedState();
                break;
        }
    }

    protected override Vector2 ResolveVelocity()
    {
        return state switch
        {
            State.Roam => roamDirection * roamSpeed,
            State.Chase => DirectionToTarget() * chaseSpeed,

            // 경직 중에는 밀려나는 속도만 남는다.
            State.Hit => KnockbackVelocity,

            // 점화 중에는 제자리에 선다. 다가오면서 터지면 "벗어난다"는 회피가
            // 성립하지 않고, 넉백으로 밀리지도 않는다 — 점화는 아무것도 못 막는다.
            _ => Vector2.zero,
        };
    }

    // ── 배회 ──────────────────────────────────────────────────────────────

    private void UpdateRoam()
    {
        roamTimer -= Time.deltaTime;
        if (roamTimer <= 0f)
        {
            roamTimer = roamDirectionSeconds;
            roamDirection = Random.insideUnitCircle.normalized;
            UpdateFacing(roamDirection.x);
        }

        if (TryAcquireTarget(DetectionRadius))
            state = State.Chase;
    }

    private void EnterRoam()
    {
        state = State.Roam;
        stateTimer = 0f;
        roamTimer = 0f;
        roamDirection = Vector2.zero;
    }

    // ── 추격 ──────────────────────────────────────────────────────────────

    /// <summary>플레이어에게 곧장 다가가고, 점화 거리에 닿으면 불을 붙인다.</summary>
    private void UpdateChase()
    {
        if (!HasLiveTarget)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        float distance = DistanceToTarget();

        // 탐지 반경이 아니라 더 넓은 LoseRadius로 판단한다(히스테리시스).
        if (distance > LoseRadius)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        UpdateFacing(DirectionToTarget().x);

        if (distance <= fuseRange) BeginFuse();
    }

    // ── 점화 / 폭발 ────────────────────────────────────────────────────────

    private void BeginFuse()
    {
        state = State.Fuse;
        stateTimer = fuseSeconds;

        Animator?.SetTrigger(AttackHash);
    }

    private void UpdateTimedState()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        switch (state)
        {
            case State.Fuse:
                Explode();
                break;

            // 경직이 풀리면 다시 쫓는다. 대상을 잃었으면 배회로 돌아간다.
            case State.Hit:
                if (HasLiveTarget) state = State.Chase;
                else { ClearTarget(); EnterRoam(); }
                break;
        }
    }

    /// <summary>
    /// 터진다. 반경 안의 플레이어를 때리고 <b>자기도 죽는다.</b>
    ///
    /// 죽는 처리를 직접 하지 않고 <see cref="Health"/>를 0으로 만드는 이유: 처치 수,
    /// 재 게이지, 전투 종료 판정, 풀 복귀가 전부 사망 흐름에 붙어 있다. 여기서 따로
    /// 흉내 내면 언젠가 그중 하나를 빠뜨리고, 그러면 "적을 다 잡았는데 문이 안 열리는"
    /// 형태로 늦게 드러난다.
    /// </summary>
    private void Explode()
    {
        exploded = true;
        state = State.Dead;

        // 이펙트를 판정보다 먼저 만든다. 맞은 쪽이 죽으면서 무슨 연출을 하든 폭발은 이미
        // 나와 있어야 한다 — 판정 결과에 따라 이펙트가 달라지면 플레이어는 "맞았을 때만
        // 터지는 것"으로 배운다. 보스의 재 폭발에서 한 판단과 같다.
        SpawnExplosionEffect();

        // 원 하나로 판정한다. 사방으로 나가는 폭발이라 방향이 없고, OverlapCircle이 그대로 맞다.
        var hit = Physics2D.OverlapCircle(transform.position, explosionRadius, PlayerLayer);
        if (hit != null)
        {
            var target = hit.GetComponentInParent<Health>();
            if (target != null) target.TakeDamage(explosionDamage, transform.position);
        }

        Health.TakeDamage(Health.Current, transform.position);
    }

    /// <summary>
    /// 폭발 이펙트를 발밑에 만든다.
    ///
    /// 자기 자식으로 붙이지 않는 이유: 폭발은 <b>그 자리에서 일어난 사건</b>이다. 자식으로
    /// 붙이면 시체가 풀로 돌아갈 때 이펙트도 같이 사라진다. 수명은 프리팹의
    /// SpriteFrameAnimator가 알아서 끝낸다.
    /// </summary>
    private void SpawnExplosionEffect()
    {
        // 비어 있어도 패턴은 멀쩡히 돌아간다 — 피해도 들어가고 자기도 죽는다. 그래서 화면만
        // 보면 "이펙트를 안 꽂았다"와 "이펙트가 안 보인다"가 똑같아 보인다. 보스에서 그 둘을
        // 구별 못 해 한 번 헤맸으므로 여기서는 이유를 남긴다.
        if (explosionEffectPrefab == null)
        {
            Debug.LogWarning($"[{name}] 폭발 이펙트 프리팹이 비어 있다. 인스펙터의 " +
                             "Explosion Effect Prefab에 BomberBlast를 꽂아라. " +
                             "지금은 아무 그림 없이 피해만 들어가서, 플레이어가 왜 맞았는지 알 수 없다.",
                             this);
            return;
        }

        var effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

        // 프리팹에 들어 있는 크기에 곱한다. 절대값으로 덮으면 프리팹마다 다른 기준 크기를
        // 여기서 다시 외워야 한다.
        if (!Mathf.Approximately(explosionEffectScale, 1f))
            effect.transform.localScale *= explosionEffectScale;
    }

    // ── 피격 / 사망 ────────────────────────────────────────────────────────

    protected override void OnStaggered()
    {
        // 점화가 시작되면 맞아도 안 멈춘다.
        //
        // 경직이 점화를 늦추면 계속 때리는 것만으로 폭발을 막을 수 있고, 그러면 이 적의
        // 위협이 통째로 사라진다. 대신 <b>죽이는 것</b>은 여전히 폭발을 막으므로,
        // 점화가 시작된 뒤에도 "끝낼 수 있는가"라는 판단이 남는다.
        if (state == State.Fuse) return;

        state = State.Hit;
        stateTimer = HitSeconds;
    }

    protected override void OnDeath()
    {
        state = State.Dead;

        // 자폭으로 죽었으면 그림을 감춘다. 안 그러면 방금 터진 몸이 다시 나타나 무릎 꿇고
        // 무너지는 그림이 이어져서, 폭발과 앞뒤가 안 맞는다.
        // 감춘 것은 OnSpawned에서 되돌린다 — 풀에서 재사용되기 때문이다.
        if (exploded && Renderer != null) Renderer.enabled = false;
    }

    // ── 표시 ──────────────────────────────────────────────────────────────

    protected override void DrawExtraGizmos()
    {
        // 터지는 범위. 플레이어가 이 밖으로 나가야 산다.
        Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);

        // 이 안으로 들어오면 불을 붙인다.
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, fuseRange);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        // 점화 거리가 폭발 반경보다 크면, 불을 붙이는 순간 플레이어가 이미 반경 밖이다.
        // 그러면 이 적은 혼자 터지고 죽기만 하는 적이 된다 — 에러도 안 나고 모션도 멀쩡해서
        // "왜 안 아프지"만 남는다.
        if (fuseRange >= explosionRadius)
        {
            Debug.LogWarning(
                $"[{name}] 점화 거리({fuseRange})가 폭발 반경({explosionRadius})보다 작지 않다. " +
                "붙어서 터져도 플레이어가 반경 밖이라 아무 피해도 안 들어간다.", this);
        }

        // 탐지 거리가 점화 거리보다 짧으면 붙어 있어도 플레이어를 대상으로 못 잡아 점화하지 않는다.
        if (DetectionRadius < fuseRange)
        {
            Debug.LogWarning(
                $"[{name}] 탐지 거리({DetectionRadius})가 점화 거리({fuseRange})보다 짧다. " +
                "코앞에 있어도 플레이어를 못 알아본다.", this);
        }
    }
}
