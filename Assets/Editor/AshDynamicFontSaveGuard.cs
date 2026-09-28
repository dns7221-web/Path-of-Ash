using TMPro;
using UnityEditor;

/// <summary>
/// 추가 생성(2026-09-28, 저장소 정리) — 동적(Dynamic) TMP 폰트를 디스크에 쓰기 직전에, 쌓인 글자를 비운다.
///
/// <b>왜 필요한가.</b> 동적 폰트는 화면에 나온 글자를 그때그때 아틀라스에 굽는다. 편집기에서는 그 결과가 폰트 에셋에
/// 그대로 남아서, 플레이하거나 씬을 열기만 해도 에셋이 바뀌고 커밋에 섞였다(09-28 기준 한글 폰트가 +3831줄, 104KB → 8.6MB).
/// 빌드는 TMP의 "Clear Dynamic Data On Build"가 이미 비운다. 이 스크립트는 같은 일을 <b>저장할 때</b> 한다 —
/// 그래서 저장소에 올라가는 폰트는 늘 빈 상태(커밋된 판과 같은 상태)로 같고, 메모리 쪽은 TMP가 필요한 글자를 다시 굽는다.
///
/// <b>저장이 되풀이되지 않는 이유.</b> TMP는 글자를 구운 뒤 에셋을 스스로 저장하지 않는다(TMP_EditorResourceManager의
/// SaveAssets가 막혀 있다). 그래서 비우기는 사람이나 도구가 저장할 때만 한 번씩 일어난다.
///
/// 대상은 동적 폰트 가운데 "Clear Dynamic Data On Build"가 켜진 것만이다. 이 표시가 "쌓인 글자는 버려도 된다"는 뜻이라
/// 같은 뜻으로 빌려 쓴다. 정적 폰트나 이 표시를 끈 폰트는 건드리지 않는다.
/// </summary>
public class AshDynamicFontSaveGuard : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        foreach (string path in paths)
        {
            // 형식부터 본다. 저장 목록에는 씬·프리팹이 섞여 있어서, 전부 불러 보면 저장이 느려진다.
            if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(TMP_FontAsset)) continue;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
            if (!ClearsOnBuild(font)) continue;

            // true = 아틀라스 크기까지 줄인다. 커밋된 판도 이 상태(글자 0개, 아틀라스 1×1)라서 저장해도 차이가 안 생긴다.
            font.ClearFontAssetData(true);
        }

        return paths;
    }

    /// <summary>TMP의 "Clear Dynamic Data On Build" 표시. 속성이 internal이라 직렬화 값으로 읽는다.</summary>
    private static bool ClearsOnBuild(TMP_FontAsset font)
    {
        SerializedProperty flag = new SerializedObject(font).FindProperty("m_ClearDynamicDataOnBuild");
        return flag != null && flag.boolValue;
    }
}
