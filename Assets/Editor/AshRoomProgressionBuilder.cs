using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 추가 생성 — 현재 Game 씬에 반복 전투방을 구성하고 무한 진행 시스템을 연결한다.
/// 메뉴: Tools → 재의 길 → 상자 문 방 진행 구성
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

    // 추가 생성 — 이전 버전의 구성 도구가 RoomDoorState가 없는 배경도 허용해
    // roomDoor 참조를 None으로 저장했던 문제를 에디터 재컴파일 시 자동 복구한다.
    [InitializeOnLoadMethod]
    private static void ScheduleMissingDoorRepair()
    {
        EditorApplication.delayCall += TryRepairActiveGameScene;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>Play Mode를 끝낸 직후에도 누락된 문 연결을 자동 복구한다.</summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.delayCall += TryRepairActiveGameScene;
    }

    /// <summary>현재 Game 씬의 문 참조와 무한 반복 방 연결을 안전하게 복구한다.</summary>
    private static void TryRepairActiveGameScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath) return;

        GameObject sequenceRoot = GameObject.Find(SequenceRootName);
        if (sequenceRoot == null || !NeedsInfiniteSequenceRepair(sequenceRoot)) return;

        RepairExistingSequence(sequenceRoot, scene);
    }

    /// <summary>
    /// 기존 Room_raw, Props, EnemyEncounter를 첫 방으로 묶고 같은 구성을 두 번째 방으로 복제한다.
    /// 이미 만들어진 경우에는 수동 배치를 보호하기 위해 덮어쓰지 않는다.
    /// </summary>
    [MenuItem("Tools/재의 길/상자 문 방 진행 구성")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
        {
            Debug.LogError($"[방 진행 구성] Game 씬을 연 뒤 실행해야 한다: {GameScenePath}");
            return;
        }

        GameObject existingSequence = GameObject.Find(SequenceRootName);
        if (existingSequence != null)
        {
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

        var sequenceRoot = new GameObject(SequenceRootName);
        var roomOne = new GameObject("Room_01");
        roomOne.transform.SetParent(sequenceRoot.transform, false);

        // 기존 월드 위치를 유지한 채 방 전용 루트 아래로 묶는다.
        roomBackground.transform.SetParent(roomOne.transform, true);
        props.transform.SetParent(roomOne.transform, true);
        encounter.transform.SetParent(roomOne.transform, true);

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
        RoomController roomTwoController = roomTwo.GetComponent<RoomController>();

        var sequence = sequenceRoot.AddComponent<RoomSequenceController>();
        LinkSequence(sequence, new[] { roomOneController, roomTwoController }, player);

        // 시작 전에 모두 꺼둬야 첫 방의 적이 SequenceController보다 먼저 생성되지 않는다.
        roomOne.SetActive(false);
        roomTwo.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = sequenceRoot;

        Debug.Log(
            "[방 진행 구성] 반복 방 생성 완료 — Room_01 → Room_02 → Room_01 무한 반복.");
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

        var entryPoint = new GameObject("PlayerEntryPoint").transform;
        entryPoint.SetParent(roomRoot.transform, false);
        entryPoint.position = new Vector3(0f, -7f, 0f);

        var controller = roomRoot.AddComponent<RoomController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("enemySpawner").objectReferenceValue = enemySpawner;
        serialized.FindProperty("rewardChest").objectReferenceValue = chest;
        serialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
        serialized.FindProperty("exitTrigger").objectReferenceValue = exit;
        serialized.FindProperty("playerEntryPoint").objectReferenceValue = entryPoint;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return controller;
    }

    /// <summary>기존 닫힌/열린 상자 프리팹을 시각 오브젝트로 재사용한다.</summary>
    private static RewardChest CreateRewardChest(
        Transform roomRoot,
        GameObject closedChestPrefab,
        GameObject openChestPrefab)
    {
        var root = new GameObject("RewardChest");
        root.transform.SetParent(roomRoot, false);
        root.transform.position = new Vector3(0f, -2f, 0f);

        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) root.layer = pickupLayer;

        var closedVisual = (GameObject)PrefabUtility.InstantiatePrefab(closedChestPrefab, root.transform);
        var openVisual = (GameObject)PrefabUtility.InstantiatePrefab(openChestPrefab, root.transform);
        closedVisual.name = "ClosedVisual";
        openVisual.name = "OpenVisual";
        closedVisual.transform.localPosition = Vector3.zero;
        openVisual.transform.localPosition = Vector3.zero;

        // 시각 프리팹의 콜라이더 대신 부모의 넓은 상호작용 범위를 하나만 사용한다.
        DisableColliders(closedVisual);
        DisableColliders(openVisual);
        openVisual.SetActive(false);

        var trigger = root.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(5f, 3f);
        trigger.offset = new Vector2(0f, 0.6f);

        var chest = root.AddComponent<RewardChest>();
        var serialized = new SerializedObject(chest);
        serialized.FindProperty("closedVisual").objectReferenceValue = closedVisual;
        serialized.FindProperty("openVisual").objectReferenceValue = openVisual;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return chest;
    }

    /// <summary>열린 문 그림의 입구에 플레이어 감지용 트리거를 만든다.</summary>
    private static RoomExitTrigger CreateExitTrigger(Transform roomRoot, RoomDoorState roomDoor)
    {
        var root = new GameObject("DoorExitTrigger");
        root.transform.SetParent(roomRoot, false);
        root.transform.position = new Vector3(0f, 8.3f, 0f);

        var trigger = root.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(5f, 2.2f);

        var exit = root.AddComponent<RoomExitTrigger>();
        var serialized = new SerializedObject(exit);
        serialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return exit;
    }

    /// <summary>프리팹 자식까지 포함해 기존 물리 판정을 끈다.</summary>
    private static void DisableColliders(GameObject root)
    {
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
            collider.enabled = false;
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
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sequence);
    }

    /// <summary>
    /// 추가 생성 — 방 배경에 문 상태 컴포넌트와 세 배경 이미지를 확실히 연결한다.
    /// 닫힌 이미지는 현재 SpriteRenderer 값을 사용해 사용자가 고른 방 그림을 보존한다.
    /// </summary>
    private static RoomDoorState EnsureRoomDoorState(GameObject roomBackground)
    {
        RoomDoorState roomDoor = roomBackground.GetComponent<RoomDoorState>();
        if (roomDoor == null) roomDoor = roomBackground.AddComponent<RoomDoorState>();

        SpriteRenderer renderer = roomBackground.GetComponent<SpriteRenderer>();
        Sprite openRoom = AssetDatabase.LoadAssetAtPath<Sprite>(OpenRoomSpritePath);
        Sprite brokenRoom = AssetDatabase.LoadAssetAtPath<Sprite>(BrokenRoomSpritePath);

        var serialized = new SerializedObject(roomDoor);
        serialized.FindProperty("closedRoom").objectReferenceValue = renderer != null ? renderer.sprite : null;
        serialized.FindProperty("openRoom").objectReferenceValue = openRoom;
        serialized.FindProperty("brokenRoom").objectReferenceValue = brokenRoom;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(roomDoor);
        return roomDoor;
    }

    /// <summary>기존 RoomSequence 안에 문 참조가 빠진 방이 있는지 검사한다.</summary>
    private static bool HasMissingDoorReference(GameObject sequenceRoot)
    {
        foreach (RoomController room in sequenceRoot.GetComponentsInChildren<RoomController>(true))
        {
            var serialized = new SerializedObject(room);
            if (serialized.FindProperty("roomDoor").objectReferenceValue == null)
                return true;
        }

        return false;
    }

    /// <summary>추가 생성 — 기존 씬의 반복 방과 플레이어 연결이 유효한지 검사한다.</summary>
    private static bool NeedsInfiniteSequenceRepair(GameObject sequenceRoot)
    {
        if (HasMissingDoorReference(sequenceRoot)) return true;

        RoomSequenceController sequence = sequenceRoot.GetComponent<RoomSequenceController>();
        if (sequence == null) return true;

        var serialized = new SerializedObject(sequence);
        SerializedProperty rooms = serialized.FindProperty("rooms");
        return rooms == null || rooms.arraySize == 0 ||
               serialized.FindProperty("player").objectReferenceValue == null;
    }

    /// <summary>
    /// 이전 도구로 생성된 반복 방들의 문과 진행 참조를 복구한다.
    /// 배치 위치와 사용자가 수정한 소품은 건드리지 않는다.
    /// </summary>
    private static void RepairExistingSequence(GameObject sequenceRoot, Scene scene)
    {
        int repaired = 0;
        foreach (RoomController room in sequenceRoot.GetComponentsInChildren<RoomController>(true))
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
            roomSerialized.ApplyModifiedPropertiesWithoutUndo();

            if (exit != null)
            {
                var exitSerialized = new SerializedObject(exit);
                exitSerialized.FindProperty("roomDoor").objectReferenceValue = roomDoor;
                exitSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(exit);
            }

            EditorUtility.SetDirty(room);
            repaired++;
        }

        RoomController roomOne = FindRoomController(sequenceRoot.transform, "Room_01");
        RoomController roomTwo = FindRoomController(sequenceRoot.transform, "Room_02");
        RoomSequenceController sequence = sequenceRoot.GetComponent<RoomSequenceController>();
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        if (sequence == null || roomOne == null || roomTwo == null || player == null)
        {
            Debug.LogError("[방 진행 복구] 방 순서 연결에 필요한 컴포넌트가 부족하다.");
            return;
        }

        LinkSequence(sequence, new[] { roomOne, roomTwo }, player);

        roomOne.gameObject.SetActive(false);
        roomTwo.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = sequenceRoot;
        Debug.Log(
            $"[방 진행 복구] {repaired}개 방 연결 복구 완료 — " +
            "Room_01 → Room_02 무한 반복.");
    }

    /// <summary>추가 생성 — 방 루트의 직계 자식에서 이름으로 RoomController를 찾는다.</summary>
    private static RoomController FindRoomController(Transform sequenceRoot, string roomName)
    {
        Transform room = sequenceRoot.Find(roomName);
        return room != null ? room.GetComponent<RoomController>() : null;
    }

}
