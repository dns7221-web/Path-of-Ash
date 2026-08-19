using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 던전 소품 시트를 이름 붙여 자르고, 각 소품의 프리팹을 만드는 에디터 도구.
///
/// 메뉴: Tools → 재의 길 → 던전 소품 슬라이스 + 프리팹 생성
///
/// 유니티의 자동 슬라이스(Automatic)를 안 쓰는 이유가 두 가지다.
/// 1) 이름이 DungeonProps_v2_0 식으로 붙어서, 어느 게 상자고 어느 게 항아리인지 알 수 없다.
/// 2) 흩날린 먼지 픽셀까지 스프라이트로 잡는다. 실제로 9개짜리 시트에서 14개가 나왔고
///    그중 5개가 8x9px 같은 쓰레기였다.
/// 그래서 좌표를 실측해서 표에 박아뒀다. 시트를 다시 뽑으면 이 표를 고쳐야 한다.
///
/// <b>소품마다 목표 크기를 따로 정하는 이유</b>: 이 시트는 소품 하나가 한 칸을 꽉 채우도록
/// 그려져 있다. 그래서 그림 안에서 항아리와 기둥이 거의 같은 크기다. 실제 게임에서는
/// 항아리가 무릎 높이, 기둥이 캐릭터 절반이어야 하므로 프리팹마다 배율을 다르게 준다.
/// </summary>
public static class AshDungeonPropBuilder
{
    private const string SheetFolder = "Assets/Project/Art/Sprites/Dungeon";
    private const string PrefabFolder = "Assets/Project/Prefabs/Props";

    private const string PropsSheet = "DungeonProps_v2";
    private const string StatesSheet = "DungeonInteractionStates";

    /// <summary>소품이 어떻게 부딪히는가.</summary>
    private enum Solid
    {
        None,    // 콜라이더 없음. 바닥 장식
        Block,   // 몸으로 막는다. Wall 레이어를 써서 기존 충돌 매트릭스를 그대로 탄다
        Trigger, // 통과하되 감지된다. 상자 같은 상호작용 대상
    }

    private struct Prop
    {
        public string Sheet;      // 어느 시트에서
        public string Name;       // 스프라이트/프리팹 이름
        public int X, Y, W, H;    // 시트 안 좌표(유니티 기준, 왼쪽 <b>아래</b>가 원점)
        public float Height;      // 게임에서의 목표 세로 크기(월드 유닛). 0이면 프리팹을 안 만든다
        public Solid Solid;
        public string SortingLayer;

        public Prop(string sheet, string name, int x, int y, int w, int h,
                    float height, Solid solid, string sortingLayer)
        {
            Sheet = sheet; Name = name; X = x; Y = y; W = w; H = h;
            Height = height; Solid = solid; SortingLayer = sortingLayer;
        }
    }

    /// <summary>
    /// 소품 목록. 좌표는 알파를 훑어서 실측한 값이다.
    ///
    /// 목표 크기의 기준은 플레이어 키 6.67유닛이다.
    /// 문 세 종류는 자르기만 하고 프리팹을 안 만든다(Height 0) — 문은 방 배경 그림에
    /// 이미 포함돼 있어서 RoomDoorState가 배경 교체로 처리한다. 나중에 여러 방을 이어붙일 때
    /// 쓸 수 있으니 스프라이트로는 남겨둔다.
    /// </summary>
    private static readonly Prop[] Props =
    {
        // ── DungeonProps_v2 (3x3) ──
        // 수정(첫 배치 확인 후): 목표 크기를 전반적으로 키웠다.
        // 기둥 3.4유닛은 캐릭터(6.67)의 절반이라 화면에서 엄폐물로 안 읽히고 바닥 장식처럼 보였다.
        // 5.5로 올리면 캐릭터 키의 82%가 되어 "뒤에 숨는 물건"으로 읽힌다.
        new Prop(PropsSheet, "prop_door_closed",      98, 878, 271, 336, 0f,   Solid.None,    "Entity"),
        new Prop(PropsSheet, "prop_stairs",          495, 862, 259, 333, 5.0f, Solid.None,    "Decal"),
        new Prop(PropsSheet, "prop_pillar_collapsed",867, 857, 253, 330, 4.8f, Solid.Block,   "Entity"),
        new Prop(PropsSheet, "prop_rubble",           67, 418, 325, 312, 2.2f, Solid.None,    "Decal"),
        new Prop(PropsSheet, "prop_torch",           574, 502, 103, 284, 3.2f, Solid.None,    "Entity"),
        new Prop(PropsSheet, "prop_chest_closed",    855, 501, 260, 227, 2.4f, Solid.Trigger, "Entity"),
        new Prop(PropsSheet, "prop_pillar_broken",    88,  94, 280, 324, 5.5f, Solid.Block,   "Entity"),
        new Prop(PropsSheet, "prop_urn",             526, 104, 194, 276, 2.2f, Solid.Block,   "Entity"),
        new Prop(PropsSheet, "prop_altar",           847, 110, 276, 237, 3.2f, Solid.None,    "Decal"),

        // ── DungeonInteractionStates (2x2) ──
        // 이 시트는 칸이 627px이라 같은 소품이라도 위 시트보다 1.5배 크게 그려져 있다.
        // 목표 크기를 유닛으로 적어두면 그 차이를 신경 쓸 필요가 없다.
        new Prop(StatesSheet, "prop_door_open",      138, 720, 364, 457, 0f,   Solid.None,    "Entity"),
        new Prop(StatesSheet, "prop_door_broken",    700, 702, 422, 475, 0f,   Solid.None,    "Entity"),
        // 열린 상자는 뚜껑이 서 있어서 닫힌 상자(2.4)보다 조금 커야 자연스럽다.
        new Prop(StatesSheet, "prop_chest_open",     148, 147, 368, 392, 2.7f, Solid.Trigger, "Entity"),
        new Prop(StatesSheet, "prop_urn_broken",     709, 143, 394, 369, 1.7f, Solid.None,    "Decal"),
    };

    [MenuItem("Tools/재의 길/던전 소품 슬라이스 + 프리팹 생성")]
    public static void BuildAll()
    {
        SliceSheet(PropsSheet);
        SliceSheet(StatesSheet);

        AssetDatabase.Refresh();

        EnsureFolder("Assets/Project/Prefabs", "Props");

        int made = 0;
        foreach (var prop in Props)
        {
            if (prop.Height <= 0f) continue; // 자르기만 하고 프리팹은 안 만드는 것들
            if (BuildPrefab(prop)) made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[던전 소품] 스프라이트 {Props.Length}개 슬라이스, 프리팹 {made}개 생성 → {PrefabFolder}");
    }

    // ── 슬라이스 ──────────────────────────────────────────────────────────

    private static void SliceSheet(string sheetName)
    {
        string path = $"{SheetFolder}/{sheetName}.png";

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[던전 소품] 시트를 못 찾았다: {path}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null)
        {
            Debug.LogError($"[던전 소품] 데이터 프로바이더를 못 얻었다: {sheetName}");
            return;
        }

        provider.InitSpriteEditorDataProvider();

        // 플레이어 슬라이서와 같은 이유로 기존 ID를 이름으로 물려준다.
        // 다시 자를 때 새 GUID를 발급하면 이미 만들어둔 프리팹의 스프라이트 참조가 끊긴다.
        var idByName = new Dictionary<string, GUID>();
        foreach (var old in provider.GetSpriteRects())
        {
            if (!idByName.ContainsKey(old.name)) idByName.Add(old.name, old.spriteID);
        }

        var rects = new List<SpriteRect>();
        foreach (var prop in Props)
        {
            if (prop.Sheet != sheetName) continue;

            rects.Add(new SpriteRect
            {
                name = prop.Name,
                rect = new Rect(prop.X, prop.Y, prop.W, prop.H),

                // 소품은 바닥에 서므로 피벗이 발밑(아래 중앙)이어야 한다.
                // 그래야 배치할 때 Y좌표가 곧 "바닥에 닿는 지점"이 되고, 나중에 Y축 정렬로
                // 앞뒤를 가릴 때도 기준이 캐릭터와 같아진다.
                alignment = SpriteAlignment.BottomCenter,
                pivot = new Vector2(0.5f, 0f),

                border = Vector4.zero,
                spriteID = idByName.TryGetValue(prop.Name, out var id) ? id : GUID.Generate(),
            });
        }

        provider.SetSpriteRects(rects.ToArray());

        var nameIdProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (nameIdProvider != null)
        {
            var pairs = new List<SpriteNameFileIdPair>(rects.Count);
            foreach (var r in rects) pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));
            nameIdProvider.SetNameFileIdPairs(pairs);
        }

        provider.Apply();
        importer.SaveAndReimport();
    }

    // ── 프리팹 ────────────────────────────────────────────────────────────

    private static bool BuildPrefab(Prop prop)
    {
        Sprite sprite = FindSprite(prop.Sheet, prop.Name);
        if (sprite == null)
        {
            Debug.LogError($"[던전 소품] 스프라이트를 못 찾았다: {prop.Name}");
            return false;
        }

        var root = new GameObject(prop.Name);

        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = prop.SortingLayer;

            // 스프라이트가 원래 차지하는 세로 크기(유닛). 여기에 배율을 곱해 목표 크기를 맞춘다.
            float nativeHeight = sprite.rect.height / sprite.pixelsPerUnit;
            float nativeWidth = sprite.rect.width / sprite.pixelsPerUnit;
            float scale = nativeHeight > 0f ? prop.Height / nativeHeight : 1f;

            root.transform.localScale = new Vector3(scale, scale, 1f);

            if (prop.Solid != Solid.None)
                AddCollider(root, prop, nativeWidth, nativeHeight);

            string path = $"{PrefabFolder}/{prop.Name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);

            if (!success) Debug.LogError($"[던전 소품] 프리팹 저장 실패: {path}");
            return success;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 소품 발치에 콜라이더를 단다.
    ///
    /// 그림 전체를 감싸지 않는 이유는 캐릭터 콜라이더와 같다. 탑다운이라 부딪히는 지점은
    /// "바닥에 놓인 면적"이지 기둥의 윗부분이 아니다. 기둥 전체를 막으면 기둥 위쪽 빈 공간에서도
    /// 벽에 막힌 것처럼 걸린다.
    ///
    /// 크기를 <b>배율 적용 전</b>의 유닛으로 넣는 이유: 콜라이더는 Transform 스케일을 같이
    /// 받으므로, 여기서 목표 크기를 계산해 넣으면 배율이 두 번 곱해진다.
    /// </summary>
    private static void AddCollider(GameObject root, Prop prop, float nativeWidth, float nativeHeight)
    {
        var collider = root.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(nativeWidth * 0.6f, nativeHeight * 0.22f);
        collider.offset = new Vector2(0f, collider.size.y * 0.5f);

        if (prop.Solid == Solid.Trigger)
        {
            collider.isTrigger = true;

            // Pickup 레이어는 AshProjectSetup이 만들어둔 것이고, 충돌 매트릭스에서
            // Player x Pickup만 켜져 있다. 적은 상자를 건드리지 않는다.
            root.layer = LayerMask.NameToLayer("Pickup");
            return;
        }

        // Wall 레이어를 쓰면 기존 충돌 매트릭스를 그대로 탄다 — Player x Wall, Enemy x Wall,
        // PlayerAttack x Wall이 이미 켜져 있다. 소품 전용 레이어를 새로 만들면 그 조합을
        // 전부 다시 켜야 하고, 하나 빠뜨리면 "가끔 적이 기둥을 통과하는" 버그가 된다.
        root.layer = LayerMask.NameToLayer("Wall");
    }

    private static Sprite FindSprite(string sheetName, string spriteName)
    {
        string path = $"{SheetFolder}/{sheetName}.png";

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite sprite && sprite.name == spriteName) return sprite;
        }

        return null;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
