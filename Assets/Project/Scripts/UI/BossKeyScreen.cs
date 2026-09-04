using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — 보스 열쇠 전용 화면. `T`로 여닫는다.
///
/// 인벤토리 안에 넣지 않고 화면을 따로 둔 이유:
/// 인벤토리 패널은 이미 보관함과 장착 칸으로 가로를 다 쓰고 있다. 기준 해상도가 640x360이라
/// 여기에 열쇠 칸 넷을 더 붙이면 어느 쪽도 제대로 안 보인다. 그리고 열쇠는 전투 중에 계속
/// 들여다볼 정보가 아니라 <b>진행 상황을 확인하는 정보</b>라 성격도 다르다.
///
/// 조작:
/// 칸을 누르면 열쇠를 빼서 보관함으로 돌려보낸다. <b>사라지지 않는다</b>는 것이 중요하다 —
/// 열쇠는 판 진행에 필요한 물건이라 잘못 눌러 잃어버리면 그 판을 되돌릴 방법이 없다.
/// 다시 끼우는 것은 인벤토리(I)에서 그 유물을 누르면 된다. 보관함에서 누른 열쇠는
/// 일반 장착 칸이 아니라 이 화면의 칸으로 들어간다.
///
/// <b>한계</b>: 이 화면은 눌러야만 보인다. 열쇠를 몇 개 모았는지가 평소에 안 보이면
/// "왜 보스가 안 나오지"를 알 방법이 없으므로, HUD에 작은 개수 표시를 따로 두는 것이 좋다.
/// </summary>
[DisallowMultipleComponent]
public class BossKeyScreen : MonoBehaviour
{
    [Header("구성")]
    [Tooltip("켜고 끌 화면 오브젝트. 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.")]
    [SerializeField] private GameObject root;

    [Tooltip("보스 열쇠 칸. 패널 그림의 팔각형 위치에 맞춰 놓는다.")]
    [SerializeField] private RelicSlotView[] bossSlots = new RelicSlotView[RelicInventory.BossSlotCount];

    [Tooltip("고른 열쇠의 이름. 없어도 동작한다.")]
    [SerializeField] private TMPro.TMP_Text nameLabel;

    [Tooltip("고른 열쇠의 설명. 없어도 동작한다.")]
    [SerializeField] private TMPro.TMP_Text descriptionLabel;

    [Tooltip("모은 개수 표시. 없어도 동작한다.")]
    [SerializeField] private TMPro.TMP_Text countLabel;

    [Header("참조 (비어 있으면 씬에서 찾는다)")]
    [SerializeField] private RelicInventory inventory;

    // 수정(화면 전환): 역할이 "동시에 열리는지 감시"에서 "전환할 상대"로 바뀌었다.
    [Tooltip("인벤토리 화면. T를 누르면 저 화면에서 이 화면으로 전환된다.")]
    [SerializeField] private InventoryScreen inventoryScreen;

    // 수정(입력 중앙화): 액션을 직접 만들지 않고 InputBindings에서 꺼내 쓴다.
    private bool isOpen;

    /// <summary>지금 열려 있는가.</summary>
    public bool IsOpen => isOpen;

    private void Awake()
    {
        // 수정(입력 중앙화): 여기서 만들던 T 바인딩은 InputBindings.Build로 옮겼다.
        if (inventory == null)
        {
            // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
            inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        }

        if (inventoryScreen == null)
        {
            inventoryScreen = FindFirstObjectByType<InventoryScreen>(FindObjectsInactive.Include);
        }

        for (int i = 0; i < bossSlots.Length; i++)
        {
            if (bossSlots[i] == null) continue;

            int slot = i;
            bossSlots[i].Bind(() => OnSlotClicked(slot), () => ShowInfo(GetBossKey(slot)));
        }

        // 켜고 끌 오브젝트가 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.
        // 꺼진 오브젝트는 Update가 안 돌아서, 키를 눌러도 스스로를 다시 켤 수 없다.
        // 추가 생성(화면 중복) — 씬에 이 화면이 둘 이상이면 여기서 잡는다.
        //
        // <see cref="InventoryScreen"/>에 적어둔 것과 같은 사고다. 둘이면 둘 다 T 키를 듣고
        // 각자 <see cref="PauseGate"/>에 들어가는데 인벤토리는 하나만 찾아 닫는다. 남은 하나가
        // 스택에서 안 빠져서 <b>timeScale이 0에 고정되고 돌아오지 않는다.</b>
        //
        // 저쪽은 실제로 터졌고 이쪽은 아직 안 터졌을 뿐이라 같이 막는다.
        // Awake에서 한 번만 도는 검사라 매 프레임 비용은 없다.
        var duplicates = FindObjectsByType<BossKeyScreen>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (duplicates.Length > 1)
        {
            Debug.LogError($"[보스 열쇠] 씬에 보스 열쇠 화면이 {duplicates.Length}개 있다. 하나만 남겨라. " +
                           "둘 이상이면 T와 I를 번갈아 누를 때 시간이 멈춘 채로 돌아오지 않는다. " +
                           "Tools → 재의 길 → 보스 열쇠 화면 생성 을 다시 실행하면 정리된다.", this);
        }

        if (root == gameObject)
        {
            Debug.LogError("[보스 열쇠] root가 이 오브젝트 자신이라 한 번 닫히면 다시 열 수 없다. " +
                           "화면 오브젝트를 자식으로 두고 그것을 연결해라.", this);
        }

        // Time.timeScale은 건드리지 않는다. 시작할 때 1로 덮으면 다른 곳에서 멈춰둔 것까지 푼다.
        isOpen = false;
        if (root != null) root.SetActive(false);
    }

    private void OnDisable()
    {

        // 수정(PauseGate): 시간을 직접 되돌리지 않고 스택에서 빠지기만 한다.
        // 여기서 1로 덮으면 이 화면 위에 겹쳐 열린 화면까지 시간이 풀린다.
        if (isOpen)
        {
            isOpen = false;
            PauseGate.Close(this);
        }
    }

    // 수정(화면 전환): 인벤토리를 감시하다 스스로 닫던 코드를 걷어냈다.
    //
    // 그 방식에는 두 가지 문제가 있었다.
    // - 닫으면서 timeScale을 1로 되돌려, 인벤토리를 연 채로 시간이 다시 흘렀다.
    //   막으려던 상황("멈춘 화면 뒤에서 적이 움직인다")이 전환 경로로 그대로 일어났다.
    // - 인벤토리가 열려 있으면 여기서 return해버려 T 입력이 검사되지도 않았다.
    //   그래서 T→I는 되는데 I→T는 안 되는 한쪽만 되는 전환이 됐다.
    //
    // 이제는 <b>여는 쪽이 상대를 닫는다</b>는 규칙 하나로 양쪽이 같게 동작한다.
    private void Update()
    {
        // timeScale이 0이어도 입력은 실제 시간으로 들어온다. 그래서 멈춘 상태에서도 닫을 수 있다.
        if (InputBindings.BossKeysAction.WasPressedThisFrame()) SetOpen(!isOpen);
    }

    private void SetOpen(bool open)
    {
        // 추가 생성 — 인벤토리가 열려 있으면 닫고 이 화면으로 바꾼다.
        //
        // 수정(PauseGate): 순서를 <b>먼저 열고 뒤에 닫기</b>로 바꿨다. 상대를 먼저 닫으면
        // 스택이 잠깐 비어 시간이 풀렸다 다시 멈추는 모양이 된다. 인벤토리 쪽과 같은 순서다.
        isOpen = open;

        if (open) PauseGate.Open(this);

        if (open && inventoryScreen != null) inventoryScreen.CloseForSwitch();

        if (root != null) root.SetActive(open);

        // 시간은 PauseGate가 잡는다. 인벤토리와 같은 규칙이라 조작감이 어긋나지 않는다.
        if (!open) PauseGate.Close(this);

        if (open) Redraw();
        else ShowInfo(RelicInstance.None);
    }

    /// <summary>
    /// 추가 생성 — 인벤토리로 전환하느라 이 화면을 닫는다.
    ///
    /// <b>전환 중에 시간이 풀리면 안 된다.</b> 전환은 멈춘 상태를 유지한 채 보는 것만
    /// 바꾸는 동작이라, 닫는 쪽이 시간을 풀면 여는 쪽이 다시 멈추기 전까지 게임이 흐른다.
    ///
    /// 수정(PauseGate): 상대가 <b>먼저 스택에 들어온 뒤</b>에 이 함수가 불리므로,
    /// 여기서 빠져도 스택이 비지 않는다.
    /// </summary>
    public void CloseForSwitch()
    {
        if (!isOpen) return;

        isOpen = false;
        if (root != null) root.SetActive(false);

        PauseGate.Close(this);

        ShowInfo(RelicInstance.None);
    }

    /// <summary>열쇠 칸을 화면에 다시 그린다.</summary>
    private void Redraw()
    {
        if (inventory == null || !isOpen) return;

        for (int i = 0; i < bossSlots.Length; i++)
        {
            if (bossSlots[i] == null) continue;
            bossSlots[i].Show(inventory.GetBossKey(i));
        }

        if (countLabel != null)
            countLabel.text = $"{inventory.BossKeyCount} / {inventory.BossKeysRequired}";
    }

    /// <summary>
    /// 열쇠 칸을 눌렀다. 빼서 보관함으로 돌려보낸다.
    ///
    /// 빼도 사라지지 않는다는 것이 중요하다. 열쇠는 판 진행에 필요한 물건이라
    /// 잘못 눌러서 잃어버리면 그 판을 되돌릴 방법이 없다. 보관함에 있으면
    /// 눌러서 다시 끼울 수 있다.
    /// </summary>
    private void OnSlotClicked(int slot)
    {
        if (inventory == null) return;
        if (inventory.GetBossKey(slot).IsEmpty) return;

        inventory.UnequipBossKey(slot);
        Redraw();
        ShowInfo(RelicInstance.None);
    }

    private RelicInstance GetBossKey(int slot)
        => inventory != null ? inventory.GetBossKey(slot) : RelicInstance.None;

    /// <summary>고른 열쇠의 이름과 설명을 보여준다.</summary>
    private void ShowInfo(RelicInstance item)
    {
        bool has = !item.IsEmpty && item.Data != null;

        if (nameLabel != null) nameLabel.text = has ? item.Data.DisplayName : string.Empty;
        if (descriptionLabel != null) descriptionLabel.text = has ? item.Data.Description : string.Empty;
    }
}
