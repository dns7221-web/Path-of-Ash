using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 망령 프리팹을 코드로 조립한다.
/// 메뉴: Tools → 재의 길 → 잿불 망령 프리팹 생성
/// </summary>
public static class AshEnemyPrefabBuilder
{
    private const string Folder = "Assets/Project/Prefabs/Enemies";
    private const string PrefabPath = Folder + "/AshEmberWraith.prefab";
    private const string ControllerPath = "Assets/Project/Animations/Enemy/Wraith.controller";

    // 추가 생성(2026-09-15) — 돌진 예고선 프리팹. AshWraithTelegraphBuilder가 만든다.
    private const string TelegraphPrefabPath = "Assets/Project/Prefabs/VFX/WraithChargeTelegraph.prefab";

    // 추가 생성(2026-09-15) — 돌진 출발 자국 프리팹. 같은 빌더의 "망령 돌진 출발 자국 생성"이 만든다.
    private const string LaunchPrefabPath = "Assets/Project/Prefabs/VFX/WraithChargeLaunch.prefab";

    private static float Height =>
        AshPlayerSpriteSheets.EnemyPixelHeight / AshSpriteImportRules.CharacterPixelsPerUnit;

    /// <summary>
    /// 추가 생성(2026-09-15) — 몸 콜라이더 폭(유닛).
    ///
    /// <b>왜 키 비율(Height x 0.5 = 2.94)에서 떼어 냈나.</b> 2026-09-15에 망령 그림이 바뀌면서 체형이 넓어졌다.
    /// 옛 망령은 폭 117px 안팎의 구부정한 몸이라 2.94가 발 사이를 덮었지만, 새 망령은 몸통 키가 같은데 폭이 159px(1.36배)이고
    /// 양쪽 발톱을 땅에 짚고 선다. 2.94면 몸 가운데 3분의 1만 덮어서 <b>발톱이나 옆구리를 베어도 헛친다.</b>
    /// 2.94 x 1.36 = 4.0. 그림 위에 판정을 겹친 목업(월드 단위)에서 뒷다리~앞발 사이를 덮고 발톱 끝은 남는 폭이다 —
    /// 그림보다 조금 좁게 맞아야 스친 공격이 억울하지 않다.
    ///
    /// 세로(바닥 깊이)는 키 비율 그대로다. 몸통 키가 옛 망령과 같아서 바닥에서 차지하는 깊이도 같다.
    /// 사수·자폭병은 새 그림으로도 발밑이 옛 폭(2.94) 안에 들어와서 그 빌더들은 바꾸지 않았다.
    /// </summary>
    private const float BodyWidth = 4f;

    [MenuItem("Tools/재의 길/잿불 망령 프리팹 생성")]
    public static void Build()
    {
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[망령 프리팹] 컨트롤러가 없다: {ControllerPath}");
            return;
        }

        var root = new GameObject("AshEmberWraith");
        try
        {
            root.layer = LayerMask.NameToLayer("Enemy");
            SetUpRenderer(root, controller);
            SetUpPhysics(root);
            var health = root.AddComponent<Health>();
            var hitbox = CreateChargeHitbox(root);
            var ai = root.AddComponent<EnemyWraith>();
            Link(ai, root, hitbox);

            EnsureFolder();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (success) Debug.Log($"[망령 프리팹] 생성 완료 → {PrefabPath}");
            else Debug.LogError($"[망령 프리팹] 저장 실패: {PrefabPath}");

            _ = health;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void SetUpRenderer(GameObject root, RuntimeAnimatorController controller)
    {
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sortingLayerName = "Entity";

        var set = AshPlayerSpriteSheets.Wraith;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(
                     $"{set.FolderPath}/{set.Sheets[0].FileName}.png"))
        {
            if (asset is Sprite sprite && sprite.name == set.SpriteName("walk", 0))
            {
                renderer.sprite = sprite;
                break;
            }
        }

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
    }

    private static void SetUpPhysics(GameObject root)
    {
        var body = root.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var collider = root.AddComponent<CapsuleCollider2D>();
        collider.direction = CapsuleDirection2D.Horizontal;
        // 수정(2026-09-15) — 폭 Height * 0.5(2.94) → BodyWidth(4). 이유는 BodyWidth 설명 참고. 세로와 오프셋은 그대로다.
        collider.size = new Vector2(BodyWidth, Height * 0.28f);
        collider.offset = new Vector2(0f, collider.size.y * 0.5f);
    }

    private static DamageHitbox CreateChargeHitbox(GameObject root)
    {
        var child = new GameObject("ChargeHitbox");
        child.transform.SetParent(root.transform, false);
        child.layer = LayerMask.NameToLayer("EnemyAttack");

        var collider = child.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        // 수정(공격 판정 점검 시점): 세로 위치를 0.42 → 0.12, 높이를 0.45 → 0.50으로 바꿨다.
        // 플레이어 검 히트박스와 같은 이유다 — 탑다운에서 y는 높이가 아니라 바닥 위치인데
        // 판정이 스프라이트 가슴 높이에 떠 있었다. 플레이어 몸통(y 0 ~ 1.67)과 이전 돌진
        // 판정(1.15 ~ 3.79)은 0.52유닛만 겹쳤다.
        collider.size = new Vector2(Height * 0.7f, Height * 0.5f);
        collider.offset = new Vector2(Height * 0.35f, Height * 0.12f);

        var hitbox = child.AddComponent<DamageHitbox>();
        var serialized = new SerializedObject(hitbox);
        serialized.FindProperty("damage").intValue = 1;
        serialized.FindProperty("targetLayers").intValue = 1 << LayerMask.NameToLayer("Player");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return hitbox;
    }

    private static void Link(EnemyWraith ai, GameObject root, DamageHitbox hitbox)
    {
        var serialized = new SerializedObject(ai);
        serialized.FindProperty("playerLayer").intValue = 1 << LayerMask.NameToLayer("Player");
        serialized.FindProperty("animator").objectReferenceValue = root.GetComponent<Animator>();
        serialized.FindProperty("spriteRenderer").objectReferenceValue = root.GetComponent<SpriteRenderer>();
        serialized.FindProperty("chargeHitbox").objectReferenceValue = hitbox;

        // 추가 생성(2026-09-15) — 예고선이 이미 만들어져 있으면 꽂는다. 망령 프리팹을 다시 조립해도 선이 조용히
        // 빠지지 않게 한다. 없으면 비워 둔다 — 예고선 빌더가 나중에 꽂으므로 어느 쪽을 먼저 돌려도 된다.
        var telegraphRoot = AssetDatabase.LoadAssetAtPath<GameObject>(TelegraphPrefabPath);
        if (telegraphRoot != null && telegraphRoot.TryGetComponent(out TelegraphLine telegraph))
            serialized.FindProperty("chargeTelegraphPrefab").objectReferenceValue = telegraph;

        // 추가 생성(2026-09-15) — 출발 자국도 같은 약속이다. 있으면 꽂고, 없으면 비워 두면 출발 자국 메뉴가 나중에 꽂는다.
        var launchRoot = AssetDatabase.LoadAssetAtPath<GameObject>(LaunchPrefabPath);
        if (launchRoot != null)
            serialized.FindProperty("chargeLaunchEffectPrefab").objectReferenceValue = launchRoot;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Project/Prefabs", "Enemies");
    }
}
