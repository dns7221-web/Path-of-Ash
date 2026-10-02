using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-10-02) — 능력치 창(<see cref="StatsScreen"/>)을 지금 열린 씬(Game)에 만든다.
///
/// 손으로 배치하지 않고 빌더로 만드는 이유는 다른 화면 빌더(보스 열쇠·인벤토리)와 같다:
/// 그림이나 배치를 바꿨을 때 메뉴 한 번으로 똑같이 다시 만들 수 있어야 하고, 무엇을 어디에 놓았는지가
/// 코드로 남아야 나중에 읽을 수 있다.
///
/// 배치 기준(패널 그림 AbilityStatsPanel.png, 1086×1448 세로형):
/// - 위쪽 15% 안쪽에 가로 구분선이 그려져 있어 그 위를 제목 칸으로 쓴다.
/// - 그 아래 테두리 안쪽을 일곱 줄로 고르게 나눈다.
/// 좌표는 모두 패널에 대한 비율(앵커)로 잡아서, 패널 크기를 바꿔도 글자 자리가 따라간다.
/// </summary>
public static class AshStatsScreenBuilder
{
    private const string PanelPath = "Assets/Project/Art/UI/AbilityStatsPanel.png";
    private const string FontPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";

    // 캔버스 기준 해상도(1920×1080)에서의 패널 높이. 너비는 그림 비율(1086:1448)에서 나온다.
    private const float PanelHeight = 900f;
    private const float PanelAspect = 1086f / 1448f;

    // 패널 안 비율 좌표(아래가 0, 위가 1 — UI 앵커 기준).
    private const float TitleBottom = 0.865f;  // 구분선 바로 위
    private const float TitleTop = 0.955f;
    private const float RowsTop = 0.80f;       // 구분선 아래 여백 다음부터
    private const float RowPitch = 0.095f;     // 일곱 줄 × 0.095 = 0.665 → 아래 테두리(0.07) 위에서 끝난다
    private const float SideMargin = 0.11f;    // 좌우 테두리 두께 + 여백

    private const float TitleFontSize = 52f;
    private const float RowFontSize = 34f;

    // 설정 화면·스킵 확인 창과 같은 글자색.
    private static readonly Color TextColor = new Color(0.94f, 0.89f, 0.81f, 1f);
    private static readonly Color NameColor = new Color(0.78f, 0.72f, 0.64f, 1f);

    [MenuItem("Tools/재의 길/화면/능력치 화면 생성")]
    public static void Build()
    {
        var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
        if (panelSprite == null)
        {
            Debug.LogError($"[능력치] 패널 그림을 못 찾았다: {PanelPath} (Texture Type이 Sprite인지 확인)");
            return;
        }

        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
        if (font == null)
            Debug.LogWarning($"[능력치] 폰트를 못 찾았다: {FontPath} — 기본 폰트로 만들면 한글이 깨진다.");

        // 캔버스를 먼저 정한다(지우기 전에 정해야 "지금 올라가 있는 캔버스를 그대로 쓴다"가 가능하다).
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            Debug.LogError("[능력치] 화면을 올릴 캔버스를 못 찾았다. Game 씬을 열고 다시 실행해라 " +
                           "(인벤토리·보스 열쇠 화면이 있는 캔버스를 쓴다).");
            return;
        }

        // 지난 실행 결과를 씬 전체에서 지운다. 이 화면이 둘이면 C를 둘 다 듣고 멈춤 스택이 꼬인다.
        foreach (var stale in Object.FindObjectsByType<StatsScreen>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(stale.gameObject);
        }

        // 항상 켜져 있는 껍데기. 키 입력을 듣는 StatsScreen이 여기 붙는다.
        var screenObject = NewRect("StatsScreen", canvas.transform);
        Stretch(screenObject);
        var screen = screenObject.gameObject.AddComponent<StatsScreen>();

        // 실제로 켜고 끄는 부분. 껍데기와 나눠야 꺼진 뒤에도 Update가 돌아 C로 다시 열 수 있다.
        var rootObject = NewRect("Root", screenObject.transform);
        Stretch(rootObject);

        // 뒤를 어둡게 하고 클릭을 막는 막.
        var dim = NewRect("Dim", rootObject.transform);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        var panel = NewRect("Panel", rootObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelHeight * PanelAspect, PanelHeight);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = panelSprite;
        panelImage.preserveAspect = true;
        panelImage.raycastTarget = false; // 클릭은 Dim이 막는다

        CreateText("Title", panel, font, "능력치", TitleFontSize, TextColor,
                   TMPro.TextAlignmentOptions.Midline, SideMargin, 1f - SideMargin, TitleBottom, TitleTop);

        var names = new TMPro.TMP_Text[StatsScreen.RowCount];
        var values = new TMPro.TMP_Text[StatsScreen.RowCount];
        for (int i = 0; i < StatsScreen.RowCount; i++)
        {
            float top = RowsTop - i * RowPitch;
            float bottom = top - RowPitch;

            // 이름과 값은 같은 줄 칸을 함께 쓰고 정렬만 반대로 둔다. 표처럼 오른쪽 끝이 맞는다.
            // 이름 글자는 StatsScreen.Awake가 채운다(이름과 값의 순서를 한 파일에서 관리하려고).
            names[i] = CreateText($"Row{i}Name", panel, font, string.Empty, RowFontSize, NameColor,
                                  TMPro.TextAlignmentOptions.MidlineLeft, SideMargin, 1f - SideMargin, bottom, top);
            values[i] = CreateText($"Row{i}Value", panel, font, "-", RowFontSize, TextColor,
                                   TMPro.TextAlignmentOptions.MidlineRight, SideMargin, 1f - SideMargin, bottom, top);
        }

        var serialized = new SerializedObject(screen);
        serialized.FindProperty("root").objectReferenceValue = rootObject.gameObject;
        SetArray(serialized.FindProperty("nameLabels"), names);
        SetArray(serialized.FindProperty("valueLabels"), values);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 화면은 꺼진 채로 시작한다. 끄는 것은 Root뿐이고 껍데기는 켜 둬야 키 입력을 듣는다.
        rootObject.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[능력치] 화면을 만들었다. C로 열고 닫는다.\n씬을 저장해라(Ctrl+S).");
    }

    /// <summary>
    /// 화면을 올릴 캔버스. 이미 능력치 창이 있으면 그 캔버스, 없으면 보스 열쇠·인벤토리 화면이 있는 캔버스를 쓴다.
    /// 캔버스를 이름순·무순서로 고르지 않는 이유: 다시 실행할 때 다른 캔버스가 집히면 화면이 두 벌 생긴다
    /// (인벤토리 빌더에서 실제로 겪은 사고, AshBossKeyUiBuilder.FindOrCreateCanvas 주석 참고).
    /// </summary>
    private static Canvas FindCanvas()
    {
        Component placed = Object.FindFirstObjectByType<StatsScreen>(FindObjectsInactive.Include);
        if (placed == null) placed = Object.FindFirstObjectByType<BossKeyScreen>(FindObjectsInactive.Include);
        if (placed == null) placed = Object.FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);
        if (placed == null) return null;

        // 인자 true는 "꺼진 부모도 본다"는 뜻이다. 캔버스가 꺼져 있어도 찾는다.
        return placed.GetComponentInParent<Canvas>(true);
    }

    /// <summary>패널 안 비율 칸에 TextMeshPro 글자를 만든다. 클릭은 가로채지 않는다.</summary>
    private static TMPro.TMP_Text CreateText(string name, RectTransform parent, TMPro.TMP_FontAsset font,
        string value, float fontSize, Color color, TMPro.TextAlignmentOptions alignment,
        float xMin, float xMax, float yMin, float yMax)
    {
        var rect = NewRect(name, parent);
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = rect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.richText = true; // 값 칸의 유물 보너스를 <color>로 칠한다
        text.raycastTarget = false;
        return text;
    }

    /// <summary>직렬화된 배열 필드에 값을 채운다.</summary>
    private static void SetArray(SerializedProperty property, Object[] items)
    {
        property.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private static RectTransform NewRect(string label, Transform parent)
    {
        var target = new GameObject(label, typeof(RectTransform));
        target.transform.SetParent(parent, false);
        return (RectTransform)target.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
