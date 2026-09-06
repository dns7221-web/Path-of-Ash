using UnityEngine;

/// <summary>
/// 잿불 망령의 행동을 소유한다. <b>답은 "옆으로 피한다"</b> — 곧게 달려드는 돌진이다.
///
/// 상태 흐름:
///   Roam ──(탐지)──> Chase ──(사거리)──> Windup ──> Charge ──> Cooldown ──> Chase
///   Chase ──(놓침)──> Roam
///   어디서든 ──(피격)──> Hit ──> Cooldown
///   어디서든 ──(사망)──> Dead
///
/// 수정(코드 리뷰 시점): Chase와 Cooldown을 추가했다. 이전 구조는 Roam(랜덤 배회) 다음이
/// 바로 Windup이라 세 가지가 동시에 깨져 있었다.
/// 1) 플레이어를 향해 다가가는 상태가 없어서, 한 번 돌진하고 빗나가면 아무 방향으로나
///    걸어갔다. 적이 쫓아오지 않으니 위협이 되지 않았다.
/// 2) 탐지 반경(7)이 돌진 이동거리(12 x 0.34 = 4.08유닛)보다 커서 돌진이 항상 허공을
///    지나갔다. 지금은 <see cref="chargeRange"/>를 돌진 거리보다 짧게 두어 반드시 닿는다.
/// 3) 돌진 직후 바로 다시 탐지되어 끊김 없이 돌진을 반복했다. Cooldown이 그 틈을 만든다.
///
/// 수정(적 추가): 탐지·넉백·피격·사망·풀 복귀를 <see cref="EnemyBase"/>로 옮겼다.
/// 여기 남은 것은 <b>이 적이 어떻게 싸우는지</b>뿐이다.
/// 옮긴 필드의 이름을 그대로 뒀기 때문에 프리팹에 저장된 값은 그대로 살아 있다 —
/// 유니티는 어느 클래스가 선언했는지가 아니라 <b>이름</b>으로 직렬화 값을 찾는다.
///
/// <b>속도 값의 기준</b>: 플레이어 걷기 11 / 달리기 20 / 방 49.8x28유닛이다.
/// 추격이 걷기보다 느려야 플레이어가 걸어서 거리를 벌 수 있고(스태미나를 안 쓰는 선택지),
/// 돌진은 달리기보다 빨라야 "달리면 무조건 안전"이 되지 않는다. 그 사이의 긴장이 이 적의
/// 전부다.
/// </summary>
public class EnemyWraith : EnemyBase
{
    private enum State { Roam, Chase, Windup, Charge, Cooldown, Hit, Dead }

    [Header("돌진 사거리")]
    [Tooltip("이 거리 안으로 들어오면 돌진을 준비한다. 돌진 이동거리(chargeSpeed x " +
             "chargeSeconds)보다 짧아야 돌진이 실제로 닿는다.")]
    [SerializeField, Min(0.1f)] private float chargeRange = 7f;

    [Header("이동 속도")]
    [Tooltip("배회 속도. 플레이어를 못 찾은 상태라 느긋해도 된다.")]
    [SerializeField, Min(0f)] private float roamSpeed = 6f;

    [Tooltip("추격 속도. 플레이어 걷기(11)보다 느려야 걸어서 거리를 벌 수 있다.")]
    [SerializeField, Min(0f)] private float chaseSpeed = 8f;

    [Tooltip("돌진 속도. 플레이어 달리기(20)보다 빨라야 달리기만으로 안전해지지 않는다.")]
    [SerializeField, Min(0f)] private float chargeSpeed = 24f;

    [Tooltip("배회 방향을 바꾸는 주기(초).")]
    [SerializeField, Min(0.1f)] private float roamDirectionSeconds = 1.2f;

    [Header("동작 시간")]
    [Tooltip("예비동작(초). windup 클립 4프레임 / 10fps = 0.4초에 맞췄다.")]
    [SerializeField, Min(0f)] private float windupSeconds = 0.4f;

    [Tooltip("돌진(초). charge 클립 4프레임 / 12fps = 0.33초에 맞췄다.")]
    [SerializeField, Min(0f)] private float chargeSeconds = 0.34f;

    [Tooltip("돌진 후 숨 고르는 시간(초). 이게 없으면 플레이어가 범위 안에 있는 동안 " +
             "끊김 없이 돌진을 반복한다.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.7f;

    [Header("참조")]
    [SerializeField] private DamageHitbox chargeHitbox;

    private State state;
    private Vector2 roamDirection;
    private Vector2 chargeDirection = Vector2.right;
    private float stateTimer;
    private float roamTimer;

    // ── 수명 ──────────────────────────────────────────────────────────────

    protected override void OnSpawned()
    {
        EnterRoam();
    }

    protected override void OnDespawned()
    {
        chargeHitbox?.Deactivate();
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
            State.Chase => ChaseDirection() * chaseSpeed,
            State.Charge => chargeDirection * chargeSpeed,

            // 경직 중에는 밀려나는 속도만 남는다.
            State.Hit => KnockbackVelocity,

            // Windup / Cooldown / Dead는 제자리에 선다.
            // 예비동작 중에 움직이면 플레이어가 "지금 돌진이 온다"를 읽을 수 없다.
            _ => Vector2.zero,
        };
    }

    // ── 배회 ──────────────────────────────────────────────────────────────

    private void UpdateRoam()
    {
        UpdateRoamDirection();

        if (TryAcquireTarget(DetectionRadius))
            state = State.Chase;
    }

    /// <summary>배회 방향을 일정 간격으로 바꿔 매 프레임 떨리는 랜덤 이동을 막는다.</summary>
    private void UpdateRoamDirection()
    {
        roamTimer -= Time.deltaTime;
        if (roamTimer > 0f) return;

        roamTimer = roamDirectionSeconds;
        roamDirection = Random.insideUnitCircle.normalized;
        UpdateFacing(roamDirection.x);
    }

    // ── 추격 ──────────────────────────────────────────────────────────────

    private void UpdateChase()
    {
        // 대상이 사라졌거나(스폰 해제) 죽었으면 추격할 이유가 없다.
        if (!HasLiveTarget)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        float distance = DistanceToTarget();

        // 탐지 반경이 아니라 더 넓은 LoseRadius로 판단한다(히스테리시스).
        // 두 값이 같으면 경계선에서 추격과 배회가 매 프레임 번갈아 바뀌며 떤다.
        if (distance > LoseRadius)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        UpdateFacing(Target.position.x - Body.position.x);

        if (distance <= chargeRange)
            BeginWindup();
    }

    /// <summary>추격 중 나아갈 방향. 대상이 없으면 멈춘다.</summary>
    private Vector2 ChaseDirection() => DirectionToTarget();

    // ── 시간이 정해진 상태들 (Windup / Charge / Cooldown / Hit) ──────────────

    private void UpdateTimedState()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        switch (state)
        {
            case State.Windup:
                BeginCharge();
                break;

            case State.Charge:
                BeginCooldown();
                break;

            // 경직이 끝나면 바로 달려들지 않고 Cooldown을 한 번 거친다.
            //
            // 이게 "턴제 느낌"을 만드는 핵심이다. 맞자마자 다시 붙으면 플레이어가 칼을 휘두르는
            // 내내 적이 코앞에 있어서 공격과 피격이 뭉개진다. 경직(0.2초) + 숨 고르기(0.7초)
            // 동안 적이 물러나 있으면, 플레이어의 공격 쿨다운(0.65초)과 주기가 맞물려
            // "쳤다 → 물러났다 → 자리 잡았다 → 다시 친다"는 리듬이 생긴다.
            case State.Hit:
                BeginCooldown();
                break;

            // 숨 고르기가 끝나면 추격으로 돌아간다. 배회로 보내면 눈앞의 플레이어를
            // 두고 딴 데로 걸어가서, 한 번 맞히면 적이 흥미를 잃는 것처럼 보인다.
            case State.Cooldown:
                ResumeChaseOrRoam();
                break;
        }
    }

    private void BeginWindup()
    {
        state = State.Windup;
        stateTimer = windupSeconds;

        // 돌진 방향을 <b>예비동작이 시작될 때</b> 고정한다.
        // 끝날 때 정하면 플레이어가 어디로 피하든 따라붙어서 피할 방법이 없어진다.
        // 시작할 때 고정하면 0.4초의 예비동작이 "지금 옆으로 비키면 산다"는 신호가 된다.
        chargeDirection = DirectionToTarget();
        if (chargeDirection.sqrMagnitude < 0.01f) chargeDirection = Vector2.right;

        UpdateFacing(chargeDirection.x);
        Animator?.SetTrigger(AttackHash);
    }

    private void BeginCharge()
    {
        state = State.Charge;
        stateTimer = chargeSeconds;
        chargeHitbox?.Activate();
    }

    private void BeginCooldown()
    {
        state = State.Cooldown;
        stateTimer = cooldownSeconds;
        chargeHitbox?.Deactivate();
    }

    /// <summary>대상이 아직 유효하면 추격으로, 아니면 배회로 돌아간다.</summary>
    private void ResumeChaseOrRoam()
    {
        if (HasLiveTarget)
        {
            state = State.Chase;
            return;
        }

        ClearTarget();
        EnterRoam();
    }

    private void EnterRoam()
    {
        state = State.Roam;
        stateTimer = 0f;
        roamTimer = 0f;
        chargeHitbox?.Deactivate();
    }

    // ── 피격 / 사망 ────────────────────────────────────────────────────────

    protected override void OnStaggered()
    {
        state = State.Hit;
        stateTimer = HitSeconds;
        chargeHitbox?.Deactivate();
    }

    protected override void OnDeath()
    {
        state = State.Dead;
        chargeHitbox?.Deactivate();
    }

    // ── 표시 ──────────────────────────────────────────────────────────────

    protected override void OnFacingChanged(bool facesLeft)
    {
        // 스프라이트와 돌진 판정을 같은 방향으로 반전한다.
        // flipX는 그림만 뒤집고 자식 Transform에는 영향이 없어서 히트박스를 따로 옮겨야 한다.
        if (chargeHitbox == null) return;

        Vector3 hitboxScale = chargeHitbox.transform.localScale;
        hitboxScale.x = Mathf.Abs(hitboxScale.x) * (facesLeft ? -1f : 1f);
        chargeHitbox.transform.localScale = hitboxScale;
    }

    protected override void DrawExtraGizmos()
    {
        // chargeRange가 돌진 이동거리보다 짧은지는 눈으로 봐야 안다.
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 1f);
        Gizmos.DrawWireSphere(transform.position, chargeRange);

        // 실제 돌진이 닿는 거리. 위 붉은 원(chargeRange)보다 커야 한다.
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, chargeSpeed * chargeSeconds);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        // 돌진이 닿지 않는 사거리는 영영 헛친다. 두 값이 인스펙터의 다른 항목에 떨어져 있어
        // 나란히 놓고 보지 않으면 모순이 안 보인다. (EnemyBoss에서 같은 문제를 겪었다.)
        float chargeDistance = chargeSpeed * chargeSeconds;
        if (chargeRange > chargeDistance)
        {
            Debug.LogWarning(
                $"[{name}] 돌진 사거리({chargeRange})가 실제 돌진 거리({chargeDistance:F2})보다 크다. " +
                "돌진이 플레이어 앞에서 멈춘다.", this);
        }
    }
}
