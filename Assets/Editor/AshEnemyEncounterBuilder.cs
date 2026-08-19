using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 추가 생성 — 현재 Game 씬에 잿불 망령 스포너와 기본 스폰 지점을 배치한다.
/// 메뉴: Tools → 재의 길 → 잿불 망령 전투 배치
/// </summary>
public static class AshEnemyEncounterBuilder
{
    private const string GameScenePath = "Assets/Scenes/Game.unity";
    private const string EnemyPrefabPath =
        "Assets/Project/Prefabs/Enemies/AshEmberWraith.prefab";

    /// <summary>
    /// 적이 나타날 위치(월드 좌표).
    ///
    /// 수정(코드 리뷰 시점): (±6, ±2)에서 아래 값으로 넓혔다. 이전 값은 방이 13x9유닛이라는
    /// AshProjectSetup 주석의 규격을 전제로 잡힌 것인데, 실제 Game 씬의 방은 <b>49.8 x 28유닛</b>
    /// (벽이 ±24.9 / ±14.1)이다. 그래서 적 셋이 화면 중앙 한 뼘 안에 겹쳐 스폰됐고,
    /// Enemy x Enemy 충돌이 켜져 있어 서로 밀어냈으며, 원점에서 시작하는 플레이어와도 겹쳤다.
    ///
    /// 위치를 플레이어를 <b>둘러싸는</b> 형태로 잡은 이유: 셋을 한쪽에 몰아두면 반대쪽으로
    /// 걸어가면 그만이라 스태미나를 쓸 일이 없다. 퇴로를 나눠 막아야 대시로 뚫는 선택이 생긴다.
    /// 벽(±24.9 / ±14.1)에서는 충분히 떨어뜨려 스폰 즉시 벽에 끼는 것을 막는다.
    /// </summary>
    private static readonly Vector2[] DefaultSpawnPositions =
    {
        new Vector2(-16f, 6f),
        new Vector2(16f, 6f),
        new Vector2(0f, -9f),
    };

    /// <summary>현재 Game 씬에 기존 배치를 교체하고 새 전투 구성을 만든다.</summary>
    [MenuItem("Tools/재의 길/잿불 망령 전투 배치")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != GameScenePath)
        {
            Debug.LogError($"[망령 전투] Game 씬을 연 뒤 실행해야 한다: {GameScenePath}");
            return;
        }

        EnemyWraith enemyPrefab = AssetDatabase.LoadAssetAtPath<EnemyWraith>(EnemyPrefabPath);
        if (enemyPrefab == null)
        {
            Debug.LogError("[망령 전투] 먼저 '잿불 망령 프리팹 생성' 메뉴를 실행해야 한다.");
            return;
        }

        // 같은 도구를 여러 번 실행해도 스포너가 중복되지 않도록 기존 도구 생성물만 교체한다.
        GameObject existing = GameObject.Find("EnemyEncounter");
        if (existing != null) Object.DestroyImmediate(existing);

        var root = new GameObject("EnemyEncounter");
        var points = CreateSpawnPoints(root.transform);
        var spawner = root.AddComponent<EnemySpawner>();
        LinkSpawner(spawner, enemyPrefab, points);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log("[망령 전투] 배치 완료 — 망령 3마리 전투 구성. 씬을 저장하세요.");
    }

    /// <summary>방 중앙을 비워 두는 삼각형 형태의 기본 스폰 지점을 만든다.</summary>
    private static Transform[] CreateSpawnPoints(Transform parent)
    {
        var points = new Transform[DefaultSpawnPositions.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var point = new GameObject($"SpawnPoint_{i + 1}").transform;
            point.SetParent(parent, false);
            point.position = DefaultSpawnPositions[i];
            points[i] = point;
        }

        return points;
    }

    /// <summary>프리팹, 스폰 지점과 런 매니저를 스포너에 연결한다.</summary>
    private static void LinkSpawner(
        EnemySpawner spawner,
        EnemyWraith enemyPrefab,
        Transform[] points)
    {
        var serialized = new SerializedObject(spawner);
        serialized.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
        serialized.FindProperty("spawnPoints").arraySize = points.Length;
        for (int i = 0; i < points.Length; i++)
            serialized.FindProperty("spawnPoints").GetArrayElementAtIndex(i).objectReferenceValue = points[i];

        serialized.FindProperty("spawnCount").intValue = points.Length;
        serialized.FindProperty("runManager").objectReferenceValue =
            Object.FindFirstObjectByType<RunManager>();
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
