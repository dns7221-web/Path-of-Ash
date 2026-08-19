using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// 플레이어 스프라이트 시트를 <see cref="AshPlayerSpriteSheets"/> 표대로 잘라내는 에디터 도구.
///
/// 메뉴: Tools → 재의 길 → 플레이어 스프라이트 슬라이스
///
/// Sprite Editor 창에서 손으로 Slice를 누르지 않고 스크립트로 둔 이유:
/// 1) 손으로 자르면 이름이 자동으로 "시트이름_0"이 되어 애니메이션 클립을 만들 때 어떤 게
///    대시고 어떤 게 피격인지 이름만 봐선 모른다. 여기서는 표에 적힌 대로 dash/hit로 나눠 붙인다.
/// 2) 피벗을 지면선(y=217)에 맞춰야 하는데, 창에서 24개 프레임을 하나씩 찍으면 반드시 어긋난다.
/// 3) 시트를 다시 뽑았을 때 메뉴 한 번으로 같은 결과가 나온다(멱등).
///
/// <b>기존 스프라이트 ID를 이름으로 찾아 재사용한다.</b> 이게 이 스크립트에서 가장 중요한 부분이다.
/// 유니티는 스프라이트를 이름이 아니라 GUID로 참조하는데, 다시 자를 때마다 새 GUID를 발급하면
/// 이미 만들어둔 애니메이션 클립이 전부 "Missing"이 된다. 이름이 같으면 ID를 물려주므로
/// 시트를 다시 뽑아 재슬라이스해도 클립이 살아 있다.
/// </summary>
public static class AshPlayerSpriteSlicer
{
    // 수정(적 추가 시점): 플레이어 전용이던 것을 캐릭터 세트 전체를 도는 형태로 바꿨다.
    // 메뉴를 캐릭터마다 따로 만들지 않은 이유: 세트가 늘 때마다 메뉴 항목과 그걸 부르는
    // 함수를 같이 추가해야 하는데, 표에만 추가하고 메뉴를 잊는 실수가 반드시 생긴다.
    // 전부 도는 쪽이 몇 초 더 걸릴 뿐 빠뜨릴 수가 없다.
    [MenuItem("Tools/재의 길/캐릭터 스프라이트 슬라이스")]
    public static void SliceAll()
    {
        int sheetOk = 0;
        int spriteTotal = 0;
        int sheetTotal = 0;

        // 여러 텍스처를 연달아 재임포트하므로 배치로 묶는다. 안 묶으면 한 장 끝날 때마다
        // 에디터가 에셋 DB를 갱신하면서 눈에 띄게 느려진다.
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var set in AshPlayerSpriteSheets.AllSets)
            {
                foreach (var sheet in set.Sheets)
                {
                    sheetTotal++;
                    int made = SliceSheet(set, sheet);
                    if (made > 0)
                    {
                        sheetOk++;
                        spriteTotal += made;
                    }
                }
            }
        }
        finally
        {
            // 중간에 예외가 나도 반드시 풀어야 한다. 안 풀면 에디터가 에셋 갱신이 멈춘
            // 상태로 남아서 이후 모든 임포트가 안 먹는다.
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        Debug.Log($"[캐릭터 슬라이스] 시트 {sheetOk}/{sheetTotal}장, " +
                  $"스프라이트 {spriteTotal}개 생성 완료. 피벗 = 가로 중앙 / 지면선 " +
                  $"y={AshPlayerSpriteSheets.GroundLineY}");
    }

    /// <summary>
    /// 시트 한 장을 자른다. 성공하면 만든 스프라이트 개수를, 실패하면 0을 돌려준다.
    /// </summary>
    private static int SliceSheet(AshPlayerSpriteSheets.CharacterSet set,
                                  AshPlayerSpriteSheets.Sheet sheet)
    {
        string path = $"{set.FolderPath}/{sheet.FileName}.png";

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[플레이어 슬라이스] 시트를 못 찾았다: {path}");
            return 0;
        }

        // 실제 텍스처 폭이 표와 다르면 셀 좌표가 전부 어긋난다. 자르기 전에 막는다.
        // 시트를 다시 뽑으면서 프레임 수가 바뀐 경우가 여기 걸린다.
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture != null && texture.width != sheet.ExpectedWidth)
        {
            Debug.LogError(
                $"[플레이어 슬라이스] {sheet.FileName}의 가로폭이 {texture.width}px인데 " +
                $"표는 {sheet.CellCount}칸({sheet.ExpectedWidth}px)을 기대한다. " +
                $"AshPlayerSpriteSheets의 칸 수를 실제 시트에 맞춰 고쳐라.");
            return 0;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;

        // SpriteDataProviderFactories는 유니티가 제공하는 공식 슬라이스 API다.
        // 예전 방식인 TextureImporter.spritesheet 배열은 레거시라 새 기능(보조 텍스처, 이름-ID 매핑)이
        // 반영되지 않아서 쓰지 않는다.
        var factory = new SpriteDataProviderFactories();
        factory.Init();

        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null)
        {
            Debug.LogError($"[플레이어 슬라이스] 데이터 프로바이더를 못 얻었다: {sheet.FileName}");
            return 0;
        }

        provider.InitSpriteEditorDataProvider();

        // 이미 잘려 있던 스프라이트들의 이름 → ID를 모아둔다. 아래에서 같은 이름이 나오면
        // 그 ID를 그대로 물려줘서 기존 애니메이션 클립의 참조를 살린다.
        var idByName = new Dictionary<string, GUID>();
        foreach (var old in provider.GetSpriteRects())
        {
            // 같은 이름이 두 번 나올 일은 없지만, 있으면 먼저 나온 쪽을 쓴다.
            if (!idByName.ContainsKey(old.name))
                idByName.Add(old.name, old.spriteID);
        }

        var rects = new List<SpriteRect>();
        foreach (var segment in sheet.Segments)
        {
            for (int i = 0; i < segment.FrameCount; i++)
            {
                int cell = segment.StartCell + i;
                string name = set.SpriteName(segment.Name, i);

                rects.Add(new SpriteRect
                {
                    name = name,
                    rect = AshPlayerSpriteSheets.CellRect(cell),

                    // 지면선 피벗은 유니티 프리셋(Center/Bottom 등)에 없는 위치라 Custom을 쓴다.
                    alignment = SpriteAlignment.Custom,
                    pivot = AshPlayerSpriteSheets.Pivot,

                    // 9-슬라이스를 안 쓰므로 테두리는 0이다. 값이 남아 있으면 스프라이트가
                    // 늘어날 때 이상하게 잘리므로 명시적으로 비운다.
                    border = Vector4.zero,

                    spriteID = idByName.TryGetValue(name, out var existingId)
                        ? existingId
                        : GUID.Generate(),
                });
            }
        }

        provider.SetSpriteRects(rects.ToArray());

        // 이름-ID 쌍 테이블도 같이 갱신해야 한다. 이걸 빼먹으면 스프라이트는 잘리는데
        // 다른 에셋이 이름으로 찾아오는 경로가 끊긴다.
        var nameIdProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (nameIdProvider != null)
        {
            var pairs = new List<SpriteNameFileIdPair>(rects.Count);
            foreach (var r in rects)
                pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));

            nameIdProvider.SetNameFileIdPairs(pairs);
        }

        provider.Apply();

        // Apply는 메모리 상의 임포터 설정만 바꾼다. 실제로 텍스처를 다시 잘라 에셋에 반영하려면
        // 재임포트를 걸어야 한다.
        importer.SaveAndReimport();

        return rects.Count;
    }
}
