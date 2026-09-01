using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보스 열쇠 화면을 씬에 만든다.
///
/// 메뉴: Tools → 재의 길 → 보스 열쇠 화면 생성
///
/// <see cref="AshInventoryUiBuilder"/>와 따로 둔 이유는 그 도구가 HUD 빌더와 갈라진 이유와 같다.
/// 한 도구에 넣으면 인벤토리를 손볼 때마다 이 화면까지 다시 만들게 되어, 손으로 맞춰둔
/// 칸 위치가 매번 날아간다. 도구가 나뉘어 있으면 <b>건드린 것만 다시 만들어진다.</b>
///
/// 작은 도우미(NewRect, Stretch 등)를 인벤토리 빌더에서 가져다 쓰지 않고 여기 다시 쓴 것도
/// 같은 이유다. 공유하면 한쪽을 고칠 때 다른 쪽이 같이 바뀐다. 열 줄짜리 함수를 아끼려고
/// 두 도구를 묶는 건 손해다.
///
/// <b>다시 실행하면 통째로 다시 만든다.</b> 칸 위치는 아래 상수가 정답이고, 씬에 남은 것은
/// 이전 실행 결과일 뿐이다. 위치를 고치고 싶으면 상수를 고치거나, 만든 뒤 씬에서 직접 옮겨라.
/// </summary>
public static class AshBossKeyUiBuilder
{
    private const string PanelPath = "Assets/Project/Art/UI/boss-key-panel-4-empty-slots-square.png";
    private const string SlotPath = "Assets/Project/Art/UI/InventorySlot.png";

    /// <summary>
    /// 라벨에 쓸 폰트.
    ///
    /// 지정하지 않으면 TMP 기본 폰트로 떨어지는데 거기엔 <b>한글 글리프가 없어서</b>
    /// 글자가 깨지거나 안 나온다. 화면을 만든 뒤에야 눈으로 알게 되는 종류라 여기 못 박아둔다.
    ///
    /// 32가 아니라 96을 쓰는 이유: 프로젝트 안에서 32는 인벤토리 화면만 쓰고 나머지 UI는
    /// 전부 96을 쓴다. 같은 글꼴이라도 아틀라스 크기가 다르면 같은 화면에서 굵기와
    /// 가장자리가 미묘하게 달라 보인다. 다수가 쓰는 쪽에 맞춘다.
    /// </summary>
    private const string FontPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";

    // 화면에 띄울 패널 크기(픽셀). 원본이 1254x1254 정사각형이라 비율을 지켰다.
    private const float PanelSize = 900f;

    /// <summary>
    /// 팔각형 칸 네 개의 중심. 0~1 비율이고 <b>왼쪽 위가 (0,0)</b>이다.
    ///
    /// 인벤토리 빌더는 이 값을 눈으로 쟀지만 여기는 그림에서 직접 측정했다.
    /// 이 패널은 팔각형 안쪽이 순수한 검정이고 바깥 배경은 투명이라, 색으로 구분이 된다.
    /// 네 덩어리 모두 69,000px 안팎으로 크기가 같게 나와서 측정이 맞다는 것도 확인됐다.
    /// </summary>
    private static readonly Vector2[] SlotCenters =
    {
        new Vector2(0.3100f, 0.3927f),  // 좌상
        new Vector2(0.6864f, 0.3927f),  // 우상
        new Vector2(0.3098f, 0.7018f),  // 좌하
        new Vector2(0.6862f, 0.7030f),  // 우하
    };

    // 팔각형 안쪽 지름(패널 폭 대비). 측정값이 0.240이라 칸 그림이 테두리를 덮지 않게 살짝 줄였다.
    private const float SlotSize = 0.215f;

    [MenuItem("Tools/재의 길/보스 열쇠 화면 생성")]
    public static void Build()
    {
        var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotPath);

        if (panelSprite == null || slotSprite == null)
        {
            Debug.LogError("[보스 열쇠] 패널/칸 그림을 못 찾았다.\n" +
                           $"필요한 파일: {PanelPath}, {SlotPath}");
            return;
        }

        Canvas canvas = FindOrCreateCanvas();

        // 이전 실행 결과를 지운다. 남겨두면 칸이 두 겹으로 쌓인다.
        var old = canvas.transform.Find("BossKeyScreen");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // 항상 켜져 있는 껍데기. 키 입력을 듣는 컴포넌트가 여기 붙는다.
        var screenObject = NewRect("BossKeyScreen", canvas.transform);
        Stretch(screenObject);
        var screen = screenObject.gameObject.AddComponent<BossKeyScreen>();

        // 실제로 켜고 끄는 부분. 껍데기와 나누는 것이 핵심이다 —
        // BossKeyScreen을 이 오브젝트에 붙이면 꺼진 순간 Update가 안 돌아서
        // 키를 눌러도 스스로를 다시 켤 수가 없다.
        var rootObject = NewRect("Root", screenObject.transform);
        Stretch(rootObject);

        var dim = NewRect("Dim", rootObject.transform);
        Stretch(dim);
        var dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.72f);

        var panel = NewRect("Panel", rootObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelSize, PanelSize);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = panelSprite;
        panelImage.preserveAspect = true;

        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogWarning($"[보스 열쇠] 폰트를 못 찾았다: {FontPath} — 기본 폰트로 만들면 한글이 깨진다.");
        }

        var slots = BuildSlots(panel, slotSprite);
        var (nameLabel, descriptionLabel, countLabel) = BuildLabels(panel, font);

        var serialized = new SerializedObject(screen);
        serialized.FindProperty("root").objectReferenceValue = rootObject.gameObject;
        serialized.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        serialized.FindProperty("descriptionLabel").objectReferenceValue = descriptionLabel;
        serialized.FindProperty("countLabel").objectReferenceValue = countLabel;

        var slotsProperty = serialized.FindProperty("bossSlots");
        slotsProperty.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();

        EnsureEventSystem();

        // 화면은 꺼진 채로 시작한다. 끄는 것은 Root뿐이고 껍데기는 켜둬야 키 입력을 듣는다.
        rootObject.gameObject.SetActive(false);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[보스 열쇠] 화면을 만들었다. T로 열고 닫는다.\n씬을 저장해라(Ctrl+S).");
    }

    /// <summary>팔각형 자리에 칸을 하나씩 얹는다.</summary>
    private static RelicSlotView[] BuildSlots(RectTransform panel, Sprite slotSprite)
    {
        var slots = new RelicSlotView[SlotCenters.Length];
        float size = PanelSize * SlotSize;

        for (int i = 0; i < SlotCenters.Length; i++)
        {
            RelicSlotView view = CreateSlot($"BossSlot{i}", panel, slotSprite, size);
            var rect = (RectTransform)view.transform;

            // 앵커를 비율로 잡으면 패널 크기를 바꿔도 자리가 따라간다.
            // 위아래가 뒤집힌 것에 주의 — UI의 y는 아래가 0인데 위 상수는 위가 0 기준이다.
            var anchor = new Vector2(SlotCenters[i].x, 1f - SlotCenters[i].y);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);

            slots[i] = view;
        }

        return slots;
    }

    /// <summary>이름·설명·개수 라벨. 패널 아래쪽에 세로로 쌓는다.</summary>
    private static (TMPro.TMP_Text, TMPro.TMP_Text, TMPro.TMP_Text) BuildLabels(
        RectTransform panel, TMPro.TMP_FontAsset font)
    {
        TMPro.TMP_Text name = CreateLabel("NameLabel", panel, font, 1.06f, 40f, TMPro.FontStyles.Bold);
        TMPro.TMP_Text description = CreateLabel("DescriptionLabel", panel, font, 1.14f, 28f, TMPro.FontStyles.Normal);
        TMPro.TMP_Text count = CreateLabel("CountLabel", panel, font, -0.06f, 34f, TMPro.FontStyles.Bold);
        return (name, description, count);
    }

    /// <summary>
    /// 라벨 하나. y는 패널 위쪽을 0으로 본 비율이라 1보다 크면 패널 아래, 음수면 패널 위다.
    /// </summary>
    private static TMPro.TMP_Text CreateLabel(string label, RectTransform panel,
                                              TMPro.TMP_FontAsset font, float y,
                                              float fontSize, TMPro.FontStyles style)
    {
        var rect = NewRect(label, panel);
        rect.anchorMin = new Vector2(0f, 1f - y);
        rect.anchorMax = new Vector2(1f, 1f - y);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, fontSize * 1.6f);

        var text = rect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = fontSize;

        // 설명이 길면 한 줄에 안 들어간다. 줄바꿈을 허용하고 넘치면 줄여서라도 보이게 한다.
        // 잘려서 안 보이는 것보다 작게라도 읽히는 편이 낫다.
        text.textWrappingMode = TMPro.TextWrappingModes.Normal;
        text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        text.fontStyle = style;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = new Color(0.90f, 0.87f, 0.82f);
        text.raycastTarget = false;
        text.text = string.Empty;
        return text;
    }

    private static RelicSlotView CreateSlot(string label, Transform parent, Sprite slotSprite, float size)
    {
        var rect = NewRect(label, parent);
        rect.sizeDelta = new Vector2(size, size);

        var frame = rect.gameObject.AddComponent<Image>();
        frame.sprite = slotSprite;
        frame.preserveAspect = true;

        // 패널 그림에 이미 팔각형 테두리가 그려져 있다. 칸 그림까지 진하게 얹으면 두 겹으로 보인다.
        // 그래도 완전히 지우지 않는 이유는 마우스를 받을 면적이 필요해서다.
        frame.color = new Color(1f, 1f, 1f, 0.12f);
        frame.raycastTarget = true;

        var icon = NewRect("Icon", rect);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = Vector2.zero;
        icon.sizeDelta = new Vector2(size * 0.74f, size * 0.74f);

        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.preserveAspect = true;

        // 아이콘이 클릭을 가로채면 칸 위에 올려도 설명이 안 뜬다.
        iconImage.raycastTarget = false;
        iconImage.enabled = false;

        var view = rect.gameObject.AddComponent<RelicSlotView>();
        view.SetImages(frame, iconImage);
        return view;
    }

    private static Canvas FindOrCreateCanvas()
    {
        foreach (var existing in Object.FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            if (existing.GetComponent<GraphicRaycaster>() == null)
            {
                existing.gameObject.AddComponent<GraphicRaycaster>();
                Debug.Log("[보스 열쇠] 캔버스에 GraphicRaycaster가 없어서 붙였다.");
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

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include) != null) return;

        new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        Debug.Log("[보스 열쇠] EventSystem이 없어서 만들었다.");
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
