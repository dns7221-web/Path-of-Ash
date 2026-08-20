using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재 게이지를 화면에 보여준다. 다른 게이지와 같은 규칙 — 값을 만들지 않고 읽어서 그린다.
///
/// 가득 찼을 때 색을 바꾸는 것이 이 바의 핵심 기능이다. 게이지가 다 찼다는 걸 눈금으로만
/// 알리면 전투 중에 못 본다. <b>색이 확 달라지면 곁눈으로도 "지금 쓸 수 있다"가 읽힌다.</b>
/// 채움 그림을 회색 재로 뽑아둔 것이 여기서 값을 한다 — 원본이 이미 붉으면 틴트를 걸어도
/// 차이가 안 난다.
/// </summary>
[DisallowMultipleComponent]
public class AshGaugeBar : MonoBehaviour
{
    [Header("참조 (비어 있으면 실행 시 찾는다)")]
    [SerializeField] private AshGauge gauge;
    [SerializeField] private RectTransform fillRect;
    [SerializeField] private Image fillImage;

    [Header("색")]
    [Tooltip("모으는 중. 원본 회색을 살짝 어둡게 둔다.")]
    [SerializeField] private Color chargingColor = new Color(0.75f, 0.73f, 0.72f, 1f);

    [Tooltip("가득 찼을 때. 재가 다시 달아오른 색이다.")]
    [SerializeField] private Color fullColor = new Color(1f, 0.45f, 0.15f, 1f);

    [Tooltip("가득 찼을 때 깜빡이는 주기(초). 0이면 안 깜빡인다.")]
    [SerializeField, Min(0f)] private float fullPulseSeconds = 1.1f;

    [Header("연출")]
    [SerializeField] private float followSpeed = 10f;

    private float displayed;
    private float target;

    private void Awake()
    {
        ConfigureFillImage();

        if (gauge == null)
        {
            // 플레이어가 프리팹 인스턴스라 HUD가 미리 참조를 걸어둘 수 없다.
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null) gauge = player.GetComponent<AshGauge>();
        }

        if (gauge == null)
        {
            Debug.LogWarning("[재 게이지] AshGauge를 못 찾았다. 게이지가 빈 채로 멈춘다.", this);
            return;
        }

        target = gauge.Normalized;
        displayed = target;
    }

    /// <summary>다른 게이지와 같은 이유로 Image 설정을 코드에서 강제한다.</summary>
    private void ConfigureFillImage()
    {
        if (fillImage == null || fillImage.sprite == null) return;

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.preserveAspect = false;
    }

    private void OnEnable()
    {
        if (gauge == null) return;

        gauge.Changed += OnGaugeChanged;
        OnGaugeChanged(gauge.Normalized);
    }

    private void OnDisable()
    {
        if (gauge == null) return;

        gauge.Changed -= OnGaugeChanged;
    }

    private void OnGaugeChanged(float normalized) => target = normalized;

    private void Update()
    {
        displayed = followSpeed <= 0f
            ? target
            : Mathf.Lerp(displayed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        ApplyFill(displayed);
        ApplyColor();
    }

    private void ApplyFill(float ratio)
    {
        if (fillImage != null && fillImage.sprite != null)
        {
            fillImage.fillAmount = Mathf.Clamp01(ratio);
            return;
        }

        if (fillRect == null) return;

        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    private void ApplyColor()
    {
        if (fillImage == null) return;

        bool full = gauge != null && gauge.IsFull;

        if (!full)
        {
            fillImage.color = chargingColor;
            return;
        }

        if (fullPulseSeconds <= 0f)
        {
            fillImage.color = fullColor;
            return;
        }

        // 밝기를 오르내려 "지금 쓸 수 있다"를 계속 알린다.
        // 색을 완전히 바꾸는 게 아니라 밝기만 흔들어서, 무엇인지 헷갈리지 않게 한다.
        float t = (Mathf.Sin(Time.time * Mathf.PI * 2f / fullPulseSeconds) + 1f) * 0.5f;
        fillImage.color = Color.Lerp(fullColor, Color.Lerp(fullColor, Color.white, 0.45f), t);
    }
}
