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

    private int currentRoomIndex = -1;
    private int enteredRoomCount;

    /// <summary>이번 런에서 반복 입장을 포함해 들어간 총 방 수.</summary>
    public int EnteredRoomCount => enteredRoomCount;

    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();

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

    /// <summary>배열의 방 하나를 초기화하고 전투를 다시 시작한다.</summary>
    private void ActivateRoom(int roomIndex)
    {
        RoomController room = rooms[roomIndex];
        if (room == null)
        {
            Debug.LogError($"[방 진행] {roomIndex + 1}번째 방 참조가 비어 있다.", this);
            return;
        }

        currentRoomIndex = roomIndex;
        enteredRoomCount++;
        room.PrepareForEntry();
        room.gameObject.SetActive(true);
        room.BeginEncounter();
        MovePlayerTo(room.PlayerEntryPoint);

        Debug.Log($"[방 진행] {enteredRoomCount}번째 방 입장 — {room.name}", this);
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
