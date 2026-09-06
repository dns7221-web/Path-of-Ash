using System.Collections.Generic;
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

    // 수정(한글 깨짐) — Regular32 → Regular96.
    //
    // <b>두 에셋은 이름만 비슷하고 담긴 글자가 완전히 다르다.</b> 문자표를 까보면
    // - Regular32: <b>딱 5자</b> — 공백, '_', '길', '의', '재'. 아틀라스 512x512.
    //   타이틀 화면의 "재의 길" 넉 자를 96pt로 크게 구우려고 만든 <b>타이틀 전용</b>이다.
    // - Regular96: 11267자(완성형 한글 11172자 전부). 아틀라스 4096x4096. 본문용이다.
    //
    // 파일 이름의 숫자는 <b>구운 크기</b>지 담긴 글자 수가 아니다. 그래서 이름만 보면
    // 32가 작은 폰트로 읽히는데 실제로는 96pt로 구운 다섯 글자짜리다. 여기서 그걸
    // 물리고 있어서 유물 이름과 설명의 한글이 통째로 두부(□)로 나왔다.
    //
    // 늦게 잡힌 이유: 폰트에 없는 글자는 <b>에러도 경고도 안 낸다.</b> TMP는 조용히
    // 빈칸이나 두부를 그린다. HUD·보스 열쇠·설정·튜토리얼 빌더는 전부 96을 쓰고 있어서
    // 다른 화면은 멀쩡하고 이 화면만 깨졌다 — 그래서 폰트 문제로 안 보였다.
    //
    // 상수로 올린 이유도 그것이다. 경로가 함수 안에 박혀 있으면 다른 빌더와 나란히
    // 놓고 비교할 수가 없다. AshBossKeyUiBuilder·AshGameHudBuilder와 같은 모양으로 맞춘다.
    //
    // 덤으로 TMP Settings의 Fallback Font Assets에 Regular96을 등록해뒀다. 앞으로 어느
    // 화면이 폰트를 잘못 물려도 한글은 폴백으로 그려진다 — 같은 사고의 안전망이다.
    private const string FontPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";

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

        // 캔버스를 먼저 정한다. 아래에서 지우기 전에 정해야 "지금 화면이 올라가 있는
        // 캔버스를 그대로 쓴다"는 판단이 가능하다.
        Canvas canvas = FindOrCreateCanvas();

        // 수정(화면 중복 생성) — 이전 실행 결과를 <b>씬 전체에서</b> 지운다.
        //
        // 예전에는 정해진 캔버스 밑만 훑었다(<c>canvas.transform.Find("InventoryScreen")</c>).
        // 그런데 <see cref="FindOrCreateCanvas"/>가 캔버스를 <b>무순서로</b> 집었기 때문에,
        // 이번에 집힌 캔버스가 지난번과 다르면 지난번 것이 그대로 남고 하나가 더 생긴다.
        // 실제로 그렇게 됐다 — GameHUD와 SettingsCanvas 밑에 하나씩, 둘 다 살아 있었다.
        //
        // <b>화면이 둘이면 게임이 멈춘 채로 복구가 안 된다.</b> 둘 다 I 키를 듣고 각자
        // PauseGate에 들어가는데, 보스 열쇠 화면은 <c>FindFirstObjectByType</c>으로 <b>하나만</b>
        // 찾아 닫는다. 남은 하나가 스택에서 안 빠져서 timeScale이 0에 고정되고, 그 뒤로는
        // I를 눌러도 두 화면이 번갈아 켜지기만 해서 스택이 절대 안 빈다.
        //
        // 캔버스를 기준으로 삼지 않고 <b>컴포넌트 타입으로</b> 찾으면 어느 캔버스 밑에 있든
        // 걸린다. 이름(<c>transform.Find</c>)이 아닌 것도 같은 이유다 — 오브젝트 이름을
        // 바꿔도 살아남는다.
        foreach (var stale in Object.FindObjectsByType<InventoryScreen>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(stale.gameObject);
        }

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
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);

        if (font == null)
            Debug.LogWarning($"[인벤토리] 한글 폰트를 못 찾아 TMP 기본 폰트로 만든다. " +
                             $"기본 폰트에는 한글이 없어서 이름과 설명이 두부(□)로 나온다: {FontPath}");

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

    /// <summary>
    /// HUD 캔버스를 찾아 쓴다. 없으면 만든다.
    ///
    /// <b>수정(화면 중복 생성) — 고르는 순서를 못 박았다.</b>
    ///
    /// 예전에는 <c>FindObjectsSortMode.None</c>으로 받은 목록의 첫 번째를 그냥 썼다.
    /// 이름 그대로 <b>순서를 보장하지 않는</b> 모드라, 같은 씬에서 두 번 실행해도 다른
    /// 캔버스가 집힐 수 있다. 그것이 화면이 두 벌 생긴 원인이었다.
    ///
    /// 이제 두 단계로 정한다.
    /// <list type="number">
    /// <item>이미 인벤토리 화면이 올라가 있는 캔버스가 있으면 <b>거기를 그대로 쓴다.</b>
    /// 다시 만들어도 화면이 캔버스 사이를 옮겨 다니지 않는다. 옮겨 다니면 그리는 순서가
    /// 바뀌어서 어떤 실행에서는 HUD 뒤에 가려진다.</item>
    /// <item>없으면 <b>이름순</b>으로 첫 번째를 쓴다. 무엇이 뽑히든 상관없지만
    /// <b>매번 같은 것이 뽑히는 것</b>이 중요하다.</item>
    /// </list>
    /// </summary>
    private static Canvas FindOrCreateCanvas()
    {
        // 1단계 — 이미 이 화면이 올라가 있는 캔버스를 그대로 쓴다.
        // 꺼진 채로 둘 수 있는 화면이라 비활성까지 포함해서 찾는다.
        var placed = Object.FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);

        if (placed != null)
        {
            // 인자 true는 "꺼진 부모도 본다"는 뜻이다. 없으면 캔버스가 꺼져 있을 때 못 찾는다.
            var owner = placed.GetComponentInParent<Canvas>(true);

            if (owner != null && owner.renderMode == RenderMode.ScreenSpaceOverlay)
                return EnsureRaycaster(owner);
        }

        // 2단계 — 후보를 모아 이름순으로 정렬한다. 정렬이 곧 "매번 같은 결과"의 보장이다.
        var candidates = new List<Canvas>();

        foreach (var existing in Object.FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            candidates.Add(existing);
        }

        if (candidates.Count > 0)
        {
            // 문화권에 따라 결과가 달라지지 않도록 Ordinal로 비교한다.
            candidates.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            return EnsureRaycaster(candidates[0]);
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
    /// 추가 생성 — 캔버스에 <see cref="GraphicRaycaster"/>가 없으면 붙이고 그대로 돌려준다.
    ///
    /// 이게 없으면 칸을 눌러도 아무 반응이 없다. HUD만 있던 캔버스에는 없을 수 있다.
    /// 위에서 캔버스를 고르는 길이 둘로 갈라지면서 같은 검사를 두 번 쓰게 되어 함수로 뺐다.
    /// </summary>
    private static Canvas EnsureRaycaster(Canvas canvas)
    {
        if (canvas.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            Debug.Log("[인벤토리] 캔버스에 GraphicRaycaster가 없어서 붙였다.");
        }

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
