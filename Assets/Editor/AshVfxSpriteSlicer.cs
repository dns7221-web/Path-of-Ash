using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 정규화된 VFX 시트를 자르는 도구.
///
/// 메뉴: Tools → 재의 길 → VFX 스프라이트 슬라이스
///
/// 캐릭터 슬라이서(<see cref="AshPlayerSpriteSlicer"/>)와 따로 둔 이유는 <b>피벗이 다르기</b>
/// 때문이다. 캐릭터는 전부 발밑(지면선)이지만, VFX는 바닥에서 솟는 것과 공중에 뜨는 것의
/// 기준점이 다르다. 캐릭터 표에 피벗 칸을 끼워 넣으면 그 표를 읽는 클립 생성기까지 같이
/// 손봐야 해서, 시트 성격이 아예 다른 이쪽을 따로 만드는 편이 건드리는 곳이 적다.
/// </summary>
public static class AshVfxSpriteSlicer
{
    private const string Folder = "Assets/Project/Art/Sprites/VFX";

    /// <summary>
    /// 자를 시트 목록.
    ///
    /// groundPivot이 true면 피벗이 지면선(캐릭터 발끝과 같은 높이)이고, false면 셀 정중앙이다.
    /// 정규화 도구가 그림을 그 기준으로 배치해뒀으므로 여기 값이 그것과 맞아야 한다.
    /// </summary>
    private static readonly (string folder, string file, string prefix, int frames, bool groundPivot)[] Sheets =
    {
        (Folder, "vfx_ember_arrow_flight_6frames_1536x256", "vfx_arrow_flight", 6, false),
        (Folder, "vfx_ember_arrow_impact_6frames_1536x256", "vfx_arrow_impact", 6, false),
        (Folder, "vfx_kings_ember_6frames_1536x256", "vfx_kings_ember", 6, true),
        (Folder, "vfx_ash_staff_ground_spell_6frames_1536x256", "vfx_staff_spell", 6, true),
        (Folder, "vfx_sword_slam_impact_6frames_1536x256", "vfx_slam_impact", 6, true),
        (Folder, "vfx_sword_slam_forward_burst_6frames_1536x256", "vfx_slam_burst", 6, true),

        // 스킬 아이콘. VFX는 아니지만 자르는 방식이 같아서 여기서 같이 처리한다.
        // UI라 바닥 개념이 없으므로 피벗은 정중앙이다.
        ("Assets/Art/Generated", "skill_icons_5frames_1280x256", "skill_icon", 5, false),
        ("Assets/Project/Art/UI", "relic_icons_3frames_768x256", "relic_icon", 3, false),
    };

    [MenuItem("Tools/재의 길/VFX 스프라이트 슬라이스")]
    public static void SliceAll()
    {
        int total = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var (folder, file, prefix, frames, groundPivot) in Sheets)
                total += Slice(folder, file, prefix, frames, groundPivot);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();
        Debug.Log($"[VFX 슬라이스] 스프라이트 {total}개 생성 완료.");
    }

    private static int Slice(string folder, string file, string prefix, int frames, bool groundPivot)
    {
        string path = $"{folder}/{file}.png";

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[VFX 슬라이스] 시트를 못 찾았다: {path}\n" +
                           "Tools → 재의 길 → 원본 시트 정규화 를 먼저 실행해라.");
            return 0;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null) return 0;

        provider.InitSpriteEditorDataProvider();

        // 캐릭터 슬라이서와 같은 이유로 기존 ID를 이름으로 물려준다.
        // 다시 자를 때 새 GUID를 발급하면 이미 연결해둔 프리팹의 참조가 끊긴다.
        var idByName = new Dictionary<string, GUID>();
        foreach (var old in provider.GetSpriteRects())
        {
            if (!idByName.ContainsKey(old.name)) idByName.Add(old.name, old.spriteID);
        }

        // 바닥 이펙트는 캐릭터 발끝과 같은 높이가 피벗이라, 플레이어 발 위치에 그냥
        // 겹쳐 놓으면 바닥이 맞는다. 공중 이펙트는 그림 한가운데가 기준이다.
        Vector2 pivot = groundPivot
            ? AshPlayerSpriteSheets.Pivot
            : new Vector2(0.5f, 0.5f);

        var rects = new List<SpriteRect>();
        for (int i = 0; i < frames; i++)
        {
            string name = $"{prefix}_{i:00}";

            rects.Add(new SpriteRect
            {
                name = name,
                rect = AshPlayerSpriteSheets.CellRect(i),
                alignment = SpriteAlignment.Custom,
                pivot = pivot,
                border = Vector4.zero,
                spriteID = idByName.TryGetValue(name, out var id) ? id : GUID.Generate(),
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

        return rects.Count;
    }
}
