using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물 인벤토리 화면을 씬에 만든다.
///
/// 메뉴: Tools → 재의 길 → 인벤토리 화면 생성
///
/// HUD 도구(<see cref="AshGameHudBuilder"/>)와 따로 둔 이유: HUD는 게임 중 항상 켜져 있는
/// 게이지와 아이콘이고, 이건 눌러서 여는 별개의 화면이다. 한 도구에 넣으면 게이지를 손볼 때마다
/// 인벤토리까지 다시 만들게 되어, 손으로 맞춰둔 칸 위치가 매번 날아간다.
///
/// <b>다시 실행하면 통째로 다시 만든다.</b> 칸 위치를 코드 상수로 잡아놨으므로 그 값이
/// 정답이고, 씬에 남은 것은 이전 실행 결과일 뿐이다. 위치를 고치고 싶으면 아래 상수를 고쳐라.
/// </summary>
public static class AshInventoryUiBuilder
{
    private const string PanelPath = "Assets/Project/Art/UI/InventoryPanel.png";
    private const string SlotPath = "Assets/Project/Art/UI/InventorySlot.png";

    // 화면에 띄울 패널 크기(픽셀). 원본이 1610x977이라 비율을 지켜 줄였다.
    private const float PanelWidth = 1400f;
    private const float PanelHeight = 849f;

    /// <summary>
    /// 패널 그림에서 각 부분이 있는 자리. 0~1 비율이고 왼쪽 위가 (0,0)이다.
    ///
    /// 그림을 보고 눈으로 잰 값이다. 자동으로 찾지 않는 이유: 팔각형 칸은 테두리가 금속이고
    /// 안쪽이 검은데, 패널 배경도 검다. 색으로 구분하려 들면 장식의 그림자까지 칸으로 잡는다.
    /// 열 줄짜리 판정을 만드는 것보다 여기 숫자를 고치는 편이 빠르고 확실하다.
    /// </summary>
    private const float BagLeft = 0.068f;
    private const float BagRight = 0.627f;
    private const float BagTop = 0.143f;
    private const float BagBottom = 0.900f;

    private const float EquipCenterX = 0.790f;
    private static readonly float[] EquipCenterY = { 0.238f, 0.488f, 0.709f };

    // 팔각형 안쪽 지름(패널 폭 대비). 칸 그림과 아이콘이 이 크기로 들어간다.
    private const float EquipSize = 0.094f;

    // 보관함 칸 하나의 크기(픽셀)와 간격.
    private const float BagCellSize = 96f;
    private const float BagSpacing = 12f;

    [MenuItem("Tools/재의 길/인벤토리 화면 생성")]
    public static void Build()
    {
        var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotPath);

        if (panelSprite == null || slotSprite == null)
        {
            Debug.LogError("[인벤토리] 패널/칸 그림을 못 찾았다.\n" +
                           "Tools → 재의 길 → 게이지 이미지 다듬기 를 먼저 실행해라.\n" +
                           $"필요한 파일: {PanelPath}, {SlotPath}");
            return;
        }

        Canvas canvas = FindOrCreateCanvas();

        // 이전 실행 결과를 지운다. 남겨두면 칸이 두 겹으로 쌓인다.
        var old = canvas.transform.Find("InventoryScreen");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // 항상 켜져 있는 껍데기. 키 입력을 듣는 컴포넌트가 여기 붙는다.
        var screenObject = NewRect("InventoryScreen", canvas.transform);
        Stretch(screenObject);
        var screen = screenObject.gameObject.AddComponent<InventoryScreen>();

        // 실제로 켜고 끄는 부분. 껍데기와 나누는 것이 핵심이다 —
        // InventoryScreen을 이 오브젝트에 붙이면, 꺼진 순간 Update가 안 돌아서
        // 키를 눌러도 스스로를 다시 켤 수가 없다. 에러도 경고도 없이 그냥 안 열린다.
        var rootObject = NewRect("Root", screenObject.transform);
        Stretch(rootObject);

        // 어두운 막. 화면을 열면 게임 화면이 뒤로 물러나 보여야 어디에 집중할지가 분명해진다.
        var dim = NewRect("Dim", rootObject.transform);
        Stretch(dim);
        var dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.72f);

        // 패널
        var panel = NewRect("Panel", rootObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = panelSprite;
        panelImage.preserveAspect = true;

        RectTransform bagArea = BuildBagArea(panel);
        RelicSlotView template = BuildSlotTemplate(screenObject.transform, slotSprite);
        var equipSlots = BuildEquipSlots(panel, slotSprite);
        var (nameLabel, descriptionLabel) = BuildLabels(panel);

        var serialized = new SerializedObject(screen);
        serialized.FindProperty("root").objectReferenceValue = rootObject.gameObject;
        serialized.FindProperty("bagArea").objectReferenceValue = bagArea;
        serialized.FindProperty("bagSlotTemplate").objectReferenceValue = template;
        serialized.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        serialized.FindProperty("descriptionLabel").objectReferenceValue = descriptionLabel;

        var slotsProperty = serialized.FindProperty("equipSlots");
        slotsProperty.arraySize = equipSlots.Length;
        for (int i = 0; i < equipSlots.Length; i++)
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = equipSlots[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();

        EnsureEventSystem();

        // 화면은 꺼진 채로 시작한다. 켜둔 채 저장하면 게임을 시작하자마자 인벤토리가 떠 있다.
        // 끄는 것은 Root뿐이다. 껍데기(screenObject)는 켜둬야 키 입력을 듣는다.
        rootObject.gameObject.SetActive(false);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[인벤토리] 화면을 만들었다. I 또는 Tab으로 열고 닫는다.\n" +
                  "씬을 저장해라(Ctrl+S).");
    }

    /// <summary>보관함 칸이 채워질 자리. 격자 배치를 Unity에 맡긴다.</summary>
    private static RectTransform BuildBagArea(RectTransform panel)
    {
        var area = NewRect("BagArea", panel);

        // 앵커를 비율로 잡으면 패널 크기를 바꿔도 자리가 따라간다.
        // 위아래가 뒤집힌 것에 주의 — UI의 y는 아래가 0인데 위 상수는 위가 0 기준이다.
        area.anchorMin = new Vector2(BagLeft, 1f - BagBottom);
        area.anchorMax = new Vector2(BagRight, 1f - BagTop);
        area.offsetMin = Vector2.zero;
        area.offsetMax = Vector2.zero;

        var grid = area.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(BagCellSize, BagCellSize);
        grid.spacing = new Vector2(BagSpacing, BagSpacing);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;

        return area;
    }

    /// <summary>
    /// 보관함 칸의 원본. 꺼진 채로 화면 아래에 둔다.
    ///
    /// BagArea 안에 두면 안 된다. GridLayoutGroup은 꺼진 자식도 자리를 잡아버려서
    /// 첫 칸이 항상 비어 보인다.
    /// </summary>
    private static RelicSlotView BuildSlotTemplate(Transform parent, Sprite slotSprite)
    {
        RelicSlotView view = CreateSlot("BagSlotTemplate", parent, slotSprite, BagCellSize);
        view.gameObject.SetActive(false);
        return view;
    }

    private static RelicSlotView[] BuildEquipSlots(RectTransform panel, Sprite slotSprite)
    {
        var result = new RelicSlotView[RelicInventory.SlotCount];
        float size = EquipSize * PanelWidth;

        for (int i = 0; i < result.Length && i < EquipCenterY.Length; i++)
        {
            RelicSlotView view = CreateSlot($"EquipSlot{i}", panel, slotSprite, size);

            var rect = (RectTransform)view.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(EquipCenterX, 1f - EquipCenterY[i]);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);

            // 팔각형 테두리가 이미 그려져 있어서 칸 그림을 또 얹으면 두 겹이 된다.
            // 클릭 판정만 남기고 그림은 감춘다.
            var frame = view.GetComponent<Image>();
            frame.color = new Color(1f, 1f, 1f, 0f);

            result[i] = view;
        }

        return result;
    }

    /// <summary>칸 하나를 만든다. 테두리 이미지가 클릭 판정을 겸하고, 자식이 유물 그림이다.</summary>
    private static RelicSlotView CreateSlot(string name, Transform parent, Sprite slotSprite, float size)
    {
        var rect = NewRect(name, parent);
        rect.sizeDelta = new Vector2(size, size);

        var frame = rect.gameObject.AddComponent<Image>();
        frame.sprite = slotSprite;
        frame.preserveAspect = true;

        // 이게 꺼져 있으면 칸을 눌러도 아무 일이 안 일어난다.
        frame.raycastTarget = true;

        var icon = NewRect("Icon", rect);
        icon.anchorMin = new Vector2(0.5f, 0.5f);
        icon.anchorMax = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = Vector2.zero;
        icon.sizeDelta = new Vector2(size * 0.74f, size * 0.74f);

        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.preserveAspect = true;

        // 아이콘이 클릭을 가로채면 칸을 눌러도 반응이 없다.
        iconImage.raycastTarget = false;
        iconImage.enabled = false;

        var view = rect.gameObject.AddComponent<RelicSlotView>();
        view.SetImages(frame, iconImage);
        return view;
    }

    /// <summary>
    /// 고른 유물의 이름과 설명. 패널 왼쪽 아래에 겹쳐 놓는다.
    ///
    /// UI.Text가 아니라 TextMeshPro를 쓰는 이유가 두 가지다.
    /// 하나는 HUD가 이미 TMP라 화면마다 글자 모양이 다르면 안 되고,
    /// 다른 하나는 <b>UI.Text의 기본 폰트가 Unity 6에서 사라졌다</b>는 것이다
    /// (Arial.ttf → LegacyRuntime.ttf로 바뀌었는데 그마저도 안 잡히는 경우가 있다).
    /// </summary>
    private static (TMPro.TMP_Text name, TMPro.TMP_Text description) BuildLabels(RectTransform panel)
    {
        // 프로젝트의 한글 폰트. 못 찾으면 TMP 기본값으로 두고 경고만 남긴다 —
        // 폰트 하나 때문에 화면 전체가 안 만들어지는 편이 더 나쁘다.
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
            "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular32.asset");

        if (font == null)
            Debug.LogWarning("[인벤토리] 한글 폰트를 못 찾아 TMP 기본 폰트로 만든다. 한글이 깨질 수 있다.");

        var nameRect = NewRect("SelectedName", panel);
        nameRect.anchorMin = new Vector2(BagLeft, 0.02f);
        nameRect.anchorMax = new Vector2(BagRight, 0.10f);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;

        var nameText = nameRect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) nameText.font = font;
        nameText.fontSize = 26;
        nameText.fontStyle = TMPro.FontStyles.Bold;
        nameText.alignment = TMPro.TextAlignmentOptions.BottomLeft;
        nameText.color = new Color(1f, 0.86f, 0.62f);
        nameText.raycastTarget = false;

        var descRect = NewRect("SelectedDescription", panel);
        descRect.anchorMin = new Vector2(BagRight + 0.01f, 0.02f);
        descRect.anchorMax = new Vector2(0.98f, 0.16f);
        descRect.offsetMin = Vector2.zero;
        descRect.offsetMax = Vector2.zero;

        var descText = descRect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) descText.font = font;
        descText.fontSize = 18;
        descText.alignment = TMPro.TextAlignmentOptions.TopLeft;
        descText.color = new Color(0.82f, 0.80f, 0.76f);
        descText.raycastTarget = false;

        return (nameText, descText);
    }

    /// <summary>HUD 캔버스를 찾아 쓴다. 없으면 만든다.</summary>
    private static Canvas FindOrCreateCanvas()
    {
        foreach (var existing in Object.FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            // 이게 없으면 칸을 눌러도 아무 반응이 없다. HUD만 있던 캔버스에는 없을 수 있다.
            if (existing.GetComponent<GraphicRaycaster>() == null)
            {
                existing.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log("[인벤토리] 캔버스에 GraphicRaycaster가 없어서 붙였다.");
            }

            return existing;
        }

        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler),
                                          typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        return canvas;
    }

    /// <summary>
    /// 클릭을 받으려면 씬에 EventSystem이 있어야 한다.
    ///
    /// 없어도 에러가 안 나고 그냥 클릭이 안 먹는다. HUD만 있던 씬에는 없을 수 있어서 확인한다.
    /// </summary>
    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include) != null) return;

        var systemObject = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        Debug.Log($"[인벤토리] EventSystem이 없어서 만들었다: {systemObject.name}");
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var target = new GameObject(name, typeof(RectTransform));
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
