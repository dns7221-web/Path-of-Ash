using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 추가 생성 — 한 방 안의 전투, 보상 상자, 문, 출구 순서를 관리한다.
/// 각 시스템은 자기 역할만 수행하고 이 컴포넌트가 진행 순서를 연결한다.
///
/// 수정(보스 방 추가): 전투 참조의 타입이 EnemySpawner에서 <see cref="RoomEncounter"/>로 넓어졌다.
/// 이 클래스는 "전투가 끝나면 상자, 상자를 열면 문"만 알면 되고 그 전투가 잡몹 떼인지
/// 보스 한 마리인지는 알 필요가 없다.
/// </summary>
[DisallowMultipleComponent]
public class RoomController : MonoBehaviour
{
    [Header("방 구성")]
    // FormerlySerializedAs가 필요한 이유: 필드 이름을 바꾸면 유니티는 다른 필드로 보고
    // 씬에 이미 꽂혀 있던 참조를 버린다. 옛 이름을 적어두면 그대로 이어받는다.
    [FormerlySerializedAs("enemySpawner")]
    [SerializeField] private RoomEncounter encounter;
    // 수정(보스 방 보상 추가): 타입을 RewardChest → RoomReward로 넓혔다.
    // 잡몹 방은 상자, 보스 방은 클리어 유물이지만 방은 "보상을 챙겼다"만 알면 된다.
    [FormerlySerializedAs("rewardChest")]
    [SerializeField] private RoomReward reward;
    [SerializeField] private RoomDoorState roomDoor;
    [SerializeField] private RoomExitTrigger exitTrigger;
    [SerializeField] private Transform playerEntryPoint;

    // 추가 생성 — 전투 없이 처음부터 문이 열려 있는 방.
    //
    // 왜 필요한가: 이 클래스는 "전투가 끝나야 보상, 보상을 챙겨야 문"이라는 한 줄기로만
    // 문을 연다. 튜토리얼처럼 적도 상자도 없는 방은 그 줄기를 탈 수 없어서
    // <b>문이 영영 안 열리고 플레이어가 갇힌다.</b>
    //
    // 예외를 여기 한 곳에 두는 이유: 방마다 다른 규칙을 스크립트로 흩뿌리면 나중에
    // "이 방은 왜 문이 열려 있지"의 답을 찾을 수 없다. 체크박스 하나면 인스펙터에서 바로 보인다.
    [Tooltip("켜면 입장 순간부터 문이 열려 있다. 전투도 보상도 없는 튜토리얼·휴식 방에 쓴다.")]
    [SerializeField] private bool startUnlocked;

    /// <summary>이 방의 열린 문으로 플레이어가 나갔을 때 발생한다.</summary>
    public event Action<RoomController> ExitRequested;

    // 추가 생성 — 보스 열쇠를 다 모았는지 물어볼 상대. 플레이어가 프리팹이라 미리 못 꽂는다.
    private RelicInventory relicInventory;

    /// <summary>
    /// 추가 생성 — 이 방의 문이 <b>보스로 가는 부서진 문</b>인가.
    ///
    /// 진행 관리자가 출구를 받았을 때 어디로 보낼지 정하는 근거다.
    /// 방은 "어떤 문을 열었는가"만 알고, 그 문이 어디로 이어지는지는 모른다.
    /// </summary>
    public bool IsBossGateOpen =>
        roomDoor != null && roomDoor.State == RoomDoorState.DoorState.Broken;

    /// <summary>다음 방 입장 시 플레이어를 놓을 위치다.</summary>
    public Transform PlayerEntryPoint => playerEntryPoint;

    private void Awake()
    {
        // 수정(문 연결 버그): 누락된 참조를 조용히 무시하면 상자는 열려도 배경은 그대로라
        // 원인을 알 수 없다. 잘못 구성된 씬은 시작 즉시 명확한 오류를 남긴다.
        if (roomDoor == null)
            Debug.LogError($"[방 진행] {name}의 RoomDoorState 참조가 비어 있다.", this);

        ResetRoomState();
    }

    private void OnEnable()
    {
        if (encounter != null) encounter.EncounterCleared += OnEncounterCleared;
        if (reward != null) reward.Claimed += OnRewardClaimed;
        if (exitTrigger != null) exitTrigger.Entered += OnExitEntered;
    }

    private void OnDisable()
    {
        if (encounter != null) encounter.EncounterCleared -= OnEncounterCleared;
        if (reward != null) reward.Claimed -= OnRewardClaimed;
        if (exitTrigger != null) exitTrigger.Entered -= OnExitEntered;
    }

    /// <summary>
    /// 추가 생성 — 방을 처음 입장할 상태로 되돌린다.
    /// 닫힌 문과 숨은 보상으로 시작하고 진행 관리자가 전투를 시작한다.
    ///
    /// <b>반드시 이 방을 활성화한 뒤에 부른다.</b> 방은 씬에 비활성으로 저장돼 있어서
    /// 유니티가 <c>Awake</c>를 첫 활성화까지 미룬다. 비활성 상태에서 먼저 부르면
    /// 여기서 맞춰놓은 문·출구 상태를 뒤늦게 깨어난 <see cref="Awake"/>의
    /// <see cref="ResetRoomState"/>가 덮어쓴다. 튜토리얼 방이 이것 때문에 문이
    /// 영영 안 열려 갇혔었다.
    /// </summary>
    public void PrepareForEntry()
    {
        // 추가 생성 — 위 순서를 어기면 증상이 "문이 안 열린다"로만 나타나서 원인을 찾기 어렵다.
        // 조용히 잘못 동작하느니 그 자리에서 이유를 남긴다.
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogError($"[방 진행] {name}이 비활성인 채로 PrepareForEntry가 불렸다. " +
                           "활성화한 뒤에 불러야 문 상태가 Awake에 덮어써지지 않는다.", this);
        }

        ResetRoomState();

        // 추가 생성 — 처음부터 열린 방은 초기화 직후에 바로 문을 연다.
        // ResetRoomState가 문을 닫으므로 반드시 그 뒤에 와야 한다.
        if (startUnlocked)
        {
            roomDoor?.SetState(RoomDoorState.DoorState.Open);
            exitTrigger?.SetPassageEnabled(true);
        }
    }

    /// <summary>
    /// 추가 생성 — 활성화가 끝난 뒤 이 방의 전투를 시작한다.
    /// 방을 재사용할 때는 EnemySpawner의 Start가 다시 호출되지 않으므로 진행 관리자가 직접 부른다.
    /// </summary>
    public void BeginEncounter()
    {
        encounter?.BeginEncounter();
    }

    /// <summary>적을 모두 잡으면 문 대신 보상을 먼저 내놓는다. 잡몹 방은 상자, 보스 방은 클리어 유물이다.</summary>
    private void OnEncounterCleared()
    {
        Debug.Log($"[방 진행] {name} 전투 종료 — 보상 등장.", this);
        reward?.SetAvailable(true);
    }

    /// <summary>보상을 챙긴 뒤에만 열린 방 그림과 출구 판정을 함께 활성화한다.</summary>
    private void OnRewardClaimed()
    {
        if (roomDoor == null)
        {
            Debug.LogError($"[방 진행] {name}의 문 참조가 없어 열린 배경으로 바꿀 수 없다.", this);
            return;
        }

        // 추가 생성 — 보스 열쇠를 다 모았으면 부서진 문이 열린다.
        //
        // 부서진 그림이 있는 방에서만 그렇게 한다. 보스 방에는 그 그림이 없어서
        // 억지로 바꾸면 통과는 되는데 닫힌 그림이 남는다. 그리고 보스 방에서 또
        // 부서진 문이 열리면 보스를 잡고도 보스 방으로 되돌아가게 된다.
        bool bossGate = roomDoor.HasBrokenRoom && HasAllBossKeys();

        roomDoor.SetState(bossGate
            ? RoomDoorState.DoorState.Broken
            : RoomDoorState.DoorState.Open);

        exitTrigger?.SetPassageEnabled(true);
        Debug.Log($"[방 진행] {name} 보상 획득 — {(bossGate ? "부서진 문 개방(보스로)" : "문 개방")}.", this);
    }

    /// <summary>방 순서 관리자에게 다음 방 이동을 요청한다.</summary>
    private void OnExitEntered()
    {
        ExitRequested?.Invoke(this);
    }

    /// <summary>추가 생성 — 보스 열쇠가 다 모였는가. 인벤토리가 없으면 아니라고 본다.</summary>
    private bool HasAllBossKeys()
    {
        if (relicInventory == null)
        {
            // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
            relicInventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        }

        return relicInventory != null && relicInventory.HasAllBossKeys;
    }

    private void ResetRoomState()
    {
        roomDoor?.SetState(RoomDoorState.DoorState.Closed);
        reward?.SetAvailable(false);
        exitTrigger?.SetPassageEnabled(false);
    }
}
