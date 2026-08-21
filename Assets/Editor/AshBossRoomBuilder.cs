using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Game 씬에 보스 방을 구성한다.
///
/// 메뉴: Tools → 재의 길 → 보스 방 생성
///
/// 씬을 새로 만들지 않고 Game 씬 안에 방 하나로 넣는 이유:
/// 방 진행이 이미 "씬 전환 없이 방 루트를 켜고 끄는" 구조다. 씬을 나누면 HP바·스태미나·재 게이지·
/// 유물 인벤토리 같은 씬 오브젝트를 전부 복제하거나 DontDestroyOnLoad로 빼야 하고, 런 상태를
/// 씬 너머로 넘기는 코드가 새로 필요해진다. 방으로 넣으면 그 전부가 공짜다.
///
/// 손으로 만들지 않고 도구로 만드는 이유:
/// 방 하나에 채워야 할 참조가 전투·상자·문·출구·입장지점까지 다섯 개고, 하나만 비어도
/// "문이 안 열린다" 같은 증상으로만 나타나 원인을 찾기 어렵다. 기존 방을 복제해서
/// 바뀌는 부분만 갈아끼우면 그 사고가 구조적으로 안 난다.
///
/// <b>이미 있으면 다시 만들지 않고 빈 참조만 채운다.</b> 보스 방을 만든 뒤 인스펙터에서 손으로
/// 맞춘 배치가 다시 실행할 때마다 날아가면 안 된다. 다른 빌더들과 같은 규칙이다.
/// </summary>
public static class AshBossRoomBuilder
{
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemy/BossAshKing.prefab";
    private const string BossRoomSpritePath =
        "Assets/Project/Art/Environment/BossRooms/ash-king-boss-room.png";
    private const string ClearRelicPath = "Assets/Project/Data/Relics/Relic_AshKingHeart.asset";
    private const string RelicPickupPath = "Assets/Project/Prefabs/Items/RelicPickup.prefab";

    private const string BossRoomName = "Room_Boss";

    /// <summary>
    /// 스프라이트 가장자리에서 걸을 수 있는 바닥까지의 거리(픽셀).
    ///
    /// 그림을 직접 재서 나온 값이다. 1254x1254 캔버스에서 방 그림은 x 52~1201에 있고,
    /// 그 안쪽으로 돌벽이 약 48픽셀 더 들어온다. 둘을 합쳐 100픽셀로 잡았다.
    /// 숫자를 유닛이 아니라 픽셀로 두는 이유: 나중에 PPU를 바꿔도 이 값은 그대로 맞는다.
    /// </summary>
    private const float InteriorInsetPixels = 100f;

    /// <summary>방 배경의 PPU. 던전 방과 보스 방 모두 32다.</summary>
    private const float RoomPixelsPerUnit = 32f;

    /// <summary>벽 충돌체 두께(유닛). 플레이어가 대시로 뚫지 못할 만큼만 있으면 된다.</summary>
    private const float WallThickness = 2f;

    [MenuItem("Tools/재의 길/보스 방 생성")]
    public static void Build()
    {
        var sequence = Object.FindFirstObjectByType<RoomSequenceController>();
        if (sequence == null)
        {
            Debug.LogError("[보스 방] 씬에서 RoomSequenceController를 못 찾았다.\n" +
                           "Game 씬을 열고 다시 실행해라.");
            return;
        }

        var bossPrefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        var bossPrefab = bossPrefabRoot != null ? bossPrefabRoot.GetComponent<EnemyBoss>() : null;
        if (bossPrefab == null)
        {
            Debug.LogError($"[보스 방] 보스 프리팹을 못 찾았다: {BossPrefabPath}\n" +
                           "Tools → 재의 길 → 보스 프리팹 생성 을 먼저 실행해라.");
            return;
        }

        var sequenceObject = new SerializedObject(sequence);
        SerializedProperty bossRoomProperty = sequenceObject.FindProperty("bossRoom");

        // 이미 만들어져 있으면 배치를 건드리지 않고 참조만 손본다.
        if (bossRoomProperty.objectReferenceValue is RoomController existing)
        {
            RepairBossRoom(existing, bossPrefab);
            RemoveLooseBosses(existing.gameObject);
            MarkDirty();
            Debug.Log($"[보스 방] 이미 있는 {existing.name}의 참조를 점검했다.", existing);
            return;
        }

        RoomController template = FindTemplateRoom(sequenceObject);
        if (template == null)
        {
            Debug.LogError("[보스 방] 복제할 일반 방이 없다. rooms 배열에 방을 먼저 등록해라.");
            return;
        }

        RoomController bossRoom = CreateFromTemplate(template, bossPrefab);
        if (bossRoom == null) return;

        bossRoomProperty.objectReferenceValue = bossRoom;
        sequenceObject.ApplyModifiedPropertiesWithoutUndo();

        // 씬 루트에 떠 있던 보스를 치운다. 이게 남아 있으면 방과 상관없이 항상 살아 있어서
        // 일반 방에도 보스가 같이 나온다 — 원래 있던 증상이 정확히 이것이었다.
        RemoveLooseBosses(bossRoom.gameObject);

        MarkDirty();
        Selection.activeGameObject = bossRoom.gameObject;
        Debug.Log($"[보스 방] {template.name}을 복제해 {BossRoomName}을 만들었다.\n" +
                  "RoomSequence의 '보스 방 주기'로 등장 간격을, '테스트' 토글로 즉시 시작을 조절해라.",
                  bossRoom);
    }

    /// <summary>rooms 배열에서 복제할 기준 방을 고른다. 비어 있는 칸은 건너뛴다.</summary>
    private static RoomController FindTemplateRoom(SerializedObject sequenceObject)
    {
        SerializedProperty rooms = sequenceObject.FindProperty("rooms");
        if (rooms == null) return null;

        for (int i = 0; i < rooms.arraySize; i++)
        {
            if (rooms.GetArrayElementAtIndex(i).objectReferenceValue is RoomController room)
                return room;
        }

        return null;
    }

    /// <summary>
    /// 일반 방을 복제해 보스 방으로 바꾼다.
    ///
    /// 복제를 쓰는 이유: 문·상자·출구 판정·입장 지점의 상대 위치가 이미 맞춰져 있다.
    /// 빈 오브젝트부터 쌓으면 그 좌표를 전부 다시 맞춰야 하고, 어긋나면 "문으로 나가지지 않는다"가 된다.
    /// 방 크기도 배경 그림이 던전 방과 같은 1254x1254 / PPU 32라 그대로 들어맞는다.
    /// </summary>
    private static RoomController CreateFromTemplate(RoomController template, EnemyBoss bossPrefab)
    {
        GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
        clone.name = BossRoomName;
        clone.transform.SetPositionAndRotation(template.transform.position, template.transform.rotation);
        clone.transform.localScale = template.transform.localScale;
        Undo.RegisterCreatedObjectUndo(clone, "보스 방 생성");

        var room = clone.GetComponent<RoomController>();
        if (room == null)
        {
            Debug.LogError("[보스 방] 복제본에 RoomController가 없다. 기준 방 구성을 확인해라.", clone);
            Undo.DestroyObjectImmediate(clone);
            return null;
        }

        Transform bossSpawnPoint = ReplaceSpawnerWithBossEncounter(clone, bossPrefab);
        RoomReward reward = ReplaceChestWithRelicReward(clone);
        SwapBackground(room);
        // 배경을 바꾼 뒤에 배치를 맞춘다. 순서가 바뀌면 옛 던전 방 그림 크기로 벽을 세운다.
        LayoutBossRoom(room, bossSpawnPoint);

        // 전투와 보상 참조를 보스용으로 바꾼다. 이 두 줄이 방 진행을 보스 방으로 만든다.
        var roomObject = new SerializedObject(room);
        roomObject.FindProperty("encounter").objectReferenceValue = clone.GetComponentInChildren<BossEncounter>(true);
        if (reward != null) roomObject.FindProperty("reward").objectReferenceValue = reward;
        roomObject.ApplyModifiedPropertiesWithoutUndo();

        if (bossSpawnPoint == null)
            Debug.LogWarning("[보스 방] 보스 등장 위치를 못 정했다. BossEncounter의 '등장 위치'를 손으로 꽂아라.", room);

        return room;
    }

    /// <summary>
    /// 잡몹 스포너를 걷어내고 그 자리에 보스 전투를 붙인다.
    ///
    /// 스폰 지점은 첫 번째 하나만 남긴다. 잡몹은 여러 곳에서 나오지만 보스는 한 마리라
    /// 나머지 지점은 쓰이지 않고 남아서 씬만 헷갈리게 만든다.
    /// </summary>
    /// <returns>보스가 등장할 위치. 못 찾으면 null.</returns>
    private static Transform ReplaceSpawnerWithBossEncounter(GameObject clone, EnemyBoss bossPrefab)
    {
        var spawner = clone.GetComponentInChildren<EnemySpawner>(true);
        GameObject host = spawner != null ? spawner.gameObject : clone;
        Transform bossSpawnPoint = null;

        if (spawner != null)
        {
            // 컴포넌트를 지우면 배열도 같이 사라지므로, 지우기 전에 스폰 지점을 확보한다.
            SerializedProperty points = new SerializedObject(spawner).FindProperty("spawnPoints");
            if (points != null)
            {
                for (int i = 0; i < points.arraySize; i++)
                {
                    if (points.GetArrayElementAtIndex(i).objectReferenceValue is not Transform point) continue;

                    if (bossSpawnPoint == null)
                    {
                        bossSpawnPoint = point;
                        point.name = "BossSpawnPoint";
                    }
                    else
                    {
                        Undo.DestroyObjectImmediate(point.gameObject);
                    }
                }
            }

            Undo.DestroyObjectImmediate(spawner);
        }

        var encounter = host.GetComponent<BossEncounter>();
        if (encounter == null) encounter = Undo.AddComponent<BossEncounter>(host);

        var encounterObject = new SerializedObject(encounter);
        encounterObject.FindProperty("bossPrefab").objectReferenceValue = bossPrefab;
        encounterObject.FindProperty("spawnPoint").objectReferenceValue = bossSpawnPoint;
        encounterObject.ApplyModifiedPropertiesWithoutUndo();

        return bossSpawnPoint;
    }

    /// <summary>
    /// 보상 상자를 걷어내고 클리어 유물 보상으로 바꾼다.
    ///
    /// 보스를 잡은 직후에 상자를 한 번 더 열게 하면 절정 뒤에 사무 절차가 끼는 꼴이 된다.
    /// 보스가 쓰러진 자리에서 전리품이 바로 튀어나오는 편이 낫다.
    ///
    /// <see cref="ChestRelicReward"/>도 같이 지우는 이유: 그건 "상자를 열면 유물을 준다"라
    /// 상자가 없으면 할 일이 없다. 남겨두면 무슨 일을 하는지 헷갈리는 컴포넌트만 남는다.
    /// </summary>
    private static RoomReward ReplaceChestWithRelicReward(GameObject clone)
    {
        var chest = clone.GetComponentInChildren<RewardChest>(true);
        GameObject host = chest != null ? chest.gameObject : clone;

        if (chest != null)
        {
            var chestRelic = chest.GetComponent<ChestRelicReward>();
            if (chestRelic != null) Undo.DestroyObjectImmediate(chestRelic);
            Undo.DestroyObjectImmediate(chest);
        }

        var reward = host.GetComponent<BossRelicReward>();
        if (reward == null) reward = Undo.AddComponent<BossRelicReward>(host);

        var relic = AssetDatabase.LoadAssetAtPath<RelicData>(ClearRelicPath);
        var pickupRoot = AssetDatabase.LoadAssetAtPath<GameObject>(RelicPickupPath);
        var pickup = pickupRoot != null ? pickupRoot.GetComponent<RelicPickup>() : null;

        if (relic == null) Debug.LogWarning($"[보스 방] 클리어 유물을 못 찾았다: {ClearRelicPath}", reward);
        if (pickup == null) Debug.LogWarning($"[보스 방] 유물 픽업 프리팹을 못 찾았다: {RelicPickupPath}", reward);

        var rewardObject = new SerializedObject(reward);
        rewardObject.FindProperty("clearRelic").objectReferenceValue = relic;
        rewardObject.FindProperty("pickupPrefab").objectReferenceValue = pickup;
        rewardObject.FindProperty("dropPoint").objectReferenceValue = host.transform;
        rewardObject.ApplyModifiedPropertiesWithoutUndo();

        return reward;
    }

    /// <summary>
    /// 방 배경을 보스 전용 그림으로 갈아끼운다.
    ///
    /// SpriteRenderer를 직접 건드리지 않고 <see cref="RoomDoorState"/>의 그림 슬롯을 바꾸는 이유:
    /// 그 컴포넌트가 방 상태에 따라 SpriteRenderer를 매번 다시 칠한다. 렌더러만 바꾸면
    /// 실행하는 순간 던전 방 그림으로 되돌아간다.
    ///
    /// 세 슬롯(닫힘/열림/부서짐)에 같은 그림을 넣는 건 보스 방 그림이 한 장뿐이라서다.
    /// 문 열린 변형이 생기면 그때 '열림' 슬롯만 나눠 꽂으면 된다.
    /// </summary>
    private static void SwapBackground(RoomController room)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BossRoomSpritePath);
        if (sprite == null)
        {
            Debug.LogWarning($"[보스 방] 배경 그림을 못 찾았다: {BossRoomSpritePath}", room);
            return;
        }

        var door = room.GetComponentInChildren<RoomDoorState>(true);
        if (door == null)
        {
            Debug.LogWarning("[보스 방] RoomDoorState가 없어 배경을 못 바꿨다.", room);
            return;
        }

        var doorObject = new SerializedObject(door);
        foreach (string slot in new[] { "closedRoom", "openRoom", "brokenRoom" })
        {
            SerializedProperty property = doorObject.FindProperty(slot);
            if (property != null) property.objectReferenceValue = sprite;
        }
        doorObject.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 방 안의 배치물을 보스 방 그림 크기에 맞춰 다시 놓는다.
    ///
    /// 왜 필요한가: 이 방은 던전 방(1678x937, 52.4x29.3유닛)을 복제해 만들었는데 보스 방 그림은
    /// 1254x1254(39.2x39.2유닛) 정사각형이다. 벽 충돌체와 입장 지점이 옛 가로 방 기준이라
    /// 그대로 두면 벽이 그림 밖에 있거나 플레이어가 벽 속에서 시작한다.
    ///
    /// 좌표를 숫자로 박지 않고 <b>스프라이트 경계에서 계산</b>하는 이유:
    /// 나중에 보스 방 그림을 다른 크기로 다시 뽑아도 이 도구를 다시 돌리기만 하면 된다.
    /// </summary>
    private static void LayoutBossRoom(RoomController room, Transform bossSpawnPoint)
    {
        var door = room.GetComponentInChildren<RoomDoorState>(true);
        var renderer = door != null
            ? door.GetComponentInChildren<SpriteRenderer>(true)
            : room.GetComponentInChildren<SpriteRenderer>(true);

        if (renderer == null || renderer.sprite == null)
        {
            Debug.LogWarning("[보스 방] 배경 렌더러를 못 찾아 배치를 못 맞췄다.", room);
            return;
        }

        // 스프라이트 경계에서 벽 안쪽까지 밀어 넣은 영역이 실제로 걸을 수 있는 바닥이다.
        Bounds bounds = renderer.bounds;
        float inset = InteriorInsetPixels / RoomPixelsPerUnit;
        float left = bounds.min.x + inset;
        float right = bounds.max.x - inset;
        float bottom = bounds.min.y + inset;
        float top = bounds.max.y - inset;
        var center = new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
        float innerWidth = right - left;
        float innerHeight = top - bottom;

        float half = WallThickness * 0.5f;
        // 벽은 바닥 바깥에 세운다. 모서리가 비지 않도록 가로 벽을 두께만큼 길게 뺀다.
        PlaceWall(room, "Wall_Left", new Vector2(left - half, center.y), new Vector2(WallThickness, innerHeight + WallThickness * 2f));
        PlaceWall(room, "Wall_Right", new Vector2(right + half, center.y), new Vector2(WallThickness, innerHeight + WallThickness * 2f));
        PlaceWall(room, "Wall_Bottom", new Vector2(center.x, bottom - half), new Vector2(innerWidth + WallThickness * 2f, WallThickness));
        PlaceWall(room, "Wall_Top", new Vector2(center.x, top + half), new Vector2(innerWidth + WallThickness * 2f, WallThickness));

        // 플레이어는 아래쪽에서 들어오고 보스는 위쪽에 선다. 마주 보는 배치라 입장하자마자
        // 보스가 화면에 들어오고, 첫 패턴이 오기 전에 움직일 거리가 생긴다.
        MoveChild(room, "PlayerEntryPoint", new Vector2(center.x, bottom + innerHeight * 0.15f));
        if (bossSpawnPoint != null)
            bossSpawnPoint.position = new Vector3(center.x, bottom + innerHeight * 0.75f, bossSpawnPoint.position.z);

        // 보상은 방 한가운데, 출구는 위쪽 벽 앞에 둔다.
        //
        // 출구가 위쪽인 이유: 던전 방 그림이 그렇게 그려져 있다. 나무 이중문이 위쪽 벽 가운데
        // 있고 아래쪽 가운데에는 들어오는 계단이 있다. 보스 방만 아래로 나가게 하면
        // 앞 방들에서 익힌 "문은 위에 있다"가 깨진다.
        //
        // 보스가 위쪽 75% 지점에 서므로 자연스럽게 문 앞을 막고 선 모양이 된다.
        MoveChild(room, "RewardChest", new Vector2(center.x, center.y));
        MoveChild(room, "DoorExitTrigger", new Vector2(center.x, top - innerHeight * 0.04f));

        Debug.Log($"[보스 방] 배치 갱신 — 바닥 {innerWidth:F1} x {innerHeight:F1} 유닛, 중심 {center}.", room);
    }

    /// <summary>벽 오브젝트의 위치와 충돌체 크기를 맞춘다. 없으면 만든다.</summary>
    private static void PlaceWall(RoomController room, string wallName, Vector2 position, Vector2 size)
    {
        Transform wall = FindChild(room.transform, wallName);
        if (wall == null)
        {
            var created = new GameObject(wallName);
            Undo.RegisterCreatedObjectUndo(created, "보스 방 배치");
            created.transform.SetParent(room.transform, false);
            created.layer = room.gameObject.layer;
            wall = created.transform;
        }

        wall.position = new Vector3(position.x, position.y, wall.position.z);

        var box = wall.GetComponent<BoxCollider2D>();
        if (box == null) box = Undo.AddComponent<BoxCollider2D>(wall.gameObject);

        Undo.RecordObject(box, "보스 방 배치");
        box.offset = Vector2.zero;
        box.size = size;
    }

    /// <summary>이름으로 찾은 자식을 옮긴다. 없으면 조용히 넘어간다 — 방마다 구성이 다를 수 있다.</summary>
    private static void MoveChild(RoomController room, string childName, Vector2 position)
    {
        Transform child = FindChild(room.transform, childName);
        if (child == null) return;

        Undo.RecordObject(child, "보스 방 배치");
        child.position = new Vector3(position.x, position.y, child.position.z);
    }

    /// <summary>비활성 오브젝트까지 포함해 이름으로 자손을 찾는다.</summary>
    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == childName) return candidate;
        }
        return null;
    }

    /// <summary>
    /// 이미 만들어둔 보스 방의 배경과 배치를 다시 맞춘다.
    ///
    /// 방을 지웠다 다시 만들면 인스펙터에서 손으로 조정한 값이 날아간다.
    /// 배경 그림을 바꿨거나 배치가 어긋난 경우를 위해 따로 뺀 메뉴다.
    /// </summary>
    [MenuItem("Tools/재의 길/보스 방 배경·배치 맞추기")]
    public static void MatchBossRoom()
    {
        var sequence = Object.FindFirstObjectByType<RoomSequenceController>();
        if (sequence == null)
        {
            Debug.LogError("[보스 방] 씬에서 RoomSequenceController를 못 찾았다. Game 씬을 열고 실행해라.");
            return;
        }

        var sequenceObject = new SerializedObject(sequence);
        if (sequenceObject.FindProperty("bossRoom").objectReferenceValue is not RoomController bossRoom)
        {
            Debug.LogError("[보스 방] 보스 방이 아직 없다. 먼저 '보스 방 생성'을 실행해라.");
            return;
        }

        SwapBackground(bossRoom);

        // 보상이 아직 상자로 남아 있으면 클리어 유물 보상으로 바꾼다.
        // 이미 바뀐 방을 다시 돌려도 컴포넌트를 새로 붙이지 않고 참조만 다시 채운다.
        RoomReward reward = ReplaceChestWithRelicReward(bossRoom.gameObject);
        var roomObject = new SerializedObject(bossRoom);
        if (reward != null) roomObject.FindProperty("reward").objectReferenceValue = reward;
        roomObject.ApplyModifiedPropertiesWithoutUndo();

        Transform spawnPoint = null;
        var encounter = bossRoom.GetComponentInChildren<BossEncounter>(true);
        if (encounter != null)
            spawnPoint = new SerializedObject(encounter).FindProperty("spawnPoint").objectReferenceValue as Transform;

        LayoutBossRoom(bossRoom, spawnPoint);
        MarkDirty();
        Debug.Log($"[보스 방] {bossRoom.name}의 배경·배치·보상을 보스 방 기준으로 맞췄다.", bossRoom);
    }

    /// <summary>이미 있는 보스 방의 빈 참조만 채운다. 배치와 수치는 건드리지 않는다.</summary>
    private static void RepairBossRoom(RoomController room, EnemyBoss bossPrefab)
    {
        var encounter = room.GetComponentInChildren<BossEncounter>(true);
        if (encounter == null)
        {
            Debug.LogWarning($"[보스 방] {room.name}에 BossEncounter가 없다. 방을 지우고 다시 생성해라.", room);
            return;
        }

        var encounterObject = new SerializedObject(encounter);
        SerializedProperty prefabProperty = encounterObject.FindProperty("bossPrefab");
        if (prefabProperty.objectReferenceValue == null) prefabProperty.objectReferenceValue = bossPrefab;
        encounterObject.ApplyModifiedPropertiesWithoutUndo();

        var roomObject = new SerializedObject(room);
        SerializedProperty encounterProperty = roomObject.FindProperty("encounter");
        if (encounterProperty.objectReferenceValue == null) encounterProperty.objectReferenceValue = encounter;
        roomObject.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 보스 방 밖에 놓인 보스를 지운다.
    ///
    /// 원래 보스가 씬 루트에 직접 배치돼 있었다(부모 없음). 방을 켜고 끄는 것과 무관하게 항상
    /// 살아 있으니 일반 방에서도 같이 튀어나왔고, 그래서 보스 패턴만 따로 확인할 수가 없었다.
    /// </summary>
    private static void RemoveLooseBosses(GameObject bossRoom)
    {
        EnemyBoss[] bosses = Object.FindObjectsByType<EnemyBoss>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (EnemyBoss boss in bosses)
        {
            if (boss == null) continue;
            if (boss.transform.IsChildOf(bossRoom.transform)) continue;

            // 프리팹 인스턴스는 자식만 지우면 남은 껍데기가 씬에 남는다. 바깥 뿌리째 지운다.
            GameObject target = PrefabUtility.GetOutermostPrefabInstanceRoot(boss.gameObject);
            if (target == null) target = boss.gameObject;

            Debug.Log($"[보스 방] 방 밖에 있던 보스 '{target.name}'을 지웠다.", bossRoom);
            Undo.DestroyObjectImmediate(target);
        }
    }

    private static void MarkDirty()
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
