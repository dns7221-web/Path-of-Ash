using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 이동 입력과 애니메이션 상태를 담당한다.
///
/// 이동을 Rigidbody2D로 처리하는 이유: transform.position을 직접 더하면 벽을 뚫는다.
/// 물리 엔진을 거쳐야 콜라이더가 이동을 막아주고, AshProjectSetup에서 짜둔 충돌 매트릭스
/// (Player x Wall)가 실제로 의미를 갖는다.
///
/// 입력을 .inputactions 에셋이 아니라 InputAction 필드로 둔 이유: 지금 액션이 이동 하나라
/// 에셋 + 자동생성 클래스는 과하다. [SerializeField]로 두면 에셋 없이도 인스펙터에서
/// 바인딩이 보이고 수정된다. 공격/대시가 붙어 액션이 늘어나면 그때 에셋으로 옮긴다.
///
/// gravityScale을 코드에서 건드리지 않는 이유: 2D 중력은 AshProjectSetup이 전역에서
/// (0,0)으로 꺼뒀다. gravityScale은 전역 중력에 곱해지는 값이라 전역이 0이면 여기가
/// 1이어도 안 떨어진다. 프리팹마다 하나씩 끄다 빠뜨리는 걸 막으려고 전역으로 정한 결정이라
/// 여기서 다시 손대면 그 결정이 흐려진다.
///
/// 추가 생성 — 애니메이션 작업 시점의 확장 요약:
/// 걷기/달리기/공격/대시/피격/사망이 붙으면서 "지금 무슨 동작 중인가"를 알아야 하게 됐다.
/// 그 상태를 <see cref="ActionState"/>로 코드가 소유하고, Animator에는 결과만 통보한다.
/// <b>Animator의 현재 상태를 코드가 되묻지 않는 것</b>이 이 설계의 핵심이다. Animator는
/// 전이 조건과 블렌딩 때문에 "지금 어느 상태인지"의 답이 한 프레임씩 늦고, 그 값으로 이동을
/// 막으면 공격 첫 프레임에 미끄러진다. 게임 규칙은 코드가, 보여줄 그림은 Animator가 정한다.
///
/// 위 주석의 "액션이 늘어나면 에셋으로 옮긴다"는 아직 실행하지 않았다. 액션이 4개까지는
/// 인스펙터에서 한눈에 보이고, .inputactions로 옮기면 키 바인딩이 이 파일 밖으로 나가서
/// "이 스크립트만 읽으면 조작을 다 안다"는 장점이 사라진다. 리바인딩 UI를 만들 때 옮긴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    // 추가 생성 — 지금 어떤 동작 중인지. 이동 가능 여부와 입력 수용 여부가 여기서 갈린다.
    private enum ActionState
    {
        Normal,    // 걷기/달리기. 입력을 모두 받는다
        Attacking, // 공격 모션 재생 중. 제자리에 선다
        Dashing,   // 대시 중. 입력과 무관하게 정해진 방향으로 밀려간다
        Hit,       // 피격 경직. 입력을 안 받는다
        Dead,      // 사망. 이후 아무 입력도 받지 않는다
    }

    [Header("이동")]
    // 수정(애니메이션 작업 시점): moveSpeed 하나였던 것을 걷기/달리기 둘로 나눴다.
    // 스태미나로 달리기를 제한하려면 "달릴 때만 빠른" 속도가 따로 있어야 의미가 생긴다.
    // 수정(Game 씬 규격 확인 시점): 3.5 / 6.5 → 8.7 / 16.
    //
    // 속도는 캐릭터 크기가 아니라 <b>방 크기</b>가 정하는 값이다. 캐릭터를 두 배로 키워도
    // 방을 가로지르는 데 걸려야 할 시간은 그대로다. 처음 값은 화면 가로 20유닛을 전제로
    // 잡았는데, 실제 Game 씬은 카메라 14 = 화면 가로 49.8유닛이었다. 배율 14/5.625 = 2.49를
    // 그대로 곱했다. 그래서 아래 세 속도가 전부 같은 비율로 커졌다.
    // 재수정(스케일 2 상태에서 체감 보정): 8.7 / 16 → 11 / 20.
    //
    // 방 크기만 놓고 계산하면 8.7 / 16이 맞다(횡단 5.7초 / 3.1초). 그런데 캐릭터를 스케일 2로
    // 키운 상태라 화면에서 차지하는 덩치가 두 배가 됐고, 그러면 같은 속도라도 "제 몸길이를
    // 초당 몇 번 지나가는가"가 절반이 되어 굼떠 보인다. 눈에 보이는 속도감은 방이 아니라
    // 캐릭터 크기 대비로 읽히기 때문에 1.25배 정도 얹었다.
    //
    // 나중에 PPU 재임포트가 정상적으로 먹어서 스케일을 1로 되돌리면 캐릭터가 다시 작아지므로
    // 이 값도 8.7 / 16으로 되돌리는 게 맞다.
    // 수정(달리기 삭제): runSpeed를 없애고 이동 속도를 하나로 합쳤다.
    //
    // 달리기와 대시가 둘 다 스태미나를 쓰니 자원 하나가 두 가지를 결정해서 과했다.
    // 달리기를 빼면 스태미나는 <b>회피 전용 자원</b>이 되어 역할이 선명해진다 —
    // "지금 대시를 쓸 것인가"만 묻게 된다.
    //
    // 속도는 걷기(11)와 달리기(20) 사이인 14로 올렸다. 달리기가 없어진 만큼 기본 이동이
    // 답답하면 안 되고, 그렇다고 20이면 회피가 필요 없어진다.
    [Tooltip("이동 속도(월드 유닛/초). 화면 가로가 49.8유닛이라 14면 약 3.6초에 횡단한다.")]
    [SerializeField] private float moveSpeed = 14f;

    // 추가 생성 — 대시
    [Header("대시")]
    // 수정(Game 씬 규격 확인 시점): 14 → 35. 걷기/달리기와 같은 2.49배였다.
    // 재수정(스케일 2 체감 보정): 35 → 44. 0.25초에 11유닛을 이동한다.
    // 대시는 달리기보다 확실히 빨라야 회피기로 읽히므로 달리기(20)의 2.2배를 유지했다.
    [Tooltip("대시 중 이동 속도(월드 유닛/초). 0.25초 동안 약 11유닛을 이동한다.")]
    [SerializeField] private float dashSpeed = 44f;

    [Tooltip("대시 지속 시간(초). 대시 클립이 4프레임 / 16fps = 0.25초라 그 값에 맞췄다. " +
             "이 값과 클립 길이가 어긋나면 모션이 끝났는데도 미끄러지거나 그 반대가 된다.")]
    [SerializeField] private float dashDuration = 0.25f;

    // 추가 생성 — 액션 지속 시간
    [Header("액션 지속 시간")]
    // 수정(스킬 시스템 도입): attackDuration과 attackCooldown을 여기서 뺐다.
    // 공격 모션 길이와 재사용 대기시간은 이제 스킬마다 다르므로 SkillData 에셋이 들고 있다.
    // 전투 리듬(공격 주기 약 1.08초 vs 적 회복 0.9초)에 대한 판단은 그대로 유효하고,
    // 그 숫자는 Q 스킬 에셋의 Cooldown Seconds에 들어간다.
    [Tooltip("피격 경직 시간(초). hit 클립 2프레임 / 10fps = 0.2초.")]
    [SerializeField] private float hitDuration = 0.2f;

    [Header("입력")]
    [Tooltip("이동 입력. 컴포넌트를 처음 붙일 때 WASD / 방향키 / 게임패드 스틱이 자동으로 채워진다.")]
    [SerializeField] private InputAction moveAction;



    [Tooltip("대시. 스태미나를 목돈으로 쓴다.")]
    [SerializeField] private InputAction dashAction;

    [Header("참조 (비어 있어도 동작한다)")]
    [Tooltip("사망 후 입력을 끊기 위해 상태를 읽는다. 비우면 항상 조작 가능한 상태로 본다.")]
    [SerializeField] private RunManager runManager;

    [Tooltip("idle/run 전환용. 비우면 애니메이션 없이 이동만 한다.")]
    [SerializeField] private Animator animator;

    [Tooltip("좌우 반전용. 비우면 반전하지 않는다.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    // 추가 생성 — 스태미나
    [Tooltip("달리기/대시 비용을 낸다. 비우면 스태미나 제한 없이 무한히 달린다.")]
    [SerializeField] private PlayerStamina stamina;

    // 추가 생성 — 공용 체력과 공격 판정
    [Tooltip("플레이어 체력. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private Health health;

    // 수정(스킬 시스템 도입): 히트박스를 켜고 끄는 건 SkillController가 한다.
    // 여기 참조가 남아 있는 이유는 두 가지뿐이다 — 좌우 반전 시 히트박스도 같이 뒤집어야
    // 하고, 피격·사망으로 시전이 끊길 때 판정을 확실히 꺼야 한다.
    [Tooltip("검 히트박스. 좌우 반전과 강제 해제에만 쓴다. 실제 켜고 끄기는 SkillController가 한다.")]
    [SerializeField] private DamageHitbox attackHitbox;

    private Rigidbody2D rb;

    // 이번 프레임의 이동 입력. Update에서 읽고 FixedUpdate에서 쓴다.
    private Vector2 moveInput;

    // 추가 생성 — 현재 동작 상태와 그에 딸린 값들
    private ActionState actionState = ActionState.Normal;

    // 이번 프레임에 실제로 달리는 중인가. 입력만으로 정하지 않고 스태미나까지 본 결과다.

    // 대시가 시작될 때 고정된 방향. 대시 중에는 입력을 무시하므로 시작 시점의 방향을 들고 있어야 한다.
    private Vector2 dashDirection = Vector2.right;

    // 마지막으로 바라본 방향이 오른쪽인가. 입력이 없을 때 대시 방향을 정하는 데 쓴다.
    private bool facingRight = true;


    // 애니메이터 파라미터 이름을 매 프레임 문자열로 넘기면 내부에서 해시를 다시 계산한다.
    // 미리 해시로 만들어두면 그 비용과 문자열 할당이 사라진다.
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // 추가 생성 — 나머지 파라미터도 같은 이유로 해시를 미리 떠둔다.
    // 이름 문자열은 AshPlayerAnimationBuilder의 상수와 같아야 한다. 한쪽만 바꾸면 컴파일은
    // 되지만 애니메이션이 조용히 안 바뀌므로, 이름을 고칠 일이 생기면 양쪽을 같이 고쳐야 한다.
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DashHash = Animator.StringToHash("Dash");
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private static readonly int DieHash = Animator.StringToHash("Die");

    /// <summary>
    /// 추가 생성 — 지금 무적인가. 대시 중에는 공격을 통과한다.
    /// 아직 데미지 시스템이 없어서 읽는 쪽이 없지만, 무적 판정의 주인이 누구인지를 여기로
    /// 못 박아두려고 먼저 노출한다. 나중에 피격 판정이 이 값을 보고 데미지를 무시한다.
    /// </summary>
    public bool IsInvincible => actionState == ActionState.Dashing;

    /// <summary>추가 생성 — 죽었는가. 적 AI가 추격을 멈출 때 읽을 값이다.</summary>
    public bool IsDead => actionState == ActionState.Dead;

    /// <summary>추가 생성 — 오른쪽을 보고 있는가. SkillController가 시전 방향으로 쓴다.</summary>
    public bool FacingRight => facingRight;

    /// <summary>
    /// 추가 생성 — 유물로 얻은 이동 속도 보정. moveSpeed에 더해진다.
    ///
    /// moveSpeed를 직접 올리지 않고 따로 둔 이유: moveSpeed는 인스펙터에서 손으로 맞춘
    /// 기본값이라, 유물이 그걸 덮어쓰면 <b>원래 값이 뭐였는지 알 수 없게 된다.</b>
    /// 나중에 "속도가 왜 이렇지"를 볼 때 기본값과 보정치가 나뉘어 있어야 셈이 보인다.
    /// </summary>
    public float BonusMoveSpeed { get; set; }

    /// <summary>
    /// 추가 생성 — 스킬 시전 모션을 시작한다. 시전할 수 있는 상태였으면 true.
    ///
    /// <b>이 함수가 PlayerController와 SkillController의 경계다.</b>
    /// 여기서는 "지금 움직일 수 있는 상태인가"만 판단하고 이동을 잠근 뒤 모션을 재생한다.
    /// 무슨 스킬인지, 데미지가 얼마인지, 쿨다운이 얼마인지는 알지 못한다.
    /// 그래서 스킬이 넷으로 늘어나도 이 파일은 바뀌지 않는다.
    ///
    /// 수정: 트리거 이름을 인자로 받는다. 처음에는 Attack 하나를 네 스킬이 공유했는데,
    /// 기본 공격·내려찍기·활이 전부 다른 시트로 그려지면서 모션이 달라졌다. 어느 트리거를
    /// 켤지는 스킬 에셋이 알고 있으므로 여기서는 받아서 켜기만 한다.
    /// </summary>
    public bool TryBeginSkillMotion(float motionSeconds, string animatorTrigger)
    {
        if (actionState != ActionState.Normal) return false;

        StartCoroutine(SkillMotionRoutine(motionSeconds, animatorTrigger));
        return true;
    }

    /// <summary>
    /// 컴포넌트를 처음 붙였을 때 참조와 기본 키 바인딩을 자동으로 채운다(에디터 전용 콜백).
    ///
    /// 바인딩을 여기서 만드는 이유: Reset은 컴포넌트를 추가할 때 딱 한 번만 불린다.
    /// Awake에서 만들면 인스펙터에서 키를 바꿔놔도 실행할 때마다 덮어써진다.
    /// </summary>
    private void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        runManager = FindFirstObjectByType<RunManager>();

        // 추가 생성
        stamina = GetComponent<PlayerStamina>();

        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

        // 2DVector 컴포지트는 키 네 개를 Vector2 하나로 묶어주는 유니티 내장 바인딩이다.
        // 키를 하나씩 읽어서 직접 벡터를 조립하지 않는 이유가 이거다.
        // 수정(QWER 스킬 확정): WASD 바인딩을 뺐다.
        //
        // 스킬을 Q/W/E/R에 두기로 하면서 W가 "위로 이동"과 정면으로 겹쳤다. 둘 다 남기면
        // 위로 걸을 때마다 스킬이 나간다. 스킬 배치는 기획이 정한 것이고 이동은 방향키로도
        // 충분하므로 이동을 옮겼다.
        //
        // 되돌리려면 여기에 WASD 컴포지트를 다시 넣고 스킬 키를 1/2/3/4로 옮기면 된다.
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        // 게임패드는 스틱 하나가 이미 Vector2라 컴포지트가 필요 없다.
        moveAction.AddBinding("<Gamepad>/leftStick");

        // 대시는 눌린 순간에만 반응하면 되므로 Button이다.
        // 수정(달리기 삭제): 비워진 Shift로 대시를 옮겼다. Space보다 방향키를 쥔 왼손에서
        // 누르기 쉬워서, 이동하다 즉시 회피하는 동작이 자연스럽다.
        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftShift");
        dashAction.AddBinding("<Keyboard>/rightShift");
        dashAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 인스펙터에서 비워둔 채로 넣었을 경우를 대비한다. 없으면 없는 대로 동작한다.
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // 추가 생성 — 스태미나도 같은 규칙. 없으면 무제한으로 달리는 상태가 된다.
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (health == null) health = GetComponent<Health>();

        // 추가 생성 — RunManager는 씬에 있는 오브젝트라 프리팹에 미리 연결해둘 수가 없다
        // (프리팹은 씬 오브젝트를 참조하지 못한다). Reset에서 채운 값도 프리팹으로 저장되는
        // 순간 비워지므로, 프리팹으로 배치했을 때를 대비해 여기서 한 번 더 찾는다.
        // 매 프레임 찾는 게 아니라 시작할 때 한 번이라 비용은 무시할 수 있다.
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();
    }

    /// <summary>InputAction은 켜야 값을 읽을 수 있다. 오브젝트가 꺼지면 같이 꺼져야 한다.</summary>
    private void OnEnable()
    {
        moveAction?.Enable();

        // 추가 생성
        dashAction?.Enable();

        // 추가 생성 — Health가 게임 규칙을 소유하고 컨트롤러는 연출에만 반응한다.
        if (health != null)
        {
            health.Damaged += OnHealthDamaged;
            health.Died += Die;
        }
    }

    private void OnDisable()
    {
        moveAction?.Disable();

        // 추가 생성
        dashAction?.Disable();
        attackHitbox?.Deactivate();

        if (health != null)
        {
            health.Damaged -= OnHealthDamaged;
            health.Died -= Die;
        }
    }

    /// <summary>코드로 만든 InputAction은 내부 리소스를 잡고 있어 직접 해제해야 한다.</summary>
    private void OnDestroy()
    {
        moveAction?.Dispose();

        // 추가 생성
        dashAction?.Dispose();
    }

    private void Update()
    {
        // 추가 생성 — RunManager가 먼저 판을 끝낸 경우(디버그 사망 키 등)를 따라잡는다.
        // 사망 진입점을 Die() 하나로 두되, 외부에서 EndRun이 먼저 불린 경로도 여기서 흡수한다.
        if (actionState != ActionState.Dead &&
            runManager != null && runManager.State != RunManager.RunState.Playing)
        {
            Die();
        }

        // 죽은 뒤에는 입력을 받지 않는다. RunManager를 안 연결했으면 항상 조작 가능으로 본다
        // (ResultScreen과 같은 규칙 — 참조가 비어도 흐름은 확인할 수 있어야 한다).
        //
        // 수정(애니메이션 작업 시점): 공격/대시/피격 중에도 이동 입력을 끊어야 해서
        // 조건에 actionState를 더했다.
        bool canMove = actionState == ActionState.Normal &&
                       (runManager == null || runManager.State == RunManager.RunState.Playing);

        // normalized가 아니라 ClampMagnitude를 쓰는 이유:
        // normalized는 길이를 무조건 1로 만들어서 게임패드를 살짝 기울여도 전력질주가 된다.
        // ClampMagnitude는 1을 넘을 때만 깎으므로, 키보드 대각선(길이 1.41)은 1로 줄이면서
        // 스틱의 아날로그 세기는 그대로 살린다.
        moveInput = canMove
            ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f)
            : Vector2.zero;

        // 추가 생성 — 바라보는 방향을 먼저 갱신한다. 대시 방향이 이 값을 쓰므로
        // 아래 액션 입력 처리보다 앞에 있어야 한다.
        if (Mathf.Abs(moveInput.x) > 0.01f)
            facingRight = moveInput.x > 0f;


        // 추가 생성
        HandleActionInput();

        UpdateVisuals();

        // 추가 생성 — 대시 무적의 최종 판정은 Health 한 곳에서 한다.
        if (health != null) health.IsInvulnerableExternally = IsInvincible;
    }

    /// <summary>
    /// 실제 이동. 물리는 FixedUpdate에서 다뤄야 한다 — Update에서 속도를 넣으면
    /// 프레임률에 따라 물리 스텝당 적용 횟수가 달라져서 이동 거리가 기기마다 달라진다.
    /// </summary>
    private void FixedUpdate()
    {
        // AddForce가 아니라 속도를 직접 대입하는 이유: 로그라이크 슬래셔는 조작이 즉각적으로
        // 붙어야 한다. 힘으로 밀면 가속과 관성이 생겨서 입력을 놓아도 미끄러진다.
        //
        // 수정(애니메이션 작업 시점): 상태에 따라 대입할 속도가 달라져서 분기를 넣었다.
        switch (actionState)
        {
            case ActionState.Normal:
                rb.linearVelocity = moveInput * (moveSpeed + BonusMoveSpeed);
                break;

            case ActionState.Dashing:
                // 대시 중에는 입력을 무시하고 시작할 때 정한 방향으로만 간다.
                // 대시 도중 방향을 꺾을 수 있으면 회피기가 아니라 그냥 빠른 이동이 된다.
                rb.linearVelocity = dashDirection * dashSpeed;
                break;

            default:
                // 공격/피격/사망 중에는 제자리에 선다. 속도를 안 지우면 직전 이동 속도가
                // 그대로 남아서 공격하는 내내 미끄러진다.
                rb.linearVelocity = Vector2.zero;
                break;
        }
    }

    // 수정(달리기 삭제): UpdateRunState를 지웠다. 이동 속도가 하나뿐이라 "지금 달리는가"를
    // 판정할 이유가 없어졌고, 스태미나는 대시에서만 소모된다.

    /// <summary>추가 생성 — 공격/대시 입력을 받아 해당 코루틴을 시작한다.</summary>
    private void HandleActionInput()
    {
        // Normal이 아닐 때는 새 액션을 받지 않는다. 공격 중 공격, 대시 중 대시를 막는다.
        if (actionState != ActionState.Normal) return;

        // 대시를 공격보다 먼저 보는 이유: 같은 프레임에 둘 다 눌렸다면 회피가 우선이어야
        // 플레이어가 손해를 안 본다.
        if (dashAction.WasPressedThisFrame() && (stamina == null || stamina.TryConsumeDash()))
        {
            StartCoroutine(DashRoutine());
            return;
        }

        // 수정(스킬 시스템 도입): 공격 입력은 SkillController가 받는다.
        // 여기 남은 건 대시뿐이라 위 분기에서 이미 처리가 끝났다.
    }

    /// <summary>
    /// 추가 생성 — 공격. 정해진 시간 동안 제자리에 서고 끝나면 Normal로 돌아온다.
    ///
    /// 코루틴을 쓴 이유: "트리거 쏘고 → 잠시 기다렸다가 → 상태 되돌리기"는 유니티가 코루틴으로
    /// 쓰라고 만들어둔 형태다. 직접 타이머 변수를 두고 Update에서 빼면 상태마다 변수가 하나씩
    /// 늘어나고, 그 변수를 초기화하는 걸 빠뜨리는 실수가 생긴다.
    /// </summary>
    /// <summary>
    /// 수정(스킬 시스템 도입): AttackRoutine을 대체한다.
    ///
    /// 예전에는 이 코루틴이 히트박스를 켜고 끄는 것까지 했는데, 그건 "무엇을 하는 스킬인가"에
    /// 딸린 일이라 SkillData로 옮겼다. 여기 남은 건 이동 잠금과 모션 재생뿐이다.
    /// 히트박스 타이밍과 스킬 실행은 SkillController가 병렬로 돌린다.
    /// </summary>
    private IEnumerator SkillMotionRoutine(float motionSeconds, string animatorTrigger)
    {
        actionState = ActionState.Attacking;

        // 이름이 비어 있으면 기본 공격 트리거로 물러선다. 에셋을 새로 만들고 트리거 이름을
        // 안 적었을 때 아무 모션도 안 나오는 것보다, 뭐라도 나오는 편이 원인을 찾기 쉽다.
        animator?.SetTrigger(string.IsNullOrEmpty(animatorTrigger) ? "Attack" : animatorTrigger);

        yield return new WaitForSeconds(motionSeconds);

        // 대기하는 동안 죽었을 수 있다. 그 경우 Normal로 되돌리면 시체가 다시 움직인다.
        if (actionState == ActionState.Attacking)
            actionState = ActionState.Normal;
    }

    /// <summary>추가 생성 — 대시. 시작 시점의 방향으로 정해진 시간 동안 밀려간다.</summary>
    private IEnumerator DashRoutine()
    {
        // 입력이 있으면 그 방향, 없으면 바라보던 방향으로 나간다.
        // 제자리 대시가 아무 데도 안 가면 회피기로 못 쓴다.
        dashDirection = moveInput.sqrMagnitude > 0.01f
            ? moveInput.normalized
            : (facingRight ? Vector2.right : Vector2.left);

        actionState = ActionState.Dashing;
        animator?.SetTrigger(DashHash);

        yield return new WaitForSeconds(dashDuration);

        if (actionState == ActionState.Dashing)
            actionState = ActionState.Normal;
    }

    /// <summary>
    /// 추가 생성 — 피격 경직. 데미지 시스템이 생기면 여기를 호출한다.
    ///
    /// 데미지 계산(체력 감소)을 여기 넣지 않은 이유: 체력은 플레이어만 갖는 값이 아니라
    /// 적도 갖는다. 나중에 Health 컴포넌트를 따로 만들어 양쪽이 공유하고, 이 함수는 그
    /// 컴포넌트가 "맞았다"고 알려줄 때 반응하는 연출 쪽만 맡는다.
    /// </summary>
    public void TakeHitReaction()
    {
        // 죽었거나 대시 무적 중이면 경직에 걸리지 않는다.
        if (actionState == ActionState.Dead || IsInvincible) return;

        // 이전 액션 코루틴이 돌고 있을 수 있으므로 전부 끊는다. 안 끊으면 공격 코루틴이
        // 나중에 깨어나서 경직 중인 상태를 Normal로 되돌려버린다.
        StopAllCoroutines();
        // 추가 생성 — 공격 도중 피격되면 코루틴과 함께 꺼지지 못한 판정도 즉시 정리한다.
        attackHitbox?.Deactivate();
        StartCoroutine(HitRoutine());
    }

    /// <summary>추가 생성 — 실제 데미지가 들어간 경우에만 피격 모션을 시작한다.</summary>
    private void OnHealthDamaged(int current, int max)
    {
        if (current > 0) TakeHitReaction();
    }

    private IEnumerator HitRoutine()
    {
        actionState = ActionState.Hit;
        animator?.SetTrigger(HitHash);

        yield return new WaitForSeconds(hitDuration);

        if (actionState == ActionState.Hit)
            actionState = ActionState.Normal;
    }

    /// <summary>
    /// 추가 생성 — 사망. 입력을 끊고 사망 모션을 재생한 뒤 그 상태로 멈춘다.
    ///
    /// 여러 번 불려도 안전하다. RunManager.EndRun도 중복 호출을 막고 있어서, 데미지 여러 개가
    /// 같은 프레임에 들어와도 결과가 같다.
    /// </summary>
    public void Die()
    {
        if (actionState == ActionState.Dead) return;

        // 진행 중이던 공격/대시 코루틴을 끊는다. 안 끊으면 사망 모션 중에 코루틴이 깨어나
        // 상태를 Normal로 되돌리고, 죽은 캐릭터가 다시 걷는다.
        StopAllCoroutines();
        attackHitbox?.Deactivate();

        actionState = ActionState.Dead;
        moveInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;

        animator?.SetTrigger(DieHash);

        // 판을 끝낸다. 이미 끝나 있으면 RunManager가 알아서 무시한다.
        runManager?.EndRun(false);
    }

    /// <summary>애니메이션 파라미터와 좌우 반전을 갱신한다.</summary>
    private void UpdateVisuals()
    {
        // 입력이 아니라 실제 속도를 넘기는 이유: 나중에 넉백이나 대시로 몸이 밀릴 때도
        // 달리는 애니메이션이 나와야 한다. 입력을 기준으로 하면 밀려나는 동안 가만히 서 있다.
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, rb.linearVelocity.magnitude);

            // 추가 생성 — 걷기/달리기 전환용. 상태 머신이 이 값으로 Walk와 Run을 오간다.
        }

        // 입력이 0에 가까울 때는 반전하지 않는다. 그래야 멈춘 순간 마지막으로 보던 방향이
        // 유지된다. 0.01은 스틱이 중앙에서 미세하게 떨리는 걸 무시하기 위한 값이다.
        //
        // 수정(애니메이션 작업 시점): moveInput 대신 facingRight를 본다. 대시나 공격 중에는
        // moveInput이 0이라 예전 코드로는 반전이 멈추는데, facingRight는 마지막 방향을
        // 계속 들고 있어서 대시 중에도 향한 방향이 유지된다.
        if (spriteRenderer != null)
            spriteRenderer.flipX = !facingRight;

        // 추가 생성 — 그림만 반전하면 공격 판정은 계속 오른쪽에 남는다.
        // 히트박스 자식의 X축을 함께 뒤집어 Collider2D 오프셋까지 진행 방향에 맞춘다.
        if (attackHitbox != null)
        {
            Vector3 hitboxScale = attackHitbox.transform.localScale;
            hitboxScale.x = Mathf.Abs(hitboxScale.x) * (facingRight ? 1f : -1f);
            attackHitbox.transform.localScale = hitboxScale;
        }
    }
}
