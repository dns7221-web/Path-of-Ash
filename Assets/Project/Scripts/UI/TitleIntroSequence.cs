using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 타이틀 등장 연출. 암전 → 배경 → 제목 → 안내 순으로 켠다. (기획 D1)
///
/// <b>재진입이면 통째로 건너뛴다 (D2).</b> 이게 D1보다 중요한 판단이다 — 등장 연출은 처음
/// 한 번만 좋다. 죽고 돌아올 때마다 같은 연출을 다시 보게 하면 그 순간부터 연출이 아니라
/// 장애물이 된다. 개발 중에도 Play를 누를 때마다 2초를 기다리게 되어 제일 먼저 거슬린다.
///
/// 재진입 판단에 <see cref="RunResultData"/>의 방 수를 쓴 이유: "한 판이라도 했는가"를
/// 이미 기록하고 있어서 새 전역 상태를 만들 필요가 없다. static 필드나 DontDestroyOnLoad를
/// 더하면 이 프로젝트가 피해온 전역 상태가 하나 생긴다.
///
/// <b>연출 중 아무 키나 누르면 즉시 완료된다 (D4).</b> 같은 이유다. 기다리게 하지 않는다.
///
/// 배경만 SpriteRenderer이고 나머지는 UI라 페이드 방식이 다르다. 배경은 색의 알파를,
/// UI는 CanvasGroup의 알파를 건드린다.
/// </summary>
[DisallowMultipleComponent]
public class TitleIntroSequence : MonoBehaviour
{
    [Header("페이드 대상")]
    [Tooltip("배경 스프라이트. 비우면 배경 페이드를 건너뛴다.")]
    [SerializeField] private SpriteRenderer background;

    [Tooltip("제목 텍스트의 CanvasGroup.")]
    [SerializeField] private CanvasGroup titleGroup;

    [Tooltip("안내 문구의 CanvasGroup.")]
    [SerializeField] private CanvasGroup promptGroup;

    [Header("참조")]
    [Tooltip("재진입 판단용. 비우면 항상 연출을 재생한다.")]
    [SerializeField] private RunResultData result;

    [Tooltip("연출 중에는 꺼둔다. 안 그러면 스킵하려고 누른 키로 게임이 바로 시작된다.")]
    [SerializeField] private TitleScreen titleScreen;

    [Header("시간(초)")]
    [SerializeField, Min(0f)] private float blackHold = 0.4f;
    [SerializeField, Min(0.05f)] private float backgroundFade = 1.2f;
    [SerializeField, Min(0f)] private float titleDelay = 0.3f;
    [SerializeField, Min(0.05f)] private float titleFade = 0.9f;
    [SerializeField, Min(0f)] private float promptDelay = 0.4f;
    [SerializeField, Min(0.05f)] private float promptFade = 0.6f;

    // 연출이 도는 중인가. 이 동안에만 스킵 입력을 받는다.
    private bool playing;

    private void Awake()
    {
        if (titleScreen == null) titleScreen = FindFirstObjectByType<TitleScreen>();
    }

    private void Start()
    {
        // 한 판이라도 했으면 재진입이다. 연출을 아예 재생하지 않는다.
        bool replay = result != null && result.RoomsEntered > 0;

        if (replay)
        {
            CompleteInstantly();
            return;
        }

        StartCoroutine(PlayIntro());
    }

    private void Update()
    {
        if (!playing) return;

        // 아무 입력이나 받으면 즉시 완료. 게임이 시작되지는 않는다 —
        // TitleScreen이 꺼져 있어서 이 입력은 스킵으로만 쓰인다.
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        bool pressed = (keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                       (mouse != null && mouse.leftButton.wasPressedThisFrame);

        if (pressed) CompleteInstantly();
    }

    private IEnumerator PlayIntro()
    {
        playing = true;

        // 연출 중에는 시작 입력을 막는다. 스킵하려고 누른 키가 그대로 게임 시작으로
        // 이어지면 "건너뛰려다 판이 시작되는" 사고가 난다.
        if (titleScreen != null) titleScreen.enabled = false;

        SetBackgroundAlpha(0f);
        SetGroupAlpha(titleGroup, 0f);
        SetGroupAlpha(promptGroup, 0f);

        yield return new WaitForSeconds(blackHold);

        yield return Fade(a => SetBackgroundAlpha(a), backgroundFade);
        yield return new WaitForSeconds(titleDelay);

        yield return Fade(a => SetGroupAlpha(titleGroup, a), titleFade);
        yield return new WaitForSeconds(promptDelay);

        yield return Fade(a => SetGroupAlpha(promptGroup, a), promptFade);

        CompleteInstantly();
    }

    /// <summary>
    /// 연출을 끝난 상태로 만든다. 재진입(D2)과 스킵(D4)이 같은 함수를 쓴다 —
    /// "끝난 상태"의 정의가 한 군데에만 있어야 둘이 어긋나지 않는다.
    /// </summary>
    private void CompleteInstantly()
    {
        StopAllCoroutines();
        playing = false;

        SetBackgroundAlpha(1f);
        SetGroupAlpha(titleGroup, 1f);
        SetGroupAlpha(promptGroup, 1f);

        if (titleScreen != null) titleScreen.enabled = true;
    }

    /// <summary>0에서 1까지 알파를 올린다.</summary>
    private IEnumerator Fade(System.Action<float> apply, float seconds)
    {
        float elapsed = 0f;

        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            apply(Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }

        apply(1f);
    }

    private void SetBackgroundAlpha(float alpha)
    {
        if (background == null) return;

        Color color = background.color;
        color.a = alpha;
        background.color = color;
    }

    private static void SetGroupAlpha(CanvasGroup group, float alpha)
    {
        if (group != null) group.alpha = alpha;
    }
}
