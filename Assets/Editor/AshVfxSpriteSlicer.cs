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
    /// 피벗을 어디에 둘지.
    ///
    /// <b>정규화 도구가 그림을 놓은 기준과 반드시 같아야 한다.</b> 도구는 바닥 이펙트를
    /// 지면선에, 공중 이펙트를 정중앙에, 화살을 촉 끝에 맞춰 그린다. 여기서 다른 곳을
    /// 피벗으로 잡으면 그림은 맞는데 <b>붙는 위치만 어긋난다.</b>
    /// </summary>
    private enum PivotKind
    {
        Ground,   // 지면선 — 캐릭터 발끝과 같은 높이
        Center,   // 셀 정중앙 — 공중에 뜬 것
        Tip,      // 촉 끝 — 앞으로 날아가는 화살
    }

    /// <summary>
    /// 화살촉이 놓인 자리. 정규화 도구의 TipRightInset과 같은 값이어야 한다.
    ///
    /// 두 곳에 같은 숫자가 있는 것이 마음에 걸리지만, 도구끼리 참조하게 만들면 슬라이서가
    /// 정규화 도구를 알아야 한다. 둘은 따로 돌 수 있어야 하므로 값을 복사하고
    /// 여기 주석으로 묶어둔다 — 한쪽을 바꾸면 다른 쪽도 바꿔야 한다.
    /// </summary>
    private const float TipPivotX = 228f / 256f;

    /// <summary>자를 시트 목록.</summary>
    private static readonly (string folder, string file, string prefix, int frames, PivotKind pivot)[] Sheets =
    {
        (Folder, "vfx_ember_arrow_flight_6frames_1536x256", "vfx_arrow_flight", 6, PivotKind.Center),
        (Folder, "vfx_ember_arrow_impact_6frames_1536x256", "vfx_arrow_impact", 6, PivotKind.Center),
        (Folder, "vfx_kings_ember_6frames_1536x256", "vfx_kings_ember", 6, PivotKind.Ground),
        (Folder, "vfx_ash_staff_ground_spell_6frames_1536x256", "vfx_staff_spell", 6, PivotKind.Ground),
        (Folder, "vfx_sword_slam_impact_6frames_1536x256", "vfx_slam_impact", 6, PivotKind.Ground),
        (Folder, "vfx_sword_slam_forward_burst_6frames_1536x256", "vfx_slam_burst", 6, PivotKind.Ground),

        // 자폭병의 폭발. 바닥에서 터지므로 피벗이 지면선이다 — 자폭병의 발끝 높이에서
        // 원이 퍼져야 판정 원(발밑 기준)과 그림이 같은 자리에 놓인다.
        (Folder, "vfx_bomber_blast_6frames_1536x256", "vfx_bomber_blast", 6, PivotKind.Ground),

        // 추가 생성 — 재의 왕 2페이즈 전환 연출 3장.
        //
        // 피벗이 지면선인 이유: 셋 다 보스의 <b>발밑</b>에서 일어나는 일이다. 재가 발밑으로
        // 모여들고, 그 자리에 알이 서고, 같은 자리에서 껍질이 깨진다. 정중앙으로 잡으면
        // 그림 크기를 키울 때마다 알이 공중으로 떠오른다 — 크기와 높이가 같이 움직여서
        // 배율을 눈으로 맞출 수가 없게 된다.
        //
        // 정규화 도구에 Mode.GroundCenter로 등록한 것과 <b>반드시 같은 기준</b>이다.
        // 한쪽만 바꾸면 그림은 맞는데 붙는 높이만 어긋난다.
        (Folder, "vfx_ashking_transition_gather_6frames_1536x256",
                 "vfx_boss_transition_gather", 6, PivotKind.Ground),
        (Folder, "vfx_ashking_transition_egg_6frames_1536x256",
                 "vfx_boss_transition_egg", 6, PivotKind.Ground),
        (Folder, "vfx_ashking_transition_shatter_6frames_1536x256",
                 "vfx_boss_transition_shatter", 6, PivotKind.Ground),

        // 사수의 화살. 촉 끝이 피벗이라 오브젝트 위치가 곧 촉 위치가 된다.
        (Folder, "ash_marksman_ember_arrow_1frame_256x256", "marksman_arrow", 1, PivotKind.Tip),

        // 스킬 아이콘. VFX는 아니지만 자르는 방식이 같아서 여기서 같이 처리한다.
        // UI라 바닥 개념이 없으므로 피벗은 정중앙이다.
        ("Assets/Art/Generated", "skill_icons_5frames_1280x256", "skill_icon", 5, PivotKind.Center),
        ("Assets/Project/Art/UI", "relic_icons_3frames_768x256", "relic_icon", 3, PivotKind.Center),
    };

    [MenuItem("Tools/재의 길/VFX 스프라이트 슬라이스")]
    public static void SliceAll()
    {
        int total = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var (folder, file, prefix, frames, pivotKind) in Sheets)
                total += Slice(folder, file, prefix, frames, pivotKind);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();
        Debug.Log($"[VFX 슬라이스] 스프라이트 {total}개 생성 완료.");
    }

    private static int Slice(string folder, string file, string prefix, int frames, PivotKind pivotKind)
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
        // 겹쳐 놓으면 바닥이 맞는다. 공중 이펙트는 그림 한가운데가 기준이고,
        // 화살은 촉 끝이 기준이라 오브젝트 위치가 곧 맞는 지점이 된다.
        Vector2 pivot = pivotKind switch
        {
            PivotKind.Ground => AshPlayerSpriteSheets.Pivot,
            PivotKind.Tip => new Vector2(TipPivotX, 0.5f),
            _ => new Vector2(0.5f, 0.5f),
        };

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
