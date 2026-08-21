using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스의 잿불 파도 투사체를 만들고 보스 프리팹에 연결한다.
///
/// 메뉴: Tools → 재의 길 → 잿불 파도 투사체 생성
///
/// 왜 필요한가:
/// <see cref="EnemyBoss"/>의 Wave 코루틴은 예비동작·조준 재계산·부채꼴 분산까지 완성돼 있는데
/// <c>wavePrefab</c>이 비어 있어서 <b>한 번도 실행된 적이 없었다.</b> 꽂을 프리팹도 없었다.
/// VFX/KingsEmber는 이름상 보스의 잿불이지만 SpriteRenderer와 프레임 애니메이터뿐이라
/// 투사체로 쓸 수 없다 — 충돌체도, 피해 판정도, Projectile도 없다.
///
/// 빈 오브젝트부터 쌓지 않고 <see cref="EmberArrow"/>를 <b>복제해서</b> 만드는 이유:
/// 투사체 하나에 Rigidbody2D, 트리거 충돌체, DamageHitbox, Projectile, 레이어, 프레임 애니메이터가
/// 서로 맞물려 있다. 손으로 다시 조립하면 어딘가 하나가 어긋나는데, 그런 어긋남은 에러가 아니라
/// "가끔 안 맞는다"로만 나타나 원인을 찾기 어렵다. 이미 동작하는 것을 복제하면 그 사고가 없다.
///
/// 복제 후 바꾸는 것은 네 가지뿐이다 — 레이어, 피해 대상, 겉모습, 속도.
/// </summary>
public static class AshBossWaveBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/EmberArrow.prefab";
    private const string VisualPath = "Assets/Project/Prefabs/VFX/KingsEmber.prefab";
    private const string OutputPath = "Assets/Project/Prefabs/VFX/BossEmberWave.prefab";
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemy/BossAshKing.prefab";

    /// <summary>
    /// 파도의 속도. 화살(34)보다 훨씬 느리다.
    ///
    /// 보스 패턴은 <b>피할 수 있어야</b> 재미가 된다. 화살 속도로 부채꼴 다섯 발을 쏘면
    /// 반응이 아니라 운으로 갈린다. 느리게 퍼지는 벽이라야 사이를 파고드는 판단이 생긴다.
    /// </summary>
    private const float WaveSpeed = 13f;

    /// <summary>파도가 살아 있는 시간. 느린 대신 멀리 간다(13 x 1.8 = 약 23유닛).</summary>
    private const float WaveLifetime = 1.8f;

    [MenuItem("Tools/재의 길/잿불 파도 투사체 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[잿불 파도] 원본 투사체를 못 찾았다: {SourcePath}");
            return;
        }

        int enemyAttackLayer = LayerMask.NameToLayer("EnemyAttack");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (enemyAttackLayer < 0 || playerLayer < 0)
        {
            Debug.LogError("[잿불 파도] EnemyAttack 또는 Player 레이어가 없다. 레이어 설정을 확인해라.");
            return;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            instance.name = "BossEmberWave";

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
                Debug.LogWarning("[잿불 파도] DamageHitbox를 못 찾았다. 피해가 안 들어갈 수 있다.");
            }

            // 3. 겉모습 — 왕의 잿불로 바꾼다.
            ApplyVisual(instance);

            // 4. 속도 — 화살보다 느리고 멀리 간다.
            var projectile = instance.GetComponent<Projectile>();
            if (projectile != null)
            {
                var projectileObject = new SerializedObject(projectile);
                projectileObject.FindProperty("speed").floatValue = WaveSpeed;
                projectileObject.FindProperty("lifetime").floatValue = WaveLifetime;
                projectileObject.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[잿불 파도] 프리팹 저장에 실패했다: {OutputPath}");
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
    /// KingsEmber의 스프라이트와 프레임 애니메이션을 복제본에 옮긴다.
    ///
    /// 못 찾으면 화살 그림 그대로 두고 경고만 남긴다. 겉모습이 틀린 것보다
    /// 패턴이 아예 안 나오는 쪽이 훨씬 나쁘기 때문에, 여기서 멈추지 않는다.
    /// </summary>
    private static void ApplyVisual(GameObject instance)
    {
        var visualSource = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
        if (visualSource == null)
        {
            Debug.LogWarning($"[잿불 파도] {VisualPath}를 못 찾아 화살 그림을 그대로 쓴다.");
            return;
        }

        var sourceRenderer = visualSource.GetComponentInChildren<SpriteRenderer>(true);
        var targetRenderer = instance.GetComponentInChildren<SpriteRenderer>(true);
        if (sourceRenderer == null || targetRenderer == null) return;

        targetRenderer.sprite = sourceRenderer.sprite;
        targetRenderer.color = sourceRenderer.color;
        targetRenderer.flipX = sourceRenderer.flipX;

        // 프레임 애니메이터의 프레임 목록도 함께 옮긴다.
        // 스프라이트만 바꾸면 첫 프레임만 보스 잿불이고 재생은 화살 프레임으로 돈다.
        var sourceAnimator = visualSource.GetComponentInChildren<SpriteFrameAnimator>(true);
        var targetAnimator = instance.GetComponentInChildren<SpriteFrameAnimator>(true);
        if (sourceAnimator == null || targetAnimator == null) return;

        var from = new SerializedObject(sourceAnimator);
        var to = new SerializedObject(targetAnimator);

        SerializedProperty fromFrames = from.FindProperty("frames");
        SerializedProperty toFrames = to.FindProperty("frames");
        if (fromFrames != null && toFrames != null)
        {
            toFrames.arraySize = fromFrames.arraySize;
            for (int i = 0; i < fromFrames.arraySize; i++)
            {
                toFrames.GetArrayElementAtIndex(i).objectReferenceValue =
                    fromFrames.GetArrayElementAtIndex(i).objectReferenceValue;
            }
        }

        CopyFloat(from, to, "fps");
        CopyBool(from, to, "loop");
        to.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CopyFloat(SerializedObject from, SerializedObject to, string field)
    {
        SerializedProperty a = from.FindProperty(field);
        SerializedProperty b = to.FindProperty(field);
        if (a != null && b != null) b.floatValue = a.floatValue;
    }

    private static void CopyBool(SerializedObject from, SerializedObject to, string field)
    {
        SerializedProperty a = from.FindProperty(field);
        SerializedProperty b = to.FindProperty(field);
        if (a != null && b != null) b.boolValue = a.boolValue;
    }

    /// <summary>만든 투사체를 보스 프리팹의 wavePrefab 칸에 꽂는다.</summary>
    private static void ConnectToBoss()
    {
        var waveRoot = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        var wave = waveRoot != null ? waveRoot.GetComponent<Projectile>() : null;
        if (wave == null)
        {
            Debug.LogError("[잿불 파도] 만든 프리팹에서 Projectile을 못 찾았다.");
            return;
        }

        GameObject boss = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        if (boss == null)
        {
            Debug.LogError($"[잿불 파도] 보스 프리팹을 못 열었다: {BossPrefabPath}");
            return;
        }

        try
        {
            var enemyBoss = boss.GetComponent<EnemyBoss>();
            if (enemyBoss == null)
            {
                Debug.LogError("[잿불 파도] 보스 프리팹에 EnemyBoss가 없다.");
                return;
            }

            var serialized = new SerializedObject(enemyBoss);
            serialized.FindProperty("wavePrefab").objectReferenceValue = wave;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(boss, BossPrefabPath);
            Debug.Log($"[잿불 파도] {OutputPath} 생성 후 보스에 연결했다.\n" +
                      $"속도 {WaveSpeed}, 지속 {WaveLifetime}초 (사거리 약 {WaveSpeed * WaveLifetime:F0}유닛).\n" +
                      "이제 보스가 내려찍기와 파도를 번갈아 쓴다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(boss);
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
