using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 정규화된 VFX 시트를 자르는 도구.
///
/// 메뉴: Tools → 재의 길 → 그림 → VFX 스프라이트 슬라이스
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
        Forward,  // 추가 생성(2026-09-14) — 출발점. 셀 왼쪽 28px, 세로 가운데 — 한 점에서 앞(오른쪽)으로 뻗는 것
    }

    /// <summary>
    /// 화살촉이 놓인 자리. 정규화 도구의 TipRightInset과 같은 값이어야 한다.
    ///
    /// 두 곳에 같은 숫자가 있는 것이 마음에 걸리지만, 도구끼리 참조하게 만들면 슬라이서가
    /// 정규화 도구를 알아야 한다. 둘은 따로 돌 수 있어야 하므로 값을 복사하고
    /// 여기 주석으로 묶어둔다 — 한쪽을 바꾸면 다른 쪽도 바꿔야 한다.
    /// </summary>
    private const float TipPivotX = 228f / 256f;

    /// <summary>
    /// 추가 생성(2026-09-14) — 앞으로 뻗는 이펙트의 출발점이 놓인 자리.
    ///
    /// Tools/NormalizeVfxStrip.ps1의 -PivotX 28과 같은 값이어야 한다(C# 정규화 도구의
    /// ForwardEffectLeftInset과도 같다). 대시 자국은 "여기서 출발했다", 활 발사 섬광은 "여기서 시위를 놓았다"를
    /// 그리는 그림이라, 오브젝트 위치가 곧 그 점이어야 회전시켰을 때 그 점을 축으로 돈다.
    /// 가운데를 피벗으로 두면 왼쪽으로 대시할 때 180도 회전하면서 자국이 출발점 반대편으로 넘어간다.
    /// </summary>
    private const float ForwardPivotX = 28f / 256f;

    /// <summary>자를 시트 목록.</summary>
    private static readonly (string folder, string file, string prefix, int frames, PivotKind pivot)[] Sheets =
    {
        // 수정(2026-09-14, 새 캐릭터 화살) — 비행 시트 피벗을 Center → Tip으로 바꿨다.
        //
        // 새 그림은 촉 뒤로 긴 잿불 꼬리가 붙어 꼬리가 전체 길이의 절반을 넘는다. 가운데 피벗이면 콜라이더
        // (오브젝트 위치 중심, 폭 2.2)가 꼬리 쪽에 있어서, <b>촉이 적 콜라이더에 약 2유닛 파고든 뒤에야</b>
        // 맞는다(배율 0.85 기준). 명중 불꽃도 오브젝트 위치에 터지므로 촉이 아니라 촉 뒤에서 터진다.
        // 사수 화살(marksman_arrow)이 같은 이유로 처음부터 Tip이다.
        (Folder, "vfx_ember_arrow_flight_6frames_1536x256", "vfx_arrow_flight", 6, PivotKind.Tip),

        // 수정(2026-09-14) — 옛 명중 시트는 어디서도 안 쓰던 것을 새 그림으로 덮어 되살렸다.
        // 한가운데가 맞은 점이다(NormalizeVfxStrip.ps1 -AnchorX LumaCentroid로 흰 심지를 가운데에 모았다).
        (Folder, "vfx_ember_arrow_impact_6frames_1536x256", "vfx_arrow_impact", 6, PivotKind.Center),

        // 추가 생성(2026-09-14) — 활 발사 섬광. 시위를 놓은 점(고리)에서 화살이 나가는 쪽으로 빛줄기가 뻗는다.
        (Folder, "vfx_ember_arrow_release_6frames_1536x256", "vfx_arrow_release", 6, PivotKind.Forward),

        // 추가 생성(2026-09-14) — 대시 자국. 출발점의 고리에서 대시한 쪽으로 잿불 줄기가 뻗는다.
        (Folder, "vfx_dash_burst_6frames_1536x256", "vfx_dash_burst", 6, PivotKind.Forward),
        (Folder, "vfx_kings_ember_6frames_1536x256", "vfx_kings_ember", 6, PivotKind.Ground),

        // 추가 생성(2026-09-13) — 새 캐릭터의 궁극기(왕의 잿불) 시트. 옛 시트는 보스 잿불 파도가 계속 쓴다.
        //
        // 피벗이 옛 시트와 <b>반대(가운데)</b>인 이유: 새 그림은 시전자 발밑을 중심으로 사방으로 퍼지는
        // 원형 폭발이다. 판정도 발밑 중심 반경 14라, 지면선 피벗이면 폭발 전체가 머리 위로 떠서 판정 원과 어긋난다.
        // 접두어를 옛 시트와 같게 둔 이유: 다시 자를 때 이름으로 기존 스프라이트 ID를 찾으므로,
        // 이름이 같아야 KingsEmber 프리팹의 참조가 끊기지 않는다.
        (Folder, "vfx_kings_ember_crown_6frames_1536x256", "vfx_kings_ember", 6, PivotKind.Center),
        // 수정(2026-09-14) — 아래 세 장은 새 캐릭터용 그림으로 같은 파일에 덮어썼다. 피벗 종류는 그대로 Ground지만
        // 지면선에 놓이는 것이 달라졌다. 옛 그림은 그림의 <b>바닥</b>, 새 그림은 바닥에 누운 <b>타원·균열선의 가운데</b>다
        // (NormalizeVfxStrip.ps1 -AnchorY WidestRow). 새 그림은 파편이 타원 앞쪽 아래로도 튀어서 바닥 끝을
        // 맞추면 프레임마다 타원이 오르내린다. 타원 가운데가 곧 검이 꽂힌 점·기둥이 솟는 점이기도 하다.
        (Folder, "vfx_ash_staff_ground_spell_6frames_1536x256", "vfx_staff_spell", 6, PivotKind.Ground),
        (Folder, "vfx_sword_slam_impact_6frames_1536x256", "vfx_slam_impact", 6, PivotKind.Ground),
        (Folder, "vfx_sword_slam_forward_burst_6frames_1536x256", "vfx_slam_burst", 6, PivotKind.Ground),

        // 추가 생성 — 기본 공격의 검 궤적.
        //
        // 같은 검이지만 피벗이 Q의 두 이펙트와 <b>반대다.</b> Q는 대검을 바닥에 내려찍어
        // 충격파가 지면에서 퍼지므로 지면선이 기준이고, 기본 공격은 허공을 베는 것이라
        // 그림 한가운데가 기준이다. 정규화 도구에 Mode.FloatCenter로 등록한 것과 같은 기준이다 —
        // 한쪽만 바꾸면 그림은 맞는데 붙는 높이만 어긋난다.
        (Folder, "vfx_ember_slash_6frames_1536x256", "vfx_ember_slash", 6, PivotKind.Center),

        // 자폭병의 폭발. 바닥에서 터지므로 피벗이 지면선이다 — 자폭병의 발끝 높이에서
        // 원이 퍼져야 판정 원(발밑 기준)과 그림이 같은 자리에 놓인다.
        //
        // 수정(2026-09-15, 새 폭발 그림) — 피벗을 Ground → Center로 바꿨다.
        // 의도(판정 원과 같은 자리)는 맞았지만 지면선 피벗은 그림의 <b>아랫변</b>을 발밑에 놓는다. 사방으로 퍼지는
        // 원은 가운데가 반지름만큼 위에 떠서(옛 그림 기준 약 3.1유닛), OverlapCircle(발밑, 6) 판정이 보이는 원 밖
        // 아래쪽에서도 맞았다. 새 시트는 Tools/NormalizeVfxStrip.ps1 -PivotX 128 -PivotY 128로 원을 셀 정중앙에
        // 모았으므로, 가운데 피벗이면 오브젝트 위치(자폭병 발밑)가 곧 원의 가운데다.
        // 왕의 잿불 새 시트(vfx_kings_ember_crown)를 Center로 둔 것과 같은 판단이다.
        (Folder, "vfx_bomber_blast_6frames_1536x256", "vfx_bomber_blast", 6, PivotKind.Center),

        // 추가 생성(2026-09-15) — 잿불 망령의 돌진 예고선. 망령이 선 자리에서 돌진할 쪽(오른쪽)으로 바닥에 선이 그어진다.
        // 대시 자국과 같은 Forward다 — 오브젝트 위치가 곧 선의 출발점이라, 돌진 방향으로 돌리면 망령 자리를 축으로 돌고
        // 가로로 늘려도 출발점이 망령에서 떨어지지 않는다. 새 시트는 NormalizeVfxStrip.ps1 -AnchorX LeftEdge -PivotX 28로 만들었다.
        (Folder, "vfx_wraith_charge_telegraph_6frames_1536x256", "vfx_wraith_telegraph", 6, PivotKind.Forward),

        // 추가 생성(2026-09-15) — 잿불 망령의 돌진 출발 자국. 바닥이 갈라져 터진 고리의 <b>왼쪽 끝</b>이 출발점이고, 불티 줄기가
        // 돌진한 쪽(오른쪽)으로 뻗는다. 예고선과 같은 Forward라, 돌진 방향으로 돌리면 망령이 선 자리를 축으로 돈다.
        // 새 시트는 NormalizeVfxStrip.ps1 -SeparateBlobs -AnchorX LeftEdge -AnchorY WidestRow -PivotX 28 -PivotY 128로 만들었다
        // (세로는 고리의 가장 넓은 행 = 고리 가운데를 피벗 높이에 맞췄다. 파편이 위로만 튀어서 범위 가운데는 고리보다 높다).
        (Folder, "vfx_wraith_charge_launch_6frames_1536x256", "vfx_wraith_launch", 6, PivotKind.Forward),

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
        ("Assets/Project/Art/UI", "skill_icons_5frames_1280x256", "skill_icon", 5, PivotKind.Center),
        ("Assets/Project/Art/UI", "relic_icons_3frames_768x256", "relic_icon", 3, PivotKind.Center),
    };

    [MenuItem("Tools/재의 길/그림/VFX 스프라이트 슬라이스")]
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
                           "Tools → 재의 길 → 그림 → 원본 시트 정규화 를 먼저 실행해라.");
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
            PivotKind.Forward => new Vector2(ForwardPivotX, 0.5f), // 추가 생성(2026-09-14)
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
