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

    [Header("키 입력 — UI 버튼이 없어도 흐름을 확인할 수 있게")]
    [SerializeField] private Key restartKey = Key.R;
    [SerializeField] private Key titleKey = Key.Escape;

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[restartKey].wasPressedThisFrame) OnRestart();
        else if (keyboard[titleKey].wasPressedThisFrame) OnTitle();
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

        ApplyBackground();

        // UI가 아직 없을 때도 값이 넘어왔는지 확인할 수 있게 남긴다. UI가 붙으면 지운다.
        Debug.Log($"[결과] 생존 {result.FormatSurvivedTime()} / 처치 {result.KillCount} / " +
                  $"{(result.Cleared ? "클리어" : "사망")}");
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
