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
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("이동")]
    [Tooltip("초당 이동 거리(월드 유닛). 화면 가로가 20유닛이라 5면 4초에 횡단한다.")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("입력")]
    [Tooltip("이동 입력. 컴포넌트를 처음 붙일 때 WASD / 방향키 / 게임패드 스틱이 자동으로 채워진다.")]
    [SerializeField] private InputAction moveAction;

    [Header("참조 (비어 있어도 동작한다)")]
    [Tooltip("사망 후 입력을 끊기 위해 상태를 읽는다. 비우면 항상 조작 가능한 상태로 본다.")]
    [SerializeField] private RunManager runManager;

    [Tooltip("idle/run 전환용. 비우면 애니메이션 없이 이동만 한다.")]
    [SerializeField] private Animator animator;

    [Tooltip("좌우 반전용. 비우면 반전하지 않는다.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;

    // 이번 프레임의 이동 입력. Update에서 읽고 FixedUpdate에서 쓴다.
    private Vector2 moveInput;

    // 애니메이터 파라미터 이름을 매 프레임 문자열로 넘기면 내부에서 해시를 다시 계산한다.
    // 미리 해시로 만들어두면 그 비용과 문자열 할당이 사라진다.
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

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

        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

        // 2DVector 컴포지트는 키 네 개를 Vector2 하나로 묶어주는 유니티 내장 바인딩이다.
        // 키를 하나씩 읽어서 직접 벡터를 조립하지 않는 이유가 이거다.
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        // 게임패드는 스틱 하나가 이미 Vector2라 컴포지트가 필요 없다.
        moveAction.AddBinding("<Gamepad>/leftStick");
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 인스펙터에서 비워둔 채로 넣었을 경우를 대비한다. 없으면 없는 대로 동작한다.
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>InputAction은 켜야 값을 읽을 수 있다. 오브젝트가 꺼지면 같이 꺼져야 한다.</summary>
    private void OnEnable()
    {
        moveAction?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
    }

    /// <summary>코드로 만든 InputAction은 내부 리소스를 잡고 있어 직접 해제해야 한다.</summary>
    private void OnDestroy()
    {
        moveAction?.Dispose();
    }

    private void Update()
    {
        // 죽은 뒤에는 입력을 받지 않는다. RunManager를 안 연결했으면 항상 조작 가능으로 본다
        // (ResultScreen과 같은 규칙 — 참조가 비어도 흐름은 확인할 수 있어야 한다).
        bool canMove = runManager == null || runManager.State == RunManager.RunState.Playing;

        // normalized가 아니라 ClampMagnitude를 쓰는 이유:
        // normalized는 길이를 무조건 1로 만들어서 게임패드를 살짝 기울여도 전력질주가 된다.
        // ClampMagnitude는 1을 넘을 때만 깎으므로, 키보드 대각선(길이 1.41)은 1로 줄이면서
        // 스틱의 아날로그 세기는 그대로 살린다.
        moveInput = canMove
            ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f)
            : Vector2.zero;

        UpdateVisuals();
    }

    /// <summary>
    /// 실제 이동. 물리는 FixedUpdate에서 다뤄야 한다 — Update에서 속도를 넣으면
    /// 프레임률에 따라 물리 스텝당 적용 횟수가 달라져서 이동 거리가 기기마다 달라진다.
    /// </summary>
    private void FixedUpdate()
    {
        // AddForce가 아니라 속도를 직접 대입하는 이유: 로그라이크 슬래셔는 조작이 즉각적으로
        // 붙어야 한다. 힘으로 밀면 가속과 관성이 생겨서 입력을 놓아도 미끄러진다.
        rb.linearVelocity = moveInput * moveSpeed;
    }

    /// <summary>애니메이션 파라미터와 좌우 반전을 갱신한다.</summary>
    private void UpdateVisuals()
    {
        // 입력이 아니라 실제 속도를 넘기는 이유: 나중에 넉백이나 대시로 몸이 밀릴 때도
        // 달리는 애니메이션이 나와야 한다. 입력을 기준으로 하면 밀려나는 동안 가만히 서 있다.
        if (animator != null)
            animator.SetFloat(SpeedHash, rb.linearVelocity.magnitude);

        // 입력이 0에 가까울 때는 반전하지 않는다. 그래야 멈춘 순간 마지막으로 보던 방향이
        // 유지된다. 0.01은 스틱이 중앙에서 미세하게 떨리는 걸 무시하기 위한 값이다.
        if (spriteRenderer != null && Mathf.Abs(moveInput.x) > 0.01f)
            spriteRenderer.flipX = moveInput.x < 0f;
    }
}