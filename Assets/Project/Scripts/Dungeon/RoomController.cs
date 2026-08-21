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
    /// </summary>
    public void PrepareForEntry()
    {
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

        roomDoor.SetState(RoomDoorState.DoorState.Open);
        exitTrigger?.SetPassageEnabled(true);
        Debug.Log($"[방 진행] {name} 보상 획득 — 문 개방.", this);
    }

    /// <summary>방 순서 관리자에게 다음 방 이동을 요청한다.</summary>
    private void OnExitEntered()
    {
        ExitRequested?.Invoke(this);
    }

    private void ResetRoomState()
    {
        roomDoor?.SetState(RoomDoorState.DoorState.Closed);
        reward?.SetAvailable(false);
        exitTrigger?.SetPassageEnabled(false);
    }
}
