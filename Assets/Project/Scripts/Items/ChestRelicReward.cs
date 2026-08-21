using UnityEngine;

/// <summary>
/// 보상 상자를 열면 유물을 하나 준다.
///
/// <see cref="RewardChest"/>를 고치지 않고 <b>따로 붙이는 컴포넌트</b>로 만든 이유:
/// 상자는 이미 "열리는 것"과 "회복을 주는 것"을 하고 있다. 거기에 유물까지 넣으면 상자가
/// 보상 종류를 전부 알게 되어, 나중에 보상이 늘 때마다 그 파일을 열어야 한다.
/// 상자는 <c>Opened</c>만 알리고, 무엇을 줄지는 붙이는 쪽이 정하는 편이 갈래가 깔끔하다.
///
/// 유물이 붙어 있어도 기존 회복은 그대로 나간다. 둘은 서로 모른다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RewardChest))]
public class ChestRelicReward : MonoBehaviour
{
    [Tooltip("이 중에서 하나를 뽑는다. 비어 있으면 아무것도 주지 않는다.")]
    [SerializeField] private RelicData[] pool;

    [Tooltip("유물이 나올 확률(0~1). 1이면 항상 나온다.")]
    [Range(0f, 1f)]
    [SerializeField] private float chance = 1f;

    [Header("보스 열쇠")]
    [Tooltip("보스방을 여는 열쇠 유물들. 아직 안 가진 것 중에서만 나온다.")]
    [SerializeField] private RelicData[] keyPool;

    [Tooltip("열쇠가 나올 확률(0~1). 이 판정에 실패하면 평범한 유물이 나온다. " +
             "0.25면 상자 넷에 하나꼴이라 열쇠 3개를 모으는 데 대략 열 방 남짓 걸린다.")]
    [Range(0f, 1f)]
    [SerializeField] private float keyChance = 0.25f;

    [Header("튀어나오기")]
    [Tooltip("상자에서 튀어나올 픽업 프리팹. 비우면 즉시 획득으로 돌아간다.")]
    [SerializeField] private RelicPickup pickupPrefab;

    [Tooltip("튀어나오는 방향의 기준 각도(도). 0이 오른쪽, 90이 위.")]
    [SerializeField] private float launchAngle = 90f;

    [Tooltip("기준 각도에서 좌우로 흔들 범위(도). 매번 조금씩 다른 곳에 떨어진다.")]
    [SerializeField, Min(0f)] private float launchSpread = 50f;

    private RewardChest chest;

    private void Awake()
    {
        chest = GetComponent<RewardChest>();
    }

    private void OnEnable()
    {
        if (chest != null) chest.Claimed += OnChestOpened;
    }

    private void OnDisable()
    {
        if (chest != null) chest.Claimed -= OnChestOpened;
    }

    private void OnChestOpened()
    {
        if (pool == null || pool.Length == 0) return;
        if (Random.value > chance) return;

        // 열쇠를 먼저 판정한다. 실패하거나 남은 열쇠가 없으면 평범한 유물로 넘어간다.
        RelicData key = TryPickKey();
        if (key != null)
        {
            Give(key);
            return;
        }

        // 플레이어를 여기서 찾는 이유: 상자는 씬에 미리 놓이고 플레이어는 프리팹 인스턴스라
        // 인스펙터로 미리 연결할 수 없다. 상자를 여는 건 한 판에 몇 번뿐이라 비용도 무시할 만하다.
        // 균등 추첨이다. 등급이나 가중치를 넣지 않은 이유는 상자 회복량을 균등 1~2로
        // 확정한 것과 같다 — 분포가 복잡해지면 플레이어가 상자를 열 때 뭘 기대할지 모른다.
        Give(pool[Random.Range(0, pool.Length)]);
    }

    /// <summary>
    /// 이번 상자가 열쇠를 줄지 정한다. 안 주면 null.
    ///
    /// <b>이미 가진 열쇠를 후보에서 빼는 것이 핵심이다.</b> 안 빼면 같은 열쇠가 계속 나와서
    /// 확률은 맞는데 진행이 안 되는 상태가 된다. 플레이어 입장에서는 "운이 나쁘다"와
    /// 구별이 안 되는 종류의 버그다.
    ///
    /// 확률 판정을 남은 열쇠 확인보다 먼저 하지 않는 이유도 같다. 다 모은 뒤에도 계속
    /// 판정에 걸리면 그만큼 평범한 유물이 안 나온다.
    /// </summary>
    private RelicData TryPickKey()
    {
        if (keyPool == null || keyPool.Length == 0) return null;

        var inventory = FindInventory();
        if (inventory == null) return null;

        var remaining = new System.Collections.Generic.List<RelicData>();
        foreach (RelicData key in keyPool)
            if (key != null && !inventory.Has(key)) remaining.Add(key);

        if (remaining.Count == 0) return null;
        if (Random.value > keyChance) return null;

        return remaining[Random.Range(0, remaining.Count)];
    }

    /// <summary>유물 하나를 실제로 내보낸다. 픽업이 있으면 튀어나오고, 없으면 즉시 획득한다.</summary>
    private void Give(RelicData relic)
    {
        if (relic == null) return;

        if (pickupPrefab != null)
        {
            // 상자에서 튀어나오게 한다. 획득은 플레이어가 밟을 때 픽업이 처리한다.
            float angle = (launchAngle + Random.Range(-launchSpread, launchSpread)) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            var pickup = Instantiate(pickupPrefab, transform.position, Quaternion.identity);
            pickup.Setup(relic, direction);
            return;
        }

        // 픽업 프리팹이 없으면 예전처럼 즉시 준다. 연출은 없지만 보상은 사라지지 않는다.
        var inventory = FindInventory();
        if (inventory == null)
        {
            Debug.LogWarning("[상자 유물] RelicInventory를 못 찾았다. 플레이어에 붙어 있는지 확인해라.", this);
            return;
        }

        inventory.Acquire(relic);
    }

    /// <summary>
    /// 플레이어의 인벤토리를 찾는다.
    ///
    /// Include가 필요하다. 꺼져 있는 순간(연출 중 등)에 못 찾으면 유물이 조용히 사라진다.
    /// </summary>
    private static RelicInventory FindInventory()
        => FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
}
