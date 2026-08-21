using UnityEditor;
using UnityEngine;

/// <summary>
/// 캐릭터 프리팹에 접지 그림자를 붙인다.
///
/// 메뉴: Tools → 재의 길 → 접지 그림자 추가
///
/// 왜 필요한가:
/// 플레이어·보스·망령 어느 프리팹에도 발밑 그림자가 없었다. 캐릭터가 바닥에 "붙어 있다"고
/// 느끼게 하는 건 배경의 투영법이 아니라 <b>접지점</b>이다. 그림자가 없으면 아무리 시점을
/// 맞춰도 스티커를 붙여 놓은 것처럼 떠 보인다. 배경을 다시 그리기 전에 이것부터 확인해야 한다.
///
/// 그림자 폭을 스프라이트가 아니라 <b>콜라이더</b>에서 가져오는 이유:
/// 스프라이트 경계는 256px 프레임 전체(10.7유닛)라 무기나 옷자락까지 포함한다. 그걸 기준으로
/// 그리면 그림자가 발보다 훨씬 넓어진다. 콜라이더는 실제로 바닥을 딛는 크기라 발자국에 가깝다.
///
/// 로컬 스케일로 크기를 맞추는 이유:
/// 그림자 PNG의 PPU가 나중에 바뀌어도 이 도구를 다시 돌리면 월드 크기가 그대로 유지된다.
/// 숫자를 박아두면 임포트 설정이 바뀔 때 조용히 어긋난다.
/// </summary>
public static class AshGroundShadowBuilder
{
    private const string ShadowSpritePath = "Assets/Project/Art/Sprites/VFX/ground_shadow.png";
    private const string ShadowName = "GroundShadow";

    /// <summary>그림자 진하기. 완전히 검으면 구멍처럼 보여서 절반 아래로 둔다.</summary>
    private const float ShadowAlpha = 0.42f;

    /// <summary>콜라이더 폭 대비 그림자 폭. 발보다 살짝 넓어야 접지가 자연스럽다.</summary>
    private const float WidthScale = 1.15f;

    /// <summary>그림자의 납작한 정도. 얕은 3/4 시점이라 세로를 많이 눌러야 바닥에 누운 것처럼 보인다.</summary>
    private const float HeightRatio = 0.38f;

    private static readonly string[] CharacterPrefabs =
    {
        "Assets/Project/Prefabs/Player/Player.prefab",
        "Assets/Project/Prefabs/Enemy/BossAshKing.prefab",
        "Assets/Project/Prefabs/Enemies/AshEmberWraith.prefab",
    };

    [MenuItem("Tools/재의 길/접지 그림자 추가")]
    public static void Build()
    {
        var shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath);
        if (shadowSprite == null)
        {
            Debug.LogError($"[접지 그림자] 그림자 스프라이트를 못 찾았다: {ShadowSpritePath}");
            return;
        }

        int done = 0;
        foreach (string path in CharacterPrefabs)
        {
            if (ApplyToPrefab(path, shadowSprite)) done++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[접지 그림자] 캐릭터 프리팹 {done}개에 그림자를 붙였다.");
    }

    /// <summary>
    /// 프리팹 하나를 열어 그림자를 붙이고 저장한다.
    ///
    /// 프리팹 에셋을 직접 여는 이유: 씬에 올라간 인스턴스만 고치면 프리팹 원본은 그대로라
    /// 런타임에 새로 만들어지는 보스에게는 그림자가 없다.
    /// </summary>
    private static bool ApplyToPrefab(string prefabPath, Sprite shadowSprite)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[접지 그림자] 프리팹을 못 열었다: {prefabPath}");
            return false;
        }

        try
        {
            // 그림자를 붙이기 전에 캐릭터 렌더러를 먼저 찾는다.
            // 붙인 뒤에 찾으면 그림자 자신이 잡힐 수 있다.
            SpriteRenderer character = FindCharacterRenderer(root);
            if (character == null)
            {
                Debug.LogWarning($"[접지 그림자] {root.name}에 SpriteRenderer가 없다. 건너뛴다.");
                return false;
            }

            float width = GetFootprintWidth(root);
            if (width <= 0f)
            {
                Debug.LogWarning($"[접지 그림자] {root.name}의 콜라이더 폭을 못 구했다. 건너뛴다.");
                return false;
            }

            Transform existing = root.transform.Find(ShadowName);
            GameObject shadowObject = existing != null
                ? existing.gameObject
                : new GameObject(ShadowName);

            if (existing == null) shadowObject.transform.SetParent(root.transform, false);

            var renderer = shadowObject.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = shadowObject.AddComponent<SpriteRenderer>();

            renderer.sprite = shadowSprite;
            renderer.color = new Color(0f, 0f, 0f, ShadowAlpha);

            // 캐릭터와 같은 정렬 레이어에 두고 한 칸 뒤로 보낸다.
            // 다른 레이어로 빼면 방 소품과의 앞뒤 관계가 캐릭터와 달라진다.
            renderer.sortingLayerID = character.sortingLayerID;
            renderer.sortingOrder = character.sortingOrder - 1;

            // 피벗이 발밑이므로 원점이 곧 접지점이다. 1픽셀만 내려 캐릭터 외곽선과 겹치지 않게 한다.
            shadowObject.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            shadowObject.transform.localRotation = Quaternion.identity;

            float spriteWidth = shadowSprite.bounds.size.x;
            float target = width * WidthScale;
            float scale = spriteWidth > 0f ? target / spriteWidth : 1f;
            shadowObject.transform.localScale = new Vector3(scale, scale * HeightRatio, 1f);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[접지 그림자] {root.name} — 발자국 폭 {width:F2} → 그림자 폭 {target:F2}유닛.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>그림자가 아닌 첫 SpriteRenderer를 찾는다.</summary>
    private static SpriteRenderer FindCharacterRenderer(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.gameObject.name == ShadowName) continue;
            return renderer;
        }
        return null;
    }

    /// <summary>
    /// 캐릭터가 바닥을 딛는 폭을 콜라이더에서 구한다.
    ///
    /// bounds가 아니라 size를 직접 읽는 이유: 프리팹을 에셋 상태로 열면 물리 월드에 올라가 있지
    /// 않아서 bounds가 0으로 나온다.
    /// </summary>
    private static float GetFootprintWidth(GameObject root)
    {
        var capsule = root.GetComponent<CapsuleCollider2D>();
        if (capsule != null) return capsule.size.x;

        var box = root.GetComponent<BoxCollider2D>();
        if (box != null) return box.size.x;

        var circle = root.GetComponent<CircleCollider2D>();
        if (circle != null) return circle.radius * 2f;

        return 0f;
    }
}
