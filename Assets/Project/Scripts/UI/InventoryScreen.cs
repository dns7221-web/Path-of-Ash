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

    // 실행 중에 만든 보관함 칸들. 다시 그릴 때 재사용한다.
    private readonly System.Collections.Generic.List<RelicSlotView> bagSlots =
        new System.Collections.Generic.List<RelicSlotView>();

    private InputAction toggleAction;
    private bool isOpen;

    /// <summary>화면이 열려 있는가. 다른 시스템이 입력을 무시할 때 읽는다.</summary>
    public bool IsOpen => isOpen;

    private void Awake()
    {
        // 액션을 코드로 만드는 이유는 SkillController와 같다 — 키 하나짜리 조작을 위해
        // .inputactions 에셋을 열고 저장하는 왕복이 없다.
        toggleAction = new InputAction("Inventory", InputActionType.Button, "<Keyboard>/i");
        toggleAction.AddBinding("<Keyboard>/tab");

        if (inventory == null)
        {
            // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
            inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
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

        // Time.timeScale은 건드리지 않는다. 시작할 때 1로 덮으면 다른 곳에서 멈춰둔 것까지 푼다.
        isOpen = false;
        if (root != null) root.SetActive(false);
    }

    private void OnEnable()
    {
        toggleAction.Enable();
        if (inventory != null) inventory.Changed += Redraw;
    }

    private void OnDisable()
    {
        toggleAction.Disable();
        if (inventory != null) inventory.Changed -= Redraw;

        // 화면을 켠 채로 씬이 바뀌면 시간이 멈춘 채 남는다. 반드시 되돌린다.
        if (isOpen) Time.timeScale = 1f;
    }

    private void Update()
    {
        // timeScale이 0이어도 입력은 실제 시간으로 들어온다. 그래서 멈춘 상태에서도 닫을 수 있다.
        if (toggleAction.WasPressedThisFrame()) SetOpen(!isOpen);
    }

    private void SetOpen(bool open)
    {
        isOpen = open;

        if (root != null) root.SetActive(open);

        // 시간을 멈춘다. 물리가 멈추므로 적의 이동도 피격 판정도 같이 멈춘다.
        Time.timeScale = open ? 0f : 1f;

        if (open) Redraw();
        else ShowInfo(RelicInstance.None);
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
