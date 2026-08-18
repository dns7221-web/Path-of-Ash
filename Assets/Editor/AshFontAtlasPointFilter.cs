using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 선택한 TMP 폰트 에셋의 아틀라스 텍스처를 Point 필터로 바꾼다.
///
/// 픽셀 폰트를 RASTER로 구우면 아틀라스는 그냥 비트맵인데, 이 텍스처의 Filter Mode가
/// Bilinear면 샘플링할 때 이웃 픽셀과 섞여서 글자가 미세하게 번진다. 픽셀 폰트를 쓰는
/// 이유가 사라지는 지점이다.
///
/// 이걸 도구로 만든 이유: 아틀라스는 폰트 에셋 안에 들어 있는 하위 에셋이라 임포트 설정
/// 인스펙터가 없다. 파일에서 임포트된 텍스처가 아니라 TMP가 생성한 것이어서, 인스펙터에서
/// 손으로 바꿀 방법이 없고 코드로만 접근할 수 있다.
///
/// 사용법: Project 창에서 폰트 에셋(들)을 선택하고 메뉴를 누른다.
/// 폰트를 다시 구우면 아틀라스가 새로 만들어지므로 그때마다 다시 눌러야 한다.
/// </summary>
public static class AshFontAtlasPointFilter
{
    [MenuItem("Tools/재의 길/선택한 폰트 아틀라스를 Point 필터로")]
    public static void ApplyToSelection()
    {
        int fontCount = 0;
        int atlasCount = 0;

        foreach (Object selected in Selection.objects)
        {
            TMP_FontAsset font = selected as TMP_FontAsset;
            if (font == null) continue;

            fontCount++;

            // 아틀라스는 여러 장일 수 있다. Multi Atlas Textures를 켜두면 글자가 많을 때
            // 두 번째, 세 번째 장이 생기므로 전부 돌아야 한다.
            if (font.atlasTextures != null)
            {
                foreach (Texture2D atlas in font.atlasTextures)
                {
                    if (atlas == null) continue;

                    atlas.filterMode = FilterMode.Point;
                    EditorUtility.SetDirty(atlas);
                    atlasCount++;
                }
            }

            EditorUtility.SetDirty(font);
        }

        if (fontCount == 0)
        {
            Debug.LogWarning("[재의 길] 선택한 것 중에 TMP 폰트 에셋이 없다. Project 창에서 폰트 에셋을 골라라.");
            return;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[재의 길] 폰트 {fontCount}개의 아틀라스 {atlasCount}장을 Point 필터로 바꿨다.");
    }

    /// <summary>TMP 폰트 에셋을 하나 이상 골랐을 때만 메뉴가 활성화되게 한다.</summary>
    [MenuItem("Tools/재의 길/선택한 폰트 아틀라스를 Point 필터로", true)]
    private static bool ValidateSelection()
    {
        foreach (Object selected in Selection.objects)
        {
            if (selected is TMP_FontAsset) return true;
        }
        return false;
    }
}
