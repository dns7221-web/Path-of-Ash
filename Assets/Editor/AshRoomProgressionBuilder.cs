using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 추가 생성 — 현재 Game 씬에 반복 전투방을 구성하고 무한 진행 시스템을 연결한다.
///
/// 메뉴:
/// - Tools → 재의 길 → 상자 문 방 진행 구성 : 처음 한 번, RoomSequence 전체를 만든다
/// - Tools → 재의 길 → 방 진행 연결 복구   : 이미 만든 RoomSequence의 참조만 다시 잇는다
///
/// 수정(자동 실행 제거): 예전에는 <c>[InitializeOnLoadMethod]</c>로 에디터가 켜질 때,
/// 스크립트가 재컴파일될 때, Play Mode를 빠져나올 때마다 복구가 자동으로 돌고
/// <c>EditorSceneManager.SaveScene</c>까지 호출했다. 그 구조에는 세 가지 문제가 있었다.
///
/// 1. <b>사람이 시키지 않은 씬 수정</b>. 방 하나를 손보는 중에 컴파일이 한 번 돌면
///    도구가 끼어들어 참조를 덮어쓰고 저장까지 해버린다. Undo도 남지 않는다.
/// 2. <b>방 개수 하드코딩</b>. 복구가 rooms 배열을 Room_01/Room_02 두 개로 되돌려서,
///    Room_03을 추가해 둔 상태에서 복구가 한 번 돌면 3번 방이 조용히 사라졌다.
/// 3. <b>일회성 마이그레이션의 영구 상주</b>. "예전 버전 도구가 남긴 None 참조를 고친다"는
///    한 번이면 끝나는 일인데, 프로젝트가 살아 있는 내내 매번 검사하고 있었다.
///
/// 그래서 자동 실행을 전부 걷어내고 메뉴 버튼으로만 돌게 바꿨다. 저장도 하지 않는다 —
/// <c>MarkSceneDirty</c>로 "바뀌었다"만 표시하고 실제 저장은 사람이 Ctrl+S로 한다.
/// 이게 유니티 에디터 확장의 기본 관례이고, 마음에 안 들면 저장하지 않고 닫으면 된다.
/// </summary>
public static class AshRoomProgressionBuilder
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string SequenceRootName = "RoomSequence";
    private const string ClosedChestPrefabPath =
        "Assets/Project/Prefabs/Props/prop_chest_closed.prefab";
    private const string OpenChestPrefabPath =
        "Assets/Project/Prefabs/Props/prop_chest_open.prefab";
    private const string OpenRoomSpritePath =
        "Assets/Project/Art/Sprites/Dungeon/Room_DoorOpen.png";
    private const string BrokenRoomSpritePath =
        "Assets/Project/Art/Sprites/Dungeon/Room_DoorBroken.png";

    // 추가 생성 — 방 배치 수치. 원래는 아래 함수들 안에 숫자가 그대로 박혀 있어서
    // "8.3이 왜 8.3인지"를 알 수 없었다. 전부 현재 방 규격에서 역산한 값이라 근거를 같이 남긴다.
    // 방 배경을 다른 크기로 바꾸면 여기만 고치면 된다.

    /// <summary>상자를 놓는 위치. 방 중앙보다 약간 아래 — 입구와 출구 사이 동선에 걸린다.</summary>
    private static readonly Vector3 ChestPosition = new Vector3(0f, -2f, 0f);

    /// <summary>상자 상호작용 범위. 시각 프리팹보다 넉넉해야 F를 누를 자리를 찾지 않게 된다.</summary>
    private static readonly Vector2 ChestInteractionSize = new Vector2(5f, 3f);

    /// <summary>상호작용 범위를 상자 그림 높이만큼 위로 올린다(발밑이 아니라 몸통 기준).</summary>
    private static readonly Vector2 ChestInteractionOffset = new Vector2(0f, 0.6f);

    /// <summary>
    /// 출구 판정 위치. 방 위쪽 벽(Wall_Top)이 y 9.3~10.3을 막고 있어서,
    /// 판정 상단이 벽 안쪽에 걸치도록 8.3 + 높이 2.2(= 7.2~9.4)로 잡았다.
    /// 벽에 닿을 때까지 올라가면 반드시 이 범위에 들어온다.
    /// </summary>
    private static readonly Vector3 ExitTriggerPosition = new Vector3(0f, 8.3f, 0f);

    /// <summary>출구 판정 크기. 가로 5는 배경에 그려진 문 폭에 맞춘 값이다.</summary>
    private static readonly Vector2 ExitTriggerSize = new Vector2(5f, 2.2f);

    /// <summary>다음 방에 들어올 때 플레이어를 놓는 자리. 문 반대쪽(방 아래)에서 시작한다.</summary>
    private static readonly Vector3 PlayerEntryPosition = new Vector3(0f, -7f, 0f);

    /// <summary>
    /// 기존 Room_raw, Props, EnemyEncounter를 첫 방으로 묶고 같은 구성을 두 번째 방으로 복제한다.
    /// 이미 RoomSequence가 있으면 새로 만들지 않고 복구 쪽으로 넘긴다 — 수동 배치를 보호한다.
    /// </summary>
    [MenuItem("Tools/재의 길/상자 문 방 진행 구성")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!IsGameSceneReady(scene)) return;

        GameObject existingSequence = GameObject.Find(SequenceRootName);
        if (existingSequence != null)
        {
            Debug.Log("[방 진행 구성] RoomSequence가 이미 있다 — 새로 만들지 않고 연결만 복구한다.");
            Selection.activeGameObject = existingSequence;
            RepairExistingSequence(existingSequence, scene);
            return;
        }

        GameObject roomBackground = GameObject.Find("Room_raw");
        GameObject props = GameObject.Find("Props");
        GameObject encounter = GameObject.Find("EnemyEncounter");
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();

        if (roomBackground == null || props == null || encounter == null || player == null)
        {
            Debug.LogError(
                "[방 진행 구성] Room_raw, Props, EnemyEncounter, Player가 모두 필요하다. " +
                "기존 던전/전투/플레이어 배치를 먼저 확인해라.");
            return;
        }

        GameObject closedChestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClosedChestPrefabPath);
        GameObject openChestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OpenChestPrefabPath);
        if (closedChestPrefab == null || openChestPrefab == null)
        {
            Debug.LogError("[방 진행 구성] 닫힌/열린 상자 프리팹을 찾지 못했다.");
            return;
        }

        // 추가 생성 — 아래 작업 전체를 Undo 한 덩어리로 묶는다. 결과가 마음에 안 들면
        // Ctrl+Z 한 번으로 실행 전 상태로 돌아간다. 씬을 저장하지 않는 것과 같은 이유다.
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        var sequenceRoot = new GameObject(SequenceRootName);
        Undo.RegisterCreatedObjectUndo(sequenceRoot, "방 진행 구성");

        var roomOne = new GameObject("Room_01");
        Undo.RegisterCreatedObjectUndo(roomOne, "방 진행 구성");
        Undo.SetTransformParent(roomOne.transform, sequenceRoot.transform, "방 진행 구성");
        roomOne.transform.localPosition = Vector3.zero;

        // 기존 월드 위치를 유지한 채 방 전용 루트 아래로 묶는다.
        // Undo.SetTransformParent는 SetParent(worldPositionStays: true)와 같은 동작이다.
        Undo.SetTransformParent(roomBackground.transform, roomOne.transform, "방 진행 구성");
        Undo.SetTransformParent(props.transform, roomOne.transform, "방 진행 구성");
        Undo.SetTransformParent(encounter.transform, roomOne.transform, "방 진행 구성");

        // 수정(문 연결 버그): 기존 배경에 RoomDoorState가 없으면 여기서 직접 붙이고
        // 현재 닫힌 배경과 열린/파괴 배경 스프라이트까지 확실히 연결한다.
        RoomDoorState roomDoor = EnsureRoomDoorState(roomBackground);

        RoomController roomOneController = ConfigureRoom(
            roomOne,
            encounter.GetComponent<EnemySpawner>(),
            roomDoor,
            closedChestPrefab,
            openChestPrefab);

        // 첫 방을 완성한 뒤 통째로 복제하면 각 방의 내부 참조도 복제된 방 안을 가리킨다.
        GameObject roomTwo = Object.Instantiate(roomOne, sequenceRoot.transform);
        roomTwo.name = "Room_02";
        Undo.RegisterCreatedObjectUndo(roomTwo, "방 진행 구성");
        RoomController roomTwoController = roomTwo.GetComponent<RoomController>();

        var sequence = Undo.AddComponent<RoomSequenceController>(sequenceRoot);
        LinkSequence(sequence, new[] { roomOneController, roomTwoController }, player);

        // 씬에 저장되는 상태를 런타임 시작 상태와 맞춘다. RoomSequenceController.Awake가
        // 어차피 전부 끄지만, 켜둔 채로 저장하면 씬 뷰에서 두 방이 같은 자리에 겹쳐 보인다.
        roomOne.SetActive(false);
        roomTwo.SetActive(false);

        Undo.SetCurrentGroupName("방 진행 구성");
        Undo.CollapseUndoOperations(undoGroup);

        // 수정(무단 저장 제거): SaveScene을 부르지 않는다. 저장은 사람이 Ctrl+S로 한다.
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = sequenceRoot;

        Debug.Log(
            "[방 진행 구성] 반복 방 생성 완료 — Room_01 → Room_02 → Room_01 무한 반복. " +
            "확인 후 Ctrl+S로 저장해라.");
    }

    /// <summary>
    /// 추가 생성 — 이미 만들어진 RoomSequence의 참조만 다시 잇는다.
    /// 예전 도구가 남긴 None 참조를 고칠 때, 또는 방을 손으로 추가/삭제한 뒤에 부른다.
    /// 자동으로 돌지 않으므로 사람이 원하는 시점에만 실행된다.
    /// </summary>
    [MenuItem("Tools/재의 길/방 진행 연결 복구")]
    public static void Repair()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!IsGameSceneReady(scene)) return;

        GameObject sequenceRoot = GameObject.Find(SequenceRootName);
        if (sequenceRoot == null)
        {
            Debug.LogError(
                $"[방 진행 복구] 씬에 {SequenceRootName}이 없다. " +
                "먼저 'Tools/재의 길/상자 문 방 진행 구성'으로 만들어라.");
            return;
        }

        Selection.activeGameObject = sequenceRoot;
        RepairExistingSequence(sequenceRoot, scene);
    }

    /// <summary>
    /// 추가 생성 — 메뉴 실행 전 공통 확인. Play Mode 중에는 씬을 건드리지 않는다.
    /// Play Mode에서 만든 오브젝트는 나갈 때 전부 사라지므로 작업이 통째로 날아간다.
    /// </summary>
    private static bool IsGameSceneReady(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[방 진행] Play Mode에서는 실행할 수 없다. 재생을 멈추고 다시 눌러라.");
            return false;
        }

        if (scene.path != GameScenePath)
        {
            Debug.LogError($"[방 진행] Game 씬을 연 뒤 실행해야 한다: {GameScenePath}");
            return false;
        }

        return true;
    }

    /// <summary>한 방에 상자, 문 출구, 입구 지점과 진행 컴포넌트를 연결한다.</summary>
    private static RoomController ConfigureRoom(
        GameObject roomRoot,
        EnemySpawner enemySpawner,
        RoomDoorState roomDoor,
        GameObject closedChestPrefab,
        GameObject openChestPrefab)
    {
        RewardChest chest = CreateRewardChest(roomRoot.transform, closedChestPrefab, openChestPrefab);
        RoomExitTrigger exit = CreateExitTrigger(roomRoot.transform, roomDoor);

        var entryObject = new GameObject("PlayerEntryPoint");
        Undo.RegisterCreatedObjectUndo(entryObject, "방 진행 구성");
        Transform entryPoint = entryObject.transform;
        entryPoint.SetParent(roomRoot.transform, false);
        entryPoint.position = PlayerEntryPosition;

        var controller = Undo.AddComponent<RoomController>(roomRoot);
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("enemySpawner").objectReferenceValue = enemySpawner;
        serialized.FindProperty("rewardChest").objectReferenceValue = chest;
        serialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
        serialized.FindProperty("exitTrigger").objectReferenceValue = exit;
        serialized.FindProperty("playerEntryPoint").objectReferenceValue = entryPoint;

        // 수정(Undo 지원): ApplyModifiedPropertiesWithoutUndo → ApplyModifiedProperties.
        // WithoutUndo를 쓰면 Ctrl+Z로 오브젝트는 사라지는데 참조 변경만 남아 어긋난다.
        serialized.ApplyModifiedProperties();
        return controller;
    }

    /// <summary>기존 닫힌/열린 상자 프리팹을 시각 오브젝트로 재사용한다.</summary>
    private static RewardChest CreateRewardChest(
        Transform roomRoot,
        GameObject closedChestPrefab,
        GameObject openChestPrefab)
    {
        var root = new GameObject("RewardChest");
        Undo.RegisterCreatedObjectUndo(root, "방 진행 구성");
        root.transform.SetParent(roomRoot, false);
        root.transform.position = ChestPosition;

        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) root.layer = pickupLayer;

        var closedVisual = (GameObject)PrefabUtility.InstantiatePrefab(closedChestPrefab, root.transform);
        var openVisual = (GameObject)PrefabUtility.InstantiatePrefab(openChestPrefab, root.transform);
        Undo.RegisterCreatedObjectUndo(closedVisual, "방 진행 구성");
        Undo.RegisterCreatedObjectUndo(openVisual, "방 진행 구성");
        closedVisual.name = "ClosedVisual";
        openVisual.name = "OpenVisual";
        closedVisual.transform.localPosition = Vector3.zero;
        openVisual.transform.localPosition = Vector3.zero;

        // 시각 프리팹의 콜라이더 대신 부모의 넓은 상호작용 범위를 하나만 사용한다.
        DisableColliders(closedVisual);
        DisableColliders(openVisual);
        openVisual.SetActive(false);

        var trigger = Undo.AddComponent<BoxCollider2D>(root);
        trigger.isTrigger = true;
        trigger.size = ChestInteractionSize;
        trigger.offset = ChestInteractionOffset;

        var chest = Undo.AddComponent<RewardChest>(root);
        var serialized = new SerializedObject(chest);
        serialized.FindProperty("closedVisual").objectReferenceValue = closedVisual;
        serialized.FindProperty("openVisual").objectReferenceValue = openVisual;
        serialized.ApplyModifiedProperties();
        return chest;
    }

    /// <summary>열린 문 그림의 입구에 플레이어 감지용 트리거를 만든다.</summary>
    private static RoomExitTrigger CreateExitTrigger(Transform roomRoot, RoomDoorState roomDoor)
    {
        var root = new GameObject("DoorExitTrigger");
        Undo.RegisterCreatedObjectUndo(root, "방 진행 구성");
        root.transform.SetParent(roomRoot, false);
        root.transform.position = ExitTriggerPosition;

        var trigger = Undo.AddComponent<BoxCollider2D>(root);
        trigger.isTrigger = true;
        trigger.size = ExitTriggerSize;

        var exit = Undo.AddComponent<RoomExitTrigger>(root);
        var serialized = new SerializedObject(exit);
        serialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
        serialized.ApplyModifiedProperties();
        return exit;
    }

    /// <summary>프리팹 자식까지 포함해 기존 물리 판정을 끈다.</summary>
    private static void DisableColliders(GameObject root)
    {
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
        {
            // 프리팹 인스턴스의 값을 바꾸는 것이므로 Undo에 기록해야 되돌릴 때 원복된다.
            Undo.RecordObject(collider, "방 진행 구성");
            collider.enabled = false;
            EditorUtility.SetDirty(collider);
        }
    }

    /// <summary>반복할 방 순서와 공용 플레이어를 진행 컴포넌트에 연결한다.</summary>
    private static void LinkSequence(
        RoomSequenceController sequence,
        RoomController[] roomsToRepeat,
        PlayerController player)
    {
        var serialized = new SerializedObject(sequence);
        SerializedProperty rooms = serialized.FindProperty("rooms");
        rooms.arraySize = roomsToRepeat.Length;
        for (int i = 0; i < roomsToRepeat.Length; i++)
            rooms.GetArrayElementAtIndex(i).objectReferenceValue = roomsToRepeat[i];

        serialized.FindProperty("player").objectReferenceValue = player;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(sequence);
    }

    /// <summary>
    /// 추가 생성 — 방 배경에 문 상태 컴포넌트와 세 배경 이미지를 확실히 연결한다.
    /// 닫힌 이미지는 현재 SpriteRenderer 값을 사용해 사용자가 고른 방 그림을 보존한다.
    /// </summary>
    private static RoomDoorState EnsureRoomDoorState(GameObject roomBackground)
    {
        RoomDoorState roomDoor = roomBackground.GetComponent<RoomDoorState>();
        if (roomDoor == null) roomDoor = Undo.AddComponent<RoomDoorState>(roomBackground);

        SpriteRenderer renderer = roomBackground.GetComponent<SpriteRenderer>();
        Sprite openRoom = AssetDatabase.LoadAssetAtPath<Sprite>(OpenRoomSpritePath);
        Sprite brokenRoom = AssetDatabase.LoadAssetAtPath<Sprite>(BrokenRoomSpritePath);

        if (openRoom == null || brokenRoom == null)
        {
            Debug.LogWarning(
                "[방 진행] 열린/부서진 문 배경 스프라이트를 찾지 못했다. " +
                "문이 열려도 그림이 바뀌지 않는다.", roomBackground);
        }

        var serialized = new SerializedObject(roomDoor);
        serialized.FindProperty("closedRoom").objectReferenceValue = renderer != null ? renderer.sprite : null;
        serialized.FindProperty("openRoom").objectReferenceValue = openRoom;
        serialized.FindProperty("brokenRoom").objectReferenceValue = brokenRoom;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(roomDoor);
        return roomDoor;
    }

    /// <summary>
    /// 추가 생성 — RoomSequence의 <b>직계 자식</b>을 하이어라키 순서 그대로 수집한다.
    ///
    /// 수정(방 개수 하드코딩 제거): 예전에는 Room_01, Room_02 두 개를 이름으로 찾아
    /// 배열을 그 둘로 덮어썼다. 방을 세 개로 늘려도 복구가 한 번 돌면 두 개로 잘렸다.
    /// 이제는 실제로 붙어 있는 방을 전부, 사람이 하이어라키에서 정렬한 순서대로 쓴다.
    /// 이름 규칙(Room_01…)에 의존하지 않으므로 방 이름을 자유롭게 지어도 된다.
    /// </summary>
    private static RoomController[] CollectRoomsInOrder(Transform sequenceRoot)
    {
        var rooms = new List<RoomController>();

        for (int i = 0; i < sequenceRoot.childCount; i++)
        {
            RoomController room = sequenceRoot.GetChild(i).GetComponent<RoomController>();
            if (room != null) rooms.Add(room);
        }

        return rooms.ToArray();
    }

    /// <summary>
    /// 이미 만들어진 방들의 문과 진행 참조를 다시 잇는다.
    /// 배치 위치, 사용자가 수정한 소품, 오브젝트의 활성 상태는 건드리지 않는다.
    /// </summary>
    private static void RepairExistingSequence(GameObject sequenceRoot, Scene scene)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        RoomController[] rooms = CollectRoomsInOrder(sequenceRoot.transform);
        if (rooms.Length == 0)
        {
            Debug.LogError(
                $"[방 진행 복구] {sequenceRoot.name} 아래에 RoomController가 붙은 방이 하나도 없다.",
                sequenceRoot);
            return;
        }

        int repaired = 0;
        foreach (RoomController room in rooms)
        {
            Transform background = room.transform.Find("Room_raw");
            if (background == null)
            {
                Debug.LogError($"[방 진행 복구] {room.name} 아래에서 Room_raw 배경을 찾지 못했다.", room);
                continue;
            }

            RoomDoorState roomDoor = EnsureRoomDoorState(background.gameObject);
            RoomExitTrigger exit = room.GetComponentInChildren<RoomExitTrigger>(true);

            var roomSerialized = new SerializedObject(room);
            roomSerialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
            roomSerialized.FindProperty("exitTrigger").objectReferenceValue = exit;
            roomSerialized.ApplyModifiedProperties();

            if (exit != null)
            {
                var exitSerialized = new SerializedObject(exit);
                exitSerialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
                exitSerialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(exit);
            }
            else
            {
                Debug.LogWarning($"[방 진행 복구] {room.name}에 RoomExitTrigger가 없다 — 나갈 수 없는 방이 된다.", room);
            }

            EditorUtility.SetDirty(room);
            repaired++;
        }

        RoomSequenceController sequence = sequenceRoot.GetComponent<RoomSequenceController>();
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        if (sequence == null || player == null)
        {
            Debug.LogError(
                "[방 진행 복구] RoomSequenceController 또는 씬의 Player를 찾지 못했다.", sequenceRoot);
            return;
        }

        LinkSequence(sequence, rooms, player);

        Undo.SetCurrentGroupName("방 진행 연결 복구");
        Undo.CollapseUndoOperations(undoGroup);

        // 수정(무단 저장 제거): 여기서도 저장하지 않는다. 활성 상태도 건드리지 않는다 —
        // 어차피 RoomSequenceController.Awake가 런타임에 전부 끄고 첫 방만 켠다.
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            $"[방 진행 복구] 방 {repaired}개 연결 복구 완료 — 등록 순서: " +
            string.Join(" → ", System.Array.ConvertAll(rooms, r => r.name)) +
            " → (첫 방으로 순환). 확인 후 Ctrl+S로 저장해라.");
    }
}
