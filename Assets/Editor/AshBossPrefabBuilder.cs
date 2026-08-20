using UnityEditor;
using UnityEngine;

/// <summary>
/// 재의 왕(보스) 프리팹을 만든다.
///
/// 메뉴: Tools → 재의 길 → 보스 프리팹 생성
///
/// 플레이어 프리팹 도구와 따로 둔 이유: 플레이어는 스킬 에셋·이펙트 프리팹까지 같이 만들어서
/// 그 파일이 이미 크다. 보스를 거기 넣으면 스킬을 손볼 때마다 보스까지 다시 만들게 된다.
///
/// <b>이미 있으면 빈 참조만 채운다.</b> 체력이나 사거리 같은 값은 인스펙터에서 손으로 맞추는
/// 대상이라, 다시 실행할 때마다 덮어쓰면 조정한 밸런스가 통째로 날아간다.
/// 유물 빌더에서 쓴 것과 같은 규칙이다.
/// </summary>
public static class AshBossPrefabBuilder
{
    private const string Folder = "Assets/Project/Prefabs/Enemy";
    private const string PrefabPath = Folder + "/BossAshKing.prefab";

    private const string Phase1Path = "Assets/Project/Animations/Boss/AshKingPhase1.controller";
    private const string Phase2Path = "Assets/Project/Animations/Boss/AshKingPhase2.controller";

    /// <summary>
    /// 보스의 세계 좌표 키(유닛).
    ///
    /// 정규화 목표 200px을 캐릭터 PPU 24로 나눈 값이다. 이 숫자를 손으로 적지 않고 계산하는
    /// 이유: 나중에 목표 키나 PPU를 바꿨을 때 <b>콜라이더만 옛날 크기로 남는</b> 사고를 막는다.
    /// 그런 어긋남은 에러가 안 나고 "가끔 공격이 안 맞는다"로만 나타난다.
    /// </summary>
    private static float HeightUnits =>
        AshPlayerSpriteSheets.BossPixelHeight / AshSpriteImportRules.CharacterPixelsPerUnit;

    [MenuItem("Tools/재의 길/보스 프리팹 생성")]
    public static void Build()
    {
        var phase1 = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Phase1Path);
        var phase2 = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Phase2Path);

        if (phase1 == null || phase2 == null)
        {
            Debug.LogError("[보스] 애니메이터 컨트롤러를 못 찾았다.\n" +
                           "Tools → 재의 길 → 캐릭터 애니메이션 생성 을 먼저 실행해라.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Project/Prefabs", "Enemy");

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Repair(existing, phase1, phase2);
            return;
        }

        var root = new GameObject("BossAshKing");
        try
        {
            root.layer = LayerMask.NameToLayer("Enemy");

            Configure(root, phase1, phase2, isNew: true);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
            if (!success || saved == null)
            {
                Debug.LogError($"[보스] 프리팹 저장 실패: {PrefabPath}");
                return;
            }

            Debug.Log($"[보스] 프리팹을 만들었다 → {PrefabPath}\n" +
                      $"키 {HeightUnits:0.##}유닛. 씬에 끌어다 놓고 Play 해봐라.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>이미 있는 프리팹의 <b>비어 있는 참조만</b> 채운다. 수치는 안 건드린다.</summary>
    private static void Repair(GameObject prefab,
                               RuntimeAnimatorController phase1, RuntimeAnimatorController phase2)
    {
        Configure(prefab, phase1, phase2, isNew: false);
        PrefabUtility.SavePrefabAsset(prefab);

        Debug.Log($"[보스] 이미 있는 프리팹의 빈 참조를 채웠다 → {PrefabPath}\n" +
                  "체력·사거리 같은 수치는 그대로 뒀다.");
    }

    private static void Configure(GameObject root,
                                  RuntimeAnimatorController phase1, RuntimeAnimatorController phase2,
                                  bool isNew)
    {
        float height = HeightUnits;

        var body = Ensure<Rigidbody2D>(root);
        var collider = Ensure<CapsuleCollider2D>(root);
        var renderer = Ensure<SpriteRenderer>(root);
        var animator = Ensure<Animator>(root);
        var health = Ensure<Health>(root);
        var boss = Ensure<EnemyBoss>(root);

        if (isNew)
        {
            // 탑다운이라 중력이 없다. 회전을 잠그지 않으면 부딪힐 때마다 보스가 빙글빙글 돈다.
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 콜라이더는 발밑부터 세운다. 피벗이 지면선(발끝)이라 중심을 0에 두면
            // 몸의 절반이 바닥 아래로 들어간다.
            collider.direction = CapsuleDirection2D.Vertical;
            collider.size = new Vector2(height * 0.42f, height * 0.9f);
            collider.offset = new Vector2(0f, height * 0.45f);

            renderer.sortingLayerName = "Entity";
        }

        // 컨트롤러는 참조라 항상 맞춘다. 애니메이션을 다시 생성해도 GUID가 유지되므로
        // 덮어써도 손해가 없고, 비어 있으면 보스가 아무 모션도 안 나온다.
        animator.runtimeAnimatorController = phase1;

        // 첫 프레임에 그림이 없으면 씬 뷰에서 위치를 못 잡는다.
        if (renderer.sprite == null)
        {
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Project/Art/Characters/Boss/AshKing/ash-king-idle.png");
        }

        var serialized = new SerializedObject(boss);
        SetIfEmpty(serialized, "phase2Controller", phase2);

        // 레이어 마스크는 0이 "아무것도 안 맞음"이라 비어 있는 것과 같다.
        var mask = serialized.FindProperty("playerLayer");
        if (mask != null && mask.intValue == 0)
            mask.intValue = 1 << LayerMask.NameToLayer("Player");

        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (isNew)
        {
            var healthObject = new SerializedObject(health);
            healthObject.FindProperty("maxHealth").intValue = 40;
            healthObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static T Ensure<T>(GameObject target) where T : Component
    {
        var found = target.GetComponent<T>();
        return found != null ? found : target.AddComponent<T>();
    }

    private static void SetIfEmpty(SerializedObject serialized, string name, Object value)
    {
        var property = serialized.FindProperty(name);
        if (property != null && property.objectReferenceValue == null)
            property.objectReferenceValue = value;
    }
}
