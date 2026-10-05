using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — 프로젝트의 모든 유물(<see cref="RelicData"/>)을 모아 <see cref="RelicCatalog"/>를 채운다.
/// 메뉴: Tools → 재의 길 → 프리팹 → 유물 카탈로그 갱신
///
/// <b>언제 누르나.</b> 유물을 새로 만들었을 때. 카탈로그에 없는 유물은 저장한 판을 이어할 때 빈 칸이 된다.
/// 잊어도 RunSaveTests의 "모든 유물이 카탈로그에 있다"가 빨갛게 알려 준다.
///
/// 손으로 목록을 고치지 않고 메뉴로 채우는 이유: 유물을 하나씩 끌어다 놓으면 빠뜨리거나 두 번 넣는다.
/// <see cref="AssetDatabase.FindAssets(string)"/>의 "t:RelicData"는 프로젝트의 그 타입 에셋을 전부 찾으므로 빠질 수가 없다.
/// 이름순으로 정렬해 두면 갱신할 때마다 순서가 흔들리지 않아 git 변경분이 "추가된 유물" 한 줄만 나온다.
/// </summary>
public static class AshRelicCatalogBuilder
{
    private const string CatalogPath = "Assets/Project/Resources/RelicCatalog.asset";

    [MenuItem("Tools/재의 길/프리팹/유물 카탈로그 갱신")]
    public static void Refresh()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<RelicCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<RelicCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        RelicData[] relics = AssetDatabase.FindAssets("t:RelicData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<RelicData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(relic => relic != null)
            .OrderBy(relic => relic.name, System.StringComparer.Ordinal)
            .ToArray();

        // 비공개 직렬화 필드라 SerializedObject로 넣는다(인스펙터에서 끌어다 놓는 것과 같은 길). 게임 코드에 "편집기 전용 입구"를 만들지 않는다.
        var serialized = new SerializedObject(catalog);
        SerializedProperty list = serialized.FindProperty("relics");
        list.arraySize = relics.Length;
        for (int i = 0; i < relics.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = relics[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"[유물 카탈로그] {relics.Length}종으로 갱신: {CatalogPath}", catalog);
    }
}
