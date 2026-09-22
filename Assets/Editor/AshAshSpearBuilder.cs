using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-20, 재의 창) — 보스의 원거리 투사체를 만들고 보스 프리팹에 연결한다.
///
/// 메뉴: Tools → 재의 길 → 프리팹 → 재의 창 투사체 생성
///
/// <b>왜 빈 오브젝트부터 쌓지 않고 <see cref="EmberArrow"/>를 복제하나.</b>
/// 투사체 하나에 Rigidbody2D, 트리거 충돌체, DamageHitbox, Projectile, 레이어, 프레임 애니메이터가
/// 서로 맞물려 있다. 손으로 다시 조립하면 어딘가 하나가 어긋나는데, 그런 어긋남은 에러가 아니라
/// "가끔 안 맞는다"로만 나타나 원인을 찾기 어렵다. 잿불 파도를 만들 때도 같은 이유로 복제했다.
///
/// 복제 후 바꾸는 것은 네 가지다 — 레이어, 피해 대상, 겉모습(창 시트), 속도.
///
/// <b>그림이 아직 없어도 만들어 둔다.</b> 시트를 못 찾으면 화살 그림을 그대로 쓰고 경고만 남긴다.
/// 모양이 임시인 것보다 패턴이 아예 안 나오는 쪽이 훨씬 나쁘다 — 그러면 타이밍·사거리·회피를
/// 확인할 방법이 사라진다.
/// </summary>
public static class AshAshSpearBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/EmberArrow.prefab";
    private const string SheetPath = "Assets/Project/Art/Sprites/VFX/vfx_ashking_ash_spear_6frames_1536x256.png";
    private const string OutputPath = "Assets/Project/Prefabs/VFX/BossAshSpear.prefab";
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemies/BossAshKing.prefab";

    /// <summary>
    /// 창의 속도. 화살(34)보다 느리고 파도(13)보다 빠르다.
    ///
    /// 창은 <b>겨누는 것을 보고 피하는</b> 패턴이다. 회피 판단은 조준선이 떠 있는 동안 이미
    /// 끝나야 하고, 날아가는 동안 또 한 번 피하게 만들면 예고가 두 번이 되어 느슨해진다.
    /// 그래서 날아가는 구간은 빠르게 지나간다.
    /// </summary>
    private const float SpearSpeed = 20f;

    /// <summary>살아 있는 시간. 20 × 1.1 = 약 22유닛으로, 보스의 창 사거리(18)를 조금 넘긴다.</summary>
    private const float SpearLifetime = 1.1f;

    /// <summary>프레임 애니메이션 속도(fps). 6프레임이 0.5초에 한 바퀴 돈다.</summary>
    private const float SpearFps = 12f;

    [MenuItem("Tools/재의 길/프리팹/재의 창 투사체 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[재의 창] 원본 투사체를 못 찾았다: {SourcePath}");
            return;
        }

        int enemyAttackLayer = LayerMask.NameToLayer("EnemyAttack");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (enemyAttackLayer < 0 || playerLayer < 0)
        {
            Debug.LogError("[재의 창] EnemyAttack 또는 Player 레이어가 없다. 레이어 설정을 확인해라.");
            return;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            // 원본(플레이어 화살)의 불티 꼬리를 걷어낸다. 적 불티는 흰 심을 뺀 색이라
            // 플레이어용 꼬리를 그대로 달면 색 규칙이 어긋난다. 필요하면 몬스터 파티클
            // 빌더 쪽에 창 전용 꼬리를 따로 넣는다.
            AshPlayerParticleBuilder.StripGarnish(instance);

            instance.name = "BossAshSpear";

            // 1. 레이어 — 플레이어 공격이 아니라 적 공격이다.
            //    이걸 안 바꾸면 충돌 매트릭스상 플레이어를 아예 못 맞힌다.
            SetLayerRecursively(instance, enemyAttackLayer);

            // 2. 피해 대상 — 플레이어만.
            var hitbox = instance.GetComponentInChildren<DamageHitbox>(true);
            if (hitbox != null)
            {
                var hitboxObject = new SerializedObject(hitbox);
                hitboxObject.FindProperty("targetLayers").intValue = 1 << playerLayer;
                // 실제 피해량은 EnemyBoss가 Launch에서 넘겨주므로 여기 값은 기본값일 뿐이다.
                hitboxObject.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[재의 창] DamageHitbox를 못 찾았다. 피해가 안 들어갈 수 있다.");
            }

            // 3. 겉모습 — 창 시트로 바꾼다(없으면 화살 그림을 그대로 둔다).
            ApplyVisual(instance);

            // 4. 속도와 수명.
            var projectile = instance.GetComponent<Projectile>();
            if (projectile != null)
            {
                var projectileObject = new SerializedObject(projectile);
                projectileObject.FindProperty("speed").floatValue = SpearSpeed;
                projectileObject.FindProperty("lifetime").floatValue = SpearLifetime;
                projectileObject.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[재의 창] 프리팹 저장에 실패했다: {OutputPath}");
                return;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        ConnectToBoss();
    }

    /// <summary>
    /// 창 시트의 스프라이트를 복제본에 넣는다.
    ///
    /// 시트를 못 찾거나 아직 잘리지 않았으면 화살 그림을 그대로 두고 경고만 남긴다.
    /// 순서는 <b>파일 이름순</b>으로 잡는다 — 슬라이서가 프레임마다 뒤에 번호를 붙이므로
    /// 이름순이 곧 프레임 순서다.
    /// </summary>
    private static void ApplyVisual(GameObject instance)
    {
        Sprite[] frames = AssetDatabase.LoadAllAssetsAtPath(SheetPath)
            .OfType<Sprite>()
            .OrderBy(sprite => sprite.name, System.StringComparer.Ordinal)
            .ToArray();

        if (frames.Length == 0)
        {
            Debug.LogWarning($"[재의 창] 창 그림을 못 찾아 화살 그림을 그대로 쓴다: {SheetPath}\n" +
                             "원본을 vfx_ashking_ash_spear_6frames_raw.png 로 넣고 " +
                             "Tools → 재의 길 → 그림 → 원본 시트 정규화 → VFX 스프라이트 슬라이스 순서로 실행한 뒤 " +
                             "이 메뉴를 다시 눌러라.");
            return;
        }

        var renderer = instance.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer != null) renderer.sprite = frames[0];

        var animator = instance.GetComponentInChildren<SpriteFrameAnimator>(true);
        if (animator == null)
        {
            Debug.LogWarning("[재의 창] SpriteFrameAnimator를 못 찾았다. 첫 프레임만 보인다.");
            return;
        }

        var serialized = new SerializedObject(animator);
        SerializedProperty list = serialized.FindProperty("frames");
        if (list != null)
        {
            list.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
        }

        SerializedProperty fps = serialized.FindProperty("fps");
        if (fps != null) fps.floatValue = SpearFps;

        // 날아가는 동안 계속 돌아야 한다. 한 바퀴만 돌고 멈추면 마지막 프레임으로 굳은 채
        // 날아가서, 불이 꺼진 창이 날아가는 것처럼 보인다.
        SerializedProperty loop = serialized.FindProperty("loop");
        if (loop != null) loop.boolValue = true;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>만든 투사체를 보스 프리팹의 spearPrefab 칸에 꽂는다.</summary>
    private static void ConnectToBoss()
    {
        var spearRoot = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        var spear = spearRoot != null ? spearRoot.GetComponent<Projectile>() : null;
        if (spear == null)
        {
            Debug.LogError("[재의 창] 만든 프리팹에서 Projectile을 못 찾았다.");
            return;
        }

        GameObject boss = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        if (boss == null)
        {
            Debug.LogError($"[재의 창] 보스 프리팹을 못 열었다: {BossPrefabPath}");
            return;
        }

        try
        {
            var enemyBoss = boss.GetComponent<EnemyBoss>();
            if (enemyBoss == null)
            {
                Debug.LogError("[재의 창] 보스 프리팹에 EnemyBoss가 없다.");
                return;
            }

            var serialized = new SerializedObject(enemyBoss);
            SerializedProperty slot = serialized.FindProperty("spearPrefab");
            if (slot == null)
            {
                Debug.LogError("[재의 창] EnemyBoss에 spearPrefab 칸이 없다. 스크립트가 컴파일됐는지 확인해라.");
                return;
            }

            slot.objectReferenceValue = spear;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(boss, BossPrefabPath);
            Debug.Log($"[재의 창] {OutputPath} 생성 후 보스에 연결했다.\n" +
                      $"속도 {SpearSpeed}, 지속 {SpearLifetime}초 (약 {SpearSpeed * SpearLifetime:F0}유닛).\n" +
                      "보스는 이제 조준선을 띄우고 창을 던진다 — 잿불 파도는 09-20에 뺐다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(boss);
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform) SetLayerRecursively(child.gameObject, layer);
    }
}
