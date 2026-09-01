using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 자폭병 프리팹을 코드로 조립한다.
/// 메뉴: Tools → 재의 길 → 잿불 자폭병 프리팹 생성
///
/// 사수 빌더(<see cref="AshMarksmanPrefabBuilder"/>)와 규격은 같고 두 가지가 다르다.
/// - 쏘는 것이 없으므로 투사체도, 발사 자리도 없다. 폭발은 반경 판정이라 히트박스가 필요 없다.
/// - <b>체력을 여기서 정한다.</b> 아래 <see cref="MaxHealth"/> 참고.
///
/// 폭발 이펙트는 <see cref="AshBomberBlastBuilder"/>가 만든 것을 꽂는다. 사람이 인스펙터에서
/// 꽂아둔 값이 있으면 그것을 우선한다.
/// </summary>
public static class AshBomberPrefabBuilder
{
    private const string Folder = "Assets/Project/Prefabs/Enemies";
    private const string PrefabPath = Folder + "/AshBomber.prefab";
    private const string BlastPrefabPath = "Assets/Project/Prefabs/VFX/BomberBlast.prefab";

    /// <summary>
    /// 자폭병의 최대 체력. 망령·사수(3)보다 하나 적다.
    ///
    /// 이 값이 곧 "점화가 시작된 뒤에도 끝낼 수 있는가"의 난이도다. 3이면 점화 0.667초 안에
    /// 세 번을 때려야 해서 사실상 못 막고, 회피 하나만 남는다. 2면 그 판단이 살아 있다.
    ///
    /// <b>도구가 정하는 이유:</b> SaveAsPrefabAsset은 프리팹을 통째로 덮어쓴다. 사람이
    /// 인스펙터에서 맞춰둔 체력은 이 도구를 다시 돌리는 순간 조용히 3으로 돌아간다.
    /// 사수의 화살 칸이 비워지던 것과 같은 사고라, 값을 여기 적어두고 그 이유를 남긴다.
    /// </summary>
    private const int MaxHealth = 2;

    private static AshPlayerSpriteSheets.CharacterSet Set => AshPlayerSpriteSheets.Bomber;

    /// <summary>적의 월드 키(유닛). 콜라이더를 이 값의 비율로 잡는다.</summary>
    private static float Height =>
        AshPlayerSpriteSheets.EnemyPixelHeight / AshSpriteImportRules.CharacterPixelsPerUnit;

    [MenuItem("Tools/재의 길/잿불 자폭병 프리팹 생성")]
    public static void Build()
    {
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Set.ControllerPath);

        if (controller == null)
        {
            Debug.LogError($"[자폭병 프리팹] 컨트롤러가 없다: {Set.ControllerPath}\n" +
                           "Tools → 재의 길 → 캐릭터 스프라이트 슬라이스 → 캐릭터 애니메이션 생성 " +
                           "순서로 먼저 실행해라.");
            return;
        }

        // 사람이 꽂아둔 폭발 이펙트를 기억해둔다. 없으면 전용 프리팹을 찾는다.
        GameObject keptEffect = LoadExistingEffect();
        if (keptEffect == null) keptEffect = LoadDedicatedBlast();

        var root = new GameObject("AshBomber");
        try
        {
            root.layer = LayerMask.NameToLayer("Enemy");

            SetUpRenderer(root, controller);
            SetUpPhysics(root);
            SetUpHealth(root);

            var ai = root.AddComponent<EnemyBomber>();
            Link(ai, root, keptEffect);

            EnsureFolder();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

            if (success)
            {
                string effectNote = keptEffect != null
                    ? $"폭발 이펙트: {keptEffect.name}"
                    : "남은 일: Tools → 재의 길 → 자폭병 폭발 이펙트 생성 을 실행해라. " +
                      "지금은 아무 그림 없이 피해만 들어가서, 플레이어가 왜 맞았는지 알 수 없다.";

                Debug.Log($"[자폭병 프리팹] 생성 완료 → {PrefabPath}\n체력 {MaxHealth}. {effectNote}");
            }
            else
            {
                Debug.LogError($"[자폭병 프리팹] 저장 실패: {PrefabPath}");
            }
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

        // 첫 걷기 프레임을 미리 넣어둔다. 안 넣으면 씬에 배치했을 때 아무것도 안 보여서
        // 위치를 눈으로 맞출 수 없다. 실행하면 Animator가 덮어쓴다.
        string sheetPath = $"{Set.FolderPath}/{Set.Sheets[0].FileName}.png";
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
        {
            if (asset is Sprite sprite && sprite.name == Set.SpriteName("walk", 0))
            {
                renderer.sprite = sprite;
                break;
            }
        }

        if (renderer.sprite == null)
            Debug.LogWarning($"[자폭병 프리팹] {Set.SpriteName("walk", 0)} 스프라이트를 못 찾았다. " +
                             "슬라이스를 먼저 실행해라.", root);

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        // 화면 밖에서도 애니메이션을 돌린다. 멈춰 있으면 사망 연출이 끝나지 않아 풀로 안 돌아간다.
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

        // 망령·사수와 같은 규격이다. 탑다운이라 콜라이더는 발밑 그림자 정도의 납작한 타원이고,
        // 세로는 키가 아니라 바닥에서 차지하는 깊이다.
        var collider = root.AddComponent<CapsuleCollider2D>();
        collider.direction = CapsuleDirection2D.Horizontal;
        collider.size = new Vector2(Height * 0.5f, Height * 0.28f);
        collider.offset = new Vector2(0f, collider.size.y * 0.5f);
    }

    /// <summary>체력을 붙이고 최대치를 낮춘다. 이유는 <see cref="MaxHealth"/> 참고.</summary>
    private static void SetUpHealth(GameObject root)
    {
        var health = root.AddComponent<Health>();

        var serialized = new SerializedObject(health);
        serialized.FindProperty("maxHealth").intValue = MaxHealth;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>이미 있는 프리팹에서 사람이 꽂아둔 폭발 이펙트를 읽어온다. 없으면 null.</summary>
    private static GameObject LoadExistingEffect()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null) return null;

        var ai = existing.GetComponent<EnemyBomber>();
        if (ai == null) return null;

        var serialized = new SerializedObject(ai);
        return serialized.FindProperty("explosionEffectPrefab").objectReferenceValue as GameObject;
    }

    /// <summary>전용 폭발 이펙트 프리팹을 찾는다. 없으면 null.</summary>
    private static GameObject LoadDedicatedBlast()
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(BlastPrefabPath);
    }

    private static void Link(EnemyBomber ai, GameObject root, GameObject keptEffect)
    {
        var serialized = new SerializedObject(ai);
        serialized.FindProperty("playerLayer").intValue = 1 << LayerMask.NameToLayer("Player");
        serialized.FindProperty("animator").objectReferenceValue = root.GetComponent<Animator>();
        serialized.FindProperty("spriteRenderer").objectReferenceValue = root.GetComponent<SpriteRenderer>();

        if (keptEffect != null)
            serialized.FindProperty("explosionEffectPrefab").objectReferenceValue = keptEffect;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Project/Prefabs", "Enemies");
    }
}
