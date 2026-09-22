using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-09-20, 보스 시전 바) — 보스가 무엇을 준비하는지 알려주는 바.
///
/// <b>알리기만 하는 바다.</b> 기획에서 "저지 가능"이 아니라 "알림만"으로 정했으므로, 이 바는
/// 플레이어가 때려서 멈출 수 있는 게이지가 아니다. 하는 일은 하나다 — <b>지금 뭐가 오는지와
/// 언제 오는지</b>를 숫자 없이 보여준다. 그래서 채움이 끝나는 순간이 곧 공격이 나가는 순간이다.
///
/// <see cref="BossHealthBar"/>와 규칙을 맞췄다. 스스로 대상을 찾지 않고 <see cref="BossEncounter"/>가
/// 보스의 신호를 받아 <see cref="Show"/>·<see cref="Hide"/>로 넘겨준다. 보스가 HUD를 직접 찾으면
/// HUD가 없는 테스트 씬에서 보스 쪽이 경고를 뱉게 되기 때문이다.
///
/// <b>시간은 게임 시간(스케일 적용)으로 센다.</b> 보스의 시전은 코루틴의 <c>WaitForSeconds</c>로
/// 흐르는데 그건 스케일 시간이다. 여기만 실제 시간으로 세면 인벤토리를 열어 게임이 멈춘 동안
/// 바만 계속 차서, 바가 가득 찼는데 공격이 안 나오는 모양이 된다. 나타남·사라짐만 실제 시간이다
/// (멈춘 화면에서 반투명인 채로 굳지 않게).
/// </summary>
[DisallowMultipleComponent]
public class BossCastBar : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("바 전체를 감싸는 그룹. 나타나고 사라지는 연출에 쓴다.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("채움 이미지. fillAmount가 진행도가 된다.")]
    [SerializeField] private Image fillImage;

    [Tooltip("채움 사각형. 채움 스프라이트가 없을 때의 대비책이다.")]
    [SerializeField] private RectTransform fillRect;

    [Tooltip("패턴 이름표. 보스가 넘겨준 이름을 그대로 쓴다.")]
    [SerializeField] private TMP_Text nameLabel;

    [Header("연출")]
    [Tooltip("나타나고 사라지는 데 걸리는 시간(초). 체력바보다 짧게 둔다 — 시전은 1초 남짓이라 " +
             "느리게 뜨면 다 뜨기도 전에 공격이 나간다.")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.12f;

    [Tooltip("시전이 끝난 뒤 바가 가득 찬 채로 남아 있는 시간(초). 0이면 즉시 사라진다. " +
             "짧게 남겨야 '다 찼고, 그래서 공격이 나왔다'가 눈으로 이어진다.")]
    [SerializeField, Min(0f)] private float holdSeconds = 0.12f;

    // 지금 시전 중인가. 이 값이 켜져 있는 동안 진행도가 흐른다.
    private bool casting;

    // 시전 전체 길이와 지난 시간(초). 둘의 비가 곧 채움이다.
    private float castSeconds;
    private float elapsed;

    // 지금 보여야 하는가. 알파가 이 값을 따라간다.
    private bool visible;

    // 가득 찬 채로 남겨두는 시간이 얼마나 남았는가.
    private float holdTimer;

    private void Awake()
    {
        // 체력바와 같은 이유로 Image 설정을 코드에서 강제한다. 씬에 Simple로 저장돼 있으면
        // fillAmount를 아무리 넣어도 그림이 안 변하고, 에러도 경고도 안 난다.
        ConfigureFillImage();

        casting = false;
        visible = false;
        elapsed = 0f;

        if (group != null)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }
    }

    private void ConfigureFillImage()
    {
        if (fillImage == null || fillImage.sprite == null) return;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.preserveAspect = false;
    }

    /// <summary>
    /// 시전을 알린다. 보스가 예비동작을 시작할 때 방(<see cref="BossEncounter"/>)을 거쳐 불린다.
    /// </summary>
    /// <param name="patternName">바에 적을 패턴 이름(예: "재의 창").</param>
    /// <param name="seconds">시전 길이(초). 이 시간에 걸쳐 바가 채워진다.</param>
    public void Show(string patternName, float seconds)
    {
        castSeconds = Mathf.Max(0.01f, seconds);
        elapsed = 0f;
        casting = true;
        visible = true;
        holdTimer = 0f;

        if (nameLabel != null) nameLabel.text = patternName;

        ApplyFill(0f);
    }

    /// <summary>
    /// 시전이 끝났음을 알린다. 바를 가득 채운 뒤 잠깐 두고 사라진다.
    ///
    /// 보스가 죽거나 방이 끝나서 부를 수도 있다. 그때는 덜 찬 바가 그 자리에서 사라지는데,
    /// 그게 맞다 — 공격이 안 나왔다는 뜻이기 때문이다.
    /// </summary>
    public void Hide()
    {
        if (!casting && !visible) return;

        casting = false;
        holdTimer = holdSeconds;
    }

    /// <summary>방이 끝날 때. 남은 연출 없이 즉시 감춘다.</summary>
    public void HideImmediate()
    {
        casting = false;
        visible = false;
        holdTimer = 0f;
        elapsed = 0f;

        ApplyFill(0f);
        if (group != null) group.alpha = 0f;
    }

    private void OnDisable()
    {
        // 꺼졌다 켜졌을 때 옛 시전이 이어지면, 아무도 안 쏘는 바가 혼자 차오른다.
        casting = false;
        visible = false;
        holdTimer = 0f;
    }

    private void Update()
    {
        if (casting)
        {
            elapsed += Time.deltaTime;

            // 끝까지 찼는데 보스가 아직 Hide를 안 불렀으면 가득 찬 채로 기다린다.
            // 여기서 스스로 숨으면, 발사가 한 프레임 늦어졌을 때 바가 먼저 사라져서
            // "다 찼는데 아무 일도 안 일어난" 것처럼 보인다.
            ApplyFill(Mathf.Clamp01(elapsed / castSeconds));
        }
        else if (visible)
        {
            // 시전이 끝났다. 잠깐 두고 사라진다.
            if (holdTimer > 0f) holdTimer -= Time.deltaTime;
            else visible = false;
        }

        ApplyFade();
    }

    private void ApplyFill(float ratio)
    {
        if (fillImage != null && fillImage.sprite != null)
        {
            fillImage.fillAmount = Mathf.Clamp01(ratio);
            return;
        }

        if (fillRect == null) return;

        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    /// <summary>나타남·사라짐. 체력바와 같은 이유로 실제 시간을 쓴다.</summary>
    private void ApplyFade()
    {
        if (group == null) return;

        float goal = visible ? 1f : 0f;

        if (fadeSeconds <= 0f)
        {
            group.alpha = goal;
            return;
        }

        group.alpha = Mathf.MoveTowards(group.alpha, goal, Time.unscaledDeltaTime / fadeSeconds);
    }
}
