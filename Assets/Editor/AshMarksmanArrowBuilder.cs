using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 사수가 쏘는 화살 프리팹을 만들고 사수 프리팹에 연결한다.
/// 메뉴: Tools → 재의 길 → 잿불 사수 화살 프리팹 생성
///
/// <b>왜 전용 화살이 필요한가.</b> 플레이어의 EmberArrow를 그대로 꽂으면 두 곳이 어긋난다.
/// 히트박스의 대상이 Enemy라 <b>적이 적을 쏘고</b>, 오브젝트 레이어가 PlayerAttack이라
/// 충돌 매트릭스상 플레이어와는 아예 판정이 일어나지 않는다. 둘 다 에러를 내지 않는다 —
/// 화살은 멀쩡히 날아가고 아무 일도 안 생긴다.
///
/// 빈 오브젝트부터 쌓지 않고 EmberArrow를 <b>복제해서</b> 만드는 이유는 잿불 파도
/// (<see cref="AshBossWaveBuilder"/>)와 같다. 투사체 하나에 Rigidbody2D, 트리거 콜라이더,
/// DamageHitbox, Projectile, 레이어가 서로 맞물려 있어서, 손으로 다시 조립하면 어긋난 곳이
/// 에러가 아니라 "가끔 안 맞는다"로만 드러난다. 이미 도는 것을 복제하면 그 사고가 없다.
///
/// 복제 후 바꾸는 것은 네 가지뿐이다 — 레이어, 피해 대상, 겉모습, 속도.
/// </summary>
public static class AshMarksmanArrowBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/EmberArrow.prefab";
    private const string OutputPath = "Assets/Project/Prefabs/VFX/AshMarksmanArrow.prefab";
    private const string MarksmanPrefabPath = "Assets/Project/Prefabs/Enemies/AshMarksman.prefab";

    private const string SheetPath =
        "Assets/Project/Art/Sprites/VFX/ash_marksman_ember_arrow_1frame_256x256.png";
    private const string ArrowSpriteName = "marksman_arrow_00";

    /// <summary>
    /// 화살 속도. 플레이어의 화살(34)보다 느리고 보스의 파도(13)보다 빠르다.
    ///
    /// 쏘는 쪽과 맞는 쪽의 요구가 다르다. 플레이어의 화살은 견제기라 빨라야 하고,
    /// 이건 <b>피해야 하는 것</b>이라 날아오는 게 보여야 한다. 사수는 12유닛쯤에서 쏘므로
    /// 22면 도달까지 약 0.55초 — 조준 0.55초와 합쳐 1초 남짓이 플레이어의 반응 시간이다.
    /// </summary>
    private const float ArrowSpeed = 22f;

    /// <summary>살아 있는 시간. 22 x 1 = 22유닛이라 사수의 최대 사거리(14)를 넉넉히 넘는다.</summary>
    private const float ArrowLifetime = 1f;

    [MenuItem("Tools/재의 길/잿불 사수 화살 프리팹 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[사수 화살] 원본 투사체를 못 찾았다: {SourcePath}");
            return;
        }

        int enemyAttackLayer = LayerMask.NameToLayer("EnemyAttack");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (enemyAttackLayer < 0 || playerLayer < 0)
        {
            Debug.LogError("[사수 화살] EnemyAttack 또는 Player 레이어가 없다. 레이어 설정을 확인해라.");
            return;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            instance.name = "AshMarksmanArrow";

            // 1. 레이어 — 플레이어 공격이 아니라 적 공격이다.
            SetLayerRecursively(instance, enemyAttackLayer);

            // 2. 피해 대상 — 플레이어만.
            ApplyTargetLayer(instance, playerLayer);

            // 3. 겉모습 — 사수의 화살 그림으로.
            ApplyVisual(instance);

            // 4. 속도 — 플레이어의 화살보다 느리게.
            ApplySpeed(instance);

            PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[사수 화살] 프리팹 저장에 실패했다: {OutputPath}");
                return;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        ConnectToMarksman();
    }

    /// <summary>
    /// 히트박스가 때릴 대상을 플레이어로 바꾼다.
    ///
    /// 실제 피해량은 <see cref="EnemyMarksman"/>이 Launch로 넘기므로 여기 damage는 기본값일 뿐이다.
    /// </summary>
    private static void ApplyTargetLayer(GameObject instance, int playerLayer)
    {
        var hitbox = instance.GetComponentInChildren<DamageHitbox>(true);
        if (hitbox == null)
        {
            Debug.LogWarning("[사수 화살] DamageHitbox를 못 찾았다. 피해가 안 들어간다.");
            return;
        }

        var serialized = new SerializedObject(hitbox);
        serialized.FindProperty("targetLayers").intValue = 1 << playerLayer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 사수 화살 그림으로 갈아 끼운다.
    ///
    /// SpriteRenderer만 바꾸지 않고 <see cref="SpriteFrameAnimator"/>의 프레임 목록까지 바꾸는
    /// 이유: 첫 프레임만 사수 화살이고 재생은 플레이어 화살 프레임으로 돌아버린다. 사수 화살은
    /// 한 장이라 목록을 한 칸으로 줄인다.
    ///
    /// 못 찾으면 그림만 그대로 두고 경고를 남긴다. 겉모습이 틀린 것보다 화살이 아예 안 나가는
    /// 쪽이 훨씬 나쁘기 때문에 여기서 멈추지 않는다 — 잿불 파도에서 한 판단과 같다.
    /// </summary>
    private static void ApplyVisual(GameObject instance)
    {
        Sprite arrowSprite = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SheetPath))
        {
            if (asset is Sprite sprite && sprite.name == ArrowSpriteName)
            {
                arrowSprite = sprite;
                break;
            }
        }

        if (arrowSprite == null)
        {
            Debug.LogWarning($"[사수 화살] {ArrowSpriteName} 스프라이트를 못 찾아 " +
                             "플레이어 화살 그림을 그대로 쓴다. 슬라이스를 먼저 실행해라.");
            return;
        }

        var renderer = instance.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer != null) renderer.sprite = arrowSprite;

        var frameAnimator = instance.GetComponentInChildren<SpriteFrameAnimator>(true);
        if (frameAnimator == null) return;

        var serialized = new SerializedObject(frameAnimator);
        SerializedProperty frames = serialized.FindProperty("frames");
        if (frames != null)
        {
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = arrowSprite;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>속도와 지속 시간을 사수용으로 낮춘다.</summary>
    private static void ApplySpeed(GameObject instance)
    {
        var projectile = instance.GetComponent<Projectile>();
        if (projectile == null)
        {
            Debug.LogWarning("[사수 화살] Projectile을 못 찾았다. 화살이 안 날아간다.");
            return;
        }

        var serialized = new SerializedObject(projectile);
        serialized.FindProperty("speed").floatValue = ArrowSpeed;
        serialized.FindProperty("lifetime").floatValue = ArrowLifetime;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 만든 화살을 사수 프리팹의 Arrow Prefab 칸에 꽂는다.
    ///
    /// 사수 프리팹이 아직 없으면 조용히 넘어간다. 순서를 강제하지 않기 위해서다 —
    /// 사수 프리팹 빌더도 이 화살이 이미 있으면 알아서 꽂는다. 어느 쪽을 먼저 돌려도 된다.
    /// </summary>
    private static void ConnectToMarksman()
    {
        var arrowRoot = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        var arrow = arrowRoot != null ? arrowRoot.GetComponent<Projectile>() : null;
        if (arrow == null)
        {
            Debug.LogError("[사수 화살] 만든 프리팹에서 Projectile을 못 찾았다.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(MarksmanPrefabPath) == null)
        {
            Debug.Log($"[사수 화살] {OutputPath} 생성 완료.\n" +
                      "사수 프리팹이 아직 없다. '잿불 사수 프리팹 생성'을 실행하면 이 화살이 꽂힌다.");
            return;
        }

        GameObject marksman = PrefabUtility.LoadPrefabContents(MarksmanPrefabPath);
        try
        {
            var ai = marksman.GetComponent<EnemyMarksman>();
            if (ai == null)
            {
                Debug.LogError("[사수 화살] 사수 프리팹에 EnemyMarksman이 없다.");
                return;
            }

            var serialized = new SerializedObject(ai);
            serialized.FindProperty("arrowPrefab").objectReferenceValue = arrow;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(marksman, MarksmanPrefabPath);
            Debug.Log($"[사수 화살] {OutputPath} 생성 후 사수에 연결했다.\n" +
                      $"속도 {ArrowSpeed}, 지속 {ArrowLifetime}초 (사거리 약 {ArrowSpeed * ArrowLifetime:F0}유닛).\n" +
                      "이제 사수가 실제로 화살을 쏜다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(marksman);
        }
    }

    /// <summary>자식까지 전부 같은 레이어로 바꾼다. 충돌 판정은 자식 콜라이더에서 일어날 수 있다.</summary>
    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
