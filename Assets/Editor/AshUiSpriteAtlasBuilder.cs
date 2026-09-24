using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// 추가 생성(2026-09-24) — UI 아이콘들을 한 장으로 묶는 스프라이트 아틀라스를 만든다.
/// 메뉴: Tools → 재의 길 → 그림 → UI 아이콘 아틀라스 만들기
///
/// <b>왜 묶나.</b> GPU는 그림(텍스처)이 바뀔 때마다 그리기 명령을 따로 보낸다. 인벤토리 칸 12개와 스킬 바 5칸에 들어가는
/// 아이콘이 전부 다른 파일이라, 아이콘 수만큼 명령이 나갔다. 한 아틀라스로 묶으면 같은 캔버스의 아이콘들이 한 번에 그려진다(배칭).
/// 유니티 내장 Sprite Atlas(V2) 기능을 쓴다 — 코드와 씬은 그대로 두고, 빌드와 플레이 때 유니티가 한 장으로 합친다.
///
/// <b>무엇을 넣나 — 작고, 같은 화면에 같이 나오는 아이콘만.</b>
/// <list type="bullet">
/// <item>유물 아이콘 relic_icon_*(256²) — 인벤토리·보스 열쇠 화면·토스트</item>
/// <item>스킬 아이콘 시트, 유물 아이콘 시트(각 256 높이) — HUD 스킬 바</item>
/// </list>
/// 게이지·패널처럼 큰 그림은 넣지 않는다. 몇 장만 넣어도 아틀라스가 꽉 차서 빈 공간까지 메모리를 먹고, 묶여서 줄어드는 명령도 거의 없다.
/// 게임에서 안 쓰는 원본(1254² 유물 원화 등)은 넣지 않는다 — 넣으면 쓰지도 않는데 빌드에 들어간다.
///
/// 설정은 UI 가져오기 규칙(AshSpriteImportRules)과 맞춘다: Bilinear, 무압축, 밉맵 없음. 조각 사이 여백 4px, 회전·타이트 패킹 끔(UI가 깨지지 않게).
/// 다시 누르면 같은 파일을 새로 만든다.
/// </summary>
public static class AshUiSpriteAtlasBuilder
{
    private const string AtlasPath = "Assets/Project/Art/UI/UiIcons.spriteatlasv2";

    // 넣을 그림. 폴더째 넣지 않고 파일로 고른다 — 같은 폴더에 쓰지 않는 원화(1254²)가 섞여 있어서다.
    private static readonly string[] SpritePaths =
    {
        "Assets/Project/Art/UI/skill_icons_5frames_1280x256.png",
        "Assets/Project/Art/UI/relic_icons_3frames_768x256.png",
    };

    private const string RelicIconFolder = "Assets/Project/Art/UI/Relics";
    private const string RelicIconPrefix = "relic_icon_";

    [MenuItem("Tools/재의 길/그림/UI 아이콘 아틀라스 만들기")]
    public static void Build()
    {
        var textures = new List<Object>();

        foreach (string path in SpritePaths)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) textures.Add(texture);
            else Debug.LogWarning($"[UI 아틀라스] 그림을 못 찾았다: {path}");
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RelicIconFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!System.IO.Path.GetFileName(path).StartsWith(RelicIconPrefix)) continue;
            textures.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        // 1) 아틀라스 에셋을 만들고 그림을 넣는다(텍스처를 넣으면 그 안의 스프라이트 조각이 전부 들어간다).
        var atlas = new SpriteAtlasAsset();
        atlas.Add(textures.ToArray());
        SpriteAtlasAsset.Save(atlas, AtlasPath);
        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

        // 2) 묶는 방식과 텍스처 설정. UI 규칙과 같은 값으로 둔다.
        var importer = AssetImporter.GetAtPath(AtlasPath) as SpriteAtlasImporter;
        if (importer == null)
        {
            Debug.LogError("[UI 아틀라스] 아틀라스 가져오기 설정을 못 열었다. Project Settings → Editor → Sprite Packer Mode가 Sprite Atlas V2인지 확인해라.");
            return;
        }

        importer.includeInBuild = true;
        importer.packingSettings = new SpriteAtlasPackingSettings
        {
            blockOffset = 1,
            padding = 4,
            enableRotation = false,
            enableTightPacking = false,
            enableAlphaDilation = true,
        };
        importer.textureSettings = new SpriteAtlasTextureSettings
        {
            filterMode = FilterMode.Bilinear,
            generateMipMaps = false,
            readable = false,
            sRGB = true,
            anisoLevel = 0,
        };

        var platform = importer.GetPlatformSettings("DefaultTexturePlatform");
        platform.maxTextureSize = 2048;
        platform.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformSettings(platform);

        importer.SaveAndReimport();

        // 3) 에디터에서도 바로 묶인 결과를 쓰게 한 번 굽는다(Play 때도 유니티가 알아서 굽는다).
        var packed = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
        if (packed != null) SpriteAtlasUtility.PackAtlases(new[] { packed }, EditorUserBuildSettings.activeBuildTarget);

        Debug.Log($"[UI 아틀라스] {AtlasPath} — 그림 {textures.Count}장을 묶었다. " +
                  "확인: Window → Analysis → Frame Debugger에서 인벤토리를 열고 아이콘 그리기 명령이 하나로 묶였는지 본다.");
    }
}
