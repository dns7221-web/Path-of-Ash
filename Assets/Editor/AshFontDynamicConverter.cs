using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-24, 메모리 정리) — 한글 폰트 에셋(96)을 정적(Static) 아틀라스에서 동적(Dynamic)으로 바꾼다.
/// 메뉴: Tools → 재의 길 → 그림 → 한글 폰트를 Dynamic으로 (메모리 줄이기)
///
/// <b>문제.</b> NeoDunggeunmoPro-Regular96은 4096×4096 정적 아틀라스에 완성형 한글 11,172자를 전부 미리 구워 뒀다(파일 37MB).
/// TMP 설정의 Fallback이라 게임 시작부터 메모리에 올라가 안 내려간다. 실제로 화면에 나오는 한글은 수백 자다.
/// 첫 GPU 업로드가 설정·인벤토리 화면을 처음 열 때 133~233ms 끊김으로 잡히기도 했다(플레이 영상 분석).
///
/// <b>해결.</b> 유니티 TMP의 내장 기능 Atlas Population Mode = Dynamic. 화면에 처음 나오는 글자만 그때 원본 폰트(TTF)에서 구워 아틀라스에 더한다.
/// 직접 "쓰는 글자 목록"을 만들어 정적으로 다시 굽지 않은 이유: 문구를 고칠 때마다 목록을 다시 뽑아야 하고, 빠진 글자는 네모로 나온다.
///
/// 하는 일:
/// <list type="number">
/// <item>원본 폰트(TTF)를 연결한다 — 동적 모드는 실행 중에 글자를 구울 원본이 있어야 한다(지금은 비어 있다).</item>
/// <item>Dynamic으로 바꾸고 아틀라스를 2048×2048로 줄인다. 넘치면 두 번째 장을 만들게 Multi Atlas를 켠다.</item>
/// <item>미리 구운 글자를 전부 지운다(ClearFontAssetData) — 이게 37MB를 없애는 단계다.</item>
/// <item>빌드할 때 편집기에서 쌓인 글자를 비우게 한다(Clear Dynamic Data On Build).</item>
/// <item>아틀라스를 Point 필터로 — 픽셀 폰트가 번지지 않게(AshFontAtlasPointFilter와 같은 이유).</item>
/// </list>
///
/// 되돌리기: git에서 폰트 에셋 파일을 되돌리면 된다(`git checkout -- Assets/Project/Art/UI/Fonts`).
/// 알아 둘 것: 동적 폰트는 편집기에서 플레이할 때 쓴 글자가 에셋에 쌓여 파일이 바뀐 것으로 보인다. 커밋 전에 다시 이 메뉴를 누르면 비워진다.
/// </summary>
public static class AshFontDynamicConverter
{
    private const string FontAssetPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";
    private const string SourceFontPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular.ttf";
    private const int AtlasSize = 2048;

    [MenuItem("Tools/재의 길/그림/한글 폰트를 Dynamic으로 (메모리 줄이기)")]
    public static void Convert()
    {
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (fontAsset == null || sourceFont == null)
        {
            Debug.LogError($"[폰트] 폰트 에셋이나 원본 폰트를 못 찾았다: {FontAssetPath} / {SourceFontPath}");
            return;
        }

        long before = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(fontAsset.atlasTexture);

        // 1~2, 4) 필드 몇 개는 스크립트에서 쓰기가 막혀 있어(원본 폰트 등) SerializedObject로 직접 넣는다.
        var serialized = new SerializedObject(fontAsset);
        serialized.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
        serialized.FindProperty("m_AtlasPopulationMode").intValue = (int)AtlasPopulationMode.Dynamic;
        serialized.FindProperty("m_IsMultiAtlasTexturesEnabled").boolValue = true;
        serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
        serialized.FindProperty("m_AtlasWidth").intValue = AtlasSize;
        serialized.FindProperty("m_AtlasHeight").intValue = AtlasSize;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 3) 미리 구운 글자와 아틀라스를 비운다. true = 아틀라스 크기를 0으로 줄였다가 글자가 들어올 때 키운다.
        fontAsset.ClearFontAssetData(true);

        // 5) 픽셀 폰트는 Point 필터여야 한다. 비운 뒤 아틀라스 텍스처가 새로 잡혔을 수 있어 다시 건다.
        if (fontAsset.atlasTextures != null)
        {
            foreach (Texture2D atlas in fontAsset.atlasTextures)
            {
                if (atlas == null) continue;
                atlas.filterMode = FilterMode.Point;
                EditorUtility.SetDirty(atlas);
            }
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();

        long after = fontAsset.atlasTexture != null
            ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(fontAsset.atlasTexture)
            : 0;
        Debug.Log($"[폰트] {fontAsset.name}을 Dynamic으로 바꿨다. 아틀라스 메모리 {before / (1024f * 1024f):0.0}MB → " +
                  $"{after / (1024f * 1024f):0.0}MB (글자는 화면에 처음 나올 때 채워진다). " +
                  "플레이해서 한글이 네모로 안 나오는지, 글자가 번지지 않는지 확인해라.");
    }
}
