using TMPro;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-02) — 능력치 창. `C`로 여닫는다.
///
/// 데미지 숫자 대신 이 창을 둔 이유(Docs/PORTFOLIO.md 10-01 결정): 이 게임의 핵심은 유물 조합이라,
/// 유물을 끼고 뺄 때 <b>숫자가 어떻게 바뀌는지</b>가 보이는 쪽이 플레이어에게 더 필요한 정보다.
///
/// <b>계산을 하지 않고 읽기만 한다.</b> 유물 보정은 <see cref="RelicInventory"/>의 Recalculate가 이미
/// 각 컴포넌트(체력·스태미나·스킬·이동·재 게이지)에 넣어 두었다. 이 창이 같은 계산을 한 번 더 하면
/// 두 계산 중 하나만 고쳐졌을 때 화면과 실제 값이 조용히 어긋난다. 그래서 각 컴포넌트가 실제로 쓰는
/// 값(예: <see cref="PlayerController.MoveSpeed"/>)을 그대로 읽는다.
///
/// 여닫기 규칙은 인벤토리·보스 열쇠 화면과 같다.
/// - 시간은 <see cref="PauseGate"/>가 멈춘다(Time.timeScale을 직접 바꾸지 않는다).
/// - <b>여는 쪽이 상대 화면을 닫는다.</b> 그래서 세 화면 중 하나만 열린다.
/// </summary>
[DisallowMultipleComponent]
public class StatsScreen : MonoBehaviour
{
    /// <summary>능력치 줄 수. 빌더가 이 수만큼 줄을 만든다.</summary>
    public const int RowCount = 7;

    /// <summary>
    /// 줄 이름. 순서가 <see cref="Redraw"/>의 값 순서와 같아야 한다.
    /// 이름을 빌더(씬)가 아니라 여기에 둔 이유: 이름과 값을 한 파일에서 나란히 봐야 순서가 어긋나지 않는다.
    /// </summary>
    private static readonly string[] RowNames =
    {
        "최대 체력",
        "최대 스태미나",
        "스태미나 회복",
        "스킬 피해",
        "이동 속도",
        "재사용 대기",
        "처치당 재"
    };

    /// <summary>유물 보너스 글자색. 게임 전체의 잉걸(주황) 강조색과 맞춘다.</summary>
    private const string BonusColor = "#E8A04A";

    [Header("구성")]
    [Tooltip("켜고 끌 화면 오브젝트. 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.")]
    [SerializeField] private GameObject root;

    [Tooltip("줄 이름 칸(왼쪽 정렬). RowCount개.")]
    [SerializeField] private TMP_Text[] nameLabels = new TMP_Text[RowCount];

    [Tooltip("줄 값 칸(오른쪽 정렬). RowCount개.")]
    [SerializeField] private TMP_Text[] valueLabels = new TMP_Text[RowCount];

    [Header("참조 (비어 있으면 씬에서 찾는다)")]
    [SerializeField] private RelicInventory inventory;
    [SerializeField] private InventoryScreen inventoryScreen;
    [SerializeField] private BossKeyScreen bossKeyScreen;

    // 값을 읽어 올 플레이어 컴포넌트. 플레이어가 프리팹 인스턴스라 인스펙터로 미리 못 걸어서,
    // 처음 열 때 RelicInventory가 붙은 오브젝트(= 플레이어)에서 찾는다.
    private Health health;
    private PlayerStamina stamina;
    private SkillController skills;
    private PlayerController movement;
    private AshGauge ashGauge;

    private bool isOpen;

    /// <summary>지금 열려 있는가.</summary>
    public bool IsOpen => isOpen;

    private void Awake()
    {
        // 꺼져 있는 순간에도 찾아야 해서 비활성 오브젝트까지 포함한다(인벤토리 화면과 같은 이유).
        if (inventory == null)
            inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        if (inventoryScreen == null)
            inventoryScreen = FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);
        if (bossKeyScreen == null)
            bossKeyScreen = FindFirstObjectByType<BossKeyScreen>(FindObjectsInactive.Include);

        // 이 화면이 둘이면 둘 다 C를 듣고 각자 PauseGate에 들어가는데, 다른 화면은 하나만 찾아 닫는다.
        // 남은 하나가 스택에서 안 빠져 시간이 멈춘 채 돌아오지 않는다(인벤토리 화면에서 실제로 겪은 사고).
        var duplicates = FindObjectsByType<StatsScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (duplicates.Length > 1)
        {
            Debug.LogError($"[능력치] 씬에 능력치 창이 {duplicates.Length}개 있다. 하나만 남겨라. " +
                           "Tools → 재의 길 → 화면 → 능력치 화면 생성 을 다시 실행하면 정리된다.", this);
        }

        // 꺼진 오브젝트는 Update가 안 돌아서, 자기 자신을 끄면 키를 눌러도 다시 켤 수 없다.
        if (root == gameObject)
        {
            Debug.LogError("[능력치] root가 이 오브젝트 자신이라 한 번 닫히면 다시 열 수 없다. " +
                           "화면 오브젝트를 자식으로 두고 그것을 연결해라.", this);
        }

        // 줄 이름은 바뀌지 않으므로 한 번만 쓴다.
        for (int i = 0; i < nameLabels.Length && i < RowNames.Length; i++)
            if (nameLabels[i] != null) nameLabels[i].text = RowNames[i];

        isOpen = false;
        if (root != null) root.SetActive(false);
    }

    private void OnDisable()
    {
        // 열린 채로 꺼지면(씬 전환 등) 멈춤 스택에서 빠져야 시간이 다시 흐른다.
        if (isOpen)
        {
            isOpen = false;
            PauseGate.Close(this);
        }
    }

    private void Update()
    {
        // 스킵 확인 창은 모달이라, 그 뒤에서 다른 창이 열리면 멈춤 스택이 꼬인다(다른 화면과 같은 규칙).
        if (BossIntroSkipDialog.IsOpen || BossIntroSkipDialog.InputConsumedThisFrame) return;

        // timeScale이 0이어도 입력은 실제 시간으로 들어온다. 그래서 멈춘 상태에서도 닫을 수 있다.
        if (InputBindings.StatsAction.WasPressedThisFrame()) SetOpen(!isOpen);
    }

    /// <summary>
    /// 화면을 열거나 닫는다.
    /// 순서가 중요하다: <b>먼저 PauseGate에 들어간 뒤</b> 상대 화면을 닫는다. 반대로 하면 스택이
    /// 잠깐 비어 그 사이에 시간이 풀릴 수 있다(InventoryScreen.SetOpen 주석과 같은 이유).
    /// </summary>
    private void SetOpen(bool open)
    {
        isOpen = open;

        if (open) PauseGate.Open(this);

        if (open)
        {
            if (inventoryScreen != null) inventoryScreen.CloseForSwitch();
            if (bossKeyScreen != null) bossKeyScreen.CloseForSwitch();
        }

        if (root != null) root.SetActive(open);

        if (!open) PauseGate.Close(this);

        if (open) Redraw();
    }

    /// <summary>
    /// 다른 화면(인벤토리·보스 열쇠)으로 전환하느라 이 화면을 닫는다.
    /// 상대가 먼저 PauseGate에 들어온 뒤 불리므로, 여기서 빠져도 시간이 풀리지 않는다.
    /// </summary>
    public void CloseForSwitch()
    {
        if (!isOpen) return;

        isOpen = false;
        if (root != null) root.SetActive(false);

        PauseGate.Close(this);
    }

    /// <summary>
    /// 플레이어 컴포넌트를 찾는다. 처음 열 때 한 번만 찾고, 플레이어가 바뀌었으면(죽고 다시 시작 등) 다시 찾는다.
    /// 매 프레임 찾지 않는 이유: GetComponent는 싸지만 Update에서 반복할 이유가 없다. 창을 열 때만 필요하다.
    /// </summary>
    private void ResolveSources()
    {
        if (inventory == null)
            inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        if (inventory == null) return;

        // RelicInventory.Awake와 똑같이 같은 오브젝트(= 플레이어)에서 찾는다.
        // 보정을 넣는 쪽과 읽는 쪽이 같은 컴포넌트를 봐야 숫자가 어긋나지 않는다.
        if (health == null) health = inventory.GetComponent<Health>();
        if (stamina == null) stamina = inventory.GetComponent<PlayerStamina>();
        if (skills == null) skills = inventory.GetComponent<SkillController>();
        if (movement == null) movement = inventory.GetComponent<PlayerController>();
        if (ashGauge == null) ashGauge = inventory.GetComponent<AshGauge>();
    }

    /// <summary>
    /// 일곱 줄의 값을 다시 쓴다. 창을 열 때만 부른다 — 열려 있는 동안은 시간이 멈춰 있어서
    /// 유물이 바뀔 일이 없으므로 매 프레임 갱신할 필요가 없다(매 프레임 문자열을 만들면 GC가 생긴다).
    /// </summary>
    private void Redraw()
    {
        ResolveSources();

        // 순서는 RowNames와 같다.
        SetValue(0, health != null ? Format(health.Max, health.BonusMax, "0") : "-");
        SetValue(1, stamina != null ? Format(stamina.Max, stamina.BonusMax, "0") : "-");
        SetValue(2, stamina != null
            ? Format(stamina.RegenPerSecond, stamina.BonusRegenPerSecond, "0.#", "/초") : "-");

        // 스킬 피해는 스킬마다 기본값이 달라서 하나의 총합을 보여줄 수 없다. 모든 스킬에 더해지는 보너스만 보여준다.
        SetValue(3, skills != null ? Colorize($"+{skills.BonusDamage}", skills.BonusDamage != 0) : "-");

        SetValue(4, movement != null ? Format(movement.MoveSpeed, movement.BonusMoveSpeed, "0.#") : "-");

        // 쿨타임 보정은 곱셈(CooldownScale)이라, 줄어든 비율을 퍼센트로 보여준다. 0.88 → -12%.
        if (skills != null)
        {
            float reduction = (1f - skills.CooldownScale) * 100f;
            SetValue(5, Colorize($"-{reduction:0}%", reduction > 0.5f));
        }
        else SetValue(5, "-");

        SetValue(6, ashGauge != null ? Format(ashGauge.ChargePerKill, ashGauge.BonusChargePerKill, "0.#") : "-");
    }

    /// <summary>값 칸 하나에 글자를 쓴다. 칸이 비어 있으면(빌더를 안 돌렸으면) 조용히 넘어간다.</summary>
    private void SetValue(int row, string text)
    {
        if (row < valueLabels.Length && valueLabels[row] != null) valueLabels[row].text = text;
    }

    /// <summary>
    /// "총값 (+보너스)" 모양으로 만든다. 보너스가 0이면 괄호를 빼서 줄을 짧게 둔다.
    /// 보너스만 주황색으로 칠해 "유물이 올려 준 만큼"이 한눈에 보이게 한다.
    /// </summary>
    private static string Format(float total, float bonus, string format, string unit = "")
    {
        string text = total.ToString(format) + unit;
        if (Mathf.Approximately(bonus, 0f)) return text;

        string sign = bonus > 0f ? "+" : string.Empty;
        return $"{text} <color={BonusColor}>({sign}{bonus.ToString(format)})</color>";
    }

    /// <summary>보너스가 있을 때만 주황색으로 칠한다.</summary>
    private static string Colorize(string text, bool highlighted)
        => highlighted ? $"<color={BonusColor}>{text}</color>" : text;
}
