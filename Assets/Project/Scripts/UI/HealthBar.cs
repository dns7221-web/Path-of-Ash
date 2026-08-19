using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면에 플레이어 체력을 보여주는 게이지 바.
///
/// <see cref="StaminaBar"/>와 규칙이 같다 — 값을 만들지 않고 <see cref="Health"/>가 가진 값을
/// 읽어서 그리기만 한다. 두 스크립트를 하나로 합치지 않은 이유는 <b>읽는 대상이 다르기</b>
/// 때문이다. 스태미나는 0~1 실수를 이벤트로 주고, 체력은 정수(현재/최대)를 준다. 공통 부모로
/// 묶으면 "값을 어디서 어떻게 받는가"가 추상화 뒤로 숨어서, 게이지가 안 움직일 때 어느 쪽을
/// 봐야 하는지 알기 어려워진다. 지금은 게이지가 둘뿐이라 중복이 이득이다.
///
/// 채움을 Image.fillAmount로 하는 이유: RectTransform 폭을 줄이면 용암 무늬 자체가 가로로
/// 압축된다. fillAmount는 원본 비율을 유지한 채 오른쪽부터 잘라내므로 무늬가 안 찌그러진다.
/// </summary>
[DisallowMultipleComponent]
public class HealthBar : MonoBehaviour
{
    [Header("참조 (비어 있으면 실행 시 찾는다)")]
    [Tooltip("읽어올 체력. 비우면 씬에서 플레이어의 것을 찾는다.")]
    [SerializeField] private Health health;

    [Tooltip("실제로 늘었다 줄었다 하는 사각형. fillAmount를 못 쓸 때의 대비책으로 쓴다.")]
    [SerializeField] private RectTransform fillRect;

    [Tooltip("채움 이미지. 이 컴포넌트의 fillAmount가 게이지 길이가 된다.")]
    [SerializeField] private Image fillImage;

    [Header("색")]
    [Tooltip("평소 색. 원본 스프라이트 색을 그대로 쓰려면 흰색으로 둔다.")]
    [SerializeField] private Color normalColor = Color.white;

    [Tooltip("체력이 위험할 때의 색.")]
    [SerializeField] private Color dangerColor = new Color(1f, 0.35f, 0.25f, 1f);

    [Tooltip("이 비율 이하로 떨어지면 위험 색으로 바뀐다.")]
    [Range(0f, 1f)]
    [SerializeField] private float dangerThreshold = 0.34f;

    [Header("연출")]
    [Tooltip("게이지가 목표값을 따라가는 속도. 클수록 즉각적이다. 0이면 즉시 반영된다. " +
             "체력은 스태미나보다 천천히 줄어드는 편이 얼마나 깎였는지 읽기 좋다.")]
    [SerializeField] private float followSpeed = 8f;

    // 화면에 지금 그려지고 있는 비율. 목표값을 향해 따라간다.
    private float displayed = 1f;

    // 체력이 알려준 실제 비율.
    private float target = 1f;

    private void Awake()
    {
        // StaminaBar와 같은 이유로 Image 설정을 코드에서 강제한다.
        // Image.fillAmount는 Type이 Filled일 때만 렌더러가 읽는다. 씬에 Simple로 저장돼 있으면
        // 매 프레임 값을 넣어도 그림이 전혀 안 바뀌고, 에러도 경고도 안 난다.
        ConfigureFillImage();

        if (health == null) health = FindPlayerHealth();

        if (health == null)
        {
            Debug.LogWarning("[체력 바] 플레이어의 Health를 못 찾았다. 게이지가 가득 찬 채로 멈춘다.", this);
            return;
        }

        target = Ratio(health.Current, health.Max);
        displayed = target;
    }

    /// <summary>
    /// 플레이어의 Health를 찾는다.
    ///
    /// FindFirstObjectByType&lt;Health&gt;()를 그냥 쓰면 안 되는 이유: Health는 플레이어와 적이
    /// <b>공유하는</b> 컴포넌트라, 씬에 적이 먼저 잡히면 체력 바가 적의 체력을 표시한다.
    /// 에러가 안 나고 그냥 엉뚱한 값이 보이는 종류의 버그라 찾기 어렵다.
    /// PlayerController를 먼저 찾아 거기서 꺼내면 그 경로가 막힌다.
    /// </summary>
    private static Health FindPlayerHealth()
    {
        var player = FindFirstObjectByType<PlayerController>();
        return player != null ? player.GetComponent<Health>() : null;
    }

    /// <summary>전용 스프라이트가 있으면 가로 채움(왼쪽 → 오른쪽)으로 설정한다.</summary>
    private void ConfigureFillImage()
    {
        if (fillImage == null || fillImage.sprite == null) return;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;

        // 켜져 있으면 Image가 스프라이트 원본 비율을 지키려고 사각형 안에서 크기를 줄여서,
        // 게이지가 프레임 안에서 위아래로 뜨거나 좌우가 남는다.
        fillImage.preserveAspect = false;
    }

    private void OnEnable()
    {
        if (health == null) return;

        health.Damaged += OnHealthChanged;

        // 구독 사이에 값이 바뀌었을 수 있으므로 현재값으로 한 번 맞춘다.
        OnHealthChanged(health.Current, health.Max);
    }

    private void OnDisable()
    {
        if (health == null) return;

        health.Damaged -= OnHealthChanged;
    }

    /// <summary>체력이 바뀔 때마다 불린다. 회복도 같은 이벤트로 온다.</summary>
    private void OnHealthChanged(int current, int max)
    {
        target = Ratio(current, max);
    }

    private static float Ratio(int current, int max)
        => max <= 0 ? 0f : Mathf.Clamp01(current / (float)max);

    private void Update()
    {
        // 목표값을 향해 부드럽게 따라간다. 값을 그대로 넣으면 한 대 맞을 때 게이지가 한 프레임에
        // 뚝 떨어져서 얼마나 깎였는지 눈으로 읽히지 않는다.
        //
        // Exp를 쓴 형태라 프레임률이 달라도 감쇠 속도가 거의 같다. UI 연출이라 이 정도면 충분하다.
        displayed = followSpeed <= 0f
            ? target
            : Mathf.Lerp(displayed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        ApplyFill(displayed);
        ApplyColor();
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

    /// <summary>체력이 위험 수준이면 색을 바꾼다.</summary>
    private void ApplyColor()
    {
        if (fillImage == null) return;

        // 표시값이 아니라 목표값으로 판단한다. 그래야 맞는 순간 바로 색이 바뀌고,
        // 게이지가 천천히 줄어드는 동안 색이 뒤늦게 따라오지 않는다.
        fillImage.color = target <= dangerThreshold ? dangerColor : normalColor;
    }
}
