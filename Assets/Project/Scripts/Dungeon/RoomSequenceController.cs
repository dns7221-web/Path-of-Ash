using UnityEngine;

/// <summary>
/// 추가 생성 — Game 씬 안에 등록된 방들을 순서대로 무한 반복한다.
/// 씬 전환 없이 방 루트 활성화만 바꾸며, Result 이동은 플레이어 사망 흐름이 담당한다.
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

    private int currentRoomIndex = -1;
    private int enteredRoomCount;

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

    }

    private void Start()
    {
        if (rooms == null || rooms.Length == 0)
        {
            Debug.LogError("[방 진행] 반복할 방이 등록되지 않았다.", this);
            return;
        }

        enteredRoomCount = 0;
        ActivateRoom(0);
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
    }

    /// <summary>현재 방을 닫고 다음 방을 열며, 마지막 다음에는 첫 방으로 되돌아간다.</summary>
    private void OnRoomExitRequested(RoomController room)
    {
        if (currentRoomIndex < 0 || currentRoomIndex >= rooms.Length) return;
        if (rooms[currentRoomIndex] != room) return;

        rooms[currentRoomIndex].gameObject.SetActive(false);
        int nextRoomIndex = (currentRoomIndex + 1) % rooms.Length;
        ActivateRoom(nextRoomIndex);
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
        enteredRoomCount++;
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
