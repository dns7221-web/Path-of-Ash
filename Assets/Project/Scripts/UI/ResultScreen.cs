using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 결과 화면. RunResultData에 기록된 값을 읽어 표시하고, 재시작/타이틀로 보낸다.
///
/// 이 컴포넌트는 값을 계산하지 않는다. 계산은 RunManager가 끝냈고 여기서는 읽어서 보여주기만
/// 한다. 화면이 값을 만들기 시작하면 나중에 "리절트에 뜨는 숫자와 실제 기록이 다르다"는
/// 문제가 생기고, 그때 어느 쪽이 맞는지 판단할 근거가 없어진다.
///
/// 참조는 비어 있어도 동작한다. UI를 만들기 전에도 키보드로 흐름을 확인할 수 있어야 하기
/// 때문이다.
/// </summary>
public class ResultScreen : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("RunManager가 결과를 기록해둔 에셋. 같은 에셋을 연결해야 한다.")]
    [SerializeField] private RunResultData result;

    [Header("표시 (없어도 동작한다)")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text survivedText;
    [SerializeField] private TMP_Text killText;

    // 추가 생성 — 방을 무한 반복하는 구조라 "몇 번째 방까지 갔는가"가 곧 이번 판의 성적이다.
    // 생존 시간만으로는 구석에서 버틴 판과 계속 전진한 판이 구분되지 않는다.
    [Tooltip("도달한 방 수. 비워두면 표시하지 않는다.")]
    [SerializeField] private TMP_Text roomText;

    // 추가 생성: 죽었을 때와 탈출했을 때 배경을 바꿔 끼우기 위한 자리.
    //
    // 씬을 둘로 나누지 않은 이유: 결과 화면의 구조(텍스트 위치, 키 입력, 흐름)가 완전히
    // 같아서 씬을 복제하면 한쪽만 고치는 실수가 생긴다. 바뀌는 건 배경 한 장과 제목 문구뿐이라
    // 이쪽이 훨씬 싸다.
    [Header("배경 (클리어 이미지는 나중에 채운다)")]
    [Tooltip("배경을 그리는 SpriteRenderer. 비워두면 배경 교체를 하지 않는다.")]
    [SerializeField] private SpriteRenderer background;
    [Tooltip("죽어서 끝났을 때의 배경.")]
    [SerializeField] private Sprite deathBackground;
    [Tooltip("클리어했을 때의 배경. 아직 없으면 비워둔다 — 그러면 사망 배경을 그대로 쓴다.")]
    [SerializeField] private Sprite clearedBackground;

    // 추가 생성 — 아래쪽 안내 문구("R 다시 내려간다  ESC 타이틀").
    //
    // 키를 글자로 적어두면 플레이어가 재시작 키를 바꿨을 때 이 문구만 옛 키를 말한다.
    // 실제 바인딩에서 읽어 채운다.
    [Tooltip("아래쪽 조작 안내. 비워두면 이름으로 찾는다(PressKeyText).")]
    [SerializeField] private TMP_Text pressKeyText;

    [Header("키 입력 — UI 버튼이 없어도 흐름을 확인할 수 있게")]

    // 수정(입력 중앙화): 재시작 액션도 InputBindings로 옮겼다. 설정의 조작 탭에서
    // '결과 화면 - 다시 시작'으로 바꿀 수 있다. 스킬 4와 같은 R이지만 쓰이는 화면이 달라
    // 겹침 검사에서 서로를 지우지 않는다.
    //
    // 재시작만 옮기고 타이틀(ESC)은 그대로 둔 이유: ESC는 <b>바꿀 수 없는 키</b>다. 이 게임에서 ESC는 화면을
    // 닫고 빠져나오는 키라, 그걸 다른 데로 옮길 수 있게 하면 창에서 못 나오는 상태를
    // 플레이어가 스스로 만들 수 있다. 바꿀 수 없는 키를 리바인딩 목록에 올리는 것은
    // 고를 수 없는 선택지를 보여주는 것과 같아서, 아예 성격이 다른 입력으로 남긴다.
    [Tooltip("타이틀로 나가는 키. 설정에서 바꾸지 않는 고정 키다.")]
    [SerializeField] private Key titleKey = Key.Escape;

    private void Awake()
    {
        // 인스펙터에 안 꽂혀 있어도 찾는다. 이 문구는 씬에 이미 놓여 있고, 새로 만든
        // 필드라 연결이 비어 있을 수밖에 없다. 이름으로 찾는 것은 타이틀 연출 도구와 같은 방식이다.
        if (pressKeyText == null)
        {
            var found = GameObject.Find("PressKeyText");
            if (found != null) pressKeyText = found.GetComponent<TMP_Text>();
        }
    }

    private void OnEnable()
    {
        // 설정에서 재시작 키를 바꾸면 안내 문구도 따라가야 한다.
        InputBindings.BindingsChanged += RefreshHint;
        RefreshHint();
    }

    private void OnDisable()
    {
        InputBindings.BindingsChanged -= RefreshHint;
    }

    /// <summary>아래쪽 조작 안내를 지금 바인딩으로 다시 쓴다.</summary>
    private void RefreshHint()
    {
        if (pressKeyText == null) return;

        // ESC는 바꿀 수 없는 고정 키라 글자로 적어도 어긋나지 않는다.
        pressKeyText.text = ControlHintLabel.Fill("{Restart} 다시 내려간다   ESC 타이틀", true);
    }

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        if (InputBindings.RestartAction.WasPressedThisFrame())
        {
            OnRestart();
            return;
        }

        // 타이틀은 고정 키라 예전처럼 키보드를 직접 읽는다.
        // 키보드가 없는 환경(패드만 연결)에서 Keyboard.current가 null일 수 있어 먼저 확인한다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[titleKey].wasPressedThisFrame) OnTitle();
    }

    /// <summary>결과 값을 화면에 반영한다.</summary>
    private void Refresh()
    {
        if (result == null)
        {
            Debug.LogError("[ResultScreen] RunResultData가 비어 있다. 인스펙터에서 에셋을 연결해라.", this);
            return;
        }

        if (titleText != null)
            titleText.text = result.Cleared ? "탈출했다" : "재가 되었다";

        if (survivedText != null)
            survivedText.text = $"생존 {result.FormatSurvivedTime()}";

        if (killText != null)
            killText.text = $"처치 {result.KillCount}";

        // 추가 생성 — 도달한 방 수. 텍스트를 안 붙여뒀으면 조용히 넘어간다.
        if (roomText != null)
            roomText.text = $"{result.RoomsEntered}번째 방";

        ApplyBackground();

        // UI가 아직 없을 때도 값이 넘어왔는지 확인할 수 있게 남긴다. UI가 붙으면 지운다.
        Debug.Log($"[결과] 생존 {result.FormatSurvivedTime()} / 처치 {result.KillCount} / " +
                  $"{result.RoomsEntered}번째 방 / {(result.Cleared ? "클리어" : "사망")}");
    }

    /// <summary>
    /// 추가 생성: 결말에 맞는 배경으로 바꾼다.
    /// 클리어 배경이 아직 없으면 사망 배경을 그대로 쓰므로, 이미지가 한 장뿐인 지금도 문제없다.
    /// </summary>
    private void ApplyBackground()
    {
        if (background == null) return;

        Sprite target = result.Cleared && clearedBackground != null
            ? clearedBackground
            : deathBackground;

        // 인스펙터에서 이미 배경을 넣어둔 상태일 수 있으니, 지정된 게 없으면 건드리지 않는다.
        if (target != null) background.sprite = target;
    }

    /// <summary>재시작. UI 버튼의 OnClick에 연결한다.</summary>
    public void OnRestart()
    {
        GameFlow.StartNewRun();
    }

    /// <summary>타이틀로. UI 버튼의 OnClick에 연결한다.</summary>
    public void OnTitle()
    {
        GameFlow.LoadTitle();
    }
}
