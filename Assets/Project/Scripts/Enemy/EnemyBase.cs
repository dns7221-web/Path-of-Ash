using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 추가 생성 — 일반 적이 공통으로 갖는 부분. 탐지·넉백·피격·사망·풀 복귀를 소유한다.
///
/// <b>왜 뽑아냈나.</b> 적을 두 번째로 만들려는데, <see cref="EnemyWraith"/> 450줄 중
/// 절반 가까이가 "행동"이 아니라 "적이면 다 하는 일"이었다. 그대로 복사하면 넉백 감속이나
/// 사망 후 풀 복귀 같은 것을 <b>두 곳에서 따로 고치게</b> 된다. 그런 중복은 한쪽만 고쳐도
/// 컴파일이 되기 때문에, 어긋난 걸 알아채는 시점이 항상 "게임에서 이상하게 보일 때"다.
///
/// <b>남긴 것과 뺀 것의 기준.</b> 이 클래스는 <b>적이 어떻게 싸우는지는 모른다.</b>
/// 상태 머신, 속도, 공격 판정은 전부 자식이 갖는다. 여기 있는 것은 "맞으면 밀린다",
/// "죽으면 잠시 뒤 풀로 돌아간다", "범위 안의 플레이어를 대상으로 삼는다"처럼
/// <b>어떤 적이든 똑같이 답해야 하는 것</b>뿐이다.
///
/// 스포너가 이 타입을 쓰기 때문에, 새 적은 이 클래스를 상속하는 것만으로 기존 방·풀·
/// 전투 종료 판정에 그대로 얹힌다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Health))]
public abstract class EnemyBase : MonoBehaviour
{
    [Header("탐지")]
    [Tooltip("플레이어를 찾을 레이어.")]
    [SerializeField] private LayerMask playerLayer;

    [Tooltip("이 거리 안에 들어오면 대상으로 삼는다.")]
    [SerializeField, Min(0.1f)] private float detectionRadius = 16f;

    [Tooltip("이 거리를 벗어나면 대상을 놓는다. 탐지 반경보다 반드시 커야 한다 — " +
             "두 값이 같으면 경계선에서 추격과 배회가 매 프레임 번갈아 바뀌며 떤다.")]
    [SerializeField, Min(0.1f)] private float loseRadius = 22f;

    [Header("피격과 넉백")]
    [Tooltip("피격 경직(초).")]
    [SerializeField, Min(0f)] private float hitSeconds = 0.2f;

    [Tooltip("맞은 직후의 밀려나는 속도(유닛/초). 감속되면서 대략 이 값의 1/7만큼 이동한다.")]
    [SerializeField, Min(0f)] private float knockbackSpeed = 14f;

    [Tooltip("넉백이 줄어드는 감속도(유닛/초²). 클수록 짧게 밀린다.")]
    [SerializeField, Min(0f)] private float knockbackDeceleration = 50f;

    [Header("사망")]
    [Tooltip("사망 후 풀로 돌아가기까지의 시간(초). 사망 클립보다 길어야 " +
             "잿더미가 되기 전에 사라지지 않는다.")]
    [SerializeField, Min(0f)] private float deathDespawnSeconds = 1.1f;

    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D body;
    private Health health;
    private Collider2D bodyCollider;

    // 남아 있는 넉백 속도. 피격 순간 채워지고 매 물리 스텝마다 줄어든다.
    private Vector2 knockbackVelocity;

    // 추격 대상. 탐지에 성공하면 채워지고 놓치면 비워진다.
    //
    // 매 프레임 OverlapCircle로 다시 찾지 않고 들고 있는 이유: 찾는 비용도 있지만,
    // 그보다 "지금 누구를 쫓고 있는가"가 상태의 일부라서 프레임마다 바뀌면 안 된다.
    private Transform target;
    private Health targetHealth;

    protected static readonly int AttackHash = Animator.StringToHash("Attack");
    protected static readonly int HitHash = Animator.StringToHash("Hit");
    protected static readonly int DieHash = Animator.StringToHash("Die");

    /// <summary>사망 연출이 끝나 풀로 돌아갈 때 스포너에 알린다.</summary>
    public event Action<EnemyBase> DespawnRequested;

    // ── 자식이 읽는 값 ────────────────────────────────────────────────────

    protected Rigidbody2D Body => body;
    protected Health Health => health;
    protected Animator Animator => animator;

    protected Transform Target => target;
    protected Health TargetHealth => targetHealth;

    /// <summary>
    /// 추가 생성 — 그림. 자폭처럼 <b>죽는 모습을 보여주면 안 되는</b> 적이 감출 때 쓴다.
    ///
    /// 감춘 뒤에는 반드시 <see cref="OnSpawned"/>에서 되돌려야 한다. 풀에서 재사용되는
    /// 오브젝트라, 꺼둔 채로 돌아가면 다음 방에 <b>안 보이는 적</b>이 돌아다닌다.
    /// </summary>
    protected SpriteRenderer Renderer => spriteRenderer;

    /// <summary>
    /// 추가 생성 — 플레이어를 찾는 레이어. 범위 판정을 자식이 직접 할 때 쓴다.
    ///
    /// 자식이 자기 레이어 마스크를 따로 갖지 않게 하려는 것이다. 두 벌이 되면 인스펙터에서
    /// 한쪽만 바꿔놓고 "왜 탐지는 되는데 판정은 안 되지"를 찾게 된다.
    /// </summary>
    protected LayerMask PlayerLayer => playerLayer;

    protected float DetectionRadius => detectionRadius;
    protected float LoseRadius => loseRadius;
    protected float HitSeconds => hitSeconds;
    protected Vector2 KnockbackVelocity => knockbackVelocity;

    /// <summary>죽었는가. 자식의 상태 머신은 이 값이 참이면 아무것도 하지 않아야 한다.</summary>
    protected bool IsDead { get; private set; }

    /// <summary>대상이 아직 쫓을 만한가. 사라졌거나 죽었으면 거짓.</summary>
    protected bool HasLiveTarget => target != null && (targetHealth == null || !targetHealth.IsDead);

    // ── 수명 ──────────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        bodyCollider = GetComponent<Collider2D>();

        if (animator == null) animator = GetComponent<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
    }

    protected virtual void OnEnable()
    {
        health.RestoreFull();
        health.Damaged += HandleDamaged;
        health.Died += HandleDied;

        IsDead = false;
        knockbackVelocity = Vector2.zero;
        if (bodyCollider != null) bodyCollider.enabled = true;

        // 사망 상태에서 풀로 돌아온 Animator를 기본 상태로 되돌린다.
        // Rebind가 없으면 Death 상태에 출구가 없어 재사용된 적이 잿더미로 남는다.
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        ClearTarget();
        OnSpawned();
    }

    protected virtual void OnDisable()
    {
        health.Damaged -= HandleDamaged;
        health.Died -= HandleDied;

        if (body != null) body.linearVelocity = Vector2.zero;
        StopAllCoroutines();

        OnDespawned();
    }

    /// <summary>
    /// 넉백을 줄이고 자식이 정한 속도를 물리에 넘긴다.
    ///
    /// 감속을 여기서 하는 이유: 아래에서 속도로 쓰이는 값이라 물리 주기와 같이 가야
    /// 이동 거리가 일정하다. deltaTime으로 줄이면 프레임률에 따라 밀리는 거리가 달라진다.
    /// </summary>
    private void FixedUpdate()
    {
        if (knockbackVelocity.sqrMagnitude > 0.0001f)
        {
            knockbackVelocity = Vector2.MoveTowards(
                knockbackVelocity, Vector2.zero, knockbackDeceleration * Time.fixedDeltaTime);
        }

        body.linearVelocity = ResolveVelocity();
    }

    // ── 자식이 채우는 것 ──────────────────────────────────────────────────

    /// <summary>이번 물리 스텝의 속도. 상태에 따라 자식이 정한다.</summary>
    protected abstract Vector2 ResolveVelocity();

    /// <summary>풀에서 꺼내져 살아난 순간. 시작 상태로 들어간다.</summary>
    protected abstract void OnSpawned();

    /// <summary>풀로 돌아가거나 꺼질 때. 켜둔 판정이 있으면 여기서 끈다.</summary>
    protected virtual void OnDespawned() { }

    /// <summary>맞아서 경직에 들어갈 때. 상태와 타이머는 자식이 정한다.</summary>
    protected virtual void OnStaggered() { }

    /// <summary>죽는 순간. 켜둔 판정을 끄고 사망 상태로 들어간다.</summary>
    protected virtual void OnDeath() { }

    /// <summary>바라보는 방향이 바뀔 때. 히트박스처럼 같이 뒤집을 것이 있으면 여기서 한다.</summary>
    protected virtual void OnFacingChanged(bool facesLeft) { }

    /// <summary>자식만 아는 반경을 기즈모로 그린다.</summary>
    protected virtual void DrawExtraGizmos() { }

    // ── 탐지 ──────────────────────────────────────────────────────────────

    /// <summary>범위 안의 플레이어를 찾아 대상으로 삼는다. 찾았으면 true.</summary>
    protected bool TryAcquireTarget(float radius)
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

    protected void ClearTarget()
    {
        target = null;
        targetHealth = null;
    }

    /// <summary>대상까지의 거리. 대상이 없으면 무한대라 어떤 사거리 검사에도 안 걸린다.</summary>
    protected float DistanceToTarget()
    {
        if (target == null) return float.PositiveInfinity;
        return Vector2.Distance(body.position, target.position);
    }

    /// <summary>대상을 향한 단위 벡터. 대상이 없거나 겹쳐 있으면 0.</summary>
    protected Vector2 DirectionToTarget()
    {
        if (target == null) return Vector2.zero;

        Vector2 delta = (Vector2)target.position - body.position;
        return delta.sqrMagnitude < 0.0001f ? Vector2.zero : delta.normalized;
    }

    // ── 피격 / 사망 ────────────────────────────────────────────────────────

    private void HandleDamaged(int current, int max)
    {
        if (current <= 0 || IsDead) return;

        // 때린 쪽의 반대 방향으로 밀려난다.
        // 방향은 Health가 데미지를 받을 때 기록해둔 값을 그대로 쓴다.
        knockbackVelocity = health.LastHitDirection * knockbackSpeed;

        animator?.SetTrigger(HitHash);

        // 맞았으면 때린 쪽을 쫓는 게 자연스럽다. 아직 대상이 없었다면 여기서 잡는다.
        // 반경을 loseRadius로 넉넉히 준 이유: 원거리에서 맞았을 때도 반응해야 한다.
        if (target == null) TryAcquireTarget(loseRadius);

        OnStaggered();
    }

    private void HandleDied()
    {
        if (IsDead) return;

        IsDead = true;
        body.linearVelocity = Vector2.zero;
        knockbackVelocity = Vector2.zero;   // 죽은 뒤 미끄러지지 않게

        if (bodyCollider != null) bodyCollider.enabled = false;

        ClearTarget();
        animator?.SetTrigger(DieHash);

        OnDeath();
        StartCoroutine(RequestDespawnAfterDeath());
    }

    private IEnumerator RequestDespawnAfterDeath()
    {
        yield return new WaitForSeconds(deathDespawnSeconds);
        DespawnRequested?.Invoke(this);
    }

    // ── 표시 ──────────────────────────────────────────────────────────────

    /// <summary>가는 방향에 맞춰 그림을 뒤집는다.</summary>
    protected void UpdateFacing(float horizontal)
    {
        if (Mathf.Abs(horizontal) <= 0.01f) return;

        bool facesLeft = horizontal < 0f;
        if (spriteRenderer != null) spriteRenderer.flipX = facesLeft;

        OnFacingChanged(facesLeft);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, loseRadius);

        DrawExtraGizmos();
    }

    /// <summary>
    /// 인스펙터 값의 모순을 경고한다. 고치지는 않는다 — 밸런스 수치는 사람이 정한다.
    ///
    /// 자동으로 바로잡으면 인스펙터에 적은 숫자와 실제로 도는 숫자가 달라져 더 헷갈린다.
    /// (<see cref="EnemyBoss"/>에서 같은 판단을 했다.)
    /// </summary>
    protected virtual void OnValidate()
    {
        if (loseRadius <= detectionRadius)
        {
            Debug.LogWarning(
                $"[{name}] 놓치는 거리({loseRadius})가 탐지 거리({detectionRadius})보다 크지 않다. " +
                "경계선에서 추격과 배회가 매 프레임 번갈아 바뀌며 떤다.", this);
        }
    }
}
