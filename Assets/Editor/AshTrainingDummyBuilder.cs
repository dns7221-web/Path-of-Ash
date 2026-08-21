using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 씬에 놓인 허수아비를 때릴 수 있게 배선한다.
///
/// 메뉴: Tools → 재의 길 → 허수아비 세팅
///
/// 왜 도구인가:
/// 허수아비가 맞으려면 <b>레이어·콜라이더·Health·TrainingDummy</b> 네 가지가 동시에 맞아야 한다.
/// 하나만 빠져도 에러 없이 "때려도 아무 일이 없다"로만 나타나서, 넷 중 무엇이 빠졌는지
/// 눈으로 찾기 어렵다. 특히 레이어는 콜라이더가 아니라 게임오브젝트에 붙어서 놓치기 쉽다.
///
/// 이름으로 찾는 이유: 허수아비는 씬에 손으로 놓은 오브젝트라 프리팹 경로가 없다.
/// </summary>
public static class AshTrainingDummyBuilder
{
    private const string DummyNamePrefix = "training-dummy";
    private const string IdleSpritePath = "Assets/Project/Art/Sprites/Dungeon/training-dummy.png";
    private const string HitSpritePath = "Assets/Project/Art/Sprites/Dungeon/training-dummy-hit.png";

    /// <summary>허수아비 체력. 튜토리얼 내내 안 죽을 만큼 크게 둔다.</summary>
    private const int DummyHealth = 999;

    [MenuItem("Tools/재의 길/허수아비 세팅")]
    public static void Build()
    {
        Sprite[] idleFrames = LoadSprites(IdleSpritePath);
        Sprite[] hitFrames = LoadSprites(HitSpritePath);
        if (idleFrames.Length == 0 || hitFrames.Length == 0)
        {
            Debug.LogError($"[허수아비] 그림을 못 찾았다.\n{IdleSpritePath}\n{HitSpritePath}");
            return;
        }

        Sprite idle = idleFrames[0];
        Sprite hit = hitFrames[0];

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0)
        {
            Debug.LogError("[허수아비] Enemy 레이어가 없다. 레이어 설정을 확인해라.");
            return;
        }

        int count = 0;
        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!renderer.gameObject.name.StartsWith(DummyNamePrefix)) continue;

            Configure(renderer.gameObject, renderer, idle, hit, enemyLayer);
            count++;
        }

        if (count == 0)
        {
            Debug.LogWarning($"[허수아비] 이름이 '{DummyNamePrefix}'로 시작하는 오브젝트를 못 찾았다.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[허수아비] {count}개를 때릴 수 있게 배선했다. 체력 {DummyHealth}, 죽지 않는다.");
    }

    /// <summary>
    /// 시트 안의 스프라이트를 이름 순으로 모두 가져온다.
    ///
    /// <c>LoadAssetAtPath&lt;Sprite&gt;</c>를 쓰지 않는 이유:
    /// 이 두 PNG는 Multiple 모드라 <b>메인 에셋이 Texture2D다.</b> 그래서 그 함수는 스프라이트를
    /// 제대로 돌려주지 못하고, 결과적으로 렌더러에 빈 참조가 꽂혀 허수아비가 통째로 사라졌다.
    /// 하위 에셋을 전부 훑어 Sprite만 골라야 확실하다.
    /// </summary>
    private static Sprite[] LoadSprites(string path)
    {
        var found = new System.Collections.Generic.List<Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite sprite) found.Add(sprite);
        }

        // 로드 순서는 보장되지 않는다. _0, _1, _2 순서를 이름으로 되찾는다.
        found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return found.ToArray();
    }

    private static void Configure(GameObject target, SpriteRenderer renderer,
                                  Sprite idle, Sprite hit, int enemyLayer)
    {
        // 레이어는 콜라이더가 아니라 게임오브젝트에 붙는다.
        // 플레이어 공격 히트박스가 Enemy 레이어만 훑으므로 이게 틀리면 영영 안 맞는다.
        target.layer = enemyLayer;

        // 맞을 면적. 밑동만 막는 소품과 달리 허수아비는 몸 전체가 표적이라 넓게 잡는다.
        var box = target.GetComponent<BoxCollider2D>();
        if (box == null) box = Undo.AddComponent<BoxCollider2D>(target);

        Undo.RecordObject(box, "허수아비 세팅");
        if (renderer.sprite != null)
        {
            Bounds bounds = renderer.sprite.bounds;
            box.size = new Vector2(bounds.size.x * 0.6f, bounds.size.y * 0.8f);
            box.offset = new Vector2(bounds.center.x, bounds.min.y + box.size.y * 0.5f);
        }
        box.isTrigger = false;

        var health = target.GetComponent<Health>();
        if (health == null) health = Undo.AddComponent<Health>(target);

        var healthObject = new SerializedObject(health);
        SerializedProperty maxHealth = healthObject.FindProperty("maxHealth");
        if (maxHealth != null) maxHealth.intValue = DummyHealth;

        // 무적 시간을 없앤다.
        //
        // 기본값 0.35초는 플레이어와 적을 위한 값이다. 연달아 맞아 죽는 걸 막으려는 장치인데,
        // 허수아비에 걸리면 <b>때린 것의 대부분이 무시된다.</b> TakeDamage가 무적 중에는
        // 아무 일도 안 하고 돌아가므로 Damaged 이벤트도 안 나가고, 그래서 피격 그림도 안 뜬다.
        // 연습 대상은 때리는 족족 반응해야 타격감을 확인할 수 있다.
        SerializedProperty invulnerable = healthObject.FindProperty("invulnerableSecondsAfterHit");
        if (invulnerable != null) invulnerable.floatValue = 0f;

        healthObject.ApplyModifiedPropertiesWithoutUndo();

        var dummy = target.GetComponent<TrainingDummy>();
        if (dummy == null) dummy = Undo.AddComponent<TrainingDummy>(target);

        var dummyObject = new SerializedObject(dummy);
        dummyObject.FindProperty("spriteRenderer").objectReferenceValue = renderer;
        dummyObject.FindProperty("idleSprite").objectReferenceValue = idle;
        dummyObject.FindProperty("hitSprite").objectReferenceValue = hit;
        dummyObject.ApplyModifiedPropertiesWithoutUndo();

        // 평소 그림을 확실히 맞춰둔다. 씬에 피격 그림이 꽂힌 채 저장돼 있을 수 있다.
        Undo.RecordObject(renderer, "허수아비 세팅");
        renderer.sprite = idle;
    }
}
