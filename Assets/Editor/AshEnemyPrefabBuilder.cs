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

    private static float Height =>
        AshPlayerSpriteSheets.EnemyPixelHeight / AshSpriteImportRules.CharacterPixelsPerUnit;

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
        collider.size = new Vector2(Height * 0.5f, Height * 0.28f);
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
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Project/Prefabs", "Enemies");
    }
}
