using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 아래에 스킬 슬롯을 보여주고 재사용 대기시간을 표시한다.
///
/// 다른 HUD와 같은 규칙 — 값을 만들지 않고 <see cref="SkillController"/>가 가진 값을
/// 읽어서 그리기만 한다.
///
/// 쿨타임을 <b>덮개가 시계 방향으로 걷히는</b> 방식으로 그린 이유: 숫자로 "2.4초"를 띄우면
/// 읽어야 알 수 있다. 전투 중에는 읽을 틈이 없다. 덮개가 얼마나 남았는지는 곁눈으로도
/// 보이고, 다 걷히는 순간이 곧 "지금 쓸 수 있다"는 신호가 된다.
/// </summary>
[DisallowMultipleComponent]
public class SkillBar : MonoBehaviour
{
    [Header("참조 (비어 있으면 실행 시 찾는다)")]
    [SerializeField] private SkillController skills;

    [Header("슬롯 (0=기본공격, 1=Q, 2=W, 3=E, 4=R)")]
    [Tooltip("각 슬롯의 아이콘 이미지.")]
    [SerializeField] private Image[] icons;

    [Tooltip("각 슬롯의 쿨타임 덮개. Filled/Radial360으로 설정돼 있어야 한다.")]
    [SerializeField] private Image[] cooldownOverlays;

    [Tooltip("각 슬롯의 남은 시간 텍스트. 비워도 된다.")]
    [SerializeField] private TMP_Text[] cooldownLabels;

    [Header("색")]
    [Tooltip("쓸 수 있을 때의 아이콘 색.")]
    [SerializeField] private Color readyColor = Color.white;

    [Tooltip("쿨타임 중일 때의 아이콘 색. 어둡게 해서 못 쓴다는 걸 알린다.")]
    [SerializeField] private Color cooldownColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Tooltip("슬롯이 비어 있을 때의 색. 아직 안 만든 스킬 자리다.")]
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.15f);

    private void Awake()
    {
        // 플레이어가 프리팹 인스턴스라 HUD가 미리 참조를 걸어둘 수 없다.
        if (skills == null) skills = FindFirstObjectByType<SkillController>();

        if (skills == null)
            Debug.LogWarning("[스킬 바] SkillController를 못 찾았다. 슬롯이 비어 보인다.", this);

        ApplyIcons();
    }

    /// <summary>
    /// 아이콘은 한 번만 넣는다. 매 프레임 sprite를 대입하면 UI가 그때마다 다시 그려진다.
    /// 스킬 교체 시스템이 생기면 그때 다시 부르면 된다.
    /// </summary>
    private void ApplyIcons()
    {
        if (icons == null) return;

        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] == null) continue;

            SkillData skill = skills != null ? skills.GetSlot(i) : null;

            // 아이콘 그림이 아직 없어도 슬롯 자체는 보여야 한다. 키 글자와 쿨타임만으로도
            // "여기에 스킬이 있다"는 정보는 전달된다.
            icons[i].sprite = skill != null ? skill.Icon : null;
            icons[i].enabled = icons[i].sprite != null;
        }
    }

    private void Update()
    {
        if (skills == null) return;

        int count = cooldownOverlays != null ? cooldownOverlays.Length : 0;

        for (int i = 0; i < count; i++)
        {
            SkillData skill = skills.GetSlot(i);
            Image overlay = cooldownOverlays[i];

            if (skill == null)
            {
                // 빈 슬롯. 덮개를 완전히 씌워서 "여기는 아직 없다"를 보여준다.
                if (overlay != null) overlay.fillAmount = 1f;
                SetIconColor(i, emptyColor);
                SetLabel(i, string.Empty);
                continue;
            }

            float remaining = skills.GetCooldownRemaining(i);
            // 유물이 쿨타임을 줄여놨을 수 있어서 SkillController가 알려주는 실제 길이로 나눈다.
            float total = Mathf.Max(0.0001f, skills.GetCooldownTotal(i));
            float ratio = Mathf.Clamp01(remaining / total);

            if (overlay != null) overlay.fillAmount = ratio;

            SetIconColor(i, remaining > 0f ? cooldownColor : readyColor);

            // 1초 미만은 소수점 한 자리로. 그 위는 정수로 — 긴 쿨타임에서 소수점이
            // 빠르게 굴러가면 오히려 읽기 어렵다.
            SetLabel(i, remaining <= 0f
                ? string.Empty
                : remaining < 1f ? remaining.ToString("0.0") : Mathf.Ceil(remaining).ToString("0"));
        }
    }

    private void SetIconColor(int index, Color color)
    {
        if (icons == null || index >= icons.Length || icons[index] == null) return;

        icons[index].color = color;
    }

    private void SetLabel(int index, string text)
    {
        if (cooldownLabels == null || index >= cooldownLabels.Length) return;
        if (cooldownLabels[index] == null) return;

        cooldownLabels[index].text = text;
    }
}
