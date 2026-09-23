using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-22) — 던전 방에 "들어갈 때마다 소품을 새로 흩뿌리기"(<see cref="RoomPropScatter"/>)를 꾸린다.
///
/// 메뉴: Tools → 재의 길 → 씬·세팅 → 던전 소품 무작위 배치 (Game 씬을 연 채로 실행)
///
/// 방 진행 관리자의 일반 방 순환(rooms 배열)에 든 방에만 붙인다. 튜토리얼·보스 방은 배열 밖이라 지금 배치를 지킨다.
/// 방마다 하는 일:
/// <list type="number">
/// <item><b>소품을 넉넉히 깐다.</b> 지금은 방마다 12개뿐이라 전부 켜면 매번 같은 구성이 된다. 종류별로 몇 개씩 더 깔아
/// (꺼 둔 채로) 들어갈 때마다 무엇이 몇 개 나올지도 달라지게 한다. 프리팹 연결을 유지한 채로 넣는다 —
/// 소품 배치 도구와 같은 이유(나중에 프리팹만 고치면 다 따라온다).</item>
/// <item><b>컴포넌트를 붙이고 칸을 채운다.</b> 바닥 범위는 벽 안쪽 면에서 구하고(<see cref="AshRoomWallBuilder"/>와 같은 계산),
/// 비울 자리는 적이 나오는 곳·입장 지점·출구·보상 상자로 채운다.</item>
/// </list>
///
/// <b>다시 돌려도 안전하다.</b> 이미 깐 소품은 다시 깔지 않고 모자란 것만 더한다. 바닥 범위는 벽에서 다시 구하고
/// (벽을 옮겼을 때 따라가야 한다), 비울 자리는 비어 있을 때만 채운다. 묶음의 최솟값·최댓값은 인스펙터에서 고친 값을 지킨다.
/// </summary>
public static class AshRoomPropScatterBuilder
{
    private const string PrefabFolder = "Assets/Project/Prefabs/Props";
    private const string PropsRootName = "Props";

    /// <summary>소품 종류 하나 — 프리팹 이름, 묶음 이름, 방마다 깔아 둘 수, 한 번에 놓을 수(최솟값~최댓값).</summary>
    private readonly struct Kind
    {
        public readonly string Prefab;
        public readonly string Label;
        public readonly int Pool;
        public readonly int Min;
        public readonly int Max;

        public Kind(string prefab, string label, int pool, int min, int max)
        {
            Prefab = prefab;
            Label = label;
            Pool = pool;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// 종류별 수. 지금 방에 있는 12개(기둥 2+2, 제단 1, 항아리 3, 깨진 항아리 1, 잔해 3)에 6개를 더해 18개가 된다.
    ///
    /// 기둥 두 종류는 최솟값을 1로 둔다 — 기둥은 망령 돌진을 끊는 엄폐물이라(소품 배치 도구 주석), 매번 적어도 둘은
    /// 있어야 "위치 싸움"이 남는다. 제단은 방의 중심 장식이라 하나만 두고, 나올 때도 안 나올 때도 있게 한다.
    /// 한 번에 나오는 수는 4~16개, 평균 10개쯤이다.
    /// </summary>
    private static readonly Kind[] Kinds =
    {
        new Kind("prop_pillar_broken", "부서진 기둥", pool: 3, min: 1, max: 3),
        new Kind("prop_pillar_collapsed", "쓰러진 기둥", pool: 3, min: 1, max: 3),
        new Kind("prop_altar", "제단", pool: 1, min: 0, max: 1),
        new Kind("prop_urn", "항아리", pool: 5, min: 1, max: 4),
        new Kind("prop_urn_broken", "깨진 항아리", pool: 2, min: 0, max: 2),
        new Kind("prop_rubble", "잔해", pool: 4, min: 1, max: 3),
    };

    [MenuItem("Tools/재의 길/씬·세팅/던전 소품 무작위 배치")]
    public static void Build()
    {
        var sequence = Object.FindFirstObjectByType<RoomSequenceController>(FindObjectsInactive.Include);
        if (sequence == null)
        {
            Debug.LogError("[소품 무작위] 씬에서 RoomSequenceController를 못 찾았다. Game 씬을 열고 다시 실행해라.");
            return;
        }

        SerializedProperty rooms = new SerializedObject(sequence).FindProperty("rooms");
        if (rooms == null || rooms.arraySize == 0)
        {
            Debug.LogError("[소품 무작위] 일반 방 순환(rooms)이 비어 있다.", sequence);
            return;
        }

        int done = 0;
        for (int i = 0; i < rooms.arraySize; i++)
        {
            if (rooms.GetArrayElementAtIndex(i).objectReferenceValue is RoomController room && SetUpRoom(room))
                done++;
        }

        EditorSceneManager.MarkSceneDirty(sequence.gameObject.scene);
        Debug.Log($"[소품 무작위] 일반 방 {done}개에 꾸렸다. 씬을 저장해라(Ctrl+S). " +
                  "플레이해서 방에 들어갈 때마다 소품이 새로 흩어지는지 본다. " +
                  "수·간격은 각 방 Props의 RoomPropScatter 인스펙터에서 조정한다(오브젝트를 고르면 씬 뷰에 바닥 범위와 비울 자리가 보인다).");
    }

    /// <summary>방 하나를 꾸린다. 못 하면 경고를 남기고 false.</summary>
    private static bool SetUpRoom(RoomController room)
    {
        Transform props = AshRoomWallBuilder.FindChild(room.transform, PropsRootName);
        if (props == null)
        {
            Debug.LogWarning($"[소품 무작위] {room.name}에 {PropsRootName}가 없다. 건너뛴다. " +
                             "Tools → 재의 길 → 씬·세팅 → 던전 소품 기본 배치 로 먼저 깔아라.", room);
            return false;
        }

        if (!TryGetFloor(room, out Rect floor)) return false;

        int added = EnsurePool(props);

        var scatter = props.GetComponent<RoomPropScatter>();
        if (scatter == null) scatter = Undo.AddComponent<RoomPropScatter>(props.gameObject);

        var serialized = new SerializedObject(scatter);
        serialized.FindProperty("floor").rectValue = floor;
        int kept = FillKeepClear(serialized.FindProperty("keepClear"), room);
        int items = FillGroups(serialized.FindProperty("groups"), props);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[소품 무작위] {room.name} — 소품 {items}개(새로 깐 것 {added}개), 비울 자리 {kept}곳, " +
                  $"바닥 x {floor.xMin:0.00}~{floor.xMax:0.00}, y {floor.yMin:0.00}~{floor.yMax:0.00}", props);
        return true;
    }

    /// <summary>벽 안쪽 면으로 둘러싸인 바닥. 벽 넷 중 하나라도 없으면 false.</summary>
    private static bool TryGetFloor(RoomController room, out Rect floor)
    {
        floor = default;

        Transform left = AshRoomWallBuilder.FindChild(room.transform, "Wall_Left");
        Transform right = AshRoomWallBuilder.FindChild(room.transform, "Wall_Right");
        Transform bottom = AshRoomWallBuilder.FindChild(room.transform, "Wall_Bottom");
        Transform top = AshRoomWallBuilder.FindChild(room.transform, "Wall_Top");
        if (left == null || right == null || bottom == null || top == null)
        {
            Debug.LogWarning($"[소품 무작위] {room.name}의 벽(Wall_Left·Right·Bottom·Top)을 못 찾았다. 건너뛴다. " +
                             "Tools → 재의 길 → 씬·세팅 → 방 벽을 방마다 소유하게 를 먼저 실행해라.", room);
            return false;
        }

        floor = Rect.MinMaxRect(
            AshRoomWallBuilder.InnerFace(left, +1),
            AshRoomWallBuilder.InnerFace(bottom, +1, vertical: true),
            AshRoomWallBuilder.InnerFace(right, -1),
            AshRoomWallBuilder.InnerFace(top, -1, vertical: true));
        return true;
    }

    /// <summary>
    /// 종류별로 모자란 소품을 꺼 둔 채로 더 깐다. 이미 있는 것은 건드리지 않는다.
    /// </summary>
    /// <returns>새로 깐 수.</returns>
    private static int EnsurePool(Transform props)
    {
        int added = 0;

        foreach (Kind kind in Kinds)
        {
            var existing = Children(props).Where(child => IsKind(child.name, kind.Prefab)).ToList();
            int missing = kind.Pool - existing.Count;
            if (missing <= 0) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{kind.Prefab}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"[소품 무작위] 소품 프리팹을 못 찾았다: {PrefabFolder}/{kind.Prefab}.prefab");
                continue;
            }

            for (int i = 0; i < missing; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, props);
                instance.name = NextFreeName(props, kind.Prefab);
                instance.transform.localPosition = Vector3.zero;

                // 꺼 둔다. 어디에 놓일지는 방에 들어갈 때 RoomPropScatter가 정한다. 켜 두면 에디터에서 방 한가운데에 겹쳐 보인다.
                instance.SetActive(false);

                Undo.RegisterCreatedObjectUndo(instance, "던전 소품 더 깔기");
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// 비울 자리를 채운다 — 적이 나오는 곳, 입장 지점, 출구, 보상 상자. 이미 무언가 들어 있으면 그대로 둔다
    /// (손으로 더한 자리를 지킨다).
    /// </summary>
    /// <returns>비울 자리 수.</returns>
    private static int FillKeepClear(SerializedProperty array, RoomController room)
    {
        if (array.arraySize > 0) return array.arraySize;

        var points = new List<Transform>();

        var spawner = room.GetComponentInChildren<EnemySpawner>(true);
        if (spawner != null)
        {
            SerializedProperty spawnPoints = new SerializedObject(spawner).FindProperty("spawnPoints");
            for (int i = 0; spawnPoints != null && i < spawnPoints.arraySize; i++)
            {
                if (spawnPoints.GetArrayElementAtIndex(i).objectReferenceValue is Transform point) points.Add(point);
            }
        }

        foreach (string name in new[] { "PlayerEntryPoint", "DoorExitTrigger", "RewardChest" })
        {
            Transform point = AshRoomWallBuilder.FindChild(room.transform, name);
            if (point != null) points.Add(point);
        }

        array.arraySize = points.Count;
        for (int i = 0; i < points.Count; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];

        return points.Count;
    }

    /// <summary>
    /// 묶음을 종류별로 다시 채운다. 소품 목록은 늘 Props 아래 자식에서 새로 모으고(깐 것이 늘었을 수 있다),
    /// 같은 이름의 묶음이 이미 있으면 최솟값·최댓값은 인스펙터에 남은 값을 지킨다.
    /// </summary>
    /// <returns>묶음에 든 소품 수.</returns>
    private static int FillGroups(SerializedProperty groups, Transform props)
    {
        // 기존 최솟값·최댓값을 이름으로 기억해 둔다.
        var kept = new Dictionary<string, (int min, int max)>();
        for (int i = 0; i < groups.arraySize; i++)
        {
            SerializedProperty group = groups.GetArrayElementAtIndex(i);
            string label = group.FindPropertyRelative("label").stringValue;
            if (!string.IsNullOrEmpty(label))
                kept[label] = (group.FindPropertyRelative("min").intValue, group.FindPropertyRelative("max").intValue);
        }

        int total = 0;
        groups.arraySize = Kinds.Length;

        for (int k = 0; k < Kinds.Length; k++)
        {
            Kind kind = Kinds[k];
            var items = Children(props).Where(child => IsKind(child.name, kind.Prefab)).Select(child => child.gameObject).ToList();

            SerializedProperty group = groups.GetArrayElementAtIndex(k);
            group.FindPropertyRelative("label").stringValue = kind.Label;

            (int min, int max) range = kept.TryGetValue(kind.Label, out var old) ? old : (kind.Min, kind.Max);
            group.FindPropertyRelative("min").intValue = range.min;
            group.FindPropertyRelative("max").intValue = range.max;

            SerializedProperty array = group.FindPropertyRelative("items");
            array.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];

            total += items.Count;
        }

        return total;
    }

    /// <summary>
    /// 이 이름이 그 종류의 소품인가. "prop_urn_0"은 항아리지만 "prop_urn_broken"은 깨진 항아리다 —
    /// 접두어 뒤가 숫자일 때만 같은 종류로 본다(이름만 앞부분이 같다고 섞이지 않게).
    /// </summary>
    private static bool IsKind(string childName, string prefab)
    {
        if (childName == prefab) return true;
        if (!childName.StartsWith(prefab + "_")) return false;

        return int.TryParse(childName.Substring(prefab.Length + 1), out _);
    }

    /// <summary>"prop_urn_3"처럼 아직 안 쓴 번호를 붙인 이름.</summary>
    private static string NextFreeName(Transform props, string prefab)
    {
        var used = new HashSet<string>(Children(props).Select(child => child.name));

        for (int n = 0; ; n++)
        {
            string name = $"{prefab}_{n}";
            if (!used.Contains(name)) return name;
        }
    }

    /// <summary>직속 자식들. 꺼져 있어도 모은다.</summary>
    private static IEnumerable<Transform> Children(Transform parent)
    {
        for (int i = 0; i < parent.childCount; i++)
            yield return parent.GetChild(i);
    }
}
