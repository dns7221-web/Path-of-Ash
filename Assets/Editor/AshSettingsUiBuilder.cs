using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 추가 생성 — 설정 화면을 씬에 조립하는 에디터 도구.
///
/// 메뉴: Tools → 재의 길 → 설정 화면 전체 구성 (Title + Game)
///
/// UI를 손으로 안 만들고 코드로 두는 이유는 다른 빌더와 같다. RectTransform은 앵커·피벗·
/// 오프셋이 서로 얽혀 있어서 창에서 끌어 맞추면 "내 화면에서는 맞는데 해상도가 바뀌면
/// 어긋나는" 결과가 나오기 쉽다. 앵커를 코드로 못 박으면 그 문제가 없고, 왜 그 값인지가
/// 주석으로 남는다.
///
/// <b>Title과 Game에서 여는 방법만 다르다.</b>
/// - Game: ESC로 연다. 전투 화면에 늘 떠 있는 버튼을 두지 않는다.
/// - Title: 구석의 '설정' 버튼으로 연다. 타이틀에서 ESC는 예전부터 게임 종료라 겹칠 수 없다.
///
/// 조작 탭의 줄은 InputCatalog에서 뽑고, 키 문구는 실행 중에 SettingsScreen이 실제
/// 바인딩에서 다시 읽는다. 여기서 구운 글자를 그대로 두면 플레이어가 키를 바꿨을 때
/// 화면만 옛날 값을 말하게 된다.
///
/// 설정 화면 자체는 <b>양쪽에서 똑같은 것을 만든다.</b> 씬마다 다른 화면을 만들면
/// 한쪽만 고치는 일이 반드시 생긴다.
///
/// 프로토타입이라 패널은 그림 없이 단색 + 잉걸색 테두리로 만든다. 나중에 인벤토리처럼
/// 패널 아트가 나오면 <see cref="Image.sprite"/>만 갈아 끼우면 되도록 구조를 같게 뒀다.
/// </summary>
public static class AshSettingsUiBuilder
{
    private const string TitleSceneName = "Title";
    private const string GameSceneName = "Game";

    private const string TitleScenePath = "Assets/Scenes/Title.unity";
    private const string GameScenePath = "Assets/Scenes/Game.unity";

    /// <summary>
    /// 예전 버전이 만들던 일시정지 메뉴의 이름.
    ///
    /// 컴포넌트는 없앴지만 이 이름은 남겨둔다 — 옛 도구로 이미 만들어둔 씬이 있으면
    /// 스크립트가 사라진 껍데기가 남아서, 그게 ESC를 먹지도 않으면서 화면만 가린다.
    /// 도구를 다시 돌릴 때 같이 치워준다.
    /// </summary>
    private const string PauseScreenName = "PauseScreen";

    private const string SettingsScreenName = "SettingsScreen";
    private const string TitleButtonName = "TitleSettingsButton";

    /// <summary>
    /// 설정 화면 전용 캔버스 이름.
    ///
    /// <b>HUD 캔버스에 얹지 않는 이유.</b> 예전에는 씬에 있는 아무 오버레이 캔버스나 찾아
    /// 그 밑에 만들었다. Game 씬에서 그건 `GameHUD`였는데, `게임 HUD 생성` 도구는 자기
    /// 루트를 통째로 지우고 다시 만든다. 그래서 <b>HUD를 한 번 다시 만들면 설정 창이
    /// 조용히 사라졌다.</b> 에러도 경고도 없고, ESC를 눌러도 아무 일이 안 일어날 뿐이다.
    ///
    /// 도구 실행 순서를 사람이 외우게 하는 대신, 서로의 소유를 겹치지 않게 나눈다.
    /// 정렬 순서를 높게 잡아 HUD 위에 그려지는 것도 여기서 함께 보장된다.
    /// </summary>
    private const string SettingsCanvasName = "SettingsCanvas";

    /// <summary>HUD보다 위. 게이지가 설정 창을 뚫고 나오면 안 된다.</summary>
    private const int SettingsSortingOrder = 100;

    /// <summary>
    /// 프로젝트의 한글 폰트. 96pt 아틀라스를 쓴다 —
    /// 설정 화면의 글자가 32pt짜리보다 커서, 작은 아틀라스를 늘리면 픽셀이 뭉개진다.
    /// </summary>
    private const string FontPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";

    // ── 색 ────────────────────────────────────────────────────────────────
    //
    // 게임플레이 아트 규칙(회색·숯색 바탕에 주황 잉걸만 강조)을 UI에도 그대로 쓴다.

    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color PanelColor = new Color(0.10f, 0.09f, 0.09f, 0.97f);
    private static readonly Color BorderColor = new Color(0.55f, 0.26f, 0.10f, 1f);
    private static readonly Color EmberColor = new Color(0.95f, 0.45f, 0.15f, 1f);
    private static readonly Color TextColor = new Color(0.86f, 0.84f, 0.80f, 1f);
    private static readonly Color DimTextColor = new Color(0.62f, 0.60f, 0.57f, 1f);
    private static readonly Color ButtonColor = new Color(0.18f, 0.16f, 0.15f, 1f);
    private static readonly Color TrackColor = new Color(0.24f, 0.22f, 0.21f, 1f);

    // ── 규격 ──────────────────────────────────────────────────────────────

    /// <summary>캔버스 기준 해상도. 다른 UI 빌더와 같은 값이어야 크기가 안 어긋난다.</summary>
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    private const float SettingsPanelWidth = 1000f;
    private const float SettingsPanelHeight = 820f;

    // 조작 탭의 줄 간격. 항목이 열한 개라 일반 탭과 같은 높이(64)로는 패널을 넘는다.
    // 스크롤을 붙이는 대신 줄을 줄인 이유: 스크롤은 "아래에 더 있다"를 플레이어가 알아야
    // 쓸모가 있는데, 한 화면에 다 들어가면 그 문제 자체가 없다.
    private const float ControlsStartY = 168f;
    private const float ControlsHeaderHeight = 30f;
    private const float ControlsHeaderPitch = 36f;
    private const float ControlsRowHeight = 28f;
    private const float ControlsRowPitch = 32f;

    /// <summary>패널 안쪽 좌우 여백. 줄(Row)이 이 만큼 들어와서 놓인다.</summary>
    private const float RowMargin = 60f;
    private const float RowHeight = 64f;

    /// <summary>슬라이더 손잡이 크기. 줄 높이보다 작아야 줄 밖으로 안 튀어나온다.</summary>
    private const float HandleWidth = 16f;
    private const float HandleHeight = 34f;

    /// <summary>체크박스 한 변. 글자 높이와 비슷해야 줄이 안 들뜬다.</summary>
    private const float CheckBoxSize = 34f;

    /// <summary>
    /// 창 크기를 좌우로 넘기는 버튼의 글자.
    ///
    /// 처음에는 ◀ ▶를 썼는데 NeoDunggeunmoPro에 그 글자가 없어서 두부(□)로 나왔다.
    /// 폰트 아틀라스에 없는 글자는 에러도 경고도 없이 네모로 그려지기 때문에,
    /// 코드만 봐서는 멀쩡해 보이고 화면에서만 깨진다. 아스키로 바꾸면 어떤 폰트에도 있다.
    /// </summary>
    private const string PrevArrow = "<";
    private const string NextArrow = ">";

    private static TMPro.TMP_FontAsset font;

    /// <summary>
    /// 추가 생성 — Title과 Game 두 씬에 한 번에 만든다.
    ///
    /// <b>왜 따로 만들었나.</b> 씬마다 도구를 돌려야 하는데, 한쪽을 빠뜨려도 에러도 경고도
    /// 안 난다. 그냥 그 씬에서만 ESC가 안 먹는다. 실제로 Title만 만들고 Game을 빠뜨려서
    /// "게임에서 ESC가 안 된다"로 한 번 헤맸다. 사람이 기억해야 하는 절차를 도구가 대신한다.
    ///
    /// 씬을 갈아 끼우는 도구라 저장 안 된 변경을 먼저 묻고, 끝나면 원래 씬으로 돌려놓는다.
    /// </summary>
    [MenuItem("Tools/재의 길/설정 화면 전체 구성 (Title + Game)")]
    public static void BuildAllScenes()
    {
        // 안 물어보면 작업 중이던 씬의 변경이 조용히 날아간다. 되돌릴 방법이 없는 종류다.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 돌아올 자리를 기억해둔다. 새 씬(저장 안 한 씬)이면 경로가 비어 있을 수 있다.
        string previousPath = SceneManager.GetActiveScene().path;

        if (!BuildInScene(TitleScenePath)) return;
        if (!BuildInScene(GameScenePath)) return;

        if (!string.IsNullOrEmpty(previousPath))
            EditorSceneManager.OpenScene(previousPath);

        Debug.Log("[설정 화면] Title과 Game 두 씬에 모두 만들고 저장했다.");
    }

    /// <summary>씬 하나를 열어 구성하고 저장한다. 성공하면 true.</summary>
    private static bool BuildInScene(string scenePath)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            Debug.LogError($"[설정 화면] 씬을 못 찾았다: {scenePath}");
            return false;
        }

        Scene scene = EditorSceneManager.OpenScene(scenePath);

        Build();

        // Build가 씬 이름을 보고 스스로 걸러낸다. 여기서는 저장만 책임진다.
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    [MenuItem("Tools/재의 길/설정 화면 생성")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        bool isTitle = scene.name == TitleSceneName;
        bool isGame = scene.name == GameSceneName;

        // 다른 씬에서 실행하면 그 씬에 화면을 만들어버린다. 먼저 막는다.
        if (!isTitle && !isGame)
        {
            Debug.LogError($"[설정 화면] 활성 씬이 '{scene.name}'이다. Title 또는 Game 씬을 열고 실행해라.");
            return;
        }

        font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
        if (font == null)
        {
            // 폰트 하나 때문에 화면 전체가 안 만들어지는 편이 더 나쁘다. 경고만 남기고 진행한다.
            Debug.LogWarning($"[설정 화면] 한글 폰트를 못 찾아 TMP 기본 폰트로 만든다. 한글이 깨질 수 있다.\n{FontPath}");
        }

        // 이전 실행 결과를 지운다. 남겨두면 화면이 두 겹으로 쌓여서, 위의 것만 닫히고
        // 아래 것이 스택에 남아 시간이 안 풀리는 상태가 된다.
        //
        // 캔버스 안이 아니라 <b>씬 전체</b>에서 찾는 이유: 옛 도구는 HUD 캔버스 밑에
        // 만들었다. 새 캔버스만 뒤지면 그 옛 화면이 남아서 두 개가 동시에 ESC를 먹는다.
        DestroyByName(SettingsScreenName);
        DestroyByName(PauseScreenName);
        DestroyByName(TitleButtonName);

        Canvas canvas = CreateSettingsCanvas();

        // 여는 방법이 씬마다 다르다.
        // - Game: ESC로 연다. 전투 화면에 늘 떠 있는 버튼을 두지 않는다.
        // - Title: 구석의 '설정' 버튼으로 연다. 타이틀에서 ESC는 예전부터 게임 종료라,
        //   같은 키를 두 곳이 먹으면 설정이 열리면서 게임도 같이 꺼진다.
        SettingsScreen settings = BuildSettingsScreen(canvas.transform, openWithKey: isGame);

        if (isTitle) BuildTitleButton(canvas.transform, settings);

        EnsureEventSystem();
        EditorSceneManager.MarkSceneDirty(scene);

        string made = isGame ? "설정 화면 (ESC로 열림)" : "설정 화면 + 타이틀 설정 버튼";
        Debug.Log($"[설정 화면] {scene.name} 씬에 {made}을(를) 만들었다. 씬을 저장해라(Ctrl+S).\n" +
                  "볼륨 슬라이더를 실제로 쓰려면 Assets/Project/Audio/Resources/GameAudioMixer.mixer를 만들고 " +
                  "MasterVolume / BgmVolume / SfxVolume 세 파라미터를 Expose 해야 한다. " +
                  "없어도 마스터는 AudioListener로 동작한다.");
    }

    // ── 설정 화면 ─────────────────────────────────────────────────────────

    /// <summary>설정 화면 전체를 만들고 참조를 배선한다.</summary>
    /// <param name="openWithKey">ESC로 열 수 있게 할지. Game 씬만 켠다.</param>
    private static SettingsScreen BuildSettingsScreen(Transform canvas, bool openWithKey)
    {
        // 항상 켜져 있는 껍데기. 키 입력을 듣는 컴포넌트가 여기 붙는다.
        // 컴포넌트를 켜고 끄는 오브젝트에 붙이면, 꺼진 순간 Update가 안 돌아서
        // 스스로를 다시 켤 수 없다. 인벤토리 화면과 같은 구조다.
        RectTransform shell = NewRect(SettingsScreenName, canvas);
        Stretch(shell);
        var screen = shell.gameObject.AddComponent<SettingsScreen>();

        RectTransform root = NewRect("Root", shell);
        Stretch(root);

        // 어두운 막. 뒤가 물러나 보여야 어디에 집중할지가 분명해진다.
        // 이 이미지는 클릭도 같이 막아준다 — 일시정지 메뉴 위에 겹쳐 열렸을 때
        // 뒤의 버튼이 눌리면 안 된다.
        RectTransform dim = NewRect("Dim", root);
        Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = DimColor;

        RectTransform panel = CreatePanel(root, SettingsPanelWidth, SettingsPanelHeight);

        CreateHeader(panel, "Title", "설정", 24f, 72f, 46f, TextColor);

        // ── 탭 ──
        //
        // 창을 두 개로 나누지 않고 한 창의 내용만 갈아 끼운다. 둘은 "다른 화면"이 아니라
        // 같은 설정의 분류다. 창을 나누면 뒤로가기 순서가 생기고 PauseGate에 두 겹이 쌓인다.
        RectTransform tabBar = CreateRow(panel, "TabBar", 104f, 52f);
        Button generalTabButton = CreateButton(Sub(tabBar, "GeneralTabButton", 0f, 0.28f), "일반", 24f);
        Button controlsTabButton = CreateButton(Sub(tabBar, "ControlsTabButton", 0.30f, 0.58f), "조작", 24f);

        // 탭 내용은 패널 위에 겹쳐 놓고 켜고 끈다. 둘 다 패널을 꽉 채우므로
        // 아래 줄들의 y 값은 패널 기준 그대로 쓸 수 있다.
        RectTransform generalTab = NewRect("GeneralTab", panel);
        Stretch(generalTab);

        RectTransform controlsTab = NewRect("ControlsTab", panel);
        Stretch(controlsTab);

        // ── 일반 탭 ──
        CreateHeader(generalTab, "SoundHeader", "사운드", 176f, 38f, 28f, EmberColor);
        var (masterSlider, masterValue) = CreateSliderRow(generalTab, "Master", "마스터", 222f);
        var (bgmSlider, bgmValue) = CreateSliderRow(generalTab, "Bgm", "배경음", 292f);
        var (sfxSlider, sfxValue) = CreateSliderRow(generalTab, "Sfx", "효과음", 362f);

        CreateHeader(generalTab, "ScreenHeader", "화면", 438f, 38f, 28f, EmberColor);
        var (fullscreenToggle, fullscreenState) =
            CreateToggleRow(generalTab, "Fullscreen", "전체화면", 484f);
        var (scaleLabel, prevButton, nextButton) =
            CreateScaleRow(generalTab, "WindowScale", "창 크기", 554f);

        TMPro.TMP_Text notice =
            CreateHeader(generalTab, "Notice", string.Empty, 630f, 36f, 20f, DimTextColor);

        // '기본값으로'는 일반 탭 안에 둔다. 되돌리는 대상이 볼륨과 화면 설정뿐이라,
        // 조작 탭에서도 보이면 "조작도 되돌아가나"로 읽힌다.
        RectTransform resetRow = CreateRow(generalTab, "ResetRow", 684f, 48f);
        Button resetButton = CreateButton(Sub(resetRow, "ResetButton", 0f, 0.32f), "기본값으로", 22f);

        // ── 조작 탭 ──
        var controlKeyLabels = new List<TMPro.TMP_Text>();
        var controlActionIds = new List<string>();
        var controlRebindButtons = new List<Button>();
        var (controlsNotice, controlsReset) =
            BuildControlsTab(controlsTab, controlKeyLabels, controlActionIds, controlRebindButtons);

        // ── 두 탭이 함께 쓰는 아래 버튼 ──
        RectTransform buttonRow = CreateRow(panel, "Buttons", 748f, 52f);
        Button closeButton = CreateButton(Sub(buttonRow, "CloseButton", 0.36f, 0.64f), "닫기", 22f);

        // 조작 탭은 꺼진 채로 시작한다. 실행 중에는 SelectTab이 다시 정하지만,
        // 에디터에서 씬을 열었을 때 두 탭이 겹쳐 보이면 위치를 못 맞춘다.
        controlsTab.gameObject.SetActive(false);

        var serialized = new SerializedObject(screen);
        serialized.FindProperty("root").objectReferenceValue = root.gameObject;
        serialized.FindProperty("masterSlider").objectReferenceValue = masterSlider;
        serialized.FindProperty("bgmSlider").objectReferenceValue = bgmSlider;
        serialized.FindProperty("sfxSlider").objectReferenceValue = sfxSlider;
        serialized.FindProperty("masterValueLabel").objectReferenceValue = masterValue;
        serialized.FindProperty("bgmValueLabel").objectReferenceValue = bgmValue;
        serialized.FindProperty("sfxValueLabel").objectReferenceValue = sfxValue;
        serialized.FindProperty("fullscreenToggle").objectReferenceValue = fullscreenToggle;
        serialized.FindProperty("fullscreenStateLabel").objectReferenceValue = fullscreenState;
        serialized.FindProperty("windowScaleLabel").objectReferenceValue = scaleLabel;
        serialized.FindProperty("windowScalePrevButton").objectReferenceValue = prevButton;
        serialized.FindProperty("windowScaleNextButton").objectReferenceValue = nextButton;
        serialized.FindProperty("resetButton").objectReferenceValue = resetButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("noticeLabel").objectReferenceValue = notice;
        serialized.FindProperty("openWithKey").boolValue = openWithKey;
        serialized.FindProperty("generalTabContent").objectReferenceValue = generalTab.gameObject;
        serialized.FindProperty("controlsTabContent").objectReferenceValue = controlsTab.gameObject;
        serialized.FindProperty("generalTabButton").objectReferenceValue = generalTabButton;
        serialized.FindProperty("controlsTabButton").objectReferenceValue = controlsTabButton;

        SerializedProperty labelsProperty = serialized.FindProperty("controlKeyLabels");
        labelsProperty.arraySize = controlKeyLabels.Count;
        for (int i = 0; i < controlKeyLabels.Count; i++)
            labelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = controlKeyLabels[i];

        SerializedProperty idsProperty = serialized.FindProperty("controlActionIds");
        idsProperty.arraySize = controlActionIds.Count;
        for (int i = 0; i < controlActionIds.Count; i++)
            idsProperty.GetArrayElementAtIndex(i).stringValue = controlActionIds[i];

        SerializedProperty buttonsProperty = serialized.FindProperty("controlRebindButtons");
        buttonsProperty.arraySize = controlRebindButtons.Count;
        for (int i = 0; i < controlRebindButtons.Count; i++)
            buttonsProperty.GetArrayElementAtIndex(i).objectReferenceValue = controlRebindButtons[i];

        serialized.FindProperty("controlsNoticeLabel").objectReferenceValue = controlsNotice;
        serialized.FindProperty("controlsResetButton").objectReferenceValue = controlsReset;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 화면은 꺼진 채로 시작한다. 켜둔 채 저장하면 게임을 켜자마자 설정이 떠 있다.
        // 끄는 것은 Root뿐이다. 껍데기는 켜둬야 키 입력을 듣는다.
        root.gameObject.SetActive(false);

        return screen;
    }

    /// <summary>
    /// 조작 탭의 내용을 <see cref="InputCatalog"/>에서 만들어 넣는다.
    ///
    /// 줄을 손으로 안 적고 표에서 뽑는 이유: 손으로 적으면 키를 바꿨을 때 이 화면만
    /// 옛날 값을 말하게 된다. 그 어긋남은 에러도 경고도 없이 <b>플레이어가 안내대로
    /// 눌러보고 안 될 때</b>만 드러난다.
    ///
    /// 실행 중이 아니라 여기(에디터)에서 만드는 이유는 이 프로젝트의 다른 UI와 같다 —
    /// 씬에 실물로 남아야 위치를 눈으로 맞출 수 있고, 매번 실행해서 확인하지 않아도 된다.
    /// </summary>
    private static (TMPro.TMP_Text notice, Button reset) BuildControlsTab(
        RectTransform tab,
        List<TMPro.TMP_Text> keyLabels,
        List<string> actionIds,
        List<Button> rebindButtons)
    {
        float y = ControlsStartY;
        string lastGroup = null;

        foreach (InputCatalog.Entry entry in InputCatalog.Entries)
        {
            // 묶음이 바뀔 때만 소제목을 넣는다. 표의 순서가 곧 화면의 순서다.
            if (entry.Group != lastGroup)
            {
                CreateHeader(tab, "Group_" + entry.Group, entry.Group,
                             y, ControlsHeaderHeight, 22f, EmberColor);
                y += ControlsHeaderPitch;
                lastGroup = entry.Group;
            }

            RectTransform row = CreateRow(tab, "Row_" + entry.Name, y, ControlsRowHeight);

            CreateText(Sub(row, "Name", 0f, 0.40f), entry.Name, 19f,
                       TMPro.TextAlignmentOptions.Left, TextColor);

            // 바꿀 수 없는 키는 흐리게 하고 이유를 붙인다. 색만 다르면 "고장인가"와 구별이 안 된다.
            string keyText = entry.Rebindable ? entry.KeyText : entry.KeyText + "  (고정)";

            TMPro.TMP_Text keyLabel =
                CreateText(Sub(row, "Key", 0.42f, 0.74f), keyText, 19f,
                           TMPro.TextAlignmentOptions.Right,
                           entry.Rebindable ? TextColor : DimTextColor);

            // 실행 중에 다시 읽고 버튼을 붙일 줄만 등록한다.
            //
            // 빠지는 둘: 이동은 키 네 개가 한 조작(복합 바인딩)이라 한 자리를 받는 방식으로는
            // 못 바꾸고, ESC는 바꾸면 창에서 못 나오게 될 수 있어 일부러 고정이다.
            // 그래서 이 두 줄에는 버튼이 안 붙고, 그 사실을 아래 안내가 말해준다.
            if (entry.Rebindable && !entry.HasManualText && !string.IsNullOrEmpty(entry.ActionId))
            {
                keyLabels.Add(keyLabel);
                actionIds.Add(entry.ActionId);
                rebindButtons.Add(CreateButton(Sub(row, "Rebind", 0.78f, 1f), "바꾸기", 16f));
            }

            y += ControlsRowPitch;
        }

        TMPro.TMP_Text notice = CreateHeader(tab, "ControlsNotice", string.Empty,
                                             y + 8f, 30f, 17f, DimTextColor);

        // 조작만 되돌리는 버튼. 일반 탭의 '기본값으로'와 따로 둔 이유는 되돌리는 대상이
        // 다르기 때문이다 — 볼륨을 되돌리려다 키까지 날아가면 되돌릴 수 없는 손해가 된다.
        RectTransform resetRow = CreateRow(tab, "ControlsResetRow", 684f, 48f);
        Button reset = CreateButton(Sub(resetRow, "ControlsResetButton", 0f, 0.34f),
                                    "조작 기본값으로", 20f);

        return (notice, reset);
    }

    /// <summary>볼륨 한 줄(이름 + 슬라이더 + 퍼센트)을 만든다.</summary>
    private static (Slider slider, TMPro.TMP_Text value) CreateSliderRow(
        RectTransform panel, string name, string label, float y)
    {
        RectTransform row = CreateRow(panel, name + "Row", y, RowHeight);

        CreateText(Sub(row, "Label", 0f, 0.26f), label, 26f,
                   TMPro.TextAlignmentOptions.Left, TextColor);

        Slider slider = CreateSlider(Sub(row, "Slider", 0.28f, 0.80f));

        TMPro.TMP_Text value = CreateText(Sub(row, "Value", 0.83f, 1f), "0%", 24f,
                                          TMPro.TextAlignmentOptions.Right, DimTextColor);

        return (slider, value);
    }

    /// <summary>
    /// 전체화면 한 줄(이름 + 체크박스 + 켜짐/꺼짐)을 만든다.
    ///
    /// 네모 칸 하나만 두지 않고 옆에 글자를 붙이는 이유: 색만 채워진 네모는 "켜짐"인지
    /// "고를 수 있음"인지 구별이 안 된다. 실제로 화면을 처음 본 사람이 저게 뭐냐고 물었다.
    /// </summary>
    private static (Toggle toggle, TMPro.TMP_Text state) CreateToggleRow(
        RectTransform panel, string name, string label, float y)
    {
        RectTransform row = CreateRow(panel, name + "Row", y, RowHeight);

        CreateText(Sub(row, "Label", 0f, 0.26f), label, 26f,
                   TMPro.TextAlignmentOptions.Left, TextColor);

        Toggle toggle = CreateToggle(Sub(row, "Toggle", 0.28f, 0.34f));

        TMPro.TMP_Text state = CreateText(Sub(row, "State", 0.37f, 0.73f), "켜짐", 24f,
                                          TMPro.TextAlignmentOptions.Left, DimTextColor);

        return (toggle, state);
    }

    /// <summary>창 크기 한 줄(이름 + ◀ + 문구 + ▶)을 만든다.</summary>
    private static (TMPro.TMP_Text label, Button prev, Button next) CreateScaleRow(
        RectTransform panel, string name, string label, float y)
    {
        RectTransform row = CreateRow(panel, name + "Row", y, RowHeight);

        CreateText(Sub(row, "Label", 0f, 0.26f), label, 26f,
                   TMPro.TextAlignmentOptions.Left, TextColor);

        // 드롭다운이 아니라 화살표 두 개로 만든 이유:
        // 고를 값이 셋뿐이라 목록을 펼칠 이유가 없고, TMP_Dropdown은 템플릿·아이템·스크롤까지
        // 만들어야 해서 코드가 몇 배로 는다. 화살표는 나중에 게임패드로도 그대로 쓸 수 있다.
        Button prev = CreateButton(Sub(row, "Prev", 0.28f, 0.35f), PrevArrow, 24f);
        TMPro.TMP_Text scaleLabel = CreateText(Sub(row, "Value", 0.37f, 0.73f), "-", 24f,
                                               TMPro.TextAlignmentOptions.Center, TextColor);
        Button next = CreateButton(Sub(row, "Next", 0.75f, 0.82f), NextArrow, 24f);

        return (scaleLabel, prev, next);
    }

    // ── 타이틀 설정 버튼 ──────────────────────────────────────────────────

    /// <summary>
    /// 타이틀 화면 구석의 '설정' 버튼을 만들고 설정 화면에 연결한다.
    ///
    /// 버튼의 OnClick 목록이 아니라 <see cref="SettingsScreen"/>의 openButton 필드로 잇는 이유:
    /// 인스펙터 OnClick은 대상 함수 이름을 바꾸면 조용히 끊긴다. 에러도 경고도 없이
    /// 버튼만 안 먹는다. 필드로 넘겨서 코드가 연결하면 컴파일 때 걸린다.
    /// </summary>
    private static void BuildTitleButton(Transform canvas, SettingsScreen settings)
    {
        RectTransform rect = NewRect(TitleButtonName, canvas);

        // 오른쪽 아래. 타이틀 아트 가운데를 가리지 않는 자리다.
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-64f, 64f);
        rect.sizeDelta = new Vector2(240f, 72f);

        Button button = CreateButton(rect, "설정", 26f);

        var serialized = new SerializedObject(settings);
        serialized.FindProperty("openButton").objectReferenceValue = button;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── 조립 도구 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 테두리가 있는 패널을 만든다. 반환값은 <b>안쪽</b> 사각형이라 자식은 여기에 붙인다.
    ///
    /// 이미지 두 장(테두리색 바탕 + 안쪽 어두운 판)으로 만드는 이유: 9-slice 스프라이트가
    /// 아직 없어서다. 나중에 패널 아트가 나오면 바깥 이미지의 sprite만 갈아 끼우면 된다.
    /// </summary>
    private static RectTransform CreatePanel(RectTransform parent, float width, float height)
    {
        RectTransform border = NewRect("Panel", parent);
        border.anchorMin = border.anchorMax = border.pivot = new Vector2(0.5f, 0.5f);
        border.anchoredPosition = Vector2.zero;
        border.sizeDelta = new Vector2(width, height);
        border.gameObject.AddComponent<Image>().color = BorderColor;

        RectTransform inner = NewRect("Inner", border);
        Stretch(inner);
        inner.offsetMin = new Vector2(4f, 4f);
        inner.offsetMax = new Vector2(-4f, -4f);
        inner.gameObject.AddComponent<Image>().color = PanelColor;

        return inner;
    }

    /// <summary>
    /// 패널 위에서 <paramref name="y"/>만큼 내려온 자리에 가로로 꽉 찬 줄을 만든다.
    ///
    /// 위(top)를 기준으로 잡는 이유: 항목을 위에서 아래로 쌓기 때문에, 위에서 잰 거리로
    /// 적어두면 중간에 항목을 하나 끼워 넣어도 아래 값만 밀면 된다. 가운데 기준으로 잡으면
    /// 항목 하나가 늘 때마다 전부 다시 계산해야 한다.
    /// </summary>
    private static RectTransform CreateRow(RectTransform panel, string name, float y, float height)
    {
        RectTransform row = NewRect(name, panel);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.offsetMin = new Vector2(RowMargin, -(y + height));
        row.offsetMax = new Vector2(-RowMargin, -y);
        return row;
    }

    /// <summary>줄 안에서 가로 비율로 자리를 잡는다. 0이 왼쪽 끝, 1이 오른쪽 끝.</summary>
    private static RectTransform Sub(RectTransform row, string name, float xMin, float xMax)
    {
        RectTransform rect = NewRect(name, row);
        rect.anchorMin = new Vector2(xMin, 0f);
        rect.anchorMax = new Vector2(xMax, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    /// <summary>제목·소제목처럼 줄 하나를 통째로 쓰는 글자를 만든다.</summary>
    private static TMPro.TMP_Text CreateHeader(RectTransform panel, string name, string text,
                                               float y, float height, float size, Color color)
    {
        RectTransform row = CreateRow(panel, name, y, height);
        return CreateText(row, text, size, TMPro.TextAlignmentOptions.Left, color);
    }

    /// <summary>글자를 만든다. raycastTarget을 끄는 이유는 아래 버튼의 클릭을 안 가로채기 위해서다.</summary>
    private static TMPro.TMP_Text CreateText(RectTransform rect, string text, float size,
                                             TMPro.TextAlignmentOptions align, Color color)
    {
        var label = rect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null) label.font = font;

        label.text = text;
        label.fontSize = size;
        label.alignment = align;
        label.color = color;
        label.raycastTarget = false;

        // 서식 태그를 끈다. 이 화면의 글자는 전부 평범한 문장이고, 켜두면 화살표로 쓰는
        // '<'를 TMP가 태그 시작으로 읽어서 글자가 통째로 사라질 수 있다.
        label.richText = false;

        return label;
    }

    /// <summary>버튼을 만든다. 글자는 자식으로 들어간다.</summary>
    private static Button CreateButton(RectTransform rect, string label, float fontSize)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = ButtonColor;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        // 눌린 상태와 잠긴 상태를 색으로 구분한다. 잠긴 버튼이 멀쩡해 보이면 고장으로 읽힌다.
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.4f, 1.2f, 1.1f, 1f);
        colors.pressedColor = new Color(0.7f, 0.6f, 0.55f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        button.colors = colors;

        RectTransform labelRect = NewRect("Label", rect);
        Stretch(labelRect);
        CreateText(labelRect, label, fontSize, TMPro.TextAlignmentOptions.Center, TextColor);

        return button;
    }

    /// <summary>
    /// 슬라이더를 만든다. uGUI가 요구하는 세 조각(배경 / 채움 / 손잡이)을 코드로 갖춘다.
    ///
    /// 앵커를 대충 잡아도 되는 이유: <see cref="Slider"/>가 실행 중에 채움과 손잡이의
    /// 앵커를 값에 맞춰 직접 덮어쓴다. 우리가 정할 것은 크기와 색뿐이다.
    /// </summary>
    private static Slider CreateSlider(RectTransform rect)
    {
        var slider = rect.gameObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        RectTransform background = NewRect("Background", rect);
        background.anchorMin = new Vector2(0f, 0.5f);
        background.anchorMax = new Vector2(1f, 0.5f);
        background.anchoredPosition = Vector2.zero;
        background.sizeDelta = new Vector2(0f, 14f);
        background.gameObject.AddComponent<Image>().color = TrackColor;

        RectTransform fillArea = NewRect("Fill Area", rect);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.anchoredPosition = Vector2.zero;
        fillArea.sizeDelta = new Vector2(-20f, 14f);

        RectTransform fill = NewRect("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        fill.sizeDelta = new Vector2(10f, 0f);
        var fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = EmberColor;

        // 손잡이가 움직이는 영역. <b>줄 전체 높이가 아니라 가운데 띠로 잡는다.</b>
        //
        // 처음에 줄 전체(위아래로 꽉 찬 사각형)로 잡았더니 손잡이가 줄 밖으로 튀어나왔다.
        // 원인은 sizeDelta를 "크기"로 착각한 것 — 위아래 앵커가 벌어져 있으면 sizeDelta는
        // 크기가 아니라 <b>앵커 대비 증감분</b>이라, 40을 주면 줄 높이(64) + 40 = 104가 된다.
        // 앵커를 세로 가운데(0.5, 0.5)로 모으면 sizeDelta가 그대로 실제 높이가 된다.
        RectTransform handleArea = NewRect("Handle Slide Area", rect);
        handleArea.anchorMin = new Vector2(0f, 0.5f);
        handleArea.anchorMax = new Vector2(1f, 0.5f);
        handleArea.anchoredPosition = Vector2.zero;
        handleArea.sizeDelta = new Vector2(-20f, HandleHeight);

        RectTransform handle = NewRect("Handle", handleArea);

        // 세로는 0. Slider가 실행 중에 손잡이의 세로 앵커를 (0)~(1)로 덮어쓰므로,
        // 여기서 0을 주면 손잡이 높이가 위 영역의 높이(HandleHeight)와 같아진다.
        handle.sizeDelta = new Vector2(HandleWidth, 0f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = TextColor;

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;

        return slider;
    }

    /// <summary>
    /// 체크박스를 만든다.
    ///
    /// Toggle을 줄 전체가 아니라 <b>네모 칸에</b> 붙이는 이유: 줄에 붙이면 이름 글자 옆의
    /// 빈 공간을 눌러도 값이 바뀐다. 눌릴 곳이 눈에 보이는 것과 같아야 한다.
    /// </summary>
    private static Toggle CreateToggle(RectTransform rect)
    {
        RectTransform box = NewRect("Box", rect);
        box.anchorMin = new Vector2(0f, 0.5f);
        box.anchorMax = new Vector2(0f, 0.5f);
        box.pivot = new Vector2(0f, 0.5f);
        box.anchoredPosition = Vector2.zero;
        box.sizeDelta = new Vector2(CheckBoxSize, CheckBoxSize);

        var boxImage = box.gameObject.AddComponent<Image>();
        boxImage.color = ButtonColor;

        RectTransform check = NewRect("Checkmark", box);
        Stretch(check);
        check.offsetMin = new Vector2(7f, 7f);
        check.offsetMax = new Vector2(-7f, -7f);
        var checkImage = check.gameObject.AddComponent<Image>();
        checkImage.color = EmberColor;

        var toggle = box.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = boxImage;
        toggle.graphic = checkImage;
        toggle.isOn = true;

        return toggle;
    }

    // ── 씬 준비 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 씬 어디에 있든 이 이름의 오브젝트를 지운다.
    ///
    /// 꺼진 오브젝트까지 찾아야 한다. 설정 화면의 Root는 꺼진 채로 저장되므로,
    /// 활성 오브젝트만 뒤지는 방법으로는 절반만 지워진다.
    /// </summary>
    private static void DestroyByName(string name)
    {
        foreach (var transform in Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // 부모를 지우면 자식도 같이 사라진다. 이미 사라진 것은 유니티의 null 검사에 걸린다.
            if (transform == null) continue;
            if (transform.name != name) continue;

            Object.DestroyImmediate(transform.gameObject);
        }
    }

    /// <summary>설정 화면만 쓰는 캔버스를 새로 만든다.</summary>
    private static Canvas CreateSettingsCanvas()
    {
        DestroyByName(SettingsCanvasName);

        var canvasObject = new GameObject(SettingsCanvasName, typeof(Canvas), typeof(CanvasScaler),
                                          typeof(GraphicRaycaster));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // 정렬 순서를 직접 잡는다. 형제 순서에 기대면 다른 도구가 오브젝트를 만들 때마다
        // 위아래가 바뀔 수 있는데, sortingOrder는 캔버스끼리의 약속이라 흔들리지 않는다.
        // (overrideSorting은 캔버스 안에 캔버스를 넣을 때만 쓰는 값이라 여기서는 필요 없다.)
        canvas.sortingOrder = SettingsSortingOrder;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;

        return canvas;
    }

    /// <summary>
    /// 클릭을 받으려면 씬에 EventSystem이 있어야 한다.
    ///
    /// 없어도 에러가 안 나고 그냥 클릭이 안 먹는다. 원인을 찾기 어려운 종류라 확인한다.
    /// </summary>
    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include) != null) return;

        var systemObject = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        Debug.Log($"[설정 화면] EventSystem이 없어서 만들었다: {systemObject.name}");
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
