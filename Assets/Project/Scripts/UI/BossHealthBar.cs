using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추가 생성 — 화면 위쪽에 보스 체력을 보여주는 게이지 바.
///
/// <see cref="HealthBar"/>·<see cref="AshGaugeBar"/>와 규칙이 같다. 값을 만들지 않고
/// <see cref="Health"/>가 가진 값을 읽어서 그리기만 한다.
///
/// <b>셋을 하나로 합치지 않은 이유는 대상을 얻는 방법이 서로 다르기 때문이다.</b>
/// 스태미나·재 게이지는 씬에 늘 있는 플레이어를 스스로 찾고, 이 바는 <b>찾을 수가 없다.</b>
/// 보스는 방에 들어가는 순간 <see cref="BossEncounter"/>가 만드는 인스턴스라 씬을 아무리
/// 뒤져도 게임 시작 시점에는 존재하지 않는다. 그래서 이 컴포넌트만 <see cref="Bind"/>로
/// 밖에서 대상을 받는다. 공통 부모로 묶으면 "대상을 어떻게 얻는가"가 추상화 뒤로 숨어서,
/// 바가 안 움직일 때 어디를 봐야 하는지 알기 어려워진다.
///
/// <b>왜 필요한가.</b> 이게 없으면 보스를 얼마나 깎았는지 화면에서 알 수 없고, 2페이즈
/// 전환이 언제 오는지도 못 읽는다. 보스전의 유일한 절정이 <b>예고 없이</b> 터지는 셈이다.
/// 그래서 눈금(<see cref="phaseMarker"/>)으로 전환 지점을 미리 보여준다.
/// </summary>
[DisallowMultipleComponent]
public class BossHealthBar : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("게이지 전체를 감싸는 그룹. 나타나고 사라지는 연출에 쓴다.")]
    [SerializeField] private CanvasGroup group;

    [Tooltip("채움 이미지. 이 컴포넌트의 fillAmount가 게이지 길이가 된다.")]
    [SerializeField] private Image fillImage;

    [Tooltip("채움 사각형. fillAmount를 못 쓸 때(스프라이트가 없을 때)의 대비책이다.")]
    [SerializeField] private RectTransform fillRect;

    [Tooltip("2페이즈 전환 지점을 알리는 눈금. 채움 영역의 자식이어야 위치가 맞는다.")]
    [SerializeField] private RectTransform phaseMarker;

    [Tooltip("보스 이름표. 바 아래에 놓는다.")]
    [SerializeField] private TMP_Text nameLabel;

    [Header("이름")]
    [Tooltip("1페이즈에 보여줄 이름. 이름을 감추는 것이 목적이므로 물음표를 쓴다.")]
    [SerializeField] private string phase1Name = "???";

    [Tooltip("2페이즈에 드러나는 본명.")]
    [SerializeField] private string phase2Name = "재의 길";

    [Header("색")]
    [Tooltip("1페이즈 색. 원본 스프라이트 색을 그대로 쓰려면 흰색으로 둔다.")]
    [SerializeField] private Color phase1Color = Color.white;

    [Tooltip("2페이즈 색. 같은 바인데 다른 상대라는 걸 색으로 알린다.")]
    [SerializeField] private Color phase2Color = new Color(1f, 0.55f, 0.3f, 1f);

    [Header("연출")]
    [Tooltip("게이지가 목표값을 따라가는 속도. 클수록 즉각적이다. 0이면 즉시 반영된다. " +
             "플레이어 체력 바(8)보다 느리게 둔다 — 보스는 최대 체력이 커서 한 대의 폭이 " +
             "작기 때문에, 천천히 흘러야 '깎이고 있다'가 눈에 보인다.")]
    [SerializeField, Min(0f)] private float followSpeed = 5f;

    [Tooltip("나타나고 사라지는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.35f;

    // 지금 보고 있는 보스의 체력. 비어 있으면 바는 숨어 있다.
    private Health health;

    // 화면에 지금 그려지고 있는 비율. 목표값을 향해 따라간다.
    private float displayed = 1f;

    // 체력이 알려준 실제 비율.
    private float target = 1f;

    // 지금 보여야 하는가. 이 값에 따라 group.alpha가 0과 1 사이를 오간다.
    private bool visible;

    // 지금 2페이즈인가. 채움 색을 정할 때 쓴다.
    //
    // 눈금이 켜져 있는지로 알아낼 수도 있지만 그러면 안 된다. 눈금은 <b>2페이즈가 아닌
    // 이유로도</b> 꺼진다 — 전환 비율이 0이나 1이라 그릴 자리가 없을 때, 그리고 눈금
    // 오브젝트가 아예 안 물려 있을 때. 그 경우 아직 1페이즈인데 색만 2페이즈로 나온다.
    // 상태를 상태로 들고 있으면 그런 우연에 기대지 않는다.
    private bool isPhase2;

    private void Awake()
    {
        // 다른 게이지와 같은 이유로 Image 설정을 코드에서 강제한다.
        // Image.fillAmount는 Type이 Filled일 때만 렌더러가 읽는다. 씬에 Simple로 저장돼
        // 있으면 매 프레임 값을 넣어도 그림이 전혀 안 바뀌고, 에러도 경고도 안 난다.
        ConfigureFillImage();

        // 대상이 붙기 전에는 숨어 있어야 한다. 잡몹 방에서 빈 보스 바가 떠 있으면
        // "지금 보스전인가?"를 화면이 잘못 알려주는 셈이다.
        visible = false;
        if (group != null)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }
    }

    /// <summary>전용 스프라이트가 있으면 가로 채움(왼쪽 → 오른쪽)으로 설정한다.</summary>
    private void ConfigureFillImage()
    {
        if (fillImage == null || fillImage.sprite == null) return;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;

        // 켜져 있으면 Image가 스프라이트 원본 비율을 지키려고 사각형 안에서 크기를 줄여서,
        // 게이지가 프레임 구멍 안에서 위아래로 뜨거나 좌우가 남는다.
        fillImage.preserveAspect = false;
    }

    /// <summary>
    /// 보스를 이 바에 물린다. <see cref="BossEncounter"/>가 보스를 만든 직후 부른다.
    ///
    /// 페이즈 비율을 인자로 받는 이유: 그 값의 주인은 <see cref="EnemyBoss"/>다. 여기에
    /// 0.5를 따로 적어두면 보스 쪽 수치를 바꿨을 때 <b>눈금만 옛 자리에 남는다.</b>
    /// 그건 아무 에러도 없이 "전환이 눈금보다 일찍/늦게 온다"로만 나타나서,
    /// 버그가 아니라 밸런스 문제로 오해하기 딱 좋다.
    /// </summary>
    /// <param name="bossHealth">보스의 체력. null이면 아무것도 하지 않는다.</param>
    /// <param name="phase2Ratio">2페이즈로 넘어가는 체력 비율(0~1). 눈금 위치가 된다.</param>
    public void Bind(Health bossHealth, float phase2Ratio)
    {
        if (bossHealth == null)
        {
            Debug.LogWarning("[보스 체력바] 물릴 체력이 비어 있다. 바를 띄우지 않는다.", this);
            return;
        }

        // 이전 대상이 남아 있으면 먼저 끊는다. 방을 다시 열었을 때 옛 보스의 신호가
        // 계속 들어오면, 죽은 보스의 체력이 새 보스의 바를 움직인다.
        Unsubscribe();

        health = bossHealth;
        health.Changed += OnHealthChanged;
        health.Died += OnBossDied;

        // 새 보스는 1페이즈에서 시작한다. 이전 판에서 2페이즈까지 갔다가 다시 들어오면
        // 색과 눈금이 그대로 남아, 아직 1페이즈인 보스가 2페이즈처럼 보인다.
        isPhase2 = false;

        // 등장할 때는 지금 체력에서 시작한다. 0에서 차오르게 하면 "보스가 회복하는 중"으로
        // 보이고, 1에서 떨어지게 하면 시작하자마자 얻어맞은 것처럼 보인다.
        target = Ratio(health.Current, health.Max);
        displayed = target;

        ApplyPhaseMarker(phase2Ratio);
        ApplyFill(displayed);
        ApplyColor();
        ApplyName();

        visible = true;
    }

    /// <summary>
    /// 보스를 떼어낸다. 방이 끝나거나 보스가 사라질 때 <see cref="BossEncounter"/>가 부른다.
    ///
    /// 채움 값을 되돌리지 않는 이유: 여기서 1로 리셋하면 사라지는 동안 <b>바가 다시 가득
    /// 차는 것이 보인다.</b> 다음 <see cref="Bind"/>가 어차피 값을 새로 맞춘다.
    /// </summary>
    public void Unbind()
    {
        Unsubscribe();
        visible = false;
    }

    /// <summary>
    /// 2페이즈에 들어갔음을 바에 알린다. 색이 바뀌고 눈금은 더 이상 의미가 없어 숨는다.
    ///
    /// <see cref="EnemyBoss"/>가 이 함수를 직접 부르지 않고 방(<see cref="BossEncounter"/>)이
    /// 신호를 받아 전달한다. 사망 처리를 그렇게 나눠둔 것과 같은 이유다 — <b>적 스크립트가
    /// 화면 구조를 모르게</b> 한다. 보스가 HUD를 직접 찾으면, HUD가 없는 씬(테스트 방 등)에
    /// 보스를 놓는 순간 경고가 뜨고 보스 쪽 코드를 고치게 된다.
    /// </summary>
    public void MarkPhase2()
    {
        isPhase2 = true;

        if (phaseMarker != null) phaseMarker.gameObject.SetActive(false);

        ApplyColor();
        ApplyName();
    }

    private void OnDisable()
    {
        // 컴포넌트가 꺼질 때 구독을 남겨두면, 다시 켜졌을 때 같은 대상을 두 번 구독하거나
        // 이미 파괴된 Health를 참조한 채로 남는다.
        Unsubscribe();

        // 대상을 잃었으니 보일 이유도 없다. 이걸 안 하면 다시 켜졌을 때 <b>아무도 안 물린
        // 바가 옛 눈금값을 단 채</b> 떠 있고, 체력이 안 변하니 영영 그대로 멈춰 있는다.
        visible = false;
    }

    /// <summary>구독을 끊는다. 대상이 없으면 아무 일도 하지 않는다.</summary>
    private void Unsubscribe()
    {
        if (health == null) return;

        health.Changed -= OnHealthChanged;
        health.Died -= OnBossDied;
        health = null;
    }

    /// <summary>체력이 바뀔 때마다 불린다. 회복도 같은 이벤트로 온다.</summary>
    private void OnHealthChanged(int current, int max)
    {
        target = Ratio(current, max);
    }

    /// <summary>
    /// 보스가 죽었을 때. 바로 숨기지 않고 게이지가 0까지 흘러내리는 것을 보여준다.
    ///
    /// <see cref="BossEncounter"/>가 사망 모션을 위해 1.6초를 기다리므로, 그동안 바가
    /// 비어가는 것이 보인다. 여기서 즉시 숨기면 <b>마지막 한 대가 얼마나 깎았는지</b>
    /// 확인할 새도 없이 사라진다.
    /// </summary>
    private void OnBossDied()
    {
        target = 0f;
    }

    private static float Ratio(int current, int max)
        => max <= 0 ? 0f : Mathf.Clamp01(current / (float)max);

    /// <summary>눈금을 채움 영역 안의 비율 위치로 옮긴다.</summary>
    private void ApplyPhaseMarker(float ratio)
    {
        if (phaseMarker == null) return;

        // 비율이 유효하지 않으면 눈금을 아예 숨긴다. 0이나 1에 붙은 눈금은
        // 장식으로 보일 뿐 아무 정보도 주지 않는다.
        if (ratio <= 0f || ratio >= 1f)
        {
            phaseMarker.gameObject.SetActive(false);
            return;
        }

        phaseMarker.gameObject.SetActive(true);

        // 앵커를 비율 자리에 세로로 세운다. 앵커로 두는 이유는 채움 영역의 크기가
        // 해상도에 따라 달라지기 때문이다 — 픽셀 좌표로 두면 창 크기를 바꿀 때 어긋난다.
        phaseMarker.anchorMin = new Vector2(ratio, 0f);
        phaseMarker.anchorMax = new Vector2(ratio, 1f);
        phaseMarker.pivot = new Vector2(0.5f, 0.5f);
        phaseMarker.anchoredPosition = Vector2.zero;

        // 세로는 앵커가 위아래로 벌어져 있으므로 sizeDelta.y가 곧 "늘어난 양"이다.
        // 0을 넣으면 채움 영역과 같은 높이가 된다. 가로만 실제 두께로 준다.
        phaseMarker.sizeDelta = new Vector2(phaseMarker.sizeDelta.x, 0f);
    }

    private void Update()
    {
        // 목표값을 향해 부드럽게 따라간다. 값을 그대로 넣으면 한 대 맞을 때 게이지가 한
        // 프레임에 뚝 떨어져서 얼마나 깎였는지 눈으로 읽히지 않는다.
        //
        // Exp를 쓴 형태라 프레임률이 달라도 감쇠 속도가 거의 같다. UI 연출이라 이 정도면 충분하다.
        displayed = followSpeed <= 0f
            ? target
            : Mathf.Lerp(displayed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        ApplyFill(displayed);
        ApplyColor();
        ApplyFade();
    }

    /// <summary>채움 비율을 반영한다. 전용 스프라이트가 있으면 fillAmount, 없으면 앵커를 쓴다.</summary>
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

    /// <summary>
    /// 페이즈에 따라 이름을 정한다.
    ///
    /// <b>1페이즈에 이름을 감추는 것이 이 연출의 전부다.</b> 물음표는 "칭호를 모른다"가 아니라
    /// <b>"이름을 모른다"</b>는 뜻이라, 2페이즈에서 나오는 것도 직함이 아니라 이름이어야 한다.
    /// 이 세계에는 고유명이 하나도 없다 — 잿불 망령·잿불 사수·잿불 자폭병은 전부 분류명이다.
    /// 그 안에서 하나만 이름을 가지면, 이름의 <b>형식만으로</b> "종류가 아니라 개체"가 전달된다.
    ///
    /// 두 이름을 인스펙터로 뺀 이유: 이건 밸런스가 아니라 연출 문구다. 바꿔보고 읽어봐야
    /// 정해지는 종류라, 고칠 때마다 코드를 건드리고 컴파일을 기다릴 이유가 없다.
    /// </summary>
    private void ApplyName()
    {
        if (nameLabel == null) return;

        nameLabel.text = isPhase2 ? phase2Name : phase1Name;
    }

    /// <summary>페이즈에 따라 채움 색을 정한다.</summary>
    private void ApplyColor()
    {
        if (fillImage == null) return;

        fillImage.color = isPhase2 ? phase2Color : phase1Color;
    }

    /// <summary>
    /// 나타남·사라짐을 처리한다.
    ///
    /// 코루틴 대신 <see cref="Update"/>에서 미는 이유: 등장 도중에 <see cref="Unbind"/>가
    /// 불릴 수 있다(보스를 만들자마자 방을 나가는 경우). 코루틴 두 개가 같은 알파를 서로
    /// 밀면 어느 쪽이 이길지 순서에 달리는데, 값 하나를 목표로 미는 방식은 그 문제가 없다.
    ///
    /// <see cref="Time.unscaledDeltaTime"/>을 쓰는 이유: 인벤토리나 설정 창을 열면
    /// timeScale이 0이 된다. 그때 사라지던 바가 <b>반투명인 채로 멈춘다.</b>
    /// </summary>
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
