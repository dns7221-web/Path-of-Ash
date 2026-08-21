using System.Collections;
using UnityEngine;

/// <summary>
/// 재의 왕(보스) AI.
///
/// <b>망령(<see cref="EnemyWraith"/>)과 따로 만든 이유:</b> 망령은 "발견 → 예비동작 → 돌진"
/// 한 줄기라 상태가 순서대로 흐른다. 보스는 매번 <b>거리를 보고 무엇을 할지 고른다.</b>
/// 망령에 패턴 선택과 페이즈 전환을 끼워 넣으면 그 클래스가 두 종류의 AI를 겸하게 되고,
/// 일반 몹을 손볼 때마다 보스가 깨지는지 확인해야 한다.
///
/// <b>패턴을 거리로 가르는 것이 이 보스의 전부다.</b>
/// 붙으면 내려찍기, 떨어지면 잿불 파도. 한 자리에 서 있으면 안 되게 만드는 장치다.
/// 둘 다 예비동작이 애니메이션에 들어 있어서, 플레이어는 모션을 보고 빠질 수 있다.
///
/// 페이즈 전환은 <b>컨트롤러를 갈아 끼운다.</b> 오브젝트도 Health도 그대로라
/// 진행 중인 체력과 위치가 안 끊긴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class EnemyBoss : MonoBehaviour
{
    private enum State { Idle, Chase, Attack, Transition, Hit, Dead }

    [Header("페이즈")]
    [Tooltip("2페이즈에서 쓸 컨트롤러. 체력이 절반이 되면 갈아 끼운다.")]
    [SerializeField] private RuntimeAnimatorController phase2Controller;

    [Tooltip("이 비율 이하로 떨어지면 2페이즈로 넘어간다.")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float phase2HealthRatio = 0.5f;

    [Tooltip("전환 연출 길이(초). 애니메이션 클립 길이와 맞춰야 한다.")]
    [SerializeField] private float transitionSeconds = 0.75f;

    [Header("이동")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Tooltip("2페이즈에서 이동 속도에 곱할 값.")]
    [SerializeField] private float phase2SpeedScale = 1.4f;

    [Tooltip("이 거리보다 가까우면 공격 사이에 뒤로 물러난다. 내려찍기 사거리보다 작아야 한다.")]
    [SerializeField] private float retreatDistance = 3.5f;

    [Tooltip("물러날 때의 속도 배율. 1이면 플레이어가 영영 못 따라잡는다.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float retreatSpeedScale = 0.55f;

    [Header("내려찍기")]
    [Tooltip("이 거리 안이면 내려찍기를 고른다.")]
    [SerializeField] private float slamRange = 5f;

    [Tooltip("모션 시작부터 판정이 나가기까지(초). 예비동작 길이다.")]
    [SerializeField] private float slamHitDelay = 0.33f;

    [Tooltip("모션 전체 길이(초). 이 동안 못 움직인다.")]
    [SerializeField] private float slamMotionSeconds = 0.6f;

    [SerializeField] private Vector2 slamHitSize = new Vector2(7f, 5f);
    [SerializeField] private int slamDamage = 2;

    [Header("잿불 파도")]
    [Tooltip("멀리 있을 때 쓴다. 비어 있으면 이 패턴을 건너뛴다.")]
    [SerializeField] private Projectile wavePrefab;

    // 추가 생성 — 잿불 파도를 쓸 수 있는 최대 거리.
    //
    // 왜 필요한가: 예전에는 "내려찍기 사거리 밖"이 곧 파도 조건이었다. 그런데 쿨다운 중에
    // 보스가 계속 다가오기 때문에, 쿨다운이 끝나는 순간에는 거의 항상 사거리 안이었다.
    // 그래서 파도는 사실상 한 번도 나오지 않았다. 이제 두 패턴의 사거리를 겹쳐두고
    // 겹치는 구간에서는 번갈아 쓴다.
    [Tooltip("잿불 파도를 쓸 수 있는 최대 거리. 내려찍기 사거리보다 커야 두 패턴이 섞인다.")]
    [SerializeField] private float waveRange = 20f;

    // 추가 생성 — 파도 전용 쿨다운.
    //
    // 왜 공용 쿨다운으로는 부족한가: 공용 쿨다운(attackCooldown)은 "공격 후 쉬는 시간"이라
    // 1.1초로 짧다. 두 패턴을 번갈아 쓰게 하면 파도가 2.2초마다 나오는데, 화면을 가로지르는
    // 광역 패턴이 그 빈도로 나오면 <b>평타처럼 보인다.</b> 보스 패턴은 가끔 나와서 예비동작을
    // 읽고 대비하는 맛이 있어야 한다. 그래서 파도만 따로 훨씬 긴 쿨다운을 둔다.
    [Tooltip("잿불 파도를 다시 쓰기까지의 시간(초). 이 값이 파도 빈도를 정한다.")]
    [SerializeField, Min(0f)] private float waveCooldown = 6f;

    [SerializeField] private float waveFireDelay = 0.4f;
    [SerializeField] private float waveMotionSeconds = 0.7f;

    [Tooltip("한 번에 나가는 발수. 2페이즈에서는 여기에 2가 더해진다.")]
    [SerializeField] private int waveCount = 3;

    [Tooltip("발 사이 각도(도).")]
    [SerializeField] private float waveSpreadDegrees = 18f;

    [SerializeField] private int waveDamage = 1;
    [SerializeField] private float waveSpawnHeight = 1.6f;

    [Header("공통")]
    [Tooltip("공격이 끝난 뒤 다음 공격까지 쉬는 시간(초). 없으면 쉴 틈 없이 맞는다.")]
    [SerializeField] private float attackCooldown = 1.1f;

    [Tooltip("2페이즈에서 쉬는 시간에 곱할 값. 작을수록 사납다.")]
    [SerializeField] private float phase2CooldownScale = 0.6f;

    [Tooltip("피격 경직 시간(초). 보스는 짧아야 한다 — 길면 연타로 아무것도 못 하게 된다.")]
    [SerializeField] private float hitStunSeconds = 0.12f;

    [SerializeField] private LayerMask playerLayer;

    private Rigidbody2D body;
    private Health health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;

    private State state = State.Idle;
    private bool isPhase2;
    private float cooldownTimer;

    // 추가 생성 — 파도를 다시 쓸 수 있을 때까지 남은 시간.
    private float waveCooldownTimer;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int SlamHash = Animator.StringToHash("Slam");
    private static readonly int WaveHash = Animator.StringToHash("Wave");
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private static readonly int DieHash = Animator.StringToHash("Die");
    private static readonly int TransitionHash = Animator.StringToHash("Transition");

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    private void Start()
    {
        // 플레이어는 프리팹 인스턴스라 인스펙터로 미리 연결할 수 없다.
        // Include가 필요하다 — 연출 중에 잠깐 꺼져 있으면 못 찾고 영영 가만히 서 있게 된다.
        var controller = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (controller != null) player = controller.transform;

        if (player == null)
            Debug.LogWarning("[보스] 플레이어를 못 찾았다. 그 자리에 서 있게 된다.", this);
    }

    private void Update()
    {
        if (state == State.Dead || state == State.Attack ||
            state == State.Transition || state == State.Hit) return;

        if (player == null) { Stop(); return; }

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        // 추가 생성 — 파도 쿨다운은 공격 중에도 계속 흐른다.
        // Update 앞부분에서 공격 상태면 return하므로, 여기까지 왔다는 건 쉬는 중이라는 뜻이다.
        if (waveCooldownTimer > 0f) waveCooldownTimer -= Time.deltaTime;

        Vector2 toPlayer = player.position - transform.position;
        float distance = toPlayer.magnitude;

        FaceTowards(toPlayer.x);

        // 쉬는 동안은 자리를 다시 잡는다. 이 틈이 없으면 플레이어가 반격할 자리가 사라진다.
        if (cooldownTimer > 0f) { Reposition(toPlayer, distance); return; }

        ChoosePattern(toPlayer, distance);
    }

    /// <summary>
    /// 추가 생성 — 이번에 쓸 패턴을 고른다.
    ///
    /// 수정(패턴 편중): 예전에는 거리 하나로만 갈랐다.
    /// <c>if (거리 &lt;= 내려찍기 사거리) 내려찍기; else 파도;</c>
    /// 그런데 쉬는 동안 <see cref="Reposition"/>이 계속 다가오기 때문에 쿨다운이 끝나는
    /// 시점에는 거의 항상 사거리 안이었다. 결과적으로 <b>내려찍기만 무한 반복</b>했고
    /// 파도 애니메이션은 한 번도 재생된 적이 없다.
    ///
    /// 두 사거리를 겹쳐두고, 겹치는 구간에서는 <b>직전과 다른 패턴</b>을 쓴다.
    /// 무작위 대신 번갈아 쓰는 이유: 무작위는 운 나쁘면 같은 패턴이 서너 번 이어져
    /// 똑같은 문제가 다시 보인다. 번갈아 쓰면 플레이어가 다음을 읽을 수 있어
    /// "패턴을 외워서 공략한다"는 보스전의 재미도 같이 생긴다.
    /// </summary>
    private void ChoosePattern(Vector2 toPlayer, float distance)
    {
        // 파도는 자기 쿨다운이 돌아왔을 때만 쓴다. 빈도를 이 하나로 통제하므로
        // "직전에 무엇을 썼는지" 같은 기억이 따로 필요 없다.
        bool canWave = wavePrefab != null && distance <= waveRange && waveCooldownTimer <= 0f;
        if (canWave) { StartWave(toPlayer); return; }

        if (distance <= slamRange) { StartCoroutine(Slam()); return; }

        Chase(toPlayer, distance);
    }

    /// <summary>
    /// 추가 생성 — 잿불 파도를 시작하고 전용 쿨다운을 건다.
    ///
    /// 쿨다운을 시전이 끝난 뒤가 아니라 <b>시작할 때</b> 거는 이유:
    /// 끝난 뒤에 걸면 모션 길이(0.7초)만큼 간격이 더 늘어나, 인스펙터에 적은 숫자와
    /// 실제 체감 주기가 어긋난다. 시작 시점 기준이라야 "6초마다 한 번"이 그대로 지켜진다.
    /// </summary>
    private void StartWave(Vector2 toPlayer)
    {
        waveCooldownTimer = waveCooldown * (isPhase2 ? phase2CooldownScale : 1f);
        StartCoroutine(Wave(toPlayer));
    }

    /// <summary>
    /// 공격 사이에 자리를 다시 잡는다. <b>절대 멈춰 서지 않는다.</b>
    ///
    /// 처음엔 사거리 안이면 Stop()을 불렀는데, 그러면 쉬는 동안 보스가 가만히 서 있어서
    /// "걷는 모션이 거의 안 나오고 공격만 반복하는" 모습이 됐다. 게다가 사거리 경계에서
    /// 프레임마다 멈췄다 갔다를 반복해 떨렸다.
    ///
    /// 대신 너무 붙었으면 <b>뒤로 물러난다.</b> 망령의 넉백과 같은 목적이다 —
    /// 한 번 치고 물러나면 플레이어가 파고들 자리가 생기고, 거리가 계속 변해서
    /// 다음 패턴이 무엇일지 읽는 재미가 생긴다.
    /// </summary>
    private void Reposition(Vector2 toPlayer, float distance)
    {
        state = State.Chase;

        float speed = moveSpeed * (isPhase2 ? phase2SpeedScale : 1f);

        // 물러날 때는 느리게. 같은 속도로 빼면 플레이어가 영영 못 따라잡는다.
        bool retreat = distance < retreatDistance;
        Vector2 direction = toPlayer.normalized * (retreat ? -1f : 1f);
        if (retreat) speed *= retreatSpeedScale;

        body.linearVelocity = direction * speed;
        if (animator != null) animator.SetFloat(SpeedHash, speed);
    }

    /// <summary>플레이어에게 다가간다. 잿불 파도가 없을 때 먼 거리에서 쓴다.</summary>
    private void Chase(Vector2 toPlayer, float distance)
    {
        Reposition(toPlayer, distance);
    }

    private void Stop()
    {
        state = State.Idle;
        body.linearVelocity = Vector2.zero;
        if (animator != null) animator.SetFloat(SpeedHash, 0f);
    }

    private IEnumerator Slam()
    {
        state = State.Attack;
        Stop();
        state = State.Attack; // Stop이 Idle로 되돌리므로 다시 잠근다

        if (animator != null) animator.SetTrigger(SlamHash);

        yield return new WaitForSeconds(slamHitDelay);

        // 판정을 모션 시작이 아니라 여기서 내는 이유: 검이 아직 머리 위에 있는데 맞으면
        // 플레이어는 "안 맞았는데 데미지가 들어왔다"고 느낀다. 예비동작을 보고 피할 수 있어야
        // 패턴을 읽는 재미가 생긴다.
        Vector2 center = (Vector2)transform.position +
                         new Vector2(FacingSign() * slamHitSize.x * 0.35f, 0f);

        var hit = Physics2D.OverlapBox(center, slamHitSize, 0f, playerLayer);
        if (hit != null)
        {
            var target = hit.GetComponentInParent<Health>();
            if (target != null) target.TakeDamage(slamDamage, transform.position);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, slamMotionSeconds - slamHitDelay));

        EndAttack();
    }

    private IEnumerator Wave(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack;

        if (animator != null) animator.SetTrigger(WaveHash);

        yield return new WaitForSeconds(waveFireDelay);

        // 시전 시작이 아니라 여기서 방향을 다시 잡는다. 예비동작 동안 플레이어가 움직였으면
        // 그쪽으로 나가야 한다 — 안 그러면 제자리에서 옆으로 걸어 나가기만 해도 전부 피해진다.
        Vector2 aim = player != null
            ? ((Vector2)player.position - (Vector2)transform.position).normalized
            : toPlayer.normalized;

        int count = waveCount + (isPhase2 ? 2 : 0);

        // 가운데를 기준으로 좌우 대칭이 되게 각도를 나눈다.
        float start = -waveSpreadDegrees * (count - 1) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, start + waveSpreadDegrees * i) * aim;

            var shot = Instantiate(wavePrefab,
                (Vector2)transform.position + Vector2.up * waveSpawnHeight,
                Quaternion.identity);
            shot.Launch(direction, waveDamage);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, waveMotionSeconds - waveFireDelay));

        EndAttack();
    }

    private void EndAttack()
    {
        cooldownTimer = attackCooldown * (isPhase2 ? phase2CooldownScale : 1f);
        state = State.Idle;
    }

    private void OnDamaged(int current, int max)
    {
        if (state == State.Dead || state == State.Transition) return;

        // 절반이 되면 페이즈 전환. 공격 도중이어도 끼어든다 — 반쯤 진행된 패턴보다
        // 페이즈가 바뀌었다는 신호가 훨씬 중요하다.
        if (!isPhase2 && phase2Controller != null && current <= max * phase2HealthRatio)
        {
            StopAllCoroutines();
            StartCoroutine(EnterPhase2());
            return;
        }

        // 공격 중에는 경직을 안 건다. 걸면 예비동작이 끊겨서, 플레이어가 계속 때리는 것만으로
        // 보스가 아무 패턴도 못 쓰는 허수아비가 된다.
        if (state == State.Attack) return;

        StartCoroutine(HitStun());
    }

    private IEnumerator HitStun()
    {
        state = State.Hit;
        body.linearVelocity = Vector2.zero;
        if (animator != null) animator.SetTrigger(HitHash);

        yield return new WaitForSeconds(hitStunSeconds);

        if (state == State.Hit) state = State.Idle;
    }

    private IEnumerator EnterPhase2()
    {
        state = State.Transition;
        body.linearVelocity = Vector2.zero;

        // 연출 중에는 무적이다. 안 그러면 못 움직이는 동안 두들겨 맞아서
        // 2페이즈를 보기도 전에 죽는 보스가 된다.
        health.IsInvulnerableExternally = true;

        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.SetTrigger(TransitionHash);
        }

        yield return new WaitForSeconds(transitionSeconds);

        isPhase2 = true;

        if (animator != null)
        {
            animator.runtimeAnimatorController = phase2Controller;

            // 컨트롤러를 바꾸면 파라미터가 새로 잡히므로 상태를 처음부터 다시 물린다.
            // 안 하면 예전 컨트롤러의 재생 위치가 남아 첫 프레임이 엉뚱하게 나온다.
            animator.Rebind();
        }

        health.IsInvulnerableExternally = false;
        cooldownTimer = 0.4f;
        state = State.Idle;

        Debug.Log("[보스] 2페이즈로 넘어갔다.", this);
    }

    private void OnDied()
    {
        StopAllCoroutines();

        state = State.Dead;
        body.linearVelocity = Vector2.zero;

        if (animator != null) animator.SetTrigger(DieHash);

        // 시체를 밟고 지나가지 않게 충돌만 끈다. 오브젝트는 남겨서 사망 모션이 끝까지 보인다.
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
    }

    /// <summary>바라보는 방향(오른쪽 +1). 스프라이트가 원래 오른쪽을 본다고 가정한다.</summary>
    private float FacingSign() => spriteRenderer != null && spriteRenderer.flipX ? -1f : 1f;

    private void FaceTowards(float deltaX)
    {
        if (spriteRenderer == null || Mathf.Abs(deltaX) < 0.1f) return;

        spriteRenderer.flipX = deltaX < 0f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, slamRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(
            transform.position + new Vector3(slamHitSize.x * 0.35f, 0f, 0f), slamHitSize);
    }
}
