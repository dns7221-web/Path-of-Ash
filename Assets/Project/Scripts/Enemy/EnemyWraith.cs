using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 잿불 망령의 행동을 소유한다.
/// 탐지는 Physics2D, 이동은 Rigidbody2D를 사용하고 Animator는 결과 그림만 담당한다.
///
/// 상태 흐름:
///   Roam ──(탐지)──> Chase ──(사거리)──> Windup ──> Charge ──> Cooldown ──> Chase
///   Chase ──(놓침)──> Roam
///   어디서든 ──(피격)──> Hit ──> Chase
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
/// <b>속도 값의 기준</b>: 플레이어 걷기 11 / 달리기 20 / 방 49.8x28유닛이다.
/// 추격이 걷기보다 느려야 플레이어가 걸어서 거리를 벌 수 있고(스태미나를 안 쓰는 선택지),
/// 돌진은 달리기보다 빨라야 "달리면 무조건 안전"이 되지 않는다. 그 사이의 긴장이 이 적의
/// 전부다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Health))]
public class EnemyWraith : MonoBehaviour
{
    private enum State { Roam, Chase, Windup, Charge, Cooldown, Hit, Dead }

    [Header("탐지")]
    [Tooltip("플레이어를 찾을 레이어.")]
    [SerializeField] private LayerMask playerLayer;

    [Tooltip("이 거리 안에 들어오면 추격을 시작한다. 방 세로가 28유닛이라 16이면 화면에 " +
             "보이는 적은 대체로 반응한다.")]
    [SerializeField, Min(0.1f)] private float detectionRadius = 16f;

    [Tooltip("이 거리를 벗어나면 추격을 포기한다. 탐지 반경보다 반드시 커야 한다 — " +
             "두 값이 같으면 경계선에서 추격과 배회가 매 프레임 번갈아 바뀌며 떤다.")]
    [SerializeField, Min(0.1f)] private float loseRadius = 22f;

    [Tooltip("이 거리 안으로 들어오면 돌진을 준비한다. 돌진 이동거리(chargeSpeed x " +
             "chargeSeconds)보다 짧아야 돌진이 플레이어에게 닿는다.")]
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
             "돌진이 끊김 없이 반복되어 피할 틈이 없다.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.7f;

    [Tooltip("피격 경직(초). hit 클립 2프레임 / 10fps = 0.2초.")]
    [SerializeField, Min(0f)] private float hitSeconds = 0.2f;

    [Header("넉백")]
    // 추가 생성 — 맞으면 뒤로 밀려난다.
    //
    // 넉백을 넣는 이유는 연출이 아니라 <b>리듬</b>이다. 이게 없으면 플레이어가 칼을 휘두르는
    // 동안 적이 코앞에 붙어 있어서, 공격과 피격이 동시에 일어나는 뭉개진 싸움이 된다.
    // 밀어내면 "쳤다 → 물러났다 → 다시 붙는다"는 주기가 생기고, 그 사이가 플레이어가
    // 위치를 잡거나 스태미나를 회복하는 시간이 된다.
    [Tooltip("맞은 직후의 밀려나는 속도(유닛/초). 감속되면서 대략 이 값의 1/7만큼 이동한다.")]
    [SerializeField, Min(0f)] private float knockbackSpeed = 14f;

    [Tooltip("넉백이 줄어드는 감속도(유닛/초²). 클수록 짧게 밀린다.")]
    [SerializeField, Min(0f)] private float knockbackDeceleration = 50f;

    [Tooltip("사망 후 풀로 돌아가기까지의 시간(초). death 클립 0.5초보다 길어야 " +
             "잿더미가 되는 마지막 프레임이 보인다.")]
    [SerializeField, Min(0f)] private float deathDespawnSeconds = 1.1f;

    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private DamageHitbox chargeHitbox;

    private Rigidbody2D body;
    private Health health;
    private Collider2D bodyCollider;
    private State state;
    private Vector2 roamDirection;
    private Vector2 chargeDirection = Vector2.right;
    private float stateTimer;
    private float roamTimer;

    // 추가 생성 — 남아 있는 넉백 속도. 피격 순간 채워지고 매 물리 스텝마다 줄어든다.
    private Vector2 knockbackVelocity;

    // 추격 대상. 탐지에 성공하면 채워지고 놓치면 비워진다.
    // 매 프레임 OverlapCircle로 다시 찾지 않고 들고 있는 이유: 찾는 비용도 있지만,
    // 그보다 "지금 누구를 쫓고 있는가"가 상태의 일부라서 프레임마다 바뀌면 안 된다.
    private Transform target;
    private Health targetHealth;

    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private static readonly int DieHash = Animator.StringToHash("Die");

    /// <summary>사망 연출이 끝나 풀로 돌아갈 때 스포너에 알린다.</summary>
    public event Action<EnemyWraith> DespawnRequested;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        bodyCollider = GetComponent<Collider2D>();
        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        health.RestoreFull();
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        if (bodyCollider != null) bodyCollider.enabled = true;
        chargeHitbox?.Deactivate();

        // 사망 상태에서 풀로 돌아온 Animator를 기본 걷기 상태로 되돌린다.
        // Rebind가 없으면 Death 상태에 출구가 없어 재사용된 적이 잿더미로 남는다.
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        ClearTarget();
        EnterRoam();
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        chargeHitbox?.Deactivate();
        if (body != null) body.linearVelocity = Vector2.zero;
        StopAllCoroutines();
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

    private void FixedUpdate()
    {
        // 추가 생성 — 넉백은 매 물리 스텝마다 줄어든다. 감속을 여기서 하는 이유는
        // 아래에서 속도로 쓰이는 값이라 물리 주기와 같이 가야 이동 거리가 일정하기 때문이다.
        if (knockbackVelocity.sqrMagnitude > 0.0001f)
        {
            knockbackVelocity = Vector2.MoveTowards(
                knockbackVelocity, Vector2.zero, knockbackDeceleration * Time.fixedDeltaTime);
        }

        body.linearVelocity = state switch
        {
            State.Roam => roamDirection * roamSpeed,
            State.Chase => ChaseDirection() * chaseSpeed,
            State.Charge => chargeDirection * chargeSpeed,

            // 추가 생성 — 경직 중에는 밀려나는 속도만 남는다.
            State.Hit => knockbackVelocity,

            // Windup / Cooldown / Dead는 제자리에 선다.
            // 예비동작 중에 움직이면 플레이어가 "지금 돌진이 온다"를 읽을 수 없다.
            _ => Vector2.zero,
        };
    }

    // ── 배회 ──────────────────────────────────────────────────────────────

    private void UpdateRoam()
    {
        UpdateRoamDirection();

        if (TryAcquireTarget(detectionRadius))
            state = State.Chase;
    }

    /// <summary>배회 방향을 일정 간격으로 바꿔 매 프레임 떨리는 랜덤 이동을 막는다.</summary>
    private void UpdateRoamDirection()
    {
        roamTimer -= Time.deltaTime;
        if (roamTimer > 0f) return;

        roamTimer = roamDirectionSeconds;
        roamDirection = UnityEngine.Random.insideUnitCircle.normalized;
        UpdateFacing(roamDirection.x);
    }

    // ── 추격 ──────────────────────────────────────────────────────────────

    private void UpdateChase()
    {
        // 대상이 사라졌거나(스폰 해제) 죽었으면 추격할 이유가 없다.
        if (target == null || (targetHealth != null && targetHealth.IsDead))
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        float distance = Vector2.Distance(body.position, target.position);

        // 탐지 반경이 아니라 더 넓은 loseRadius로 판단한다(히스테리시스).
        // 두 값이 같으면 경계선에서 추격과 배회가 매 프레임 번갈아 바뀌며 떤다.
        if (distance > loseRadius)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        UpdateFacing(target.position.x - body.position.x);

        if (distance <= chargeRange)
            BeginWindup();
    }

    /// <summary>추격 중 나아갈 방향. 대상이 없으면 멈춘다.</summary>
    private Vector2 ChaseDirection()
    {
        if (target == null) return Vector2.zero;

        Vector2 delta = (Vector2)target.position - body.position;
        return delta.sqrMagnitude < 0.0001f ? Vector2.zero : delta.normalized;
    }

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

            // 추가 생성 — 경직이 끝나면 바로 달려들지 않고 Cooldown을 한 번 거친다.
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
        chargeDirection = ((Vector2)target.position - body.position).normalized;
        if (chargeDirection.sqrMagnitude < 0.01f) chargeDirection = Vector2.right;

        UpdateFacing(chargeDirection.x);
        animator?.SetTrigger(AttackHash);
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
        if (target != null && (targetHealth == null || !targetHealth.IsDead))
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

    // ── 탐지 ──────────────────────────────────────────────────────────────

    /// <summary>범위 안의 플레이어를 찾아 대상으로 삼는다. 찾았으면 true.</summary>
    private bool TryAcquireTarget(float radius)
    {
        Collider2D found = Physics2D.OverlapCircle(transform.position, radius, playerLayer);
        if (found == null) return false;

        // 이미 죽은 플레이어는 쫓지 않는다. 사망 연출 중에 적이 달려드는 그림을 막는다.
        Health foundHealth = found.GetComponentInParent<Health>();
        if (foundHealth != null && foundHealth.IsDead) return false;

        target = found.transform;
        targetHealth = foundHealth;
        return true;
    }

    private void ClearTarget()
    {
        target = null;
        targetHealth = null;
    }

    // ── 피격 / 사망 ────────────────────────────────────────────────────────

    private void OnDamaged(int current, int max)
    {
        if (current <= 0 || state == State.Dead) return;

        state = State.Hit;
        stateTimer = hitSeconds;
        chargeHitbox?.Deactivate();
        animator?.SetTrigger(HitHash);

        // 추가 생성 — 때린 쪽의 반대 방향으로 밀려난다.
        // 방향은 Health가 데미지를 받을 때 기록해둔 값을 그대로 쓴다.
        knockbackVelocity = health.LastHitDirection * knockbackSpeed;

        // 맞았으면 때린 쪽을 쫓는 게 자연스럽다. 아직 대상이 없었다면 여기서 잡는다.
        // 반경을 loseRadius로 넉넉히 준 이유: 원거리에서 맞았을 때도 반응해야 한다.
        if (target == null) TryAcquireTarget(loseRadius);
    }

    private void OnDied()
    {
        if (state == State.Dead) return;

        state = State.Dead;
        body.linearVelocity = Vector2.zero;
        knockbackVelocity = Vector2.zero; // 추가 생성 — 죽은 뒤 미끄러지지 않게
        chargeHitbox?.Deactivate();
        if (bodyCollider != null) bodyCollider.enabled = false;
        ClearTarget();
        animator?.SetTrigger(DieHash);
        StartCoroutine(RequestDespawnAfterDeath());
    }

    private IEnumerator RequestDespawnAfterDeath()
    {
        yield return new WaitForSeconds(deathDespawnSeconds);
        DespawnRequested?.Invoke(this);
    }

    // ── 표시 ──────────────────────────────────────────────────────────────

    private void UpdateFacing(float horizontal)
    {
        if (Mathf.Abs(horizontal) <= 0.01f) return;

        bool facesLeft = horizontal < 0f;
        if (spriteRenderer != null) spriteRenderer.flipX = facesLeft;

        // 스프라이트와 돌진 판정을 같은 방향으로 반전한다.
        // flipX는 그림만 뒤집고 자식 Transform에는 영향이 없어서 히트박스를 따로 옮겨야 한다.
        if (chargeHitbox != null)
        {
            Vector3 hitboxScale = chargeHitbox.transform.localScale;
            hitboxScale.x = Mathf.Abs(hitboxScale.x) * (facesLeft ? -1f : 1f);
            chargeHitbox.transform.localScale = hitboxScale;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 세 반경을 한눈에 비교할 수 있어야 한다. 특히 chargeRange가 돌진 이동거리보다
        // 짧은지는 눈으로 봐야 안다.
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, loseRadius);

        Gizmos.color = new Color(1f, 0.1f, 0.1f, 1f);
        Gizmos.DrawWireSphere(transform.position, chargeRange);

        // 실제 돌진이 닿는 거리. 위 붉은 원(chargeRange)보다 커야 한다.
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, chargeSpeed * chargeSeconds);
    }
}
