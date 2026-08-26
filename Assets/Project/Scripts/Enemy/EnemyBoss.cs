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

    // 수정(타이밍 정합): 0.75 → 0.875.
    //
    // ashking_transition 클립은 8fps 7프레임이라 길이가 0.875초인데 0.75초만 기다리고 있었다.
    // 그래서 갑옷이 무너지는 <b>마지막 한 프레임이 재생되기 전에 컨트롤러가 갈아 끼워졌다.</b>
    // 보스전에서 유일한 절정을 0.125초 차이로 아무도 못 보고 있었던 셈이다.
    [Tooltip("전환 연출 길이(초). ashking_transition 클립 길이(7프레임 ÷ 8fps = 0.875)와 맞춘다.")]
    [SerializeField] private float transitionSeconds = 0.875f;

    // 추가 생성 — 2페이즈에서 공격 모션 시간에 곱할 값.
    //
    // 왜 필요한가: 아래 slamMotionSeconds·waveMotionSeconds는 1페이즈 클립에 맞춘 숫자다.
    // 그런데 2페이즈 클립은 15fps로 뽑아서 1페이즈(12fps)보다 짧다. 프레임 수는 7로 같으므로
    // 길이 비가 12/15 = 0.8로 딱 떨어진다.
    //
    // 이 값이 없을 때 실제로 어떻게 보였나: 2페이즈 내려찍기 클립은 0.467초인데 코드가 0.6초를
    // 기다려서 보스가 <b>마지막 프레임에서 0.13초 굳어 있었다.</b> 파도는 0.23초였다.
    // "2페이즈는 빨라진다"고 만들어놓고 정작 2페이즈가 더 오래 멈춰 있는 상태였다.
    //
    // 이동 속도(phase2SpeedScale)나 쿨다운(phase2CooldownScale)과 합치지 않은 이유:
    // 저 둘은 <b>연출 의도</b>이고(더 사납게), 이건 <b>클립 길이라는 사실</b>이다.
    // 사납기를 조절하려고 배율을 만졌다가 애니메이션이 어긋나면 원인을 찾을 수 없다.
    [Tooltip("2페이즈에서 공격 모션 시간에 곱할 값. 2페이즈 클립이 15fps라 1페이즈(12fps)의 0.8배다.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float phase2MotionScale = 0.8f;

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

    [Tooltip("모션 시작부터 판정이 나가기까지(초). 예비동작 길이다. " +
             "ashking_slam의 4번째 프레임(0.333)에 맞춰 검이 바닥에 닿는 순간이다.")]
    [SerializeField] private float slamHitDelay = 0.33f;

    // 수정(타이밍 정합): 0.6 → 0.583. 클립 길이 그대로다.
    // 어차피 컨트롤러의 전이가 ExitTime 1이라 클립은 끝까지 재생된다. 코드가 더 오래 잠그면
    // 그 차이만큼 마지막 프레임에서 굳어 있고, 짧게 잠그면 공격 그림 위로 보스가 걷는다.
    [Tooltip("모션 전체 길이(초). ashking_slam 클립 길이(7프레임 ÷ 12fps = 0.583)와 맞춘다.")]
    [SerializeField] private float slamMotionSeconds = 0.583f;

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

    [Tooltip("모션 시작부터 발사까지(초). ashking_wave의 5번째 프레임(0.417) 근처다.")]
    [SerializeField] private float waveFireDelay = 0.4f;

    // 수정(타이밍 정합): 0.7 → 0.583. 내려찍기와 같은 이유다.
    [Tooltip("모션 전체 길이(초). ashking_wave 클립 길이(7프레임 ÷ 12fps = 0.583)와 맞춘다.")]
    [SerializeField] private float waveMotionSeconds = 0.583f;

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

    // 수정(타이밍 정합): 0.12 → 0.125. 코드가 아니라 <b>클립을 코드에 맞췄다.</b>
    //
    // 어긋난 상황: 피격 클립이 10fps 3프레임(0.3초)인데 경직은 0.12초였다. 컨트롤러의 전이가
    // ExitTime 1이라 클립은 0.3초를 다 재생하므로, 나머지 0.18초 동안 <b>움찔하는 그림 위로
    // 보스가 걸어다녔다.</b>
    //
    // 클립을 늘리지 않고 줄인 이유: 0.3초 경직은 Health의 피격 무적 0.35초와 거의 같아서,
    // 플레이어가 계속 때리면 보스가 <b>서 있는 시간의 86%를 경직으로 보낸다.</b> 그건 위 툴팁이
    // 경계하는 바로 그 상태다. 그래서 클립 쪽을 24fps로 다시 잡아 0.125초로 만들었다
    // (플레이어 Dash 클립을 16 → 24fps로 줄인 것과 같은 처리다).
    [Tooltip("피격 경직 시간(초). 보스는 짧아야 한다 — 길면 연타로 아무것도 못 하게 된다. " +
             "ashking_hit 클립 길이(3프레임 ÷ 24fps = 0.125)와 맞춘다.")]
    [SerializeField] private float hitStunSeconds = 0.125f;

    [SerializeField] private LayerMask playerLayer;

    private Rigidbody2D body;
    private Health health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;

    // 추가 생성 — 조준할 때 겨눌 플레이어 콜라이더. 발밑(Transform)이 아니라 이쪽을 노린다.
    //
    // 왜 필요한가: 이 게임은 발바닥을 원점으로 쓴다. 그래서 Transform 위치는 <b>맞아야 할 몸이
    // 아니라 서 있는 바닥</b>이다. 잿불 파도처럼 높이를 두고 나가는 것은 그 차이만큼 빗나간다.
    private Collider2D playerCollider;

    private State state = State.Idle;
    private bool isPhase2;
    private float cooldownTimer;

    // 추가 생성 — 파도를 다시 쓸 수 있을 때까지 남은 시간.
    private float waveCooldownTimer;

    // 추가 생성 — 이번 내려찍기가 노리는 방향. 예비동작이 시작될 때 고정하고 판정 때 그대로 쓴다.
    //
    // 지역 변수가 아니라 필드로 둔 이유: 기즈모가 같은 값을 그려야 인스펙터에서 눈으로 보는
    // 범위와 실제 판정이 일치한다. 판정 범위를 맞추는 도구가 실제와 다르면 없느니만 못하다.
    private Vector2 slamAim = Vector2.right;

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
        if (controller != null)
        {
            player = controller.transform;

            // 추가 생성 — 플레이어 오브젝트에는 콜라이더가 여럿 붙어 있다(몸통, 공격 히트박스).
            // 이름이나 순서로 고르지 않고 <b>playerLayer 마스크에 걸리는 것</b>을 고른다.
            // 내려찍기 판정이 쓰는 마스크와 같은 값이라, 둘이 서로 다른 것을 겨눌 수가 없다.
            foreach (Collider2D candidate in controller.GetComponentsInChildren<Collider2D>(true))
            {
                if ((playerLayer.value & (1 << candidate.gameObject.layer)) == 0) continue;

                playerCollider = candidate;
                break;
            }
        }

        if (player == null)
            Debug.LogWarning("[보스] 플레이어를 못 찾았다. 그 자리에 서 있게 된다.", this);
    }

    private void Update()
    {
        // 수정(주석과 동작 불일치) — 파도 쿨다운을 상태 검사보다 <b>위로</b> 올렸다.
        //
        // 예전에는 아래 return 뒤에 있으면서 주석만 "공격 중에도 계속 흐른다"고 적혀 있었다.
        // 실제로는 공격·경직·전환 중에 멈춰 있었고, 그래서 인스펙터의 6초는 <b>쉬는 시간 6초</b>를
        // 뜻했다. 패턴 하나가 0.58초씩 걸리니 체감 주기는 7~8초까지 늘어난다.
        // waveCooldown을 시전 <b>시작</b> 시점에 거는 이유(주기를 인스펙터 숫자와 맞추기 위해)와
        // 정면으로 어긋나던 자리다.
        if (waveCooldownTimer > 0f) waveCooldownTimer -= Time.deltaTime;

        if (state == State.Dead || state == State.Attack ||
            state == State.Transition || state == State.Hit) return;

        if (player == null) { Stop(); return; }

        // 공격 쿨다운은 여기 그대로 둔다. 이 값은 EndAttack이 걸고 아래 "쉬는 동안 자리를 다시
        // 잡는다" 분기에서만 쓰이므로, 쉬는 상태에서만 흘러도 뜻이 어긋나지 않는다.
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

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

        if (distance <= slamRange) { StartCoroutine(Slam(toPlayer)); return; }

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

    /// <summary>
    /// 내려찍기. 예비동작을 두고 판정을 뒤늦게 낸다.
    ///
    /// 수정(8방향 판정): 판정 상자를 <b>플레이어가 있는 쪽으로 돌린다.</b>
    ///
    /// 예전에는 중심을 <c>FacingSign() * slamHitSize.x * 0.35f</c>만큼 x축으로만 밀었다.
    /// <see cref="FacingSign"/>이 읽는 것은 <c>spriteRenderer.flipX</c> 하나뿐이라
    /// 보스가 아는 방향은 좌우 둘밖에 없었는데, 이 게임은 탑다운이고 플레이어는 8방향으로 움직인다.
    /// 그래서 <c>slamRange</c>(5) 안이면서 상자 세로(중심에서 ±2.5) 밖인
    /// <b>보스의 정북·정남 구간이 통째로 헛쳤다.</b> 옆에서는 맞고 위아래에서는 안 맞는데,
    /// 플레이어 쪽에서는 그 이유를 알 방법이 없다.
    ///
    /// 그림은 여전히 좌우 두 방향뿐이다 — 시트가 그것뿐이라 어쩔 수 없다. 하지만
    /// <b>판정은 그림이 아니라 실제 방향을 따라야 한다.</b> 둘을 같은 값으로 묶어둔 것이 원래 문제였다.
    /// </summary>
    /// <param name="toPlayer">패턴을 고른 시점의 보스 → 플레이어 벡터.</param>
    private IEnumerator Slam(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack; // Stop이 Idle로 되돌리므로 다시 잠근다

        // 추가 생성 — 조준 방향을 예비동작이 시작되는 지금 고정한다.
        //
        // 잿불 파도(<see cref="Wave"/>)는 반대로 발사 직전에 방향을 다시 잡는다. 두 패턴이 다른 이유:
        // 파도는 화면을 가로지르는 원거리 견제라, 제자리에서 옆으로 한 발짝 걷는 것만으로 전부
        // 피해지면 패턴이 성립하지 않는다. 반면 내려찍기는 <b>예비동작을 보고 그 자리를 벗어나는 것이
        // 회피 그 자체다.</b> 판정 순간에 다시 조준하면 어디로 도망쳐도 맞게 되고, 그러면 아래
        // slamHitDelay로 예비동작을 둔 이유가 통째로 사라진다.
        slamAim = AimFrom(toPlayer);

        // 추가 생성 — 페이즈에 맞춘 시간을 여기서 한 번에 구한다.
        //
        // 기다릴 때마다 MotionTime()을 다시 부르지 않는 이유: 대기 도중에 페이즈가 넘어가면
        // 앞의 대기는 1페이즈 값으로, 뒤의 대기는 2페이즈 값으로 계산된다. 그러면 마지막 줄의
        // (모션 − 판정) 뺄셈이 음수가 되어 <b>모션이 판정 직후에 그냥 끝나버린다.</b>
        // 시작 시점에 한 쌍으로 묶어두면 그 어긋남이 생길 수 없다.
        float hitDelay = MotionTime(slamHitDelay);
        float motionSeconds = MotionTime(slamMotionSeconds);

        if (animator != null) animator.SetTrigger(SlamHash);

        yield return new WaitForSeconds(hitDelay);

        // 판정을 모션 시작이 아니라 여기서 내는 이유: 검이 아직 머리 위에 있는데 맞으면
        // 플레이어는 "안 맞았는데 데미지가 들어왔다"고 느낀다. 예비동작을 보고 피할 수 있어야
        // 패턴을 읽는 재미가 생긴다.
        //
        // 수정(8방향 판정) — 중심을 조준 방향으로 밀고, 상자도 같은 각도로 돌린다.
        // 이렇게 해야 slamHitSize.x가 "조준 방향으로의 길이", y가 "그 축을 가로지르는 폭"이라는
        // 뜻이 여덟 방향 어디서나 똑같이 유지된다. 예전 코드에서 그 뜻은 좌우일 때만 맞았다.
        Vector2 center = (Vector2)transform.position + slamAim * (slamHitSize.x * 0.35f);
        float angle = Mathf.Atan2(slamAim.y, slamAim.x) * Mathf.Rad2Deg;

        // 상자 네 귀퉁이를 직접 구해 판정하지 않고 OverlapBox의 각도 인자를 쓴다.
        // 회전한 사각형과의 겹침 판정은 유니티가 이미 해주는 일이다.
        var hit = Physics2D.OverlapBox(center, slamHitSize, angle, playerLayer);
        if (hit != null)
        {
            var target = hit.GetComponentInParent<Health>();
            if (target != null) target.TakeDamage(slamDamage, transform.position);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, motionSeconds - hitDelay));

        EndAttack();
    }

    /// <summary>
    /// 추가 생성 — 판정에 쓸 조준 방향을 길이 1로 만든다.
    ///
    /// 보스와 플레이어가 정확히 겹쳐 방향을 정할 수 없을 때는 그림이 보는 쪽으로 떨어뜨린다.
    /// 0 벡터를 그대로 쓰면 중심이 안 밀리고 각도도 0이 되어 상자가 보스 발밑에 놓인다.
    /// <b>코앞의 플레이어를 오히려 못 때리는</b> 반대 상황이 된다.
    /// </summary>
    private Vector2 AimFrom(Vector2 toPlayer)
    {
        return toPlayer.sqrMagnitude > 0.0001f
            ? toPlayer.normalized
            : new Vector2(FacingSign(), 0f);
    }

    private IEnumerator Wave(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack;

        // 추가 생성 — 내려찍기와 같은 이유로 페이즈 시간을 한 쌍으로 먼저 구한다.
        float fireDelay = MotionTime(waveFireDelay);
        float motionSeconds = MotionTime(waveMotionSeconds);

        if (animator != null) animator.SetTrigger(WaveHash);

        yield return new WaitForSeconds(fireDelay);

        // 수정(조준 원점 어긋남) — <b>쏘는 자리에서 맞힐 자리로</b> 겨눈다.
        //
        // 예전에는 방향을 "보스 발밑 → 플레이어 발밑"으로 잡아놓고, 정작 투사체는 발밑이 아니라
        // waveSpawnHeight(1.6)만큼 위에서 내보냈다. 그러면 파도는 목표에 도달했을 때
        // <b>플레이어 발밑보다 1.6유닛 위</b>에 있다. 플레이어 캡슐이 0~1.25이고 파도 히트박스가
        // ±0.7이라, 옆에서 쏠 때 겹치는 구간이 0.9~1.25의 <b>0.35유닛</b>뿐이었다. 그것도
        // 캡슐의 둥근 꼭대기라서, 값 하나만 건드려도 조용히 안 맞게 되는 상태였다.
        //
        // 내려찍기(<see cref="Slam"/>)와 같은 계열의 실수다. 거기서는 판정 방향이 그림을 따라갔고
        // 여기서는 조준 원점이 발사 원점을 안 따라갔다.
        Vector2 spawn = (Vector2)transform.position + Vector2.up * waveSpawnHeight;
        Vector2 target = playerCollider != null
            ? (Vector2)playerCollider.bounds.center
            : (player != null ? (Vector2)player.position : (Vector2)transform.position + toPlayer);

        // 시전 시작이 아니라 여기서 방향을 다시 잡는다. 예비동작 동안 플레이어가 움직였으면
        // 그쪽으로 나가야 한다 — 안 그러면 제자리에서 옆으로 걸어 나가기만 해도 전부 피해진다.
        // (내려찍기는 반대로 시작 시점에 고정한다. 이유는 Slam 쪽 주석에 적어뒀다.)
        Vector2 toTarget = target - spawn;
        Vector2 aim = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : AimFrom(toPlayer);

        int count = waveCount + (isPhase2 ? 2 : 0);

        // 가운데를 기준으로 좌우 대칭이 되게 각도를 나눈다.
        float start = -waveSpreadDegrees * (count - 1) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, start + waveSpreadDegrees * i) * aim;

            var shot = Instantiate(wavePrefab, spawn, Quaternion.identity);
            shot.Launch(direction, waveDamage);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, motionSeconds - fireDelay));

        EndAttack();
    }

    /// <summary>
    /// 추가 생성 — 페이즈에 맞춘 동작 시간. 1페이즈는 그대로, 2페이즈는 클립 길이 비만큼 줄인다.
    ///
    /// 곱하는 자리를 한 함수로 모은 이유: 곱해야 할 곳이 네 군데(내려찍기 판정·모션,
    /// 파도 발사·모션)라, 각자 곱하게 두면 언젠가 한 곳을 빠뜨린다. 그리고 그 한 곳은
    /// <b>2페이즈에서 한 패턴만 어긋나는</b> 증상으로 나타나서, 보고도 재현 조건을 잡기 어렵다.
    ///
    /// 판정 시점까지 같이 줄이는 것이 핵심이다. 클립 전체가 0.8배면 검이 바닥에 닿는 프레임도
    /// 0.8배 지점이다. 모션만 줄이고 판정을 그대로 두면 2페이즈에서 판정이 모션의 훨씬 뒷부분에
    /// 걸려서, 예비동작을 읽고 피하라고 만든 시간이 사라진다.
    /// </summary>
    private float MotionTime(float seconds)
        => seconds * (isPhase2 ? phase2MotionScale : 1f);

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

    /// <summary>
    /// 인스펙터에서 사거리와 판정 범위를 눈으로 확인한다.
    ///
    /// 수정(8방향 판정): 상자를 조준 방향으로 돌려서 그린다. 예전에는 항상 오른쪽으로 그렸는데,
    /// 코드가 좌우만 보던 시절에는 그게 사실이었지만 지금은 <b>기즈모가 거짓말을 하게 된다.</b>
    /// 판정 범위를 맞추는 도구가 실제 판정과 다르면 없느니만 못하다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, slamRange);

        // 실행 중이면 지금 플레이어 쪽을, 편집 중이면(플레이어가 없다) 마지막 조준 방향을 그린다.
        // 편집 중 기본값은 오른쪽이라 예전 기즈모와 같은 그림이 나온다 — 눈으로 맞추던 기준이 안 바뀐다.
        Vector2 aim = Application.isPlaying && player != null
            ? AimFrom((Vector2)player.position - (Vector2)transform.position)
            : slamAim;

        float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
        Vector3 center = transform.position + (Vector3)(aim * (slamHitSize.x * 0.35f));

        // Gizmos.matrix로 좌표계를 통째로 돌린다. 귀퉁이 네 점을 직접 구해 선을 긋는 것보다 짧고,
        // 무엇보다 Physics2D.OverlapBox에 넘기는 각도와 같은 값을 쓰므로 둘이 어긋날 여지가 없다.
        Matrix4x4 saved = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, slamHitSize);

        Gizmos.matrix = saved;
    }
}
