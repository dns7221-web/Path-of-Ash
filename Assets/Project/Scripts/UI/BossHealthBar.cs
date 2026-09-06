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
/// <b>수정(페이즈마다 바를 새로 쓴다) — 이 바는 체력 전체가 아니라 지금 페이즈의 몫만
/// 보여준다.</b> 전환 비율이 0.5라면 1페이즈는 체력 100~50%를 바 100~0%로, 2페이즈는
/// 체력 50~0%를 다시 바 100~0%로 그린다. 그래서 전환 직전에 바가 <b>완전히 비고</b>,
/// 전환이 끝나면 <b>다시 가득 찬다.</b>
///
/// 예전에는 체력 전체를 바 하나에 담고 전환 지점에 눈금을 세워 예고했다. 바꾼 이유는
/// <b>"다 깎았다"가 예고보다 강하기 때문이다.</b> 눈금은 "곧 뭔가 온다"까지만 말하지만,
/// 바가 0이 되는 것은 <b>이겼다고 믿게 만든다.</b> 그 믿음이 뒤집히는 것이 2페이즈다.
/// 눈금은 같이 걷어냈다 — 이 방식에서는 전환 지점이 곧 바의 0이라 눈금이 설 자리가 없고,
/// 남겨두면 언제나 틀린 자리를 가리키는 장식이 된다.
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
             "작기 때문에, 천천히 흘러야 깎이고 있다는 것이 눈에 보인다.")]
    [SerializeField, Min(0f)] private float followSpeed = 5f;

    // 추가 생성 — 2페이즈로 넘어갈 때 빈 바가 다시 차오르는 데 걸리는 시간(초).
    //
    // 평소의 followSpeed(지수 감쇠)를 안 쓰는 이유: 지수 보간은 목표에 가까울수록 느려져서
    // 마지막 20%가 뭉개진다. 깎이는 연출에는 그게 어울리지만(끝이 부드럽다), 차오르는 것은
    // 끝까지 같은 속도로 올라가서 가득 찬 순간이 딱 떨어져야 "다시 채워졌다"가 읽힌다.
    [Tooltip("2페이즈 전환 때 바가 다시 차오르는 시간(초). 일정한 속도로 채운다.")]
    [SerializeField, Min(0.01f)] private float refillSeconds = 0.7f;

    [Tooltip("나타나고 사라지는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.35f;

    // 지금 보고 있는 보스의 체력. 비어 있으면 바는 숨어 있다.
    private Health health;

    // 화면에 지금 그려지고 있는 비율. 목표값을 향해 따라간다.
    private float displayed = 1f;

    // 체력이 알려준 값을 이 페이즈의 몫으로 환산한 비율.
    private float target = 1f;

    // 추가 생성 — 체력이 마지막으로 알려준 날것의 값.
    //
    // 환산 결과(target)만 들고 있으면 안 된다. 페이즈가 바뀌는 순간 <b>같은 체력을 다른
    // 기준으로 다시 환산해야</b> 하는데, 환산된 값에서는 원래 체력을 되돌릴 수 없다.
    private int lastCurrent;
    private int lastMax = 1;

    // 지금 보여야 하는가. 이 값에 따라 group.alpha가 0과 1 사이를 오간다.
    private bool visible;

    // 지금 2페이즈인가. 채움 색과 환산 기준을 정할 때 쓴다.
    private bool isPhase2;

    // 추가 생성 — 2페이즈로 넘어가는 체력 비율(0~1). 이 값이 두 페이즈의 경계다.
    private float phase2Ratio;

    // 추가 생성 — 전환 비율이 실제로 바를 둘로 나누는가.
    //
    // 0이나 1이면 나눌 수 없다(한쪽 몫이 0이 되어 0으로 나누게 된다). 그때는 페이즈가
    // 없는 것으로 보고 체력 전체를 바 하나에 그린다 — 전환이 없는 보스를 이 바에 물릴
    // 수도 있기 때문이다.
    private bool hasPhaseSplit;

    // 추가 생성 — 지금 차오르는 중인가. 켜져 있는 동안만 일정 속도로 채운다.
    private bool refilling;

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
    /// 0.5를 따로 적어두면 보스 쪽 수치를 바꿨을 때 <b>바가 옛 기준으로 나뉜다.</b>
    /// 그건 아무 에러도 없이 "바가 다 비었는데 전환이 안 온다"로만 나타나서,
    /// 버그가 아니라 밸런스 문제로 오해하기 딱 좋다.
    /// </summary>
    /// <param name="bossHealth">보스의 체력. null이면 아무것도 하지 않는다.</param>
    /// <param name="phase2HealthRatio">2페이즈로 넘어가는 체력 비율(0~1). 두 페이즈의 경계다.</param>
    public void Bind(Health bossHealth, float phase2HealthRatio)
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
        // 색과 환산 기준이 그대로 남아, 아직 1페이즈인 보스가 2페이즈처럼 보인다.
        isPhase2 = false;
        refilling = false;

        phase2Ratio = phase2HealthRatio;
        hasPhaseSplit = phase2Ratio > 0f && phase2Ratio < 1f;

        // 등장할 때는 지금 체력에서 시작한다. 0에서 차오르게 하면 "보스가 회복하는 중"으로
        // 보이고, 1에서 떨어지게 하면 시작하자마자 얻어맞은 것처럼 보인다.
        lastCurrent = health.Current;
        lastMax = health.Max;
        target = PhaseRatio(lastCurrent, lastMax);
        displayed = target;

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
        refilling = false;
        visible = false;
    }

    /// <summary>
    /// 2페이즈에 들어갔음을 바에 알린다. 색과 이름이 바뀌고, 빈 바가 다시 차오른다.
    ///
    /// <see cref="EnemyBoss"/>가 이 함수를 직접 부르지 않고 방(<see cref="BossEncounter"/>)이
    /// 신호를 받아 전달한다. 사망 처리를 그렇게 나눠둔 것과 같은 이유다 — <b>적 스크립트가
    /// 화면 구조를 모르게</b> 한다. 보스가 HUD를 직접 찾으면, HUD가 없는 씬(테스트 방 등)에
    /// 보스를 놓는 순간 경고가 뜨고 보스 쪽 코드를 고치게 된다.
    ///
    /// <b>수정 — 여기서 바가 다시 찬다.</b> 환산 기준을 2페이즈로 바꾸면 같은 체력이
    /// 훨씬 큰 비율이 되므로(경계에서는 1.0), 목표값만 새로 잡아주면 채우는 일은
    /// <see cref="Update"/>가 알아서 한다. 여기서 displayed를 직접 1로 밀지 않는 이유는
    /// <b>가득 찬 결과가 아니라 차오르는 과정이 연출이기 때문이다.</b>
    /// </summary>
    public void MarkPhase2()
    {
        isPhase2 = true;

        // 같은 체력을 2페이즈 기준으로 다시 환산한다. 경계를 넘겨 때렸다면(예: 55%에서
        // 45%로) 목표가 1.0이 아니라 0.9가 된다. 그게 맞다 — 넘겨서 깎은 만큼은 2페이즈
        // 체력에서 이미 빠져 있고, 바가 가득 차버리면 그 한 대가 없던 일이 된다.
        target = PhaseRatio(lastCurrent, lastMax);
        refilling = true;

        ApplyColor();
        ApplyName();
    }

    private void OnDisable()
    {
        // 컴포넌트가 꺼질 때 구독을 남겨두면, 다시 켜졌을 때 같은 대상을 두 번 구독하거나
        // 이미 파괴된 Health를 참조한 채로 남는다.
        Unsubscribe();

        // 대상을 잃었으니 보일 이유도 없다. 이걸 안 하면 다시 켜졌을 때 <b>아무도 안 물린
        // 바가 옛 값을 단 채</b> 떠 있고, 체력이 안 변하니 영영 그대로 멈춰 있는다.
        refilling = false;
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
        lastCurrent = current;
        lastMax = max;
        target = PhaseRatio(current, max);
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
        refilling = false;
    }

    /// <summary>
    /// 추가 생성 — 체력을 <b>지금 페이즈의 몫</b>에 대한 비율(0~1)로 환산한다.
    ///
    /// 전환 비율이 0.5일 때 1페이즈는 체력 1.0~0.5를 1~0으로, 2페이즈는 체력 0.5~0을
    /// 다시 1~0으로 옮긴다. 두 구간의 폭이 다르면(예: 0.3) 각자의 폭으로 나누므로
    /// <b>바가 깎이는 속도가 페이즈마다 달라진다.</b> 그게 맞다 — 바는 남은 체력이 아니라
    /// "이 페이즈가 얼마나 남았는가"를 말하는 물건이다.
    /// </summary>
    private float PhaseRatio(int current, int max)
    {
        if (max <= 0) return 0f;

        float raw = current / (float)max;

        // 경계가 없으면 예전처럼 체력 전체를 바 하나에 그린다.
        if (!hasPhaseSplit) return Mathf.Clamp01(raw);

        return isPhase2
            ? Mathf.Clamp01(raw / phase2Ratio)
            : Mathf.Clamp01((raw - phase2Ratio) / (1f - phase2Ratio));
    }

    private void Update()
    {
        if (refilling) StepRefill();
        else StepFollow();

        ApplyFill(displayed);
        ApplyColor();
        ApplyFade();
    }

    /// <summary>
    /// 평소의 따라가기. 목표값을 향해 부드럽게 감쇠한다.
    ///
    /// 값을 그대로 넣으면 한 대 맞을 때 게이지가 한 프레임에 뚝 떨어져서 얼마나 깎였는지
    /// 눈으로 읽히지 않는다. Exp를 쓴 형태라 프레임률이 달라도 감쇠 속도가 거의 같다.
    /// </summary>
    private void StepFollow()
    {
        displayed = followSpeed <= 0f
            ? target
            : Mathf.Lerp(displayed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
    }

    /// <summary>
    /// 추가 생성 — 2페이즈 전환의 재충전. 일정한 속도로 목표까지 올린다.
    ///
    /// 채우는 도중 목표가 도로 내려가면(전환이 끝나자마자 얻어맞은 경우) 재충전을 끝내고
    /// 평소 방식으로 돌려보낸다. 그대로 두면 <b>맞았는데 바가 계속 차오르는</b> 그림이 된다.
    /// </summary>
    private void StepRefill()
    {
        if (target <= displayed)
        {
            refilling = false;
            return;
        }

        displayed = Mathf.MoveTowards(displayed, target, Time.deltaTime / refillSeconds);

        if (Mathf.Approximately(displayed, target)) refilling = false;
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
