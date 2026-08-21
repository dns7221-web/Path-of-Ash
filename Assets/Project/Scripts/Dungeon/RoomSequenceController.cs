using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — Game 씬 안에 등록된 방들을 순서대로 무한 반복한다.
/// 씬 전환 없이 방 루트 활성화만 바꾸며, Result 이동은 플레이어 사망 흐름이 담당한다.
///
/// 수정(보스 방 추가): 일반 방 배열과 별개로 보스 방을 하나 두고 <see cref="bossRoomInterval"/>번째
/// 방마다 그 자리에 끼워 넣는다.
///
/// 보스 방을 <see cref="rooms"/> 배열에 그냥 한 칸으로 넣지 않은 이유:
/// 배열은 "일반 방 순환"이라는 하나의 뜻만 가져야 등장 주기를 숫자 하나로 조절할 수 있다.
/// 배열에 섞어 넣으면 주기를 바꿀 때마다 배열을 다시 짜야 하고, 방을 추가하면 보스 등장
/// 간격이 같이 밀린다.
/// </summary>
[DisallowMultipleComponent]
public class RoomSequenceController : MonoBehaviour
{
    [Header("반복할 방 순서")]
    [Tooltip("마지막 방을 나가면 첫 번째 방부터 다시 시작한다.")]
    [SerializeField] private RoomController[] rooms;
    [SerializeField] private PlayerController player;

    // 추가 생성 — 들어간 방 수를 판 기록에 남기기 위한 연결. 비어 있으면 씬에서 찾는다.
    [Tooltip("방 진행도를 기록할 RunManager. 비우면 씬에서 찾는다.")]
    [SerializeField] private RunManager runManager;

    [Header("튜토리얼 방")]
    // 추가 생성 — 판을 시작하면 던전이 아니라 여기부터 들어간다.
    //
    // 별도 씬으로 만들지 않은 이유: 튜토리얼은 <b>진짜 플레이어와 진짜 HUD</b>로 가르쳐야
    // 의미가 있다. 씬을 나누면 플레이어·체력바·스태미나·스킬바를 전부 복제해야 하고,
    // 복제본이 원본과 조금이라도 달라지면 "튜토리얼에서 배운 게 본편에서 안 통하는"
    // 가장 나쁜 상황이 된다. 방으로 두면 그 위험이 구조적으로 없다.
    [Tooltip("판 시작 시 한 번만 들어가는 튜토리얼 방. 비우면 바로 던전부터 시작한다.")]
    [SerializeField] private RoomController tutorialRoom;

    [Header("보스 방")]
    // 추가 생성 — 일반 방 순환과 따로 관리하는 보스 방. 비워두면 보스가 안 나온다.
    [Tooltip("보스 방. 비우면 일반 방만 반복한다.")]
    [SerializeField] private RoomController bossRoom;

    [Tooltip("몇 번째 방마다 보스 방을 넣을지. 3이면 3, 6, 9번째 방이 보스 방이다.")]
    [SerializeField, Min(1)] private int bossRoomInterval = 3;

    // 추가 생성 — 보스 방 문을 나가는 것이 이 게임의 승리 조건이다.
    //
    // 끌 수 있게 둔 이유: 이걸 켜면 판 길이가 bossRoomInterval에 그대로 묶인다(3이면 3방에서 끝).
    // 보스를 중간 관문으로 쓰고 무한 런을 유지하고 싶어지면 여기만 끄면 된다.
    [Tooltip("켜면 보스 방 문을 나갈 때 판이 클리어로 끝난다. 끄면 다음 방으로 계속 이어진다.")]
    [SerializeField] private bool bossClearEndsRun = true;

    [Header("테스트")]
    // 추가 생성 — 보스 패턴을 확인할 때 일반 방을 매번 클리어하고 오는 게 너무 느려서 넣었다.
    // 켜면 판 시작과 동시에 보스 방부터 들어간다. 빌드 전에 반드시 끈다.
    [Tooltip("켜면 판 시작 즉시 보스 방부터 시작한다. 보스 패턴 확인용.")]
    [SerializeField] private bool debugStartAtBossRoom;

    // 추가 생성 — 판 도중 아무 때나 보스 방으로 건너뛴다.
    //
    // 시작 토글과 따로 둔 이유: 토글은 껐다 켜려면 플레이를 멈추고 인스펙터를 만져야 한다.
    // 보스 패턴은 "유물을 몇 개 먹은 상태에서 어떤가", "재 게이지가 찬 상태면 어떤가"처럼
    // 판을 진행한 뒤에 확인해야 하는 것도 있어서, 실행 중에 누를 수단이 따로 필요하다.
    [Tooltip("켜두면 아래 키로 언제든 보스 방으로 건너뛴다. 빌드 전에 반드시 끈다.")]
    [SerializeField] private bool enableDebugKeys;

    [Tooltip("보스 방으로 건너뛸 키. RunManager의 사망 키(K), 상자 키(F), 문 디버그 키(1/2/3)와 겹치지 않게 고른다.")]
    [SerializeField] private Key debugBossRoomKey = Key.B;

    private int currentRoomIndex = -1;
    private int enteredRoomCount;

    // 추가 생성 — 지금 열려 있는 방. 보스 방은 rooms 배열 밖에 있어서 인덱스만으로는 못 가리킨다.
    private RoomController currentRoom;

    /// <summary>이번 런에서 반복 입장을 포함해 들어간 총 방 수.</summary>
    public int EnteredRoomCount => enteredRoomCount;

    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();

        // 추가 생성 — EnemySpawner가 runManager를 찾는 방식과 같다. 씬 오브젝트라
        // 프리팹에 미리 연결해둘 수 없어서 시작할 때 한 번 찾는다.
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();

        if (rooms != null)
        {
            foreach (RoomController room in rooms)
            {
                if (room == null) continue;
                room.ExitRequested += OnRoomExitRequested;
                room.gameObject.SetActive(false);
            }
        }

        // 추가 생성 — 보스 방도 똑같이 구독하고 꺼둔다. 이걸 빠뜨리면 보스가
        // 판 시작부터 씬에 살아 있어서 일반 방에도 같이 나온다.
        if (bossRoom != null)
        {
            bossRoom.ExitRequested += OnRoomExitRequested;
            bossRoom.gameObject.SetActive(false);
        }

        // 추가 생성 — 튜토리얼 방도 같은 규칙으로 등록하고 꺼둔다.
        if (tutorialRoom != null)
        {
            tutorialRoom.ExitRequested += OnRoomExitRequested;
            tutorialRoom.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        // 추가 생성 — 보스 확인용 시작. 일반 방 배열이 비어 있어도 보스 방만으로 돌아간다.
        if (debugStartAtBossRoom && bossRoom != null)
        {
            Debug.LogWarning("[방 진행] 테스트 모드 — 보스 방부터 시작한다.", this);
            EnterRoom(bossRoom);
            return;
        }

        enteredRoomCount = 0;

        // 추가 생성 — 튜토리얼 방이 있으면 던전보다 먼저 들어간다.
        //
        // 방 수에 포함하지 않는 이유: 결과 화면의 "몇 번째 방까지 갔는가"는 던전 진행도다.
        // 튜토리얼을 세면 아무것도 안 하고 죽어도 1방을 간 것으로 기록된다.
        if (tutorialRoom != null)
        {
            EnterRoom(tutorialRoom, countAsProgress: false);
            return;
        }

        if (rooms == null || rooms.Length == 0)
        {
            Debug.LogError("[방 진행] 반복할 방이 등록되지 않았다.", this);
            return;
        }

        ActivateRoom(0);
    }

    /// <summary>
    /// 추가 생성 — 디버그 키 입력만 본다.
    ///
    /// RunManager의 디버그 사망 키, RoomDoorState의 문 상태 키와 같은 방식이다.
    /// 토글이 꺼져 있으면 첫 줄에서 바로 빠져나가므로 평소에는 비용이 없다.
    /// </summary>
    private void Update()
    {
        if (!enableDebugKeys) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[debugBossRoomKey].wasPressedThisFrame) JumpToBossRoom();
    }

    /// <summary>
    /// 추가 생성 — 지금 방을 닫고 보스 방으로 곧바로 들어간다.
    ///
    /// public인 이유: 나중에 디버그 UI 버튼이나 치트 콘솔에서도 같은 동작을 부를 수 있게 열어둔다.
    /// 방을 여는 절차는 <see cref="EnterRoom"/>가 그대로 처리하므로 일반 입장과 완전히 같은 상태가 된다.
    /// </summary>
    public void JumpToBossRoom()
    {
        if (bossRoom == null)
        {
            Debug.LogWarning("[방 진행] 보스 방이 등록되지 않아 건너뛸 수 없다.\n" +
                             "Tools → 재의 길 → 보스 방 생성 을 먼저 실행해라.", this);
            return;
        }

        if (currentRoom == bossRoom)
        {
            Debug.Log("[방 진행] 이미 보스 방이다.", this);
            return;
        }

        // 지금 방을 끄는 걸 빠뜨리면 이전 방의 적과 상자가 살아 있는 채로 겹친다.
        if (currentRoom != null) currentRoom.gameObject.SetActive(false);

        Debug.LogWarning("[방 진행] 디버그 — 보스 방으로 건너뛴다.", this);
        EnterRoom(bossRoom);
    }

    private void OnDestroy()
    {
        if (rooms != null)
        {
            foreach (RoomController room in rooms)
            {
                if (room != null) room.ExitRequested -= OnRoomExitRequested;
            }
        }

        if (bossRoom != null) bossRoom.ExitRequested -= OnRoomExitRequested;
        if (tutorialRoom != null) tutorialRoom.ExitRequested -= OnRoomExitRequested;
    }

    /// <summary>
    /// 현재 방을 닫고 다음 방을 연다.
    ///
    /// 수정(보스 방 추가): 예전에는 인덱스만 보고 판정했는데, 보스 방은 배열 밖이라
    /// 인덱스가 없다. 지금 열려 있는 방 참조와 직접 비교한다.
    /// </summary>
    private void OnRoomExitRequested(RoomController room)
    {
        if (currentRoom == null || currentRoom != room) return;

        // 추가 생성 — 튜토리얼을 나가면 던전 첫 방부터 시작한다.
        //
        // 튜토리얼은 rooms 배열 밖에 있어서 순환에 끼지 않는다. 그래서 한 번 나가면
        // 이 판에서 다시 나오지 않는다 — 무한 순환 중에 튜토리얼이 또 나오면 흐름이 끊긴다.
        if (room == tutorialRoom)
        {
            tutorialRoom.gameObject.SetActive(false);

            if (rooms == null || rooms.Length == 0)
            {
                Debug.LogError("[방 진행] 튜토리얼을 나왔는데 던전 방이 등록되지 않았다.", this);
                return;
            }

            Debug.Log("[방 진행] 튜토리얼 종료 — 던전 시작.", this);
            ActivateRoom(0);
            return;
        }

        // 추가 생성 — 보스 방 문을 나가면 판이 끝난다(클리어).
        //
        // 여기가 이 게임의 유일한 승리 조건이다. 지금까지 결과 화면으로 가는 길은 사망뿐이었다.
        // 보스 방 문은 클리어 유물을 주워야만 열리므로, 이 지점에 온 것 자체가
        // "보스를 잡고 전리품을 챙겨 제 발로 걸어 나왔다"는 뜻이다.
        if (room == bossRoom && bossClearEndsRun)
        {
            currentRoom.gameObject.SetActive(false);
            currentRoom = null;

            if (runManager == null) runManager = FindFirstObjectByType<RunManager>();
            if (runManager == null)
            {
                Debug.LogError("[방 진행] RunManager가 없어 클리어 처리를 못 했다.", this);
                return;
            }

            Debug.Log("[방 진행] 보스 방을 나갔다 — 클리어로 판을 끝낸다.", this);
            runManager.EndRun(true);
            return;
        }

        currentRoom.gameObject.SetActive(false);
        AdvanceToNextRoom();
    }

    /// <summary>
    /// 추가 생성 — 다음에 들어갈 방이 보스 방인지 일반 방인지 정한다.
    ///
    /// 보스 방에 들어가도 <see cref="currentRoomIndex"/>는 건드리지 않는다.
    /// 그래야 보스를 잡고 나왔을 때 일반 방 순환이 끊긴 자리에서 이어진다.
    /// </summary>
    private void AdvanceToNextRoom()
    {
        if (IsBossRoomTurn(enteredRoomCount + 1))
        {
            EnterRoom(bossRoom);
            return;
        }

        if (rooms == null || rooms.Length == 0)
        {
            // 테스트 모드로 보스 방만 돌릴 때 여기로 온다. 보스 방을 다시 연다.
            if (bossRoom != null) EnterRoom(bossRoom);
            else Debug.LogError("[방 진행] 갈 수 있는 방이 없다.", this);
            return;
        }

        ActivateRoom((currentRoomIndex + 1) % rooms.Length);
    }

    /// <summary>추가 생성 — 이번 차례가 보스 방인지 판정한다.</summary>
    private bool IsBossRoomTurn(int roomNumber)
    {
        return bossRoom != null && roomNumber % bossRoomInterval == 0;
    }

    /// <summary>
    /// 배열의 방 하나를 초기화하고 전투를 다시 시작한다.
    ///
    /// 수정(진행 정지 버그): 예전에는 방 참조가 비어 있으면 에러만 찍고 그대로 return했다.
    /// 그 시점에 이전 방은 이미 꺼진 뒤이고 currentRoomIndex도 옛 값 그대로라, 켜진 방이
    /// 하나도 없는 채로 진행이 <b>영구히</b> 멈췄다. 죽지도 못하니 판이 끝나지도 않는다.
    /// 이제는 빈 칸을 건너뛰고 다음 방을 찾는다. 배열이 통째로 비어 있을 때만 포기한다.
    /// </summary>
    private void ActivateRoom(int roomIndex)
    {
        RoomController room = FindNextValidRoom(ref roomIndex);
        if (room == null)
        {
            Debug.LogError("[방 진행] 배열에 유효한 방이 하나도 없다. 진행을 멈춘다.", this);
            return;
        }

        currentRoomIndex = roomIndex;
        EnterRoom(room);
    }

    /// <summary>
    /// 추가 생성 — 방 하나를 실제로 여는 공통 처리.
    ///
    /// 일반 방과 보스 방이 입장할 때 해야 할 일(초기화 → 활성화 → 전투 시작 → 플레이어 이동 →
    /// 기록)이 완전히 같아서 한 곳으로 모았다. 다른 점은 "어느 방을 고르느냐"뿐이다.
    /// </summary>
    private void EnterRoom(RoomController room, bool countAsProgress = true)
    {
        if (room == null) return;

        currentRoom = room;
        if (countAsProgress) enteredRoomCount++;

        room.PrepareForEntry();
        room.gameObject.SetActive(true);
        room.BeginEncounter();
        MovePlayerTo(room.PlayerEntryPoint);

        // 추가 생성 — 결과 화면에 "몇 번째 방까지 갔는가"를 남긴다.
        runManager?.ReportRoomEntered(enteredRoomCount);

        Debug.Log($"[방 진행] {enteredRoomCount}번째 방 입장 — {room.name}", this);
    }

    /// <summary>
    /// 추가 생성 — 주어진 위치부터 순환하며 비어 있지 않은 첫 방을 찾는다.
    ///
    /// 배열 길이만큼만 도는 이유: 전부 비어 있으면 무한 루프에 빠지기 때문이다.
    /// 한 바퀴를 다 돌고도 못 찾으면 유효한 방이 없다는 뜻이므로 null을 돌려준다.
    /// </summary>
    /// <param name="roomIndex">시작 위치. 실제로 찾은 방의 인덱스로 갱신된다.</param>
    private RoomController FindNextValidRoom(ref int roomIndex)
    {
        for (int step = 0; step < rooms.Length; step++)
        {
            int candidateIndex = (roomIndex + step) % rooms.Length;
            RoomController candidate = rooms[candidateIndex];

            if (candidate == null)
            {
                Debug.LogWarning($"[방 진행] {candidateIndex + 1}번 칸의 방 참조가 비어 있다 — 건너뛴다.", this);
                continue;
            }

            roomIndex = candidateIndex;
            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Rigidbody2D의 속도까지 지워서 이전 방에서의 대시 관성이 다음 방으로 이어지지 않게 한다.
    /// </summary>
    private void MovePlayerTo(Transform entryPoint)
    {
        if (player == null || entryPoint == null) return;

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = entryPoint.position;
        }
        else
        {
            player.transform.position = entryPoint.position;
        }

        Physics2D.SyncTransforms();
    }
}
