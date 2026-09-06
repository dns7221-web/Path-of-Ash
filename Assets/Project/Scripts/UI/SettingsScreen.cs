using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 추가 생성 — 설정 화면. 볼륨 셋, 전체화면, 창 크기를 고른다.
///
/// <b>두 씬에서 같은 화면을 쓰고, 여는 방법만 다르다.</b>
/// - Title: 화면 구석의 '설정' 버튼으로 연다. 여기서 ESC는 예전부터 게임 종료라 건드리지 않는다.
/// - Game: ESC로 바로 연다. 게임 중에는 눈에 보이는 버튼을 두지 않는다 — 전투 화면에 늘 떠
///   있는 UI는 거슬리기만 하고, 설정은 자주 여는 것도 아니다.
///
/// 씬마다 다른 화면을 만들지 않는 이유는 이 프로젝트가 보스 방을 별도 씬으로 안 나눈 것과 같다.
/// 복제본은 언젠가 반드시 원본과 어긋나고, 그때 어느 쪽이 맞는지 알 방법이 없다.
///
/// <b>시간은 직접 안 멈춘다.</b> <see cref="PauseGate"/>에 "나 열렸다"고 알리기만 하고,
/// Time.timeScale은 그쪽이 판단한다. 인벤토리 같은 다른 화면과 겹쳐도 스택이 안 비므로
/// 닫는 순간 시간이 잠깐 흐르는 일이 없다.
///
/// 값을 이 컴포넌트가 안 들고 있는 것도 의도다. 값의 주인은 <see cref="GameSettings"/>이고
/// 여기는 <b>보여주고 넘기기만</b> 한다. 그래서 화면을 껐다 켜도, 씬을 옮겨도 값이 안 흔들린다.
/// </summary>
[DisallowMultipleComponent]
public class SettingsScreen : MonoBehaviour
{
    [Header("구성")]
    [Tooltip("켜고 끌 화면 오브젝트. 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.")]
    [SerializeField] private GameObject root;

    [Header("탭")]
    [Tooltip("사운드·화면 항목이 들어 있는 쪽.")]
    [SerializeField] private GameObject generalTabContent;

    [Tooltip("조작 안내가 들어 있는 쪽.")]
    [SerializeField] private GameObject controlsTabContent;

    [SerializeField] private Button generalTabButton;
    [SerializeField] private Button controlsTabButton;

    [Header("사운드")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;

    [Tooltip("슬라이더 오른쪽의 퍼센트 표시. 없어도 동작한다.")]
    [SerializeField] private TMPro.TMP_Text masterValueLabel;
    [SerializeField] private TMPro.TMP_Text bgmValueLabel;
    [SerializeField] private TMPro.TMP_Text sfxValueLabel;

    [Header("화면")]
    [SerializeField] private Toggle fullscreenToggle;

    [Tooltip("체크박스 옆의 켜짐/꺼짐 표시. 색만 채워진 네모는 켜진 건지 고를 수 있는 건지 " +
             "구별이 안 돼서 글자로 같이 알려준다. 없어도 동작한다.")]
    [SerializeField] private TMPro.TMP_Text fullscreenStateLabel;

    [Tooltip("창 크기 문구. 예: 3배 (1920x1080)")]
    [SerializeField] private TMPro.TMP_Text windowScaleLabel;

    [SerializeField] private Button windowScalePrevButton;
    [SerializeField] private Button windowScaleNextButton;

    [Header("아래 버튼")]
    [SerializeField] private Button resetButton;
    [SerializeField] private Button closeButton;

    [Tooltip("이 화면을 여는 바깥 버튼. 타이틀 화면의 '설정' 버튼이 여기 들어간다. " +
             "게임 안에서는 일시정지 메뉴가 대신 열어주므로 비워둔다.")]
    [SerializeField] private Button openButton;

    [Tooltip("믹서가 없을 때 띄울 안내. 없어도 동작한다.")]
    [SerializeField] private TMPro.TMP_Text noticeLabel;

    [Header("조작 탭")]
    // 조작 탭의 키 문구는 씬에 구워두면 안 된다. 구워두면 플레이어가 키를 바꿔도 글자가
    // 그대로라, 표와 실제가 어긋나는 바로 그 상황이 된다. 화면을 열 때마다 다시 읽는다.
    [Tooltip("조작 탭의 키 문구들. 빌더가 채운다.")]
    [SerializeField] private TMPro.TMP_Text[] controlKeyLabels;

    [Tooltip("위 문구가 가리키는 InputBindings 액션 이름. 같은 순서여야 한다.")]
    [SerializeField] private string[] controlActionIds;

    [Tooltip("각 줄의 '바꾸기' 버튼. 위 두 배열과 같은 순서다.")]
    [SerializeField] private Button[] controlRebindButtons;

    [Tooltip("조작 탭 아래 안내. 키를 받는 중이라거나 겹쳐서 비웠다는 것을 알린다.")]
    [SerializeField] private TMPro.TMP_Text controlsNoticeLabel;

    [Tooltip("조작만 기본값으로 되돌리는 버튼.")]
    [SerializeField] private Button controlsResetButton;

    [Header("키 입력")]
    // 수정(입력 중앙화): 여닫는 키는 InputBindings의 Settings 액션이 들고 있다.
    // ESC는 바꿀 수 없는 키라 인스펙터에도 노출하지 않는다.
    [Tooltip("이 키로 화면을 열 수도 있게 한다. Game 씬은 켜고, Title 씬은 끈다 — " +
             "타이틀에서 ESC는 예전부터 게임 종료라 같은 키를 두 곳이 먹으면 둘 다 실행된다.")]
    [SerializeField] private bool openWithKey = true;

    /// <summary>탭 번호. 이름을 붙여두면 SelectTab(0) 같은 숫자를 안 읽어도 된다.</summary>
    public const int GeneralTab = 0;
    public const int ControlsTab = 1;

    // 탭 색. 빌더의 색표와 같은 값이다 — 실행 중에 바꿔야 해서 이쪽에도 있어야 한다.
    private static readonly Color SelectedTabColor = new Color(0.26f, 0.16f, 0.10f, 1f);
    private static readonly Color UnselectedTabColor = new Color(0.14f, 0.13f, 0.12f, 1f);
    private static readonly Color SelectedTabTextColor = new Color(0.95f, 0.45f, 0.15f, 1f);
    private static readonly Color UnselectedTabTextColor = new Color(0.62f, 0.60f, 0.57f, 1f);

    /// <summary>조작 탭에 평소 띄워둘 안내.</summary>
    private const string ControlsDefaultNotice =
        "* 이동(방향키)과 ESC는 바꿀 수 없다. 새 키가 다른 조작과 겹치면 그쪽 키를 비운다.";

    private bool isOpen;
    private int currentTab = GeneralTab;

    // 값을 화면에 다시 그리는 중인가.
    // 이게 없으면 Refresh가 슬라이더 값을 세팅하면서 onValueChanged를 부르고,
    // 그게 다시 GameSettings에 쓰면서 Changed를 띄워 Refresh를 부르는 고리가 생긴다.
    private bool refreshing;

    /// <summary>지금 열려 있는가.</summary>
    public bool IsOpen => isOpen;

    /// <summary>지금 보고 있는 탭.</summary>
    public int CurrentTab => currentTab;

    private void Awake()
    {
        // 수정(입력 중앙화): 여기서 만들던 ESC 바인딩은 InputBindings.Build로 옮겼다.

        // 켜고 끌 오브젝트가 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.
        // 꺼진 오브젝트는 Update가 안 돌아서 스스로를 다시 켤 수 없다.
        if (root == gameObject)
        {
            Debug.LogError("[설정] root가 이 오브젝트 자신이라 한 번 닫히면 다시 열 수 없다. " +
                           "자식 오브젝트를 root로 넣어라.", this);
        }

        WireControls();

        // 시작할 때 Time.timeScale을 건드리지 않는다. 1로 덮으면 다른 곳에서 멈춰둔 것까지 푼다.
        isOpen = false;
        if (root != null) root.SetActive(false);
    }

    /// <summary>
    /// UI 요소의 이벤트를 GameSettings에 연결한다.
    ///
    /// 인스펙터의 OnClick 목록이 아니라 코드로 다는 이유: 인스펙터 연결은 대상 함수의 이름을
    /// 바꾸면 조용히 끊긴다. 에러도 경고도 없이 버튼만 안 먹는다. 코드로 달면 컴파일 때 걸린다.
    /// </summary>
    private void WireControls()
    {
        if (masterSlider != null)
            masterSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.MasterVolume = v; });

        if (bgmSlider != null)
            bgmSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.BgmVolume = v; });

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.SfxVolume = v; });

        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(v => { if (!refreshing) GameSettings.Fullscreen = v; });

        if (windowScalePrevButton != null)
            windowScalePrevButton.onClick.AddListener(() => StepWindowScale(-1));

        if (windowScaleNextButton != null)
            windowScaleNextButton.onClick.AddListener(() => StepWindowScale(+1));

        if (resetButton != null)
            resetButton.onClick.AddListener(OnResetClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        // 바깥에서 이 화면을 여는 버튼(타이틀의 '설정'). 게임 안에서는 ESC가 직접
        // Open()을 부르므로 비어 있는 것이 정상이다.
        if (openButton != null)
            openButton.onClick.AddListener(Open);

        // 추가 생성 — 탭 버튼.
        if (generalTabButton != null)
            generalTabButton.onClick.AddListener(() => SelectTab(GeneralTab));

        if (controlsTabButton != null)
            controlsTabButton.onClick.AddListener(() => SelectTab(ControlsTab));

        // 추가 생성 — 줄마다 붙은 '바꾸기' 버튼.
        // index를 지역 변수로 복사하는 이유: 람다가 루프 변수를 참조로 붙잡으면
        // 모든 버튼이 마지막 번호를 가리키게 된다.
        if (controlRebindButtons != null)
        {
            for (int i = 0; i < controlRebindButtons.Length; i++)
            {
                if (controlRebindButtons[i] == null) continue;

                int index = i;
                controlRebindButtons[i].onClick.AddListener(() => BeginRebind(index));
            }
        }

        if (controlsResetButton != null)
            controlsResetButton.onClick.AddListener(OnControlsResetClicked);
    }

    /// <summary>
    /// 추가 생성 — 그 줄의 키를 새로 받는다.
    ///
    /// 받는 동안 모든 '바꾸기' 버튼을 잠근다. 두 개를 동시에 받으면 다음에 누른 키가
    /// 어느 조작으로 갈지 정할 수 없다.
    /// </summary>
    private void BeginRebind(int index)
    {
        if (controlActionIds == null || index < 0 || index >= controlActionIds.Length) return;
        if (InputBindings.IsRebinding) return;

        SetRebindButtonsInteractable(false);

        // 그 줄만 "누르세요"로 바꾼다. 안내만 바뀌면 어느 줄을 바꾸는 중인지 알 수 없다.
        if (controlKeyLabels != null && index < controlKeyLabels.Length &&
            controlKeyLabels[index] != null)
        {
            controlKeyLabels[index].text = "누르세요";
        }

        if (controlsNoticeLabel != null)
            controlsNoticeLabel.text = "* 새 키를 누른다. ESC를 누르면 그만둔다.";

        InputBindings.StartRebind(controlActionIds[index], (changed, cleared) =>
        {
            SetRebindButtonsInteractable(true);

            // 성공이든 취소든 다시 읽는다. 취소했으면 "누르세요"를 원래 키로 되돌려야 한다.
            RefreshControlKeys();

            if (controlsNoticeLabel == null) return;

            if (!changed)
                controlsNoticeLabel.text = "* 그만뒀다. 키는 그대로다.";
            else if (!string.IsNullOrEmpty(cleared))
                controlsNoticeLabel.text = $"* 겹쳐서 '{cleared}'의 키를 비웠다. 다시 정해라.";
            else
                controlsNoticeLabel.text = ControlsDefaultNotice;
        });
    }

    /// <summary>조작만 기본값으로 되돌린다. 볼륨·화면은 건드리지 않는다.</summary>
    private void OnControlsResetClicked()
    {
        if (InputBindings.IsRebinding) return;

        InputBindings.ResetAllToDefaults();
        RefreshControlKeys();

        if (controlsNoticeLabel != null)
            controlsNoticeLabel.text = "* 조작을 기본값으로 되돌렸다.";
    }

    /// <summary>키를 받는 동안 조작 탭의 버튼을 전부 잠근다.</summary>
    private void SetRebindButtonsInteractable(bool on)
    {
        if (controlRebindButtons != null)
        {
            foreach (Button button in controlRebindButtons)
            {
                if (button != null) button.interactable = on;
            }
        }

        if (controlsResetButton != null) controlsResetButton.interactable = on;
    }

    /// <summary>
    /// 추가 생성 — 탭을 고른다.
    ///
    /// 창을 두 개로 나누지 않고 한 창의 내용만 갈아 끼우는 이유: 둘은 "다른 화면"이 아니라
    /// 같은 설정의 분류다. 창을 나누면 뒤로가기 순서가 생기고, <see cref="PauseGate"/>에
    /// 두 겹이 쌓이고, ESC가 어느 것을 닫아야 하는지 매번 판단해야 한다.
    /// </summary>
    public void SelectTab(int index)
    {
        currentTab = index;

        if (generalTabContent != null) generalTabContent.SetActive(index == GeneralTab);
        if (controlsTabContent != null) controlsTabContent.SetActive(index == ControlsTab);

        ApplyTabLook(generalTabButton, index == GeneralTab);
        ApplyTabLook(controlsTabButton, index == ControlsTab);
    }

    /// <summary>
    /// 고른 탭과 안 고른 탭을 색으로 구분한다.
    ///
    /// 버튼의 <see cref="Selectable.interactable"/>을 끄는 방법도 있지만 그러면 "지금 보고
    /// 있는 탭"이 "고장난 버튼"과 같은 회색이 된다. 켜진 쪽을 밝게 하는 편이 읽힌다.
    /// </summary>
    private void ApplyTabLook(Button button, bool selected)
    {
        if (button == null) return;

        if (button.targetGraphic is Image image)
            image.color = selected ? SelectedTabColor : UnselectedTabColor;

        var label = button.GetComponentInChildren<TMPro.TMP_Text>();
        if (label != null) label.color = selected ? SelectedTabTextColor : UnselectedTabTextColor;
    }

    private void OnEnable()
    {
        GameSettings.Changed += Refresh;
    }

    private void OnDisable()
    {
        GameSettings.Changed -= Refresh;

        // 열린 채로 꺼지거나 씬이 바뀌면 시간이 멈춘 상태로 남는다. 반드시 스택에서 뺀다.
        if (isOpen)
        {
            isOpen = false;
            PauseGate.Close(this);
            GameSettings.Flush();
        }
    }

    private void Update()
    {
        // 키를 받는 중에는 ESC가 "그만두기"다. 여기서도 ESC를 보면 같은 누름으로
        // 화면까지 닫혀서, 그만두려던 사람이 설정 밖으로 튕겨 나간다.
        if (InputBindings.IsRebinding) return;

        // timeScale이 0이어도 입력은 실제 시간으로 들어온다. 그래서 멈춘 상태에서도 닫을 수 있다.
        if (isOpen)
        {
            // <b>맨 위에 있을 때만</b> 닫는다.
            // 이 화면 위에 다른 화면이 겹쳐 열려 있으면 그쪽이 먼저 닫혀야 한다.
            // "누가 키를 먹는가"를 스택 순서 하나로 정하면 화면이 늘어나도 규칙이 그대로다.
            if (!PauseGate.IsTop(this)) return;

            if (InputBindings.SettingsAction.WasPressedThisFrame()) Close();
            return;
        }

        if (!openWithKey) return;

        // 다른 화면(인벤토리·보스 열쇠)이 열려 있으면 손을 뗀다.
        // 그 화면들은 각자 자기 키로 닫는 것이 기존 조작이라, 여기서 위에 겹쳐 열면
        // 플레이어는 화면 두 겹을 각각 다른 키로 닫아야 한다.
        if (PauseGate.IsPaused) return;

        if (InputBindings.SettingsAction.WasPressedThisFrame()) Open();
    }

    /// <summary>설정 화면을 연다. 버튼의 OnClick에도 연결할 수 있다.</summary>
    public void Open()
    {
        if (isOpen) return;

        isOpen = true;

        // 화면을 켜기 전에 스택에 먼저 넣는다. 순서를 지키는 이유는 아래 Close와 짝이다 —
        // 열림 표시와 실제 멈춤 사이에 한 프레임이라도 틈이 생기면 그 프레임에 게임이 흐른다.
        PauseGate.Open(this);

        if (root != null) root.SetActive(true);

        // 항상 일반 탭부터 보여준다. 지난번에 보던 탭을 기억하면, 볼륨을 만지러 연
        // 플레이어가 조작 목록을 먼저 보게 된다. 자주 쓰는 쪽이 기본이어야 한다.
        SelectTab(GeneralTab);

        Refresh();
    }

    /// <summary>설정 화면을 닫는다. 버튼의 OnClick에도 연결할 수 있다.</summary>
    public void Close()
    {
        if (!isOpen) return;

        isOpen = false;

        if (root != null) root.SetActive(false);

        PauseGate.Close(this);

        // 디스크 쓰기는 여기서 한 번만. 슬라이더를 끄는 동안 매 프레임 쓰면 드래그가 끊긴다.
        GameSettings.Flush();
    }

    /// <summary>창 크기를 한 칸 옮긴다.</summary>
    private void StepWindowScale(int step)
    {
        // 순환(4배 다음에 2배)시키지 않는다. 끝에서 멈추면 지금이 끝이라는 게 눌러 보면 바로 안다.
        // 순환시키면 목록의 처음과 끝을 알 수 없어서 원하는 값을 지나치기 쉽다.
        GameSettings.WindowScaleIndex = GameSettings.WindowScaleIndex + step;
        Refresh();
    }

    private void OnResetClicked()
    {
        GameSettings.ResetToDefaults();
        Refresh();
    }

    /// <summary>
    /// GameSettings의 현재 값을 화면에 그린다.
    ///
    /// SetValueWithoutNotify를 쓰는 이유: 보통 방식으로 값을 넣으면 onValueChanged가 불려서
    /// 방금 읽어온 값을 도로 GameSettings에 쓴다. 값이 같아 무해해 보이지만, 그 쓰기가
    /// Changed를 띄우고 Changed가 다시 Refresh를 불러 무한히 돈다.
    /// (refreshing 잠금과 둘 다 두는 것은, 어느 한쪽만으로도 막히지만 UI 요소를 나중에
    ///  추가할 때 한쪽을 빠뜨리기 쉬워서다.)
    /// </summary>
    private void Refresh()
    {
        if (refreshing) return;
        refreshing = true;

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(GameSettings.BgmVolume);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);

        SetPercent(masterValueLabel, GameSettings.MasterVolume);
        SetPercent(bgmValueLabel, GameSettings.BgmVolume);
        SetPercent(sfxValueLabel, GameSettings.SfxVolume);

        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);

        if (fullscreenStateLabel != null)
            fullscreenStateLabel.text = GameSettings.Fullscreen ? "켜짐" : "꺼짐 (창 모드)";

        if (windowScaleLabel != null)
        {
            // 전체화면일 때 창 크기는 의미가 없다. 값을 숨기는 대신 왜 안 쓰이는지 적는다 —
            // 회색으로만 만들면 "고장인가"와 구별이 안 된다.
            windowScaleLabel.text = GameSettings.Fullscreen
                ? "전체화면 사용 중"
                : GameSettings.WindowScaleLabel;
        }

        // 전체화면이면 배수 버튼을 잠근다. 눌러도 화면이 안 바뀌는 버튼은 고장으로 읽힌다.
        int index = GameSettings.WindowScaleIndex;
        bool windowed = !GameSettings.Fullscreen;

        if (windowScalePrevButton != null)
            windowScalePrevButton.interactable = windowed && index > 0;

        if (windowScaleNextButton != null)
            windowScaleNextButton.interactable = windowed && index < GameSettings.WindowScales.Length - 1;

        RefreshControlKeys();

        if (controlsNoticeLabel != null && !InputBindings.IsRebinding)
            controlsNoticeLabel.text = ControlsDefaultNotice;

        if (noticeLabel != null)
        {
            // 지금 프로젝트에는 소리가 하나도 없다. 슬라이더를 움직여도 아무 일이 안 일어나는데
            // 안내가 없으면 "설정이 고장났다"로 읽힌다. 사실을 적어두는 편이 낫다.
            noticeLabel.text = GameSettings.HasMixer
                ? string.Empty
                : "* 오디오 믹서가 아직 없어 마스터만 실제로 적용된다.";
        }

        refreshing = false;
    }

    /// <summary>
    /// 조작 탭의 키 문구를 실제 바인딩에서 다시 읽는다.
    ///
    /// 두 배열을 나란히 두는 이유: 유니티 인스펙터는 (글자, 이름) 같은 짝을 담은 구조체
    /// 배열을 빌더에서 채우기가 번거롭다. 순서만 맞추면 되는 단순한 대응이라 배열 둘로 뒀고,
    /// 길이가 어긋나도 짧은 쪽까지만 도니 예외는 안 난다.
    /// </summary>
    private void RefreshControlKeys()
    {
        if (controlKeyLabels == null || controlActionIds == null) return;

        int count = Mathf.Min(controlKeyLabels.Length, controlActionIds.Length);
        for (int i = 0; i < count; i++)
        {
            if (controlKeyLabels[i] == null) continue;
            controlKeyLabels[i].text = InputBindings.KeyboardTextFor(controlActionIds[i]);
        }
    }

    /// <summary>0~1 값을 퍼센트 문구로 바꿔 넣는다.</summary>
    private void SetPercent(TMPro.TMP_Text label, float value)
    {
        if (label == null) return;
        label.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
