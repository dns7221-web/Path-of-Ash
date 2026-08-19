using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 던전 소품을 Game 씬에 기본 위치로 깔아주는 도구.
///
/// 메뉴: Tools → 재의 길 → 던전 소품 기본 배치
///
/// 여기 적힌 좌표는 <b>최종 배치가 아니라 출발점</b>이다. 세부 위치는 씬에서 눈으로 보며
/// 직접 옮기는 게 맞다 — 소품 배치는 숫자로 판단할 수 있는 종류의 작업이 아니다.
/// 그래서 이 도구는 다른 빌더들과 달리 <b>멱등하지 않다.</b> 이미 배치가 있으면 지우고 다시
/// 만드는 대신 그냥 멈춘다. 손으로 맞춰둔 위치를 날려버리는 게 훨씬 큰 손해이기 때문이다.
///
/// 다시 깔고 싶으면 Hierarchy에서 Props 오브젝트를 지우고 실행하면 된다.
///
/// 프리팹 연결을 유지한 채로 넣는(PrefabUtility.InstantiatePrefab) 이유: 나중에 소품의
/// 콜라이더나 크기를 고칠 때 프리팹만 고치면 씬에 놓인 것들이 전부 따라온다. 위치만
/// 인스턴스별 오버라이드로 남는다.
/// </summary>
public static class AshDungeonPropPlacer
{
    private const string TargetSceneName = "Game";
    private const string RootName = "Props";
    private const string PrefabFolder = "Assets/Project/Prefabs/Props";

    private struct Placement
    {
        public string Prefab;
        public Vector2[] Positions;

        public Placement(string prefab, params Vector2[] positions)
        {
            Prefab = prefab;
            Positions = positions;
        }
    }

    /// <summary>
    /// 기본 배치.
    ///
    /// 방은 가로 49.8 x 세로 28유닛이고, 걸어다닐 수 있는 범위는 대략 x ±22.9 / y -14 ~ +7.6이다
    /// (위쪽은 벽면이 시작되는 y=7.65에서 막힌다).
    ///
    /// 기둥 위치가 곧 전투 설계다. 망령의 돌진은 예비동작이 시작될 때 방향이 고정되므로,
    /// 기둥 뒤로 돌면 돌진이 기둥에 막힌다. 지금까지는 텅 빈 방이라 회피가 "옆으로 비키기"
    /// 하나뿐이었는데, 엄폐물이 생기면 위치 싸움이 된다.
    ///
    /// 적 스폰 지점 (-16, 6) / (16, 6) / (0, -9)와 겹치지 않게 띄웠다. 스폰 순간 소품에
    /// 끼면 적이 밀려나면서 이상한 방향으로 튄다.
    /// </summary>
    private static readonly Placement[] Placements =
    {
        // 중앙 위쪽 양옆 — 플레이어가 뒤로 돌 수 있는 주 엄폐물
        new Placement("prop_pillar_broken",
            new Vector2(-12f, 1f), new Vector2(12f, 1f)),

        // 아래쪽 — (0,-9) 스폰에서 올라오는 적의 직선 경로를 끊는다
        new Placement("prop_pillar_collapsed",
            new Vector2(-7f, -8f), new Vector2(7f, -8f)),

        // 파괴 가능한 항아리. 벽 가까이 둬서 전투 동선을 막지 않는다
        new Placement("prop_urn",
            new Vector2(-19f, -4f), new Vector2(19f, -4f), new Vector2(-3f, 5f)),

        // 바닥 장식. 콜라이더가 없어 동선에 영향이 없다
        new Placement("prop_rubble",
            new Vector2(-21f, -11f), new Vector2(21f, -11f), new Vector2(4f, -12f)),

        new Placement("prop_altar", new Vector2(0f, 2f)),

        // 계단은 위쪽 벽에 붙인다. 나중에 다음 방으로 넘어가는 출구가 될 자리다
        new Placement("prop_stairs", new Vector2(0f, 6.8f)),
    };

    [MenuItem("Tools/재의 길/던전 소품 기본 배치")]
    public static void Place()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != TargetSceneName)
        {
            Debug.LogError($"[소품 배치] 활성 씬이 '{scene.name}'이다. {TargetSceneName} 씬을 열고 실행해라.");
            return;
        }

        if (GameObject.Find(RootName) != null)
        {
            Debug.LogWarning(
                $"[소품 배치] 이미 '{RootName}'이 씬에 있다. 손으로 맞춰둔 위치를 지우지 않으려고 " +
                "아무것도 하지 않았다. 처음부터 다시 깔려면 Hierarchy에서 그 오브젝트를 지우고 다시 실행해라.");
            return;
        }

        var root = new GameObject(RootName);
        int placed = 0;

        foreach (var placement in Placements)
        {
            string path = $"{PrefabFolder}/{placement.Prefab}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                Debug.LogError($"[소품 배치] 프리팹을 못 찾았다: {path}\n" +
                               "Tools → 재의 길 → 던전 소품 슬라이스 + 프리팹 생성 을 먼저 실행해라.");
                continue;
            }

            for (int i = 0; i < placement.Positions.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                instance.transform.position = placement.Positions[i];

                // 같은 소품이 여러 개일 때 Hierarchy에서 구분되게 번호를 붙인다.
                if (placement.Positions.Length > 1)
                    instance.name = $"{placement.Prefab}_{i}";

                placed++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;

        Debug.Log($"[소품 배치] 소품 {placed}개를 '{RootName}' 아래에 깔았다. " +
                  "위치는 씬에서 직접 옮겨 맞춰라. 다 맞췄으면 Ctrl+S.");
    }
}
