using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-10-03) — 스킵 확인 창이 연출에게 묻는 두 가지. 1페이즈 등장과 2페이즈 전환이 함께 쓴다.
///
/// 인터페이스로 뺀 이유: 확인 창이 <see cref="BossIntroSequence"/>만 알면 2페이즈 전환에는 같은 창을
/// 못 쓰고, 창을 하나 더 복사하게 된다. 창이 "건너뛸 수 있나?"와 "건너뛰어라" 두 마디만 알면
/// 어떤 연출이든 같은 창 하나로 처리된다.
/// </summary>
public interface ISkippableCutscene
{
    /// <summary>지금 건너뛸 수 있는가(재생 중인가).</summary>
    bool CanSkip { get; }

    /// <summary>연출을 끝으로 보내고 결과를 확정한다.</summary>
    void Skip();
}

/// <summary>
/// 추가 생성(2026-10-02) — 등장 중 ESC를 누르면 스킵 의사를 확인한다.
/// 설정 창과 같은 색·한글 글꼴을 쓰고 PauseGate에 올라가므로, 취소하면 멈춘 지점부터 연출이 이어진다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossIntroSkipDialog : MonoBehaviour
{
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color PanelColor = new Color(0.10f, 0.09f, 0.09f, 0.98f);
    private static readonly Color BorderColor = new Color(0.55f, 0.26f, 0.10f, 1f);
    private static readonly Color TextColor = new Color(0.94f, 0.89f, 0.81f, 1f);

    private static BossIntroSkipDialog current;
    private static int inputConsumedFrame = -1;
    // 수정(2026-10-03) — BossIntroSequence → MonoBehaviour. 2페이즈 전환도 같은 창을 쓴다.
    // ISkippableCutscene이 아니라 MonoBehaviour로 들고 있는 이유: 파괴된 연출을 유니티의 null 검사(==)로 걸러야 한다.
    private MonoBehaviour owner;
    private EventSystem eventSystem;
    private GameObject previousSelection;
    private int openedFrame;
    private bool closed;

    /// <summary>추가 생성(2026-10-02) — 모달 아래에서 인벤토리 등 다른 창이 열리지 않도록 공개한다.</summary>
    public static bool IsOpen => current != null && !current.closed;

    /// <summary>추가 생성(2026-10-02) — 닫는 데 쓴 ESC가 같은 프레임에 확인 창을 다시 여는 일을 막는다.</summary>
    public static bool InputConsumedThisFrame => inputConsumedFrame == Time.frameCount;

    /// <summary>추가 생성(2026-10-02) — 도메인 리로드가 꺼져 있어도 지난 플레이의 모달 참조가 남지 않게 한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        inputConsumedFrame = -1;
    }

    /// <summary>
    /// 추가 생성(2026-10-02) — 연출 소유자에게 묶인 확인 창을 하나만 만든다.
    /// 씬 재구성 없이도 기존 Game HUD에서 바로 작동하도록 캔버스와 버튼은 실행 중 생성한다.
    /// </summary>
    public static BossIntroSkipDialog Show(BossIntroSequence sequence)
        => Show(sequence, "인트로를 스킵하시겠습니까?");

    /// <summary>
    /// 추가 생성(2026-10-03) — 연출 종류와 상관없이 확인 창을 연다. 문구만 연출마다 다르다.
    /// 제네릭 제약(MonoBehaviour + ISkippableCutscene)으로 "씬에 있는 컴포넌트이면서 건너뛸 수 있는 것"만 받는다.
    /// </summary>
    public static BossIntroSkipDialog Show<T>(T sequence, string message) where T : MonoBehaviour, ISkippableCutscene
    {
        if (IsOpen) return current;
        if (sequence == null || !sequence.CanSkip || PauseGate.IsPaused || InputConsumedThisFrame) return null;

        var host = new GameObject("BossIntroSkipDialog", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        // 화면 좌표 캔버스는 최상위에 둬 보스의 위치·배율·Timeline Transform에 영향을 받지 않게 한다.
        // 수명은 Sequence의 RestorePresentation이 Close로 관리하고, 씬 교체도 함께 정리되도록 같은 씬에 둔다.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, sequence.gameObject.scene);

        Canvas canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dialog = host.AddComponent<BossIntroSkipDialog>();
        dialog.owner = sequence;
        dialog.openedFrame = Time.frameCount;
        current = dialog;
        inputConsumedFrame = Time.frameCount;
        // 수정(2026-10-03) — 문구를 받아서 쓴다.
        dialog.BuildContents(FindFont(sequence), message);

        // 입력을 막는 등록을 마친 다음 화면을 내보내야 연출이 확인 창 뒤에서 한 프레임 더 진행하지 않는다.
        PauseGate.Open(dialog);
        return dialog;
    }

    /// <summary>추가 생성(2026-10-02) — 현재 HUD/설정의 한글 TMP 글꼴을 재사용해 별도 Resources 경로를 요구하지 않는다.</summary>
    // 수정(2026-10-03) — 매개변수 BossIntroSequence → Component. 자식의 글꼴만 보면 되므로 연출 종류를 몰라도 된다.
    private static TMP_FontAsset FindFont(Component sequence)
    {
        SettingsScreen settings = FindFirstObjectByType<SettingsScreen>(FindObjectsInactive.Include);
        if (settings != null)
        {
            foreach (TMP_Text label in settings.GetComponentsInChildren<TMP_Text>(true))
                if (label.font != null) return label.font;
        }

        foreach (TMP_Text label in sequence.GetComponentsInChildren<TMP_Text>(true))
            if (label.font != null) return label.font;

        foreach (TMP_Text label in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (label.font != null) return label.font;

        return TMP_Settings.defaultFontAsset;
    }

    /// <summary>추가 생성(2026-10-02) — 뒤쪽 클릭을 막는 막과 가운데 확인·취소 버튼을 구성한다.</summary>
    // 수정(2026-10-03) — 확인 문구를 매개변수로 받는다(인트로 / 2페이즈 전환).
    private void BuildContents(TMP_FontAsset font, string message)
    {
        RectTransform dim = NewRect("Dim", transform);
        SetSlot(dim, 0f, 1f, 0f, 1f);
        dim.gameObject.AddComponent<Image>().color = DimColor;

        RectTransform border = NewRect("Panel", dim);
        border.anchorMin = border.anchorMax = new Vector2(0.5f, 0.5f);
        border.sizeDelta = new Vector2(680f, 270f);
        border.gameObject.AddComponent<Image>().color = BorderColor;

        RectTransform panel = NewRect("Inner", border);
        SetSlot(panel, 0f, 1f, 0f, 1f);
        panel.offsetMin = new Vector2(4f, 4f);
        panel.offsetMax = new Vector2(-4f, -4f);
        panel.gameObject.AddComponent<Image>().color = PanelColor;

        CreateLabel(panel, "Message", message, font, 30f,
            0.06f, 0.94f, 0.43f, 0.91f);
        Button confirm = CreateButton(panel, "Skip", "스킵", font, 0.10f, 0.46f);
        Button cancel = CreateButton(panel, "CancelButton", "취소", font, 0.54f, 0.90f);
        confirm.onClick.AddListener(Confirm);
        cancel.onClick.AddListener(Close);

        // 마우스뿐 아니라 UI 내장 탐색으로 패드·키보드에서도 두 버튼 사이를 이동할 수 있게 한다.
        Navigation confirmNavigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = cancel };
        Navigation cancelNavigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirm };
        confirm.navigation = confirmNavigation;
        cancel.navigation = cancelNavigation;

        eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            // HUD 없는 테스트 씬에서도 클릭할 수 있게, 이 모달의 수명 안에서만 입력 모듈을 준비한다.
            var inputHost = new GameObject("IntroDialogEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            inputHost.transform.SetParent(transform, false);
            inputHost.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            eventSystem = inputHost.GetComponent<EventSystem>();
        }

        previousSelection = eventSystem.currentSelectedGameObject;
        eventSystem.SetSelectedGameObject(cancel.gameObject);
    }

    /// <summary>추가 생성(2026-10-02) — 여는 ESC와 닫는 ESC를 구분하고 맨 위 확인 창만 취소한다.</summary>
    private void Update()
    {
        if (closed || Time.frameCount <= openedFrame || !PauseGate.IsTop(this) || InputBindings.IsRebinding) return;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (escape || InputBindings.SettingsAction.WasPressedThisFrame()) Close();
    }

    /// <summary>추가 생성(2026-10-02) — 먼저 모달의 일시정지를 해제하고, 연출의 공통 스킵 경로를 호출한다.</summary>
    private void Confirm()
    {
        if (closed || !PauseGate.IsTop(this)) return;
        // 수정(2026-10-03) — 어떤 연출이든 ISkippableCutscene.Skip 하나로 끝낸다.
        MonoBehaviour target = owner;
        Close();
        if (target != null && target is ISkippableCutscene cutscene) cutscene.Skip();
    }

    /// <summary>추가 생성(2026-10-02) — 취소·전투 시작·방 종료가 겹쳐도 PauseGate 해제는 한 번만 한다.</summary>
    public void Close()
    {
        if (closed) return;
        Release();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    /// <summary>추가 생성(2026-10-02) — 씬 변경이나 부모 비활성화에도 멈춤 상태와 선택된 버튼을 남기지 않는다.</summary>
    private void OnDisable()
    {
        Release();
    }

    /// <summary>추가 생성(2026-10-02) — 해제 순서를 한 곳에 모아 정상 닫기와 Unity 수명 종료가 같은 결과를 낸다.</summary>
    private void Release()
    {
        if (closed) return;
        closed = true;
        inputConsumedFrame = Time.frameCount;
        PauseGate.Close(this);

        if (eventSystem != null && eventSystem.currentSelectedGameObject != null &&
            eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
            eventSystem.SetSelectedGameObject(previousSelection);

        if (current == this) current = null;
        owner = null;
    }

    /// <summary>추가 생성(2026-10-02) — 설정 화면의 어두운 판·주황 강조색에 맞춘 기본 UI 버튼을 만든다.</summary>
    private static Button CreateButton(Transform parent, string name, string label, TMP_FontAsset font,
        float xMin, float xMax)
    {
        RectTransform rect = NewRect(name, parent);
        SetSlot(rect, xMin, xMax, 0.12f, 0.35f);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.23f, 0.17f, 0.13f, 1f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.35f, 1.25f, 1.10f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.8f, 0.75f, 0.65f, 1f);
        button.colors = colors;
        CreateLabel(rect, "Label", label, font, 28f, 0.04f, 0.96f, 0f, 1f);
        return button;
    }

    /// <summary>추가 생성(2026-10-02) — 모든 문구가 같은 TMP 글꼴을 쓰고 버튼 클릭을 가로채지 않게 한다.</summary>
    private static TMP_Text CreateLabel(Transform parent, string name, string value, TMP_FontAsset font,
        float fontSize, float xMin, float xMax, float yMin, float yMax)
    {
        RectTransform rect = NewRect(name, parent);
        SetSlot(rect, xMin, xMax, yMin, yMax);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = value;
        label.fontSize = fontSize;
        label.color = TextColor;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>추가 생성(2026-10-02) — 부모와 같은 UI 좌표계를 쓰는 빈 사각형을 만든다.</summary>
    private static RectTransform NewRect(string name, Transform parent)
    {
        var host = new GameObject(name, typeof(RectTransform));
        host.transform.SetParent(parent, false);
        return (RectTransform)host.transform;
    }

    /// <summary>추가 생성(2026-10-02) — 기준 해상도가 달라도 같은 비율 자리를 차지하도록 앵커로 배치한다.</summary>
    private static void SetSlot(RectTransform rect, float xMin, float xMax, float yMin, float yMax)
    {
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
