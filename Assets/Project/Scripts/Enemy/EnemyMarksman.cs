using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 사수. <b>답은 "거리를 좁힌다"</b>다.
///
/// <b>왜 이 적을 만들었나.</b> 지금까지 적은 잿불 망령 하나였고, 그 답은 "옆으로 피한다"였다.
/// 체력만 다른 적을 몇 종 늘려도 플레이어가 하는 일은 <b>기다렸다 피하고 때린다</b> 하나 그대로다.
/// 이 프로젝트가 보스 패턴에 세워둔 원칙이 그대로 적용된다 —
/// "패턴을 늘릴 때는 회피법이 겹치지 않아야 한다."
///
/// 이 적은 <b>플레이어에게 오지 않는다.</b> 오히려 가까워지면 물러난다. 그래서 가만히
/// 기다리는 것이 답이 아니게 되고, 플레이어에게 "먼저 다가가야 한다"는 새 일이 생긴다.
///
/// <b>물러나는 속도가 플레이어보다 느린 것이 이 설계의 전부다.</b>
/// 플레이어 이동이 14인데 이 적이 그보다 빠르면 영영 못 잡는 술래잡기가 된다. 느리면
/// 접근이 <b>항상 성립하는 답</b>이 되고, 다가가는 동안 화살을 몇 대 맞느냐가 실력이 된다.
///
/// 상태 흐름:
///   Roam ──(탐지)──> Reposition ──(사거리 안·정지)──> Aim ──> Shoot ──> Cooldown ──> Reposition
///   Reposition ──(놓침)──> Roam
///   어디서든 ──(피격)──> Hit ──> Cooldown
///   어디서든 ──(사망)──> Dead
///
/// 탐지·넉백·피격·사망·풀 복귀는 <see cref="EnemyBase"/>가 갖는다. 여기 있는 것은
/// 이 적이 어떻게 싸우는지뿐이다.
/// </summary>
public class EnemyMarksman : EnemyBase
{
    private enum State { Roam, Reposition, Aim, Shoot, Cooldown, Hit, Dead }

    [Header("사거리")]
    [Tooltip("이 거리쯤에서 쏘려 한다. 방 세로가 28유닛이라 12면 방 절반쯤에서 쏜다.")]
    [SerializeField, Min(0.1f)] private float preferredRange = 12f;

    [Tooltip("이 거리보다 가까워지면 물러난다. 붙으면 활을 못 쓴다는 뜻이라, " +
             "플레이어가 파고드는 것이 곧 정답이 된다.")]
    [SerializeField, Min(0.1f)] private float retreatDistance = 6f;

    [Tooltip("사거리 안이어도 이 만큼은 어긋나야 다시 자리를 잡는다. " +
             "0이면 목표 거리에서 한 걸음씩 앞뒤로 떨며 영영 못 쏜다.")]
    [SerializeField, Min(0.1f)] private float rangeTolerance = 2f;

    [Header("이동 속도")]
    [Tooltip("배회 속도. 플레이어를 못 찾은 상태라 느긋해도 된다.")]
    [SerializeField, Min(0f)] private float roamSpeed = 5f;

    [Tooltip("자리를 잡을 때의 속도. <b>플레이어 이동(14)보다 반드시 느려야 한다</b> — " +
             "빠르면 영영 못 잡는 술래잡기가 되어 이 적이 답이 없는 적이 된다.")]
    [SerializeField, Min(0f)] private float repositionSpeed = 7f;

    [Tooltip("배회 방향을 바꾸는 주기(초).")]
    [SerializeField, Min(0.1f)] private float roamDirectionSeconds = 1.4f;

    [Header("동작 시간")]
    [Tooltip("조준(초). 망령의 예비동작(0.4)보다 길다 — 날아오는 것을 보고 비켜야 하므로 " +
             "읽을 시간이 더 필요하다.")]
    [SerializeField, Min(0f)] private float aimSeconds = 0.55f;

    [Tooltip("발사 동작(초). 이 시간이 끝나면 재사용 대기로 넘어간다.")]
    [SerializeField, Min(0f)] private float shootSeconds = 0.25f;

    [Tooltip("다음 화살까지의 대기(초). 짧으면 연사가 되어 피할 틈이 없다.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 1.2f;

    [Header("화살")]
    [Tooltip("쏠 투사체 프리팹. <b>이 프리팹의 히트박스는 플레이어를 때리도록 설정돼야 한다</b> — " +
             "플레이어의 화살(EmberArrow)을 그대로 꽂으면 적이 적을 쏜다.")]
    [SerializeField] private Projectile arrowPrefab;

    // 수정 — 발사 자리를 자식 오브젝트(ArrowSpawn)가 아니라 쏘는 방향에서 계산한다.
    //
    // 자식으로 두면 그 위치가 항상 +x 쪽에 고정된다. 스프라이트는 flipX로 뒤집히지만
    // 자식 Transform은 안 뒤집히고, 이 적은 8방향으로 쏘기 때문에 뒤집기로도 위·아래는
    // 답이 안 나온다. 플레이어의 투사체 스킬(ProjectileSkillData)이 이미 같은 이유로
    // 원점을 방향에 맞춰 돌리고 있다 — 같은 문제의 답을 두 벌 두지 않는다.
    [Tooltip("발사 위치를 쏘는 방향으로 얼마나 밀지(유닛). 몸 반지름(약 1.5)보다 커야 " +
             "화살이 자기 콜라이더 안에서 태어나지 않는다.")]
    [SerializeField, Min(0f)] private float forwardOffset = 2f;

    [Tooltip("발사 높이(유닛). 피벗이 발밑이라 0이면 바닥을 긁는다. 플레이어 캡슐이 " +
             "발밑 기준 0~1.25이고 화살 판정이 위아래 ±0.7이라, 1이면 0.3~1.7을 훑어 " +
             "몸통을 확실히 지난다.")]
    [SerializeField, Min(0f)] private float launchHeight = 1f;

    [Tooltip("화살 한 대의 피해량.")]
    [SerializeField, Min(0)] private int arrowDamage = 1;

    private State state;
    private Vector2 roamDirection;
    private Vector2 repositionDirection;

    // 조준을 시작한 순간에 고정한 발사 방향.
    private Vector2 shotDirection = Vector2.right;

    private float stateTimer;
    private float roamTimer;

    // ── 수명 ──────────────────────────────────────────────────────────────

    protected override void OnSpawned()
    {
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

            case State.Reposition:
                UpdateReposition();
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
            State.Reposition => repositionDirection * repositionSpeed,

            // 경직 중에는 밀려나는 속도만 남는다.
            State.Hit => KnockbackVelocity,

            // 조준·발사·대기는 제자리에 선다.
            // 조준 중에 움직이면 플레이어가 "지금 화살이 온다"를 읽을 수 없다.
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
            state = State.Reposition;
    }

    private void EnterRoam()
    {
        state = State.Roam;
        stateTimer = 0f;
        roamTimer = 0f;
        repositionDirection = Vector2.zero;
    }

    // ── 자리 잡기 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 쏘기 좋은 거리로 움직인다. 너무 가까우면 물러나고, 너무 멀면 다가간다.
    ///
    /// 가까울 때 <b>물러나는</b> 것이 이 적의 정체성이다. 망령처럼 달려들면 결국
    /// "기다렸다 피하고 때린다"로 답이 같아진다.
    /// </summary>
    private void UpdateReposition()
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

        Vector2 toTarget = DirectionToTarget();
        UpdateFacing(toTarget.x);

        if (distance < retreatDistance)
        {
            // 너무 가깝다. 등을 보이며 물러난다.
            repositionDirection = -toTarget;
            return;
        }

        if (distance > preferredRange + rangeTolerance)
        {
            // 너무 멀다. 사거리 안으로 들어간다.
            repositionDirection = toTarget;
            return;
        }

        // 쏘기 좋은 자리다. 멈춰서 조준한다.
        repositionDirection = Vector2.zero;
        BeginAim();
    }

    // ── 시간이 정해진 상태들 (Aim / Shoot / Cooldown / Hit) ─────────────────

    private void UpdateTimedState()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        switch (state)
        {
            case State.Aim:
                BeginShoot();
                break;

            case State.Shoot:
                BeginCooldown();
                break;

            // 맞으면 바로 다시 조준하지 않고 대기를 한 번 거친다.
            // 망령과 같은 판단이다 — 그 틈이 "쳤다 → 물러났다 → 다시 친다"의 리듬을 만든다.
            case State.Hit:
                BeginCooldown();
                break;

            case State.Cooldown:
                ResumeOrRoam();
                break;
        }
    }

    private void BeginAim()
    {
        state = State.Aim;
        stateTimer = aimSeconds;

        // 쏘는 방향을 <b>조준이 시작될 때</b> 고정한다.
        //
        // 발사 순간에 정하면 플레이어가 어디로 비키든 따라와서 피할 방법이 없어진다.
        // 시작할 때 고정하면 조준 시간이 "지금 옆으로 비키면 산다"는 신호가 된다.
        // 망령의 돌진 방향을 예비동작에서 고정한 것과 같은 이유다.
        shotDirection = DirectionToTarget();
        if (shotDirection.sqrMagnitude < 0.01f) shotDirection = Vector2.right;

        UpdateFacing(shotDirection.x);
        Animator?.SetTrigger(AttackHash);
    }

    private void BeginShoot()
    {
        state = State.Shoot;
        stateTimer = shootSeconds;

        FireArrow();
    }

    /// <summary>화살 하나를 만들어 고정해둔 방향으로 쏜다.</summary>
    private void FireArrow()
    {
        if (arrowPrefab == null)
        {
            Debug.LogWarning($"[{name}] 화살 프리팹이 비어 있어 쏘지 못했다.", this);
            return;
        }

        // 수정 — 앞으로 미는 것은 쏘는 방향을 따라가고, 높이는 방향과 무관하게 항상 +y다.
        //
        // 탑다운이라 y 하나가 "깊이"와 "높이"를 겸한다. 높이를 방향에 섞으면 위로 쏠 때만
        // 화살이 더 뜨고 아래로 쏠 때는 바닥을 긁는다. 두 값을 따로 더해야 어느 방향으로
        // 쏘든 같은 몸통 높이로 날아간다. (ProjectileSkillData가 같은 계산을 한다.)
        //
        // 조준 원점과 발사 원점의 높이를 맞추는 것이 핵심이다. shotDirection은 발밑 → 발밑으로
        // 그어져 있고 여기서 발사 쪽에만 높이를 더하므로, 화살은 그 선과 나란히 launchHeight
        // 높이로 날아가 플레이어 몸통(0~1.25)을 지난다. 보스의 잿불 파도가 발밑을 조준해놓고
        // 1.6 위에서 쏘다가 스치기만 했던 것이 바로 이 자리다.
        Vector3 origin = transform.position
                         + (Vector3)(shotDirection * forwardOffset)
                         + new Vector3(0f, launchHeight, 0f);

        var arrow = Instantiate(arrowPrefab, origin, Quaternion.identity);
        arrow.Launch(shotDirection, arrowDamage);
    }

    private void BeginCooldown()
    {
        state = State.Cooldown;
        stateTimer = cooldownSeconds;
    }

    /// <summary>대상이 아직 유효하면 자리 잡기로, 아니면 배회로 돌아간다.</summary>
    private void ResumeOrRoam()
    {
        if (HasLiveTarget)
        {
            state = State.Reposition;
            return;
        }

        ClearTarget();
        EnterRoam();
    }

    // ── 피격 / 사망 ────────────────────────────────────────────────────────

    protected override void OnStaggered()
    {
        state = State.Hit;
        stateTimer = HitSeconds;
    }

    protected override void OnDeath()
    {
        state = State.Dead;
    }

    // ── 표시 ──────────────────────────────────────────────────────────────

    protected override void DrawExtraGizmos()
    {
        // 쏘려는 거리. 이 원 위에 서려고 한다.
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, preferredRange);

        // 이 안으로 들어오면 물러난다. 플레이어가 파고들어야 하는 선이다.
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 1f);
        Gizmos.DrawWireSphere(transform.position, retreatDistance);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        // 물러나는 거리가 쏘려는 거리보다 크면 영영 자리를 못 잡는다.
        // 물러났다가 "너무 멀다"고 다시 다가오기를 반복하며 제자리에서 떤다.
        if (retreatDistance >= preferredRange)
        {
            Debug.LogWarning(
                $"[{name}] 물러나는 거리({retreatDistance})가 쏘려는 거리({preferredRange})보다 " +
                "작지 않다. 자리를 못 잡고 앞뒤로 떨기만 한다.", this);
        }

        // 탐지 거리가 쏘려는 거리보다 짧으면, 사거리 안에 들어와도 대상을 못 잡아 안 쏜다.
        if (DetectionRadius < preferredRange)
        {
            Debug.LogWarning(
                $"[{name}] 탐지 거리({DetectionRadius})가 쏘려는 거리({preferredRange})보다 짧다. " +
                "사거리 안에서도 플레이어를 못 알아본다.", this);
        }
    }
}
