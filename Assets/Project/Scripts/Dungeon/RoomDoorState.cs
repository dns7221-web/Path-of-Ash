using UnityEngine;

/// <summary>
/// 방 배경을 문 상태에 따라 갈아끼운다.
///
/// 문을 별도 오브젝트로 만들지 않은 이유: 방 배경 그림에 문이 이미 그려져 들어가 있다.
/// 문만 따로 소품으로 덮으려면 배경 위 정확한 픽셀 위치에 맞춰야 하는데, 배경을 다시 뽑을
/// 때마다 그 정렬이 깨진다. 세 장 모두 1678x937로 크기가 같으므로 통째로 교체하면
/// 정렬 문제가 아예 존재하지 않는다. 문 콜라이더도, 문 애니메이터도 필요 없어진다.
///
/// 이 컴포넌트는 상태를 <b>판단하지 않는다</b>. "언제 열리는가"는 전투 쪽(적을 다 잡았는가,
/// 보스가 등장했는가)이 아는 정보이고, 여기서 그걸 추측하면 나중에 전투 규칙이 바뀔 때
/// 두 군데를 고쳐야 한다. 여기서는 시키는 대로 그림만 바꾼다.
///
/// 방이 여러 종류로 늘어나면 이 방식은 텍스처를 방 개수 x 3장 들고 있어야 해서 비싸진다.
/// 그 시점에는 바닥/벽 타일셋으로 방을 조립하고 문만 소품으로 얹는 구조로 가야 한다.
/// 지금은 방이 하나뿐인 아레나라 이쪽이 훨씬 싸다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class RoomDoorState : MonoBehaviour
{
    /// <summary>문 상태. 배경 그림 한 장과 1:1로 대응한다.</summary>
    public enum DoorState
    {
        Closed, // 전투 중. 나갈 수 없다
        Open,   // 방을 클리어했다. 다음 방으로 갈 수 있다
        Broken, // 보스가 문을 부수고 등장했다
    }

    [Header("배경 그림 (세 장 모두 같은 크기여야 한다)")]
    [Tooltip("문이 닫힌 기본 상태.")]
    [SerializeField] private Sprite closedRoom;

    [Tooltip("문이 열린 상태. 방 클리어 시.")]
    [SerializeField] private Sprite openRoom;

    [Tooltip("문이 부서진 상태. 보스 등장 시.")]
    [SerializeField] private Sprite brokenRoom;

    [Header("디버그 — 전투가 붙기 전까지 눈으로 확인하는 용도")]
    // 수정(전투 연결 시점): 기본값을 true → false로 바꿨다.
    // 수정(상자 보상 진행 도입): 이제 RoomController가 "상자를 열면 문을 연다"를 담당한다.
    // 디버그 키를 켜둔 채로 두면 사람이 누른 상태와 방 진행 상태가 서로 덮어써서,
    // 문이 왜 그 상태인지 알 수 없어진다.
    [Tooltip("켜두면 숫자키 1/2/3으로 상태를 강제 전환한다. 전투를 거치지 않고 그림만 " +
             "확인할 때만 켠다 — 스포너의 문 제어와 충돌한다.")]
    [SerializeField] private bool enableDebugKeys = false;

    private SpriteRenderer spriteRenderer;

    /// <summary>현재 문 상태. 다른 시스템이 "지금 나갈 수 있는가"를 판단할 때 읽는다.</summary>
    public DoorState State { get; private set; } = DoorState.Closed;

    /// <summary>지금 방을 나갈 수 있는가. 부서진 문도 통로로는 열려 있다.</summary>
    public bool IsPassable => State != DoorState.Closed;

#if UNITY_EDITOR
    /// <summary>
    /// 컴포넌트를 처음 붙일 때 배경 세 장을 자동으로 채운다(에디터 전용).
    ///
    /// 경로를 코드에 적어둔 이유: 인스펙터에서 손으로 세 칸을 채우면 순서를 한 번은 바꿔 넣고,
    /// 바꿔 넣으면 "클리어했는데 문이 부서진 그림이 나오는" 상태가 된다. 에러도 안 난다.
    /// </summary>
    private void Reset()
    {
        const string folder = "Assets/Project/Art/Sprites/Dungeon/";

        closedRoom = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(folder + "Room_raw.png");
        openRoom = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(folder + "Room_DoorOpen.png");
        brokenRoom = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(folder + "Room_DoorBroken.png");
    }
#endif

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        // Awake가 아니라 Start에서 적용하는 이유: 다른 컴포넌트가 Awake에서 이 방의 상태를
        // 미리 바꿔놓았을 수 있다. Start 시점이면 그 값이 이미 들어와 있으므로 덮어쓰지 않는다.
        Apply();
    }

    private void Update()
    {
        if (!enableDebugKeys) return;

        // 적도 전투도 아직 없어서 상태를 바꿀 방법이 이것뿐이다.
        // RunManager가 디버그 사망 키를 둔 것과 같은 이유다.
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame) SetState(DoorState.Closed);
        else if (keyboard.digit2Key.wasPressedThisFrame) SetState(DoorState.Open);
        else if (keyboard.digit3Key.wasPressedThisFrame) SetState(DoorState.Broken);
    }

    /// <summary>
    /// 문 상태를 바꾼다. 같은 상태로 다시 불러도 안전하다.
    ///
    /// 방 진행 코드에서 부를 진입점이다. 예: 보상 상자를 열면 SetState(Open),
    /// 보스가 등장하면 SetState(Broken).
    /// </summary>
    public void SetState(DoorState state)
    {
        if (State == state) return;

        State = state;
        Apply();
    }

    /// <summary>현재 상태에 해당하는 그림을 렌더러에 넣는다.</summary>
    private void Apply()
    {
        if (spriteRenderer == null) return;

        Sprite wanted = State switch
        {
            DoorState.Open => openRoom,
            DoorState.Broken => brokenRoom,
            _ => closedRoom,
        };

        // 그림이 비어 있으면 바꾸지 않는다. null을 넣으면 방이 통째로 사라져서
        // "왜 화면이 검은가"를 한참 찾게 된다.
        if (wanted == null)
        {
            Debug.LogWarning($"[방 문 상태] {State}에 해당하는 배경이 비어 있다. 그림을 유지한다.", this);
            return;
        }

        spriteRenderer.sprite = wanted;
    }
}
