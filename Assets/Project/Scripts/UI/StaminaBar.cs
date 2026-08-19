using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면에 스태미나를 보여주는 게이지 바.
///
/// 이 컴포넌트는 값을 만들지 않는다. <see cref="PlayerStamina"/>가 가진 값을 읽어서
/// 그리기만 한다 — ResultScreen이 RunResultData를 읽기만 하는 것과 같은 규칙이다.
/// UI가 값을 계산하기 시작하면 "화면에 보이는 스태미나와 실제로 달릴 수 있는 양이 다른"
/// 문제가 생기고, 그때 어느 쪽이 맞는지 판단할 근거가 없어진다.
///
/// 추가 생성 — 전용 용암 Fill 스프라이트가 생긴 뒤에는 Image.fillAmount를 사용한다.
/// RectTransform 폭을 줄이면 용암 균열 무늬 자체가 압축되지만, fillAmount는 원본 비율을
/// 유지한 채 오른쪽만 잘라내므로 게이지가 줄어도 무늬가 찌그러지지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class StaminaBar : MonoBehaviour
{
    [Header("참조 (비어 있으면 실행 시 찾는다)")]
    [Tooltip("읽어올 스태미나. 비우면 씬에서 찾는다 — 플레이어는 프리팹이라 미리 연결할 수 없다.")]
    [SerializeField] private PlayerStamina stamina;

    [Tooltip("실제로 늘었다 줄었다 하는 사각형의 RectTransform.")]
    [SerializeField] private RectTransform fillRect;

    [Tooltip("색을 바꿀 대상. 보통 fillRect와 같은 오브젝트에 있다.")]
    [SerializeField] private Image fillImage;

    [Tooltip("가득 찼을 때 흐려지게 만들 CanvasGroup. 비우면 항상 또렷하게 보인다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("색")]
    [Tooltip("평소 색. 검날의 잉걸 색과 맞춘 주황이다.")]
    [SerializeField] private Color normalColor = new Color(1f, 0.42f, 0.12f, 1f);

    [Tooltip("고갈 잠금 상태의 색. 달릴 수 없다는 걸 색으로 알린다.")]
    [SerializeField] private Color exhaustedColor = new Color(0.45f, 0.16f, 0.16f, 1f);

    [Header("연출")]
    [Tooltip("게이지가 목표값을 따라가는 속도. 클수록 즉각적이다. 0이면 즉시 반영된다.")]
    [SerializeField] private float followSpeed = 12f;

    // 수정(게이지 프레임 도입 시점): 기본값을 true → false로 바꿨다.
    // CanvasGroup은 프레임까지 같이 흐리게 만드는데, 프레임은 보라고 그린 장식이라
    // 평소에 흐려져 있으면 아깝다. 화면이 지저분하다고 느끼면 다시 켜면 된다.
    [Tooltip("가득 찬 상태에서 게이지를 흐리게 만든다. 프레임 장식까지 같이 흐려진다.")]
    [SerializeField] private bool dimWhenFull = false;

    [Tooltip("가득 찼을 때의 불투명도.")]
    [Range(0f, 1f)]
    [SerializeField] private float dimmedAlpha = 0.25f;

    // 화면에 지금 그려지고 있는 비율. 목표값(스태미나의 실제 비율)을 향해 따라간다.
    private float displayed = 1f;

    // 스태미나가 알려준 실제 비율.
    private float target = 1f;

    private void Awake()
    {
        // 추가 생성 — Image의 채움 설정을 코드에서 강제한다.
        //
        // 이걸 넣은 이유: 씬에 저장돼 있던 Fill 이미지가 m_Type: 0(Simple), m_FillMethod: 4
        // (Radial360)였다. Image.fillAmount는 <b>Type이 Filled일 때만</b> 렌더러가 읽는다.
        // Simple이면 아래 ApplyFill이 매 프레임 값을 넣어도 그림이 전혀 안 바뀐다 —
        // 에러도 경고도 없이 게이지가 가득 찬 채로 멈춘다.
        //
        // HUD 빌더가 이 값을 제대로 넣어주지만, 빌더를 고치기 전에 만들어진 씬 오브젝트에는
        // 옛날 값이 그대로 저장돼 남는다. 씬에 박힌 값과 코드가 어긋나는 문제라, 코드 쪽에서
        // 실행할 때마다 맞추는 것이 확실하다. 메뉴를 다시 돌리는 걸 잊어도 동작한다.
        ConfigureFillImage();

        // 플레이어는 프리팹으로 배치되므로 HUD 쪽에서 미리 참조를 걸어둘 수 없다.
        // (프리팹은 씬 오브젝트를, 씬 오브젝트는 프리팹 인스턴스를 미리 가리킬 수 없다.)
        if (stamina == null) stamina = FindFirstObjectByType<PlayerStamina>();

        if (stamina == null)
        {
            Debug.LogWarning("[스태미나 바] PlayerStamina를 못 찾았다. 게이지가 가득 찬 채로 멈춘다.", this);
            return;
        }

        target = stamina.Normalized;
        displayed = target;
    }

    /// <summary>
    /// 이벤트 구독은 OnEnable에서, 해제는 OnDisable에서 한다.
    /// Awake에서 구독하고 OnDestroy에서 해제하면, 오브젝트를 껐다 켜는 동안에도 계속 구독
    /// 상태로 남아서 꺼진 UI가 갱신을 받는다.
    /// </summary>
    private void OnEnable()
    {
        if (stamina == null) return;

        stamina.Changed += OnStaminaChanged;

        // 구독 사이에 값이 바뀌었을 수 있으므로 현재값으로 한 번 맞춘다.
        OnStaminaChanged(stamina.Normalized);
    }

    private void OnDisable()
    {
        if (stamina == null) return;

        stamina.Changed -= OnStaminaChanged;
    }

    /// <summary>스태미나가 바뀔 때마다 불린다. 목표값만 갱신하고 그리기는 Update가 한다.</summary>
    private void OnStaminaChanged(float normalized)
    {
        target = normalized;
    }

    private void Update()
    {
        // 목표값을 향해 부드럽게 따라간다. 값을 그대로 넣으면 대시할 때 게이지가 한 프레임에
        // 뚝 떨어져서 얼마나 썼는지 눈으로 읽히지 않는다.
        //
        // Lerp를 매 프레임 호출하는 이 형태는 프레임률에 따라 속도가 조금 달라지지만,
        // UI 연출이라 그 차이가 게임 플레이에 영향을 주지 않는다. 정확한 감쇠가 필요한
        // 물리 쪽이었다면 이렇게 쓰면 안 된다.
        displayed = followSpeed <= 0f
            ? target
            : Mathf.Lerp(displayed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        ApplyFill(displayed);
        ApplyColor();
        ApplyDim();
    }

    /// <summary>
    /// 추가 생성 — 전용 스프라이트가 있으면 가로 채움(왼쪽 → 오른쪽)으로 설정한다.
    ///
    /// preserveAspect를 끄는 이유: 켜져 있으면 Image가 스프라이트의 원본 비율(844x44)을
    /// 유지하려고 사각형 안에서 크기를 줄인다. 프레임 안쪽 영역(804x43)과 비율이 조금 달라서
    /// 게이지가 프레임 안에서 위아래로 뜨거나 좌우가 남는다.
    /// </summary>
    private void ConfigureFillImage()
    {
        if (fillImage == null || fillImage.sprite == null) return;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.preserveAspect = false;
    }

    /// <summary>채움 비율을 반영한다. 전용 스프라이트가 있으면 fillAmount, 없으면 앵커를 쓴다.</summary>
    private void ApplyFill(float ratio)
    {
        if (fillImage != null && fillImage.sprite != null)
        {
            fillImage.fillAmount = Mathf.Clamp01(ratio);
            return;
        }

        // 전용 스프라이트를 못 읽은 경우에는 예전 단색 사각형 방식으로라도 동작한다.
        if (fillRect == null) return;

        // 왼쪽 끝(0)은 고정하고 오른쪽 끝만 움직인다. 그래야 왼쪽에서 오른쪽으로 차오른다.
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);

        // 앵커를 옮겼으면 오프셋을 0으로 눌러줘야 부모 사각형에 정확히 맞는다.
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    /// <summary>고갈 잠금 여부에 따라 색을 바꾼다.</summary>
    private void ApplyColor()
    {
        if (fillImage == null || stamina == null) return;

        fillImage.color = stamina.IsExhausted ? exhaustedColor : normalColor;
    }

    /// <summary>가득 찼을 때 흐리게 만든다.</summary>
    private void ApplyDim()
    {
        if (canvasGroup == null) return;

        if (!dimWhenFull)
        {
            canvasGroup.alpha = 1f;
            return;
        }

        // 목표값이 아니라 화면에 그려지는 값을 기준으로 판단한다. 그래야 게이지가 다 차오르는
        // 연출이 끝난 뒤에 흐려진다.
        bool isFull = displayed >= 0.999f;
        float wanted = isFull ? dimmedAlpha : 1f;

        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, wanted, Time.deltaTime * 3f);
    }
}
