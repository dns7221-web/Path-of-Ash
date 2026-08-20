using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Game 씬의 HUD(지금은 스태미나 게이지 하나)를 조립하는 에디터 도구.
///
/// 메뉴: Tools → 재의 길 → 게임 HUD 생성
///
/// UI를 손으로 만들지 않고 스크립트로 둔 이유는 프리팹 빌더와 같다. RectTransform은 앵커,
/// 피벗, 오프셋, sizeDelta가 서로 얽혀 있어서 창에서 끌어 맞추면 "내 화면에서는 맞는데
/// 해상도가 바뀌면 어긋나는" 결과가 나오기 쉽다. 앵커 값을 코드로 못 박으면 그 문제가 없고,
/// 왜 그 앵커인지 주석으로 남는다.
///
/// 특히 게이지 프레임처럼 <b>그림 안쪽의 특정 영역에만 채움이 들어가야 하는 경우</b>, 그
/// 영역을 눈으로 맞추면 해상도가 바뀔 때마다 어긋난다. 여기서는 원본 텍스처를 실측한 픽셀
/// 좌표를 비율로 바꿔 앵커에 넣으므로, 게이지를 어떤 크기로 늘려도 채움이 프레임 안에 남는다.
///
/// <b>Game 씬이 열려 있어야 실행된다.</b> 다른 씬에서 실행하면 그 씬에 HUD를 만들어버리므로
/// 먼저 막는다.
/// </summary>
public static class AshGameHudBuilder
{
    private const string TargetSceneName = "Game";
    private const string HudRootName = "GameHUD";
    private const string BarName = "StaminaBar";

    // ── 프레임 스프라이트 ──────────────────────────────────────────────────

    private const string FramePath = "Assets/Project/Art/UI/StaminaGaugeFrame.png";
    private const string FillPath = "Assets/Project/Art/UI/StaminaGaugeFill.png";

    /// <summary>프레임 원본 텍스처 크기(px). 아래 안쪽 여백 값들이 이 크기 기준이다.</summary>
    private const float FrameTextureWidth = 1024f;
    private const float FrameTextureHeight = 116f;

    // 프레임 안쪽에서 실제로 채워도 되는 영역의 여백(px, 원본 텍스처 기준).
    //
    // 눈대중이 아니라 텍스처의 알파와 밝기를 픽셀 단위로 훑어서 구한 값이다.
    // 이 프레임은 안쪽이 단순한 직사각형이 아니다 — 양 끝의 화살촉 장식이 중앙 높이로
    // 파고들어 있어서, 세로 가운데(y=58)에서 어두운 내부가 x 110~913으로 가장 좁아진다.
    // 위아래(y=36, y=78)에서는 x 82~942까지 넓어지지만, 그 폭에 맞춰 채우면 세로 가운데에서
    // 채움이 장식 위로 삐져나온다. 그래서 "모든 행에 공통으로 안전한 가장 큰 사각형"을 쓴다.
    private const float InteriorLeft = 110f;   // 왼쪽 끝에서 안쪽까지
    private const float InteriorRight = 110f;  // 오른쪽 끝에서 안쪽까지 (1024 - 914)
    private const float InteriorTop = 36f;     // 위쪽 끝에서 안쪽까지
    private const float InteriorBottom = 37f;  // 아래쪽 끝에서 안쪽까지 (116 - 79)

    // ── 배치 규격 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 캔버스 기준 해상도. Title 씬의 캔버스가 1920x1080을 쓰고 있어 맞춘다.
    /// 화면마다 다른 크기로 보이는 걸 막으려면 프로젝트 안에서 이 값이 같아야 한다.
    /// </summary>
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    /// <summary>
    /// 게이지 크기(기준 해상도 기준 픽셀).
    ///
    /// 원본 1024x116의 정확히 절반이다. 가로세로 비율을 원본과 같게 유지해야 프레임의
    /// 화살촉 장식이 찌그러지지 않는다. 크기를 바꾸려면 8.83:1 비율을 지켜라
    /// (예: 440x50, 620x70).
    /// </summary>
    private static readonly Vector2 BarSize = new Vector2(512f, 58f);

    /// <summary>화면 왼쪽 아래 모서리로부터의 여백.</summary>
    private static readonly Vector2 BarMargin = new Vector2(48f, 48f);

    /// <summary>프레임 스프라이트를 못 찾았을 때 대신 깔 배경색.</summary>
    private static readonly Color FallbackBackgroundColor = new Color(0.08f, 0.06f, 0.06f, 0.85f);

    // ── 체력 게이지 ────────────────────────────────────────────────────────
    //
    // 스태미나 게이지와 구조가 하나 다르다. 스태미나 프레임은 안쪽이 불투명한 어두운 색이라
    // 채움을 프레임 <b>위에</b> 그려야 보였다. 체력 프레임은 속이 빈 액자형(안쪽이 투명)이라
    // 채움을 프레임 <b>뒤에</b> 깔아야 한다. 그래야 왼쪽 하트 장식 옆의 사선 부분에서 채움이
    // 조금 삐져나와도 프레임 그림이 덮어 가린다.

    private const string HealthBarName = "HealthBar";
    private const string HealthFramePath = "Assets/Project/Art/UI/HealthGaugeFrame.png";
    private const string HealthFillPath = "Assets/Project/Art/UI/HealthGaugeFill.png";

    private const float HealthFrameWidth = 1838f;
    private const float HealthFrameHeight = 189f;

    // 안쪽 빈 공간의 여백(px). 알파를 열/행 단위로 훑어서 구한 값이다.
    // 대부분의 열에서 투명 구간이 y 52~136으로 안정적이고, 가운데 행(y=94)의 투명 구간이
    // x 177~1757이다. 양 끝은 하트 다이아(x<230)와 화살촉(x>1740)이 파고들어 좁아지는데,
    // 그 부분은 프레임이 위에 덮이므로 여유 있게 잡아도 된다.
    private const float HealthInteriorLeft = 185f;
    private const float HealthInteriorRight = 87f;
    private const float HealthInteriorTop = 52f;
    private const float HealthInteriorBottom = 51f;

    /// <summary>체력 게이지 크기. 원본 비율 1838:189 = 9.72:1을 지킨다.</summary>
    private static readonly Vector2 HealthBarSize = new Vector2(583f, 60f);

    /// <summary>스태미나 바 바로 위에 놓는다.</summary>
    private static readonly Vector2 HealthBarMargin = new Vector2(48f, 118f);

    [MenuItem("Tools/재의 길/게임 HUD 생성")]
    public static void BuildHud()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != TargetSceneName)
        {
            Debug.LogError(
                $"[게임 HUD] 활성 씬이 '{scene.name}'이다. " +
                $"{TargetSceneName} 씬을 열고 다시 실행해라.");
            return;
        }

        // 이미 만들어져 있으면 지우고 다시 만든다. 값을 하나씩 덮어쓰는 것보다 이쪽이
        // 여러 번 실행해도 결과가 같다(멱등).
        var existing = GameObject.Find(HudRootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
            Debug.Log($"[게임 HUD] 기존 {HudRootName}을 지우고 다시 만든다.");
        }

        var canvasObject = CreateCanvas();
        var bar = CreateBar(canvasObject.transform);
        CreateHealthBar(canvasObject.transform);
        CreateSkillBar(canvasObject.transform);
        CreateAshGauge(canvasObject.transform);
        CreateRelicToast(canvasObject.transform);

        // 씬 변경을 유니티에 알린다. 이걸 안 하면 저장 없이 씬을 닫았을 때 조용히 사라진다.
        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeGameObject = bar;

        Debug.Log("[게임 HUD] 스태미나 게이지 생성 완료. 씬을 저장해라(Ctrl+S).");
    }

    /// <summary>화면 위에 겹쳐 그리는 캔버스를 만든다.</summary>
    private static GameObject CreateCanvas()
    {
        var canvasObject = new GameObject(HudRootName, typeof(Canvas), typeof(CanvasScaler));

        // UI 레이어는 유니티 예약 레이어(5번)라 AshProjectSetup이 만드는 사용자 레이어와 별개다.
        canvasObject.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasObject.GetComponent<Canvas>();

        // ScreenSpaceOverlay는 카메라와 무관하게 화면 맨 위에 그린다. 카메라가 플레이어를
        // 따라다녀도 게이지는 화면에 고정돼 있어야 하므로 이게 맞다.
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasObject.GetComponent<CanvasScaler>();

        // 해상도가 달라져도 화면 대비 같은 비율로 보이게 한다. 기본값(ConstantPixelSize)으로
        // 두면 4K 화면에서 게이지가 손톱만 해진다.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;

        // 0.5는 가로와 세로를 반반씩 참고한다는 뜻이다. 0(가로만)으로 두면 21:9 같은
        // 초광각 화면에서 UI가 세로로 넘친다.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 프레임 스프라이트의 PPU(100)와 맞춘다. 다르면 "Set Native Size"가 어긋난다.
        scaler.referencePixelsPerUnit = 100f;

        // GraphicRaycaster를 안 붙인 이유: 이 HUD에는 누를 수 있는 요소가 없다.
        // 붙여두면 매 프레임 마우스 위치로 UI를 훑는 비용만 든다.

        return canvasObject;
    }

    /// <summary>게이지 바 본체를 만든다. 만들어진 루트 오브젝트를 돌려준다.</summary>
    private static GameObject CreateBar(Transform parent)
    {
        // ── 바 루트 ──
        var barObject = new GameObject(BarName, typeof(RectTransform), typeof(CanvasGroup));
        barObject.layer = parent.gameObject.layer;
        var barRect = barObject.GetComponent<RectTransform>();
        barRect.SetParent(parent, false);

        // 앵커와 피벗을 모두 왼쪽 아래(0,0)에 둔다. 그러면 anchoredPosition이 곧
        // "왼쪽 아래 모서리로부터의 거리"가 되어, 해상도가 바뀌어도 여백이 그대로 유지된다.
        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = Vector2.zero;
        barRect.pivot = Vector2.zero;
        barRect.anchoredPosition = BarMargin;
        barRect.sizeDelta = BarSize;

        // ── 프레임 ──
        // 먼저 만들어야 아래(뒤)에 깔린다. UI는 계층 순서가 곧 그리는 순서고, 나중에 만든
        // 형제가 위에 그려진다. 프레임 안쪽이 불투명한 어두운 색이라 채움이 위에 와야 보인다.
        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
        var frame = CreateStretchedImage("Frame", barRect, Color.white);

        if (frameSprite != null)
        {
            frame.sprite = frameSprite;

            // Simple은 스프라이트를 사각형에 그대로 늘려 그린다. BarSize가 원본과 같은
            // 비율이라 찌그러지지 않는다.
            //
            // 9-슬라이스(Sliced)를 안 쓴 이유: 양 끝 장식을 고정하고 가운데만 늘릴 수 있어
            // 더 유연하지만, 그러면 안쪽 채움 영역이 "비율"이 아니라 "고정 px + 나머지"가 되어
            // 아래 앵커 계산이 통째로 달라진다. 지금은 게이지 크기가 하나뿐이라 이득이 없다.
            frame.type = Image.Type.Simple;
        }
        else
        {
            // 스프라이트를 못 찾아도 게이지 자체는 동작해야 한다. 임포트 설정이 아직
            // 반영되지 않았을 때(Multiple로 남아 있으면 Sprite 에셋이 안 생긴다) 여기로 온다.
            frame.color = FallbackBackgroundColor;

            Debug.LogWarning(
                $"[게임 HUD] 프레임 스프라이트를 못 읽었다: {FramePath}\n" +
                "프로젝트 창에서 해당 PNG를 우클릭 → Reimport 한 뒤 이 메뉴를 다시 실행해라. " +
                "(AshSpriteImportRules가 Sprite Mode를 Single로 되돌린다.)",
                frame);
        }

        // ── 채움 영역 ──
        // 프레임 안쪽의 안전 사각형. 원본 픽셀 좌표를 0~1 비율로 바꿔 앵커에 넣는다.
        // 비율이라서 BarSize를 바꿔도 채움이 프레임을 따라 같이 늘어난다.
        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.layer = barObject.layer;
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.SetParent(barRect, false);

        // y는 아래가 0이라 위/아래 여백을 뒤집어 넣는다.
        fillAreaRect.anchorMin = new Vector2(
            InteriorLeft / FrameTextureWidth,
            InteriorBottom / FrameTextureHeight);
        fillAreaRect.anchorMax = new Vector2(
            1f - (InteriorRight / FrameTextureWidth),
            1f - (InteriorTop / FrameTextureHeight));
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;

        // ── 채워지는 그림 ──
        // 추가 생성 — 용암 무늬가 가로로 찌그러지지 않도록 Image.fillAmount로 잘라 보여준다.
        var fill = CreateStretchedImage("Fill", fillAreaRect, Color.white);
        var fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FillPath);
        if (fillSprite != null)
        {
            fill.sprite = fillSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
        }
        else
        {
            Debug.LogWarning($"[게임 HUD] Fill 스프라이트를 못 읽었다: {FillPath}", fill);
        }

        // ── 스크립트 연결 ──
        var bar = barObject.AddComponent<StaminaBar>();
        LinkBarReferences(bar, fill.rectTransform, fill, barObject.GetComponent<CanvasGroup>());

        return barObject;
    }

    /// <summary>
    /// 체력 게이지를 만든다.
    ///
    /// 스태미나 바와 자식 순서가 반대다. 여기서는 <b>채움을 먼저</b> 만들어 뒤에 깔고
    /// 프레임을 나중에 만들어 위에 얹는다. 프레임 안쪽이 뚫려 있어서 채움이 그 구멍으로
    /// 비쳐 보이는 구조이고, 하트 옆 사선처럼 채움이 조금 넘치는 자리는 프레임이 가려준다.
    /// </summary>
    private static GameObject CreateHealthBar(Transform parent)
    {
        var barObject = new GameObject(HealthBarName, typeof(RectTransform));
        barObject.layer = parent.gameObject.layer;
        var barRect = barObject.GetComponent<RectTransform>();
        barRect.SetParent(parent, false);

        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = Vector2.zero;
        barRect.pivot = Vector2.zero;
        barRect.anchoredPosition = HealthBarMargin;
        barRect.sizeDelta = HealthBarSize;

        // ── 채움 영역 (프레임보다 먼저 = 뒤에 깔림) ──
        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.layer = barObject.layer;
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.SetParent(barRect, false);

        // 실측 픽셀을 0~1 비율로 바꿔 앵커에 넣는다. 비율이라 BarSize를 바꿔도 따라온다.
        // y는 아래가 0이라 위/아래 여백을 뒤집어 넣는다.
        fillAreaRect.anchorMin = new Vector2(
            HealthInteriorLeft / HealthFrameWidth,
            HealthInteriorBottom / HealthFrameHeight);
        fillAreaRect.anchorMax = new Vector2(
            1f - (HealthInteriorRight / HealthFrameWidth),
            1f - (HealthInteriorTop / HealthFrameHeight));
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;

        var fill = CreateStretchedImage("Fill", fillAreaRect, Color.white);
        var fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HealthFillPath);
        if (fillSprite != null)
        {
            fill.sprite = fillSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.preserveAspect = false;
        }
        else
        {
            Debug.LogWarning($"[게임 HUD] 체력 Fill 스프라이트를 못 읽었다: {HealthFillPath}\n" +
                             "Tools → 재의 길 → 게이지 원본 이미지 다듬기 를 먼저 실행해라.", fill);
        }

        // ── 프레임 (나중에 = 위에 얹힘) ──
        var frame = CreateStretchedImage("Frame", barRect, Color.white);
        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HealthFramePath);
        if (frameSprite != null)
        {
            frame.sprite = frameSprite;
            frame.type = Image.Type.Simple;
        }
        else
        {
            // 프레임이 없으면 아무것도 안 그린다. 체력 프레임은 속이 빈 액자라 단색 사각형으로
            // 대체하면 채움을 통째로 가려버린다 — 스태미나 쪽 대체 처리와 반대다.
            frame.enabled = false;

            Debug.LogWarning($"[게임 HUD] 체력 프레임 스프라이트를 못 읽었다: {HealthFramePath}\n" +
                             "Tools → 재의 길 → 게이지 원본 이미지 다듬기 를 먼저 실행해라.", frame);
        }

        var bar = barObject.AddComponent<HealthBar>();
        var serialized = new SerializedObject(bar);
        serialized.FindProperty("fillRect").objectReferenceValue = fill.rectTransform;
        serialized.FindProperty("fillImage").objectReferenceValue = fill;
        // health는 비워둔다. 플레이어가 프리팹 인스턴스라 HealthBar.Awake가 실행 시점에 찾는다.
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return barObject;
    }

    // ── 유물 획득 알림 ─────────────────────────────────────────────────────

    private const string RelicToastName = "RelicToast";
    private static readonly Vector2 ToastSize = new Vector2(520f, 96f);

    /// <summary>
    /// 유물 획득 알림을 만든다.
    ///
    /// 화면 아래 가운데, 스킬 바 위에 둔다. 시선이 이미 그 근처에 있고(쿨타임을 보러 가는
    /// 자리) 캐릭터가 있는 화면 중앙은 가리지 않는다.
    /// </summary>
    private static GameObject CreateRelicToast(Transform parent)
    {
        var existing = GameObject.Find(RelicToastName);
        if (existing != null) Object.DestroyImmediate(existing);

        var toastObject = new GameObject(RelicToastName, typeof(RectTransform), typeof(CanvasGroup));
        toastObject.layer = parent.gameObject.layer;
        var rect = toastObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 150f); // 스킬 바(40 + 72) 바로 위
        rect.sizeDelta = ToastSize;

        CreateStretchedImage("Background", rect, new Color(0.06f, 0.05f, 0.05f, 0.85f));

        // 아이콘은 왼쪽 정사각형 자리.
        var icon = CreateStretchedImage("Icon", rect, Color.white);
        icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.sizeDelta = new Vector2(72f, 72f);
        icon.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        icon.enabled = false; // 그림이 들어오면 RelicToast가 켠다

        var nameText = CreateToastLabel(rect, "NameText", 28f, new Vector2(0f, 20f),
            TMPro.TextAlignmentOptions.Left, new Color(1f, 0.72f, 0.4f));

        var descriptionText = CreateToastLabel(rect, "DescriptionText", 18f, new Vector2(0f, -14f),
            TMPro.TextAlignmentOptions.TopLeft, new Color(0.82f, 0.8f, 0.78f));

        var toast = toastObject.AddComponent<RelicToast>();
        var serialized = new SerializedObject(toast);
        serialized.FindProperty("group").objectReferenceValue = toastObject.GetComponent<CanvasGroup>();
        serialized.FindProperty("iconImage").objectReferenceValue = icon;
        serialized.FindProperty("nameText").objectReferenceValue = nameText;
        serialized.FindProperty("descriptionText").objectReferenceValue = descriptionText;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return toastObject;
    }

    /// <summary>알림 안의 글자. 아이콘 오른쪽 영역을 쓴다.</summary>
    private static TMPro.TMP_Text CreateToastLabel(RectTransform parent, string name,
        float fontSize, Vector2 position, TMPro.TextAlignmentOptions alignment, Color color)
    {
        var labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.layer = parent.gameObject.layer;

        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(ToastSize.x - 110f, 40f);
        rect.anchoredPosition = new Vector2(96f, 0f) + position;

        var label = labelObject.AddComponent<TMPro.TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.text = string.Empty;

        return label;
    }

    // ── 재 게이지 ──────────────────────────────────────────────────────────

    private const string AshBarName = "AshGaugeBar";
    private const string AshFramePath = "Assets/Project/Art/UI/AshGaugeFrame.png";
    private const string AshFillPath = "Assets/Project/Art/UI/AshGaugeFill.png";

    private const float AshFrameWidth = 1803f;
    private const float AshFrameHeight = 253f;

    // 프레임 안쪽 빈 공간(px). 알파를 열 단위로 훑어 구한 값이다.
    // 대부분의 열에서 세로 58~196이 비어 있고, 가운데 마름모 장식이 있는 자리만 좁아진다.
    // 채움이 프레임 <b>뒤에</b> 깔리므로 그 자리는 장식이 덮어주면 된다.
    private const float AshInteriorLeft = 240f;
    private const float AshInteriorRight = 272f;
    private const float AshInteriorTop = 58f;
    private const float AshInteriorBottom = 56f;

    /// <summary>재 게이지 크기. 원본 비율 1803:253 = 7.13:1을 지킨다.</summary>
    private static readonly Vector2 AshBarSize = new Vector2(428f, 60f);

    /// <summary>스태미나 바 아래. 세 게이지가 왼쪽 아래에 세로로 쌓인다.</summary>
    private static readonly Vector2 AshBarMargin = new Vector2(48f, -18f);

    /// <summary>
    /// 재 게이지를 만든다. 체력 게이지와 같은 구조 — 속이 빈 액자라 채움이 프레임 뒤에 깔린다.
    /// </summary>
    private static GameObject CreateAshGauge(Transform parent)
    {
        var existing = GameObject.Find(AshBarName);
        if (existing != null) Object.DestroyImmediate(existing);

        var barObject = new GameObject(AshBarName, typeof(RectTransform));
        barObject.layer = parent.gameObject.layer;
        var barRect = barObject.GetComponent<RectTransform>();
        barRect.SetParent(parent, false);

        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = Vector2.zero;
        barRect.pivot = Vector2.zero;
        barRect.anchoredPosition = AshBarMargin;
        barRect.sizeDelta = AshBarSize;

        // 채움을 먼저 = 뒤에 깔린다.
        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.layer = barObject.layer;
        var fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.SetParent(barRect, false);

        fillAreaRect.anchorMin = new Vector2(
            AshInteriorLeft / AshFrameWidth, AshInteriorBottom / AshFrameHeight);
        fillAreaRect.anchorMax = new Vector2(
            1f - (AshInteriorRight / AshFrameWidth), 1f - (AshInteriorTop / AshFrameHeight));
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;

        var fill = CreateStretchedImage("Fill", fillAreaRect, Color.white);
        var fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AshFillPath);
        if (fillSprite != null)
        {
            fill.sprite = fillSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            fill.preserveAspect = false;
        }
        else
        {
            Debug.LogWarning($"[게임 HUD] 재 게이지 채움을 못 읽었다: {AshFillPath}\n" +
                             "Tools → 재의 길 → 게이지 원본 이미지 다듬기 를 먼저 실행해라.");
        }

        // 프레임을 나중에 = 위에 얹힌다.
        var frame = CreateStretchedImage("Frame", barRect, Color.white);
        var frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AshFramePath);
        if (frameSprite != null)
        {
            frame.sprite = frameSprite;
            frame.type = Image.Type.Simple;
        }
        else
        {
            frame.enabled = false;
            Debug.LogWarning($"[게임 HUD] 재 게이지 프레임을 못 읽었다: {AshFramePath}");
        }

        var bar = barObject.AddComponent<AshGaugeBar>();
        var serialized = new SerializedObject(bar);
        serialized.FindProperty("fillRect").objectReferenceValue = fill.rectTransform;
        serialized.FindProperty("fillImage").objectReferenceValue = fill;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return barObject;
    }

    // ── 스킬 바 ────────────────────────────────────────────────────────────

    private const string SkillBarName = "SkillBar";
    private static readonly string[] SkillKeyLabels = { "Ctrl", "Q", "W", "E", "R" };

    private const float SlotSize = 72f;
    private const float SlotGap = 10f;

    /// <summary>
    /// 화면 아래 가운데에 스킬 슬롯 5개를 만든다.
    ///
    /// 게이지(왼쪽 아래)와 떨어뜨려 가운데에 둔 이유: 체력·스태미나는 "지금 상태"라 곁눈으로
    /// 보는 정보지만, 스킬 쿨타임은 "다음에 뭘 쓸까"를 정할 때 <b>보러 가는</b> 정보다.
    /// 시선이 가는 자리가 달라서 붙여두면 서로 방해한다.
    /// </summary>
    private static GameObject CreateSkillBar(Transform parent)
    {
        var existing = GameObject.Find(SkillBarName);
        if (existing != null) Object.DestroyImmediate(existing);

        var barObject = new GameObject(SkillBarName, typeof(RectTransform));
        barObject.layer = parent.gameObject.layer;
        var barRect = barObject.GetComponent<RectTransform>();
        barRect.SetParent(parent, false);

        // 화면 아래 가운데 기준. 해상도가 바뀌어도 가운데에 남는다.
        barRect.anchorMin = new Vector2(0.5f, 0f);
        barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.anchoredPosition = new Vector2(0f, 40f);

        int count = SkillKeyLabels.Length;
        float totalWidth = count * SlotSize + (count - 1) * SlotGap;
        barRect.sizeDelta = new Vector2(totalWidth, SlotSize);

        var icons = new Image[count];
        var overlays = new Image[count];
        var labels = new TMPro.TMP_Text[count];

        // 플레이어 프리팹의 스킬 슬롯을 읽어 아이콘을 <b>만들 때 바로</b> 넣는다.
        //
        // 처음에는 SkillBar가 실행 시점에 넣게 했는데, 그러면 에디터에서 슬롯이 빈 사각형으로
        // 보여서 "아이콘이 안 들어갔다"고 오해하게 된다. 화면에 나올 그림은 만들 때 보이는
        // 편이 낫다 — 배치를 눈으로 맞출 수 있고, 잘못 끼운 것도 바로 드러난다.
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Project/Prefabs/Player/Player.prefab");
        var prefabSkills = playerPrefab != null ? playerPrefab.GetComponent<SkillController>() : null;

        if (prefabSkills == null)
            Debug.LogWarning("[게임 HUD] 플레이어 프리팹의 SkillController를 못 찾았다. " +
                             "아이콘은 실행할 때 채워진다.");

        for (int i = 0; i < count; i++)
        {
            float x = -totalWidth * 0.5f + SlotSize * 0.5f + i * (SlotSize + SlotGap);
            CreateSkillSlot(barRect, i, x, out icons[i], out overlays[i], out labels[i]);

            SkillData skill = prefabSkills != null ? prefabSkills.GetSlot(i) : null;
            if (skill == null || skill.Icon == null) continue;

            icons[i].sprite = skill.Icon;
            icons[i].enabled = true;
        }

        var skillBar = barObject.AddComponent<SkillBar>();
        var serialized = new SerializedObject(skillBar);
        AssignArray(serialized.FindProperty("icons"), icons);
        AssignArray(serialized.FindProperty("cooldownOverlays"), overlays);
        AssignArray(serialized.FindProperty("cooldownLabels"), labels);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return barObject;
    }

    private static void AssignArray(SerializedProperty property, Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>슬롯 하나. 배경 → 아이콘 → 쿨타임 덮개 → 숫자 → 키 글자 순으로 쌓는다.</summary>
    private static void CreateSkillSlot(RectTransform parent, int index, float x,
        out Image icon, out Image overlay, out TMPro.TMP_Text cooldownLabel)
    {
        var slot = new GameObject($"Slot_{SkillKeyLabels[index]}", typeof(RectTransform));
        slot.layer = parent.gameObject.layer;
        var rect = slot.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(SlotSize, SlotSize);
        rect.anchoredPosition = new Vector2(x, 0f);

        CreateStretchedImage("Background", rect, new Color(0.07f, 0.06f, 0.06f, 0.8f));

        icon = CreateStretchedImage("Icon", rect, Color.white);
        icon.rectTransform.offsetMin = new Vector2(6f, 6f);
        icon.rectTransform.offsetMax = new Vector2(-6f, -6f);
        icon.enabled = false; // 아이콘 그림이 들어오면 SkillBar가 켠다

        // 쿨타임 덮개. 시계 방향으로 걷힌다.
        overlay = CreateStretchedImage("Cooldown", rect, new Color(0f, 0f, 0f, 0.68f));
        overlay.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        overlay.type = Image.Type.Filled;
        overlay.fillMethod = Image.FillMethod.Radial360;
        overlay.fillOrigin = (int)Image.Origin360.Top;

        // 시계 방향으로 걷히게 한다. 시계가 도는 방향과 같아야 "시간이 간다"로 읽힌다.
        overlay.fillClockwise = true;
        overlay.fillAmount = 0f;

        cooldownLabel = CreateLabel(rect, "CooldownText", string.Empty, 26f,
            new Vector2(0f, 0f), TMPro.TextAlignmentOptions.Center);

        // 키 글자. 슬롯 아래에 붙인다 — 아이콘을 가리면 안 된다.
        CreateLabel(rect, "KeyText", SkillKeyLabels[index], 18f,
            new Vector2(0f, -SlotSize * 0.5f - 12f), TMPro.TextAlignmentOptions.Center);
    }

    private static TMPro.TMP_Text CreateLabel(RectTransform parent, string name, string text,
        float fontSize, Vector2 position, TMPro.TextAlignmentOptions alignment)
    {
        var labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.layer = parent.gameObject.layer;

        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(SlotSize + 20f, 30f);
        rect.anchoredPosition = position;

        var label = labelObject.AddComponent<TMPro.TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;

        return label;
    }

    /// <summary>부모에 꽉 차는 단색 이미지를 만든다.</summary>
    private static Image CreateStretchedImage(string name, Transform parent, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.layer = parent.gameObject.layer;

        var rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        // (0,0)~(1,1) 앵커는 "부모 크기에 맞춰 늘어난다"는 뜻이다. 오프셋을 0으로 두면
        // 부모와 정확히 같은 사각형이 된다.
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = imageObject.GetComponent<Image>();

        // 스프라이트를 비워두면 단색 사각형이 그려진다. 채움 막대는 이게 맞다 —
        // 유니티 기본 UI 스프라이트는 모서리가 둥글어서 프레임 안쪽에 안 맞는다.
        image.sprite = null;
        image.color = color;

        // 이 HUD는 클릭 대상이 아니다. 꺼두면 UI 레이캐스트 대상에서 빠진다.
        image.raycastTarget = false;

        return image;
    }

    /// <summary>
    /// StaminaBar의 [SerializeField] 참조를 연결한다.
    /// private 필드라 직접 대입할 수 없어 SerializedObject를 쓴다 — 프리팹 빌더와 같은 이유다.
    /// </summary>
    private static void LinkBarReferences(
        StaminaBar bar, RectTransform fillRect, Image fillImage, CanvasGroup canvasGroup)
    {
        var serialized = new SerializedObject(bar);

        serialized.FindProperty("fillRect").objectReferenceValue = fillRect;
        serialized.FindProperty("fillImage").objectReferenceValue = fillImage;
        serialized.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
        serialized.FindProperty("normalColor").colorValue = Color.white;

        // stamina는 비워둔다. 플레이어가 프리팹 인스턴스라 씬 오브젝트인 이 HUD가 미리
        // 가리킬 수 없고, StaminaBar.Awake가 실행 시점에 찾는다.

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
