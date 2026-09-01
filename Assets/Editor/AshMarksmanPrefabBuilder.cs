using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 사수 프리팹을 코드로 조립한다.
/// 메뉴: Tools → 재의 길 → 잿불 사수 프리팹 생성
///
/// 망령 빌더(<see cref="AshEnemyPrefabBuilder"/>)와 거의 같지만 두 가지가 다르다.
/// - 돌진 히트박스가 없다. 이 적은 붙어서 때리지 않고 화살을 쏜다.
/// - 대신 화살을 쏜다. 발사 자리는 자식 오브젝트가 아니라 <see cref="EnemyMarksman"/>이
///   쏘는 방향에서 계산한다(수정) — 자식으로 두면 +x에 고정돼 왼쪽·위·아래로 쏠 때
///   등 뒤에서 화살이 나갔다.
///
/// <b>화살은 전용 프리팹을 쓴다.</b> 플레이어의 EmberArrow를 그대로 쓰면 히트박스가
/// 적 레이어를 때리도록 돼 있어서 <b>적이 적을 쏜다.</b> 그래서 플레이어를 때리는 사본을
/// <see cref="AshMarksmanArrowBuilder"/>가 따로 만든다.
///
/// 수정 — 예전에는 "사람이 인스펙터에서 꽂아라"로 두었다. 그 결과 <b>아무것도 안 꽂힌 채로
/// 저장됐고</b>(Arrow Prefab이 비어 있었다), 그 상태의 사수는 조준만 하고 아무것도 안 쏜다.
/// 고를 여지가 없어진 지금은 <b>사람이 꽂아둔 값 → 전용 화살 → 비움</b> 순으로 이 도구가
/// 알아서 채운다. "코드가 아무거나 골라 꽂으면 안 된다"는 원칙은 고를 것이 여럿일 때의
/// 이야기지, 정답이 하나일 때 비워두라는 뜻이 아니었다.
/// </summary>
public static class AshMarksmanPrefabBuilder
{
    private const string Folder = "Assets/Project/Prefabs/Enemies";
    private const string PrefabPath = Folder + "/AshMarksman.prefab";

    // 추가 생성 — 전용 화살 프리팹. AshMarksmanArrowBuilder가 만든다.
    private const string ArrowPrefabPath = "Assets/Project/Prefabs/VFX/AshMarksmanArrow.prefab";

    private static AshPlayerSpriteSheets.CharacterSet Set => AshPlayerSpriteSheets.Marksman;

    /// <summary>적의 월드 키(유닛). 콜라이더와 화살 위치를 이 값의 비율로 잡는다.</summary>
    private static float Height =>
        AshPlayerSpriteSheets.EnemyPixelHeight / AshSpriteImportRules.CharacterPixelsPerUnit;

    [MenuItem("Tools/재의 길/잿불 사수 프리팹 생성")]
    public static void Build()
    {
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Set.ControllerPath);

        if (controller == null)
        {
            Debug.LogError($"[사수 프리팹] 컨트롤러가 없다: {Set.ControllerPath}\n" +
                           "Tools → 재의 길 → 캐릭터 스프라이트 슬라이스 → 캐릭터 애니메이션 생성 " +
                           "순서로 먼저 실행해라.");
            return;
        }

        // 이미 꽂아둔 화살 프리팹을 기억해둔다.
        //
        // SaveAsPrefabAsset은 프리팹을 통째로 덮어쓴다. 그래서 이 도구를 다시 돌리면
        // <b>사람이 인스펙터에서 꽂은 화살이 조용히 비워진다.</b> 그 뒤로는 적이 조준만 하고
        // 아무것도 안 쏘는데, 경고도 안 뜨고 코드도 멀쩡해서 원인을 찾기 어렵다.
        // 소품 배치 도구와 같은 판단이다 — 사람이 정한 값을 도구가 되돌리면 안 된다.
        Projectile keptArrow = LoadExistingArrow();

        // 수정 — 사람이 꽂아둔 것이 없으면 전용 화살을 대신 찾는다.
        // 예전에는 여기서 비어 있으면 그대로 비운 채 저장했고, 그렇게 저장된 프리팹은 안 쏜다.
        //
        // ??가 아니라 == null로 검사하는 이유: 유니티 오브젝트의 "없음"은 C#의 null이 아니라
        // 파괴된 상태를 흉내 내는 값이다. ??는 그 값을 통과시켜 깨진 참조를 그대로 꽂는다.
        if (keptArrow == null) keptArrow = LoadDedicatedArrow();

        var root = new GameObject("AshMarksman");
        try
        {
            root.layer = LayerMask.NameToLayer("Enemy");

            SetUpRenderer(root, controller);
            SetUpPhysics(root);
            root.AddComponent<Health>();

            var ai = root.AddComponent<EnemyMarksman>();
            Link(ai, root, keptArrow);

            EnsureFolder();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

            if (success)
            {
                string arrowNote = keptArrow != null
                    ? $"화살: {keptArrow.name}"
                    : "남은 일: Tools → 재의 길 → 잿불 사수 화살 프리팹 생성 을 먼저 실행해라. " +
                      "지금 상태의 사수는 조준만 하고 아무것도 안 쏜다.";

                Debug.Log($"[사수 프리팹] 생성 완료 → {PrefabPath}\n{arrowNote}");
            }
            else
            {
                Debug.LogError($"[사수 프리팹] 저장 실패: {PrefabPath}");
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
            Debug.LogWarning($"[사수 프리팹] {Set.SpriteName("walk", 0)} 스프라이트를 못 찾았다. " +
                             "슬라이스를 먼저 실행해라.", root);

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        // 화면 밖에서도 애니메이션을 돌린다. 방이 카메라보다 좁아 대부분 보이지만,
        // 멈춰 있으면 사망 연출이 끝나지 않아 풀로 안 돌아간다.
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

        // 망령과 같은 규격이다. 탑다운이라 콜라이더는 발밑 그림자 정도의 납작한 타원이고,
        // 세로는 키가 아니라 <b>바닥에서 차지하는 깊이</b>다.
        var collider = root.AddComponent<CapsuleCollider2D>();
        collider.direction = CapsuleDirection2D.Horizontal;
        collider.size = new Vector2(Height * 0.5f, Height * 0.28f);
        collider.offset = new Vector2(0f, collider.size.y * 0.5f);
    }

    /// <summary>
    /// 이미 있는 프리팹에서 꽂아둔 화살을 읽어온다. 없으면 null.
    ///
    /// 프리팹 에셋에서 바로 컴포넌트를 읽는다. 씬에 꺼내놓지 않아도 값을 볼 수 있다.
    /// </summary>
    private static Projectile LoadExistingArrow()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing == null) return null;

        var ai = existing.GetComponent<EnemyMarksman>();
        if (ai == null) return null;

        var serialized = new SerializedObject(ai);
        return serialized.FindProperty("arrowPrefab").objectReferenceValue as Projectile;
    }

    /// <summary>
    /// 추가 생성 — 전용 화살 프리팹(AshMarksmanArrow)에서 Projectile을 읽어온다. 없으면 null.
    ///
    /// 여기서 EmberArrow로 넘어가지 않는 이유: 그건 플레이어의 화살이라 <b>적을 때린다.</b>
    /// 꽂히면 사수가 자기 편을 쏘는데, 화면에서는 "화살이 나가긴 하는" 그림이라
    /// 안 꽂힌 것보다 원인을 찾기 어렵다.
    /// </summary>
    private static Projectile LoadDedicatedArrow()
    {
        var arrowRoot = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefabPath);
        return arrowRoot != null ? arrowRoot.GetComponent<Projectile>() : null;
    }

    private static void Link(EnemyMarksman ai, GameObject root, Projectile keptArrow)
    {
        var serialized = new SerializedObject(ai);
        serialized.FindProperty("playerLayer").intValue = 1 << LayerMask.NameToLayer("Player");
        serialized.FindProperty("animator").objectReferenceValue = root.GetComponent<Animator>();
        serialized.FindProperty("spriteRenderer").objectReferenceValue = root.GetComponent<SpriteRenderer>();

        // 사람이 꽂아둔 값이 있으면 그것을, 없으면 전용 화살을 꽂는다(수정).
        // 둘 다 없을 때만 비운 채로 둔다 — 그때는 로그가 무엇을 해야 하는지 알려준다.
        if (keptArrow != null)
            serialized.FindProperty("arrowPrefab").objectReferenceValue = keptArrow;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Project/Prefabs", "Enemies");
    }
}
