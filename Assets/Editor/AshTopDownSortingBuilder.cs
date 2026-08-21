using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 탑다운에 필요한 Y축 정렬과 소품 충돌체를 한 번에 맞춘다.
///
/// 메뉴: Tools → 재의 길 → 탑다운 정렬·충돌 세팅
///
/// 왜 필요한가:
/// 캐릭터가 기둥보다 위에 서 있어도 기둥 앞에 그려지는 문제가 있었다. 원인은 두 가지다.
///
/// 1. 투명 정렬 모드가 <c>Default</c>였다. 탑다운은 <c>CustomAxis</c> + 축 (0,1,0)이어야
///    화면에서 아래에 있는 것이 앞에 그려진다. 축은 이미 (0,1,0)으로 맞춰져 있었는데
///    모드가 꺼져 있어서 아무 효과가 없었다.
///
/// 2. 모든 SpriteRenderer의 <c>spriteSortPoint</c>가 <c>Center</c>였다. 캐릭터 스프라이트는
///    256px 프레임에 피벗이 발밑이라, Center로 정렬하면 <b>실제 발보다 4.5유닛 위</b>를
///    기준으로 앞뒤를 판단한다. 발이 기둥보다 아래에 있어도 가슴이 위에 있으면 뒤로 밀린다.
///    <c>Pivot</c>이어야 발 위치로 정렬된다.
///
/// 소품 충돌체를 같이 다루는 이유: 손으로 배치한 소품 일부에 충돌체가 없어서 그냥 통과됐다.
/// 정렬만 고치면 "앞뒤는 맞는데 뚫고 지나간다"가 되어 오히려 더 어색해진다.
/// </summary>
public static class AshTopDownSortingBuilder
{
    private const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
    private const string PropFolder = "Assets/Project/Prefabs/Props";

    /// <summary>
    /// 충돌체를 붙이지 않을 소품.
    ///
    /// 계단은 밟고 지나가는 곳이고, 횃불은 벽에 걸려 있어 바닥을 차지하지 않는다.
    /// 여기에 충돌체를 붙이면 보이지 않는 벽이 생겨 "왜 여기서 막히지?"가 된다.
    /// </summary>
    private static readonly HashSet<string> NoColliderProps = new HashSet<string>
    {
        "prop_stairs",
        "prop_torch",
    };

    /// <summary>충돌체가 차지할 스프라이트 가로 비율. 어깨보다 발이 좁으므로 전부 덮지 않는다.</summary>
    private const float ColliderWidthRatio = 0.75f;

    /// <summary>충돌체가 차지할 스프라이트 세로 비율. 탑다운은 밑동만 막고 윗부분은 지나갈 수 있어야 한다.</summary>
    private const float ColliderHeightRatio = 0.28f;

    [MenuItem("Tools/재의 길/탑다운 정렬·충돌 세팅")]
    public static void Build()
    {
        ApplySortAxis();
        int sceneCount = ApplySortPointToScene();
        int prefabCount = ApplySortPointToPrefabs();
        int colliderCount = AddMissingPropColliders();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[탑다운 세팅] 정렬 축 적용 완료.\n" +
                  $"정렬 기준점(Pivot) — 씬 {sceneCount}개, 프리팹 {prefabCount}개.\n" +
                  $"소품 충돌체 {colliderCount}개 추가.\n" +
                  "씬을 저장해라.");
    }

    /// <summary>
    /// 투명 정렬을 Y축 기준으로 바꾼다.
    ///
    /// 프로젝트 설정과 URP 2D 렌더러 <b>양쪽</b>을 건드리는 이유:
    /// 2D 렌더러는 자기 설정을 우선 쓰지만, 렌더러를 갈아끼우거나 다른 카메라가 끼어들면
    /// 프로젝트 설정이 다시 기준이 된다. 둘이 어긋나 있으면 그때 정렬이 조용히 바뀐다.
    /// </summary>
    private static void ApplySortAxis()
    {
        var axis = new Vector3(0f, 1f, 0f);

        // 프로젝트 전역 설정. 런타임 API지만 에디터에서 바꾸면 설정 파일에 저장된다.
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = axis;

        var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Renderer2DPath);
        if (renderer == null)
        {
            Debug.LogWarning($"[탑다운 세팅] 2D 렌더러를 못 찾았다: {Renderer2DPath}\n" +
                             "프로젝트 설정만 바꿨다.");
            return;
        }

        var serialized = new SerializedObject(renderer);
        SerializedProperty mode = serialized.FindProperty("m_TransparencySortMode");
        SerializedProperty sortAxis = serialized.FindProperty("m_TransparencySortAxis");

        if (mode == null || sortAxis == null)
        {
            Debug.LogWarning("[탑다운 세팅] 2D 렌더러의 정렬 항목을 못 찾았다. URP 버전을 확인해라.");
            return;
        }

        mode.enumValueIndex = (int)TransparencySortMode.CustomAxis;
        sortAxis.vector3Value = axis;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
    }

    /// <summary>열린 씬의 SpriteRenderer 정렬 기준점을 발밑(Pivot)으로 바꾼다.</summary>
    private static int ApplySortPointToScene()
    {
        int count = 0;
        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (renderer.spriteSortPoint == SpriteSortPoint.Pivot) continue;

            Undo.RecordObject(renderer, "탑다운 정렬 세팅");
            renderer.spriteSortPoint = SpriteSortPoint.Pivot;
            EditorUtility.SetDirty(renderer);
            count++;
        }
        return count;
    }

    /// <summary>프리팹 쪽도 바꾼다. 런타임에 생성되는 보스·유물이 씬 오브젝트와 같은 규칙을 타야 한다.</summary>
    private static int ApplySortPointToPrefabs()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Project/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            try
            {
                bool changed = false;
                foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.spriteSortPoint == SpriteSortPoint.Pivot) continue;
                    renderer.spriteSortPoint = SpriteSortPoint.Pivot;
                    changed = true;
                    count++;
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return count;
    }

    /// <summary>
    /// 몸으로 막아주는 충돌체가 없는 소품에 밑동 충돌체를 붙인다.
    ///
    /// "충돌체가 아예 없는가"가 아니라 <b>"트리거가 아닌 충돌체가 있는가"</b>로 판정하는 이유:
    /// 상자는 F 상호작용용 트리거를 이미 갖고 있다. 트리거는 <b>감지만 하고 막지는 않는다.</b>
    /// 충돌체 유무만 보면 상자는 "있음"으로 걸러져, 실제로는 몸이 그대로 통과하는데도
    /// 도구가 손대지 않는다. 상자를 뚫고 지나가던 것이 정확히 이 경우였다.
    ///
    /// 스프라이트 전체를 덮지 않고 <b>아랫부분만</b> 막는 이유:
    /// 탑다운에서 기둥의 윗부분은 "높이"를 그린 것이지 바닥을 차지하는 게 아니다.
    /// 전체를 막으면 기둥 뒤로 돌아갈 수 없어서 방이 실제보다 훨씬 좁게 느껴진다.
    /// </summary>
    private static int AddMissingPropColliders()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (NoColliderProps.Contains(name)) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            try
            {
                if (HasSolidCollider(root)) continue;

                var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer == null || renderer.sprite == null)
                {
                    Debug.LogWarning($"[탑다운 세팅] {name}에 스프라이트가 없어 충돌체를 못 만들었다.");
                    continue;
                }

                Bounds bounds = renderer.sprite.bounds;
                var size = new Vector2(bounds.size.x * ColliderWidthRatio,
                                       bounds.size.y * ColliderHeightRatio);
                // 스프라이트 경계는 피벗 기준이라 그대로 쓰면 밑동에 정확히 놓인다.
                var offset = new Vector2(bounds.center.x, bounds.min.y + size.y * 0.5f);

                GameObject host = GetBlockerHost(root);
                var box = host.AddComponent<BoxCollider2D>();
                box.size = size;
                box.offset = offset;
                box.isTrigger = false;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[탑다운 세팅] {name} — 밑동 충돌체 {size.x:F2} x {size.y:F2} 추가" +
                          $"{(host != root ? " (Blocker 자식으로)" : "")}.");
                count++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return count;
    }

    /// <summary>트리거가 아닌, 실제로 몸을 막는 충돌체가 하나라도 있는가.</summary>
    private static bool HasSolidCollider(GameObject root)
    {
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
        {
            if (!collider.isTrigger) return true;
        }
        return false;
    }

    /// <summary>
    /// 막는 충돌체를 붙일 오브젝트를 고른다.
    ///
    /// 레이어는 콜라이더가 아니라 <b>게임오브젝트</b>에 붙는다. 상자는 Pickup 레이어라
    /// 거기에 그냥 충돌체를 더하면 충돌 매트릭스상 Player와 안 부딪힌다.
    /// 그래서 Wall 레이어를 쓰는 자식을 따로 만들어 거기에 붙인다.
    /// 이미 Wall 레이어인 소품(기둥 등)은 자식 없이 본체에 바로 붙인다.
    /// </summary>
    private static GameObject GetBlockerHost(GameObject root)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer < 0 || root.layer == wallLayer) return root;

        Transform existing = root.transform.Find("Blocker");
        if (existing != null)
        {
            existing.gameObject.layer = wallLayer;
            return existing.gameObject;
        }

        var blocker = new GameObject("Blocker") { layer = wallLayer };
        blocker.transform.SetParent(root.transform, false);
        return blocker;
    }
}
