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

    // 수정(확률 종속 버그): 툴팁이 열쇠 3개 기준이라 지금 구성(4개)과 안 맞았다.
    // 열쇠 판정이 일반 유물 판정보다 먼저 돌게 바뀐 것도 같이 적는다.
    [Tooltip("열쇠가 나올 확률(0~1). 열쇠를 먼저 판정하고, 실패하면 평범한 유물로 넘어간다. " +
             "0.25면 상자 넷에 하나꼴이라 열쇠 4개를 모으는 데 기대값으로 상자 16개가 든다.")]
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

    // 추가 생성 — 이번에 띄운 픽업. 방을 나갈 때 치우려고 들고 있는다.
    private RelicPickup spawned;

    private void Awake()
    {
        chest = GetComponent<RewardChest>();
    }

    // 수정(문 개방 시점): Claimed가 아니라 Opened를 듣는다.
    // Claimed는 이제 "플레이어가 유물을 실제로 주웠다"는 신호라, 그걸 들으면 유물을 띄울
    // 차례가 영영 오지 않는다(내가 띄워야 그 신호가 나온다).
    private void OnEnable()
    {
        if (chest != null) chest.Opened += OnChestOpened;
    }

    private void OnDisable()
    {
        if (chest != null) chest.Opened -= OnChestOpened;

        // 추가 생성 — 방을 나가거나 방이 초기화되면 안 주운 유물을 치운다.
        //
        // 예전에는 픽업을 부모 없이 만들어서 방이 꺼져도 씬에 그대로 남았다. 방을 돌수록
        // 안 주운 유물이 쌓이고, 이전 방 자리에 떠 있는 물건을 나중에 지나가다 줍기도 했다.
        if (spawned != null)
        {
            Destroy(spawned.gameObject);
            spawned = null;
        }
    }

    private void OnChestOpened()
    {
        // 수정(열쇠 확률 종속): 열쇠 판정을 일반 유물 판정보다 <b>먼저</b> 한다.
        //
        // 예전에는 pool 검사와 chance 판정을 통과해야 여기까지 왔다. 그래서 실제 열쇠 확률이
        // chance × keyChance가 되고, 일반 유물 풀이 비어 있으면 열쇠는 아예 안 나왔다.
        // 열쇠는 <b>진행에 반드시 필요한 물건</b>이라 다른 보상의 확률에 얹혀 있으면 안 된다.
        RelicData key = TryPickKey();
        if (key != null)
        {
            Give(key);
            return;
        }

        if (pool == null || pool.Length == 0) return;
        if (Random.value > chance) return;

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

            // 수정(방 넘어 잔존): 상자를 부모로 붙인다. 방이 꺼지면 같이 꺼지고,
            // 방을 나갈 때 OnDisable이 확실히 치운다.
            //
            // Instantiate의 부모 인자를 쓰지 않고 SetParent(true)로 붙이는 이유:
            // 상자 오브젝트의 스케일이 1이 아니면 부모 인자로 붙일 때 픽업이 같이 늘어난다.
            // worldPositionStays가 true면 화면에 보이는 크기와 자리가 그대로 유지된다.
            spawned = Instantiate(pickupPrefab, transform.position, Quaternion.identity);
            spawned.transform.SetParent(transform, true);

            // 수정(문 개방 시점): 유물을 주울 때까지 문 개방을 미룬다.
            // 상자를 여는 것과 보상을 손에 넣는 것은 다른 사건이고, 방이 알아야 하는 건 뒤쪽이다.
            chest.HoldClaim();
            spawned.Setup(relic, direction, OnPickupCollected);
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
    /// 추가 생성 — 튀어나온 유물을 플레이어가 실제로 주웠을 때 상자의 보류를 푼다.
    ///
    /// 이 시점이 중요하다. 방은 여기서 문 종류를 정하는데, 네 번째 열쇠가 인벤토리에
    /// 들어간 뒤라야 <b>그 자리에서</b> 부서진 문이 열린다. 예전에는 상자를 여는 순간
    /// 문 종류가 정해져서, 열쇠를 다 모으고도 방을 하나 더 돌아야 보스로 갈 수 있었다.
    /// </summary>
    private void OnPickupCollected()
    {
        spawned = null;
        if (chest != null) chest.ReleaseClaim();
    }

    /// <summary>
    /// 플레이어의 인벤토리를 찾는다.
    ///
    /// Include가 필요하다. 꺼져 있는 순간(연출 중 등)에 못 찾으면 유물이 조용히 사라진다.
    /// </summary>
    private static RelicInventory FindInventory()
        => FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
}
