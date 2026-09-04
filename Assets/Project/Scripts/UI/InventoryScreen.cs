using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 유물 보관함 화면. I 또는 Tab으로 열고 닫는다.
///
/// <b>여는 동안 시간을 멈춘다.</b> 실시간 액션에서 화면을 띄운 채 시간이 흐르면, 유물을
/// 바꿔 끼우는 동안 적에게 맞는다. 그러면 플레이어는 인벤토리를 "위험을 무릅쓰고 여는 것"으로
/// 배우고, 결국 안 열게 된다. 고를 것이 있는 화면은 고를 시간을 줘야 의미가 있다.
///
/// 칸 오브젝트를 코드가 아니라 에디터 도구(AshInventoryUiBuilder)가 만드는 이유:
/// 패널 그림 위에 팔각형 칸이 그려져 있어서, 칸 위치는 그림에 맞춰 사람이 눈으로 맞춰야 한다.
/// 런타임에 만들면 위치를 고칠 때마다 게임을 실행해서 확인해야 한다.
/// 보관함 칸만 개수가 계속 변하므로 그것만 여기서 만든다.
/// </summary>
[DisallowMultipleComponent]
public class InventoryScreen : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("열고 닫을 화면 전체. 이 오브젝트를 켜고 끈다.")]
    [SerializeField] private GameObject root;

    [Tooltip("보관함 칸이 채워질 자리. GridLayoutGroup이 붙어 있다.")]
    [SerializeField] private RectTransform bagArea;

    [Tooltip("보관함 칸 하나의 원본. 꺼진 채로 두면 이걸 복제해서 쓴다.")]
    [SerializeField] private RelicSlotView bagSlotTemplate;

    [Tooltip("장착 칸. 패널 그림의 팔각형 위치에 맞춰 놓는다.")]
    [SerializeField] private RelicSlotView[] equipSlots = new RelicSlotView[RelicInventory.SlotCount];

    [Tooltip("고른 유물의 이름.")]
    [SerializeField] private TMPro.TMP_Text nameLabel;

    [Tooltip("고른 유물의 설명.")]
    [SerializeField] private TMPro.TMP_Text descriptionLabel;

    [Header("대상 (비어 있으면 실행 시 찾는다)")]
    [SerializeField] private RelicInventory inventory;

    // 추가 생성 — 보스 열쇠 화면. 이 화면을 열 때 저쪽을 닫아 한 번에 하나만 열리게 한다.
    [Tooltip("보스 열쇠 화면. I/Tab을 누르면 저 화면에서 이 화면으로 전환된다.")]
    [SerializeField] private BossKeyScreen bossKeyScreen;

    // 실행 중에 만든 보관함 칸들. 다시 그릴 때 재사용한다.
    private readonly System.Collections.Generic.List<RelicSlotView> bagSlots =
        new System.Collections.Generic.List<RelicSlotView>();

    // 수정(입력 중앙화): 액션을 직접 만들지 않고 InputBindings에서 꺼내 쓴다.
    private bool isOpen;

    /// <summary>화면이 열려 있는가. 다른 시스템이 입력을 무시할 때 읽는다.</summary>
    public bool IsOpen => isOpen;

    private void Awake()
    {
        // 수정(입력 중앙화): 여기서 만들던 I/Tab 바인딩은 InputBindings.Build로 옮겼다.
        if (inventory == null)
        {
            // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
            inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        }

        // 추가 생성 — 인스펙터에 안 꽂혀 있어도 스스로 찾는다.
        // 서로를 찾는 구조지만 Awake에서 필드만 채우고 상대의 상태를 읽지는 않으므로
        // 어느 쪽이 먼저 깨어나도 상관없다.
        if (bossKeyScreen == null)
        {
            bossKeyScreen = FindFirstObjectByType<BossKeyScreen>(FindObjectsInactive.Include);
        }

        if (bagSlotTemplate != null) bagSlotTemplate.gameObject.SetActive(false);

        for (int i = 0; i < equipSlots.Length; i++)
        {
            if (equipSlots[i] == null) continue;

            int slot = i;
            equipSlots[i].Bind(() => OnEquipSlotClicked(slot), () => ShowInfo(GetEquipped(slot)));
        }

        // 켜고 끌 오브젝트가 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.
        // 꺼진 오브젝트는 Update가 안 돌아서, 키를 눌러도 스스로를 다시 켤 수 없다.
        // 에러도 경고도 없이 그냥 안 열리는 종류라 여기서 잡아준다.
        if (root == gameObject)
        {
            Debug.LogError("[인벤토리] root가 이 오브젝트 자신이라 한 번 닫히면 다시 열 수 없다. " +
                           "자식 오브젝트를 root로 넣어라.", this);
        }

        // 추가 생성(화면 중복) — 씬에 이 화면이 둘 이상이면 여기서 잡는다.
        //
        // <b>둘이면 게임이 멈춘 채로 복구가 안 된다.</b> 둘 다 I 키를 듣고 각자
        // <see cref="PauseGate"/>에 들어가는데, 보스 열쇠 화면은 <c>FindFirstObjectByType</c>으로
        // <b>하나만</b> 찾아 닫는다. 남은 하나가 스택에서 안 빠져서 timeScale이 0에 고정되고,
        // 그 뒤로는 I를 눌러도 두 화면이 번갈아 켜지기만 해서 스택이 절대 안 빈다.
        //
        // 원인은 빌더(<c>AshInventoryUiBuilder</c>)에서 막았지만 그건 <b>도구를 다시 돌릴 때만</b>
        // 듣는 방어다. 씬을 손으로 복사하거나 화면을 통째로 붙여 넣으면 같은 상태가 다시 만들어진다.
        // 증상이 "게임이 멈췄다"로만 나타나서 원인을 찾는 데 제일 오래 걸리는 종류라,
        // 실행하는 순간 개수를 대고 알려주는 편이 싸다.
        //
        // Awake에서 한 번만 도는 검사라 매 프레임 비용은 없다.
        var duplicates = FindObjectsByType<InventoryScreen>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (duplicates.Length > 1)
        {
            Debug.LogError($"[인벤토리] 씬에 인벤토리 화면이 {duplicates.Length}개 있다. 하나만 남겨라. " +
                           "둘 이상이면 I와 T를 번갈아 누를 때 시간이 멈춘 채로 돌아오지 않는다. " +
                           "Tools → 재의 길 → 인벤토리 화면 생성 을 다시 실행하면 정리된다.", this);
        }

        // Time.timeScale은 건드리지 않는다. 시작할 때 1로 덮으면 다른 곳에서 멈춰둔 것까지 푼다.
        isOpen = false;
        if (root != null) root.SetActive(false);
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.Changed += Redraw;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= Redraw;

        // 수정(PauseGate): 시간을 직접 되돌리지 않고 스택에서 빠지기만 한다.
        // 여기서 1로 덮으면, 이 화면 위에 설정 화면이 겹쳐 있을 때 그쪽까지 시간이 풀린다.
        // 스택이 비었는지는 PauseGate가 판단한다.
        if (isOpen)
        {
            isOpen = false;
            PauseGate.Close(this);
        }
    }

    private void Update()
    {
        // timeScale이 0이어도 입력은 실제 시간으로 들어온다. 그래서 멈춘 상태에서도 닫을 수 있다.
        if (InputBindings.InventoryAction.WasPressedThisFrame()) SetOpen(!isOpen);
    }

    private void SetOpen(bool open)
    {
        // 추가 생성 — 여는 쪽이 상대 화면을 닫는다.
        //
        // 이렇게 여는 쪽에 책임을 두면 "한 번에 하나만 열린다"가 두 화면 어디서 열든 지켜진다.
        // 예전에는 보스 열쇠 화면이 매 프레임 인벤토리를 감시하다가 스스로 닫았는데,
        // 그때 닫으면서 timeScale까지 1로 되돌려서 <b>인벤토리를 연 채로 시간이 흘렀다.</b>
        //
        // 수정(PauseGate): 시간 제어를 PauseGate로 넘기고, 순서를 <b>먼저 열고 뒤에 닫기</b>로 바꿨다.
        // 상대를 먼저 닫으면 스택이 잠깐 0이 되어 timeScale이 1 → 0으로 튄다. 같은 프레임 안이라
        // 실제로 시간이 흐르진 않지만, "전환 중에는 스택이 안 빈다"를 값으로 보장해두는 편이
        // 나중에 화면을 더 붙였을 때 안전하다.
        isOpen = open;

        if (open) PauseGate.Open(this);

        if (open && bossKeyScreen != null) bossKeyScreen.CloseForSwitch();

        if (root != null) root.SetActive(open);

        // 시간은 PauseGate가 잡는다. 물리가 멈추므로 적의 이동도 피격 판정도 같이 멈춘다.
        if (!open) PauseGate.Close(this);

        if (open) Redraw();
        else ShowInfo(RelicInstance.None);
    }

    /// <summary>
    /// 추가 생성 — 보스 열쇠 화면으로 전환하느라 이 화면을 닫는다.
    ///
    /// <b>전환 중에 시간이 풀리면 안 되는 것이 핵심이다.</b> 전환은 "멈춘 상태를 유지한 채
    /// 보는 것만 바꾸는" 동작이라, 닫는 쪽이 시간을 풀면 여는 쪽이 다시 멈추기 전까지
    /// 한 프레임 동안 게임이 흘러버린다. 화면 뒤에서 적이 한 걸음 움직이는 그 한 프레임이
    /// 실제로는 피격으로 이어진다.
    ///
    /// 수정(PauseGate): 예전에는 "timeScale을 안 건드린다"로 지켰지만, 이제는 상대가
    /// <b>먼저 스택에 들어온 뒤</b>에 이 함수가 불리므로 여기서 빠져도 스택이 안 빈다.
    /// 규칙을 지키는 주체가 주석에서 코드로 옮겨간 셈이다.
    /// </summary>
    public void CloseForSwitch()
    {
        if (!isOpen) return;

        isOpen = false;
        if (root != null) root.SetActive(false);

        PauseGate.Close(this);

        ShowInfo(RelicInstance.None);
    }

    /// <summary>보관함과 장착 칸을 화면에 다시 그린다.</summary>
    private void Redraw()
    {
        if (inventory == null || !isOpen) return;

        // 장착 칸
        for (int i = 0; i < equipSlots.Length; i++)
        {
            if (equipSlots[i] == null) continue;
            equipSlots[i].Show(inventory.GetEquipped(i));
        }

        // 보관함 칸은 개수가 변한다. 모자라면 만들고, 남으면 끄기만 한다.
        // 매번 지우고 새로 만들면 클릭한 프레임에 오브젝트가 사라져서 이벤트가 씹힌다.
        var bag = inventory.Bag;

        while (bagSlots.Count < bag.Count) bagSlots.Add(CreateBagSlot());

        for (int i = 0; i < bagSlots.Count; i++)
        {
            bool used = i < bag.Count;
            bagSlots[i].gameObject.SetActive(used);
            if (used) bagSlots[i].Show(bag[i]);
        }
    }

    private RelicSlotView CreateBagSlot()
    {
        var slot = Instantiate(bagSlotTemplate, bagArea);
        slot.gameObject.SetActive(true);

        int index = bagSlots.Count;
        slot.Bind(() => OnBagSlotClicked(index), () => ShowInfo(GetBag(index)));
        return slot;
    }

    /// <summary>
    /// 보관함 칸을 눌렀다. 빈 장착 칸이 있으면 끼운다.
    ///
    /// 끌어다 놓기가 아니라 클릭으로 만든 이유: 칸이 세 개뿐이라 어디에 넣을지 고를 일이 거의 없다.
    /// 끌기를 넣으면 코드가 몇 배로 늘고, 게임패드에서는 아예 다른 조작을 또 만들어야 한다.
    /// 칸이 다 찼을 때만 어느 것을 뺄지 고르면 되므로, 그건 장착 칸을 눌러 빼는 것으로 충분하다.
    /// </summary>
    private void OnBagSlotClicked(int index)
    {
        if (inventory == null) return;

        // 추가 생성 — 열쇠는 일반 칸이 아니라 보스 칸으로 간다.
        //
        // 여기서 갈라주는 이유: 플레이어는 "보관함에 있는 유물을 누르면 끼워진다"만 알면 된다.
        // 열쇠라고 다른 조작을 요구하면(예: T 화면에서만 끼우기) 왜 안 끼워지는지 알 수 없다.
        // 누르는 동작은 하나로 두고 <b>어디로 갈지는 코드가 판단한다.</b>
        RelicInstance clicked = GetBag(index);
        if (clicked.Data != null && clicked.Data.Role == RelicData.RelicRole.BossKey)
        {
            if (inventory.FindEmptyBossSlot() < 0)
            {
                ShowInfo(clicked, "보스 칸이 가득 찼다. T 화면에서 하나를 빼라.");
                return;
            }

            inventory.EquipBossKey(index);
            Redraw();
            ShowInfo(RelicInstance.None);
            return;
        }

        int empty = inventory.FindEmptySlot();
        if (empty < 0)
        {
            ShowInfo(GetBag(index), "장착 칸이 가득 찼다. 오른쪽 칸을 눌러 빼라.");
            return;
        }

        inventory.Equip(index, empty);
    }

    private void OnEquipSlotClicked(int slot)
    {
        if (inventory != null) inventory.Unequip(slot);
    }

    private RelicInstance GetBag(int index)
    {
        if (inventory == null) return RelicInstance.None;
        return index >= 0 && index < inventory.Bag.Count ? inventory.Bag[index] : RelicInstance.None;
    }

    private RelicInstance GetEquipped(int slot)
        => inventory != null ? inventory.GetEquipped(slot) : RelicInstance.None;

    /// <summary>고른 유물의 이름과 설명을 보여준다.</summary>
    private void ShowInfo(RelicInstance item, string note = null)
    {
        if (nameLabel == null || descriptionLabel == null) return;

        if (item.IsEmpty)
        {
            nameLabel.text = "";
            descriptionLabel.text = note ?? "";
            return;
        }

        nameLabel.text = item.Data.DisplayName;

        // 무작위 유물은 설명에 범위가 적혀 있어서, 이 개체가 얼마로 굴렀는지를 따로 알려준다.
        // 그게 없으면 인벤토리에서 주사위 두 개를 구분할 수 없다.
        // 대괄호를 쓰는 이유: TMP는 <...>를 서식 태그로 읽어서 이 줄이 통째로 사라진다.
        string rolled = item.Data.IsRandom ? $"\n[이 유물: +{item.Amount:0.##}]" : "";

        descriptionLabel.text = item.Data.Description + rolled +
                                (string.IsNullOrEmpty(note) ? "" : $"\n{note}");
    }
}
