using System;
using UnityEngine;

/// <summary>
/// 추가 생성 — 한 방 안의 전투, 보상 상자, 문, 출구 순서를 관리한다.
/// 각 시스템은 자기 역할만 수행하고 이 컴포넌트가 진행 순서를 연결한다.
/// </summary>
[DisallowMultipleComponent]
public class RoomController : MonoBehaviour
{
    [Header("방 구성")]
    [SerializeField] private EnemySpawner enemySpawner;
    [SerializeField] private RewardChest rewardChest;
    [SerializeField] private RoomDoorState roomDoor;
    [SerializeField] private RoomExitTrigger exitTrigger;
    [SerializeField] private Transform playerEntryPoint;

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
        if (enemySpawner != null) enemySpawner.EncounterCleared += OnEncounterCleared;
        if (rewardChest != null) rewardChest.Opened += OnChestOpened;
        if (exitTrigger != null) exitTrigger.Entered += OnExitEntered;
    }

    private void OnDisable()
    {
        if (enemySpawner != null) enemySpawner.EncounterCleared -= OnEncounterCleared;
        if (rewardChest != null) rewardChest.Opened -= OnChestOpened;
        if (exitTrigger != null) exitTrigger.Entered -= OnExitEntered;
    }

    /// <summary>
    /// 추가 생성 — 방을 처음 입장할 상태로 되돌린다.
    /// 닫힌 문과 숨은 상자로 시작하고 EnemySpawner의 Start가 전투를 시작한다.
    /// </summary>
    public void PrepareForEntry()
    {
        ResetRoomState();
    }

    /// <summary>
    /// 추가 생성 — 활성화가 끝난 뒤 이 방의 전투를 시작한다.
    /// 방을 재사용할 때는 EnemySpawner의 Start가 다시 호출되지 않으므로 진행 관리자가 직접 부른다.
    /// </summary>
    public void BeginEncounter()
    {
        enemySpawner?.BeginEncounter();
    }

    /// <summary>적을 모두 잡으면 문 대신 보상 상자를 먼저 보여준다.</summary>
    private void OnEncounterCleared()
    {
        Debug.Log($"[방 진행] {name} 전투 종료 — 보상 상자 등장.", this);
        rewardChest?.SetAvailable(true);
    }

    /// <summary>상자를 연 뒤에만 열린 방 그림과 출구 판정을 함께 활성화한다.</summary>
    private void OnChestOpened()
    {
        if (roomDoor == null)
        {
            Debug.LogError($"[방 진행] {name}의 문 참조가 없어 열린 배경으로 바꿀 수 없다.", this);
            return;
        }

        roomDoor.SetState(RoomDoorState.DoorState.Open);
        exitTrigger?.SetPassageEnabled(true);
        Debug.Log($"[방 진행] {name} 상자 개봉 — 문 개방.", this);
    }

    /// <summary>방 순서 관리자에게 다음 방 이동을 요청한다.</summary>
    private void OnExitEntered()
    {
        ExitRequested?.Invoke(this);
    }

    private void ResetRoomState()
    {
        roomDoor?.SetState(RoomDoorState.DoorState.Closed);
        rewardChest?.SetAvailable(false);
        exitTrigger?.SetPassageEnabled(false);
    }
}
