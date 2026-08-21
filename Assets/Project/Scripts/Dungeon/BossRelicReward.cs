using UnityEngine;

/// <summary>
/// 추가 생성 — 보스를 잡으면 클리어 전용 유물을 떨어뜨리는 보상.
///
/// 상자가 아니라 유물을 바로 떨어뜨리는 이유:
/// 보스를 잡은 직후에 상자를 한 번 더 열게 하면 절정 뒤에 사무 절차가 하나 끼는 꼴이 된다.
/// 보스가 쓰러진 자리에서 전리품이 튀어나오는 편이 "잡았다"와 "받았다"가 한 동작으로 이어진다.
///
/// 유물을 주는 즉시 판을 끝내지 않고 <b>문을 여는</b> 이유:
/// 즉시 결과 화면으로 넘기면 무엇을 받았는지 볼 시간이 없다. 유물을 줍고, 숫자를 확인하고,
/// 제 발로 문을 나가는 것까지가 클리어다.
///
/// 획득 감지를 <see cref="RelicPickup"/>이 아니라 <see cref="RelicInventory.Gained"/>로 하는 이유:
/// 줍는 처리는 이미 인벤토리가 끝까지 책임진다(굴림·장착·알림). 그 뒤에 오는 신호를 듣는 편이
/// 픽업 쪽에 이벤트를 새로 뚫는 것보다 건드리는 파일이 적다.
/// </summary>
[DisallowMultipleComponent]
public class BossRelicReward : RoomReward
{
    [Header("보상 유물")]
    [Tooltip("보스를 잡으면 떨어뜨릴 클리어 전용 유물. 역할이 RunEnd인 것을 꽂는다.")]
    [SerializeField] private RelicData clearRelic;

    [Tooltip("바닥에 떨어질 유물 오브젝트. 상자 보상과 같은 프리팹을 쓴다.")]
    [SerializeField] private RelicPickup pickupPrefab;

    [Tooltip("유물이 튀어나올 자리. 비우면 이 오브젝트 자리에서 나온다.")]
    [SerializeField] private Transform dropPoint;

    [Header("튀어나오기")]
    [Tooltip("튀어나갈 방향(도). 90이면 위쪽이다.")]
    [SerializeField] private float launchAngle = 90f;

    private RelicPickup dropped;
    private RelicInventory inventory;
    private bool claimed;

    private void OnDisable()
    {
        // 방을 나가면 구독을 끊는다. 안 끊으면 다른 방에서 같은 유물을 주웠을 때
        // 이 방의 문이 열린 것으로 처리된다.
        Unsubscribe();
    }

    /// <summary>
    /// 보상을 내놓거나 거둔다.
    ///
    /// <paramref name="available"/>가 false면 방을 처음 상태로 되돌린다. 방은 껐다 켜서
    /// 재사용하므로, 지난 판에 떨어뜨린 유물이 남아 있으면 다음 입장 때 공짜로 하나 더 먹는다.
    /// </summary>
    public override void SetAvailable(bool available)
    {
        if (!available)
        {
            ResetReward();
            return;
        }

        // 이미 떨어뜨렸거나 이미 챙긴 뒤라면 다시 떨어뜨리지 않는다.
        if (claimed || dropped != null) return;

        if (clearRelic == null || pickupPrefab == null)
        {
            Debug.LogError("[보스 보상] 클리어 유물 또는 픽업 프리팹이 비어 있다. " +
                           "보스를 잡아도 아무것도 안 나온다.", this);
            return;
        }

        Transform point = dropPoint != null ? dropPoint : transform;
        dropped = Instantiate(pickupPrefab, point.position, Quaternion.identity, transform);

        float radians = launchAngle * Mathf.Deg2Rad;
        dropped.Setup(clearRelic, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));

        Subscribe();
        Debug.Log($"[보스 보상] 클리어 유물 '{clearRelic.DisplayName}'을 떨어뜨렸다. " +
                  "주우면 문이 열린다.", this);
    }

    /// <summary>인벤토리의 획득 알림을 듣기 시작한다. 인벤토리는 플레이어에 붙어 있어 미리 못 꽂는다.</summary>
    private void Subscribe()
    {
        if (inventory != null) return;

        inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        if (inventory == null)
        {
            Debug.LogError("[보스 보상] RelicInventory를 못 찾았다. 유물을 주워도 문이 안 열린다.", this);
            return;
        }

        inventory.Gained += OnRelicGained;
    }

    private void Unsubscribe()
    {
        if (inventory == null) return;

        inventory.Gained -= OnRelicGained;
        inventory = null;
    }

    /// <summary>클리어 유물을 주웠을 때만 방에 알린다. 다른 유물은 무시한다.</summary>
    private void OnRelicGained(RelicData relic)
    {
        if (claimed || relic != clearRelic) return;

        claimed = true;
        dropped = null;
        Unsubscribe();

        Debug.Log("[보스 보상] 클리어 유물 획득 — 문을 연다.", this);
        RaiseClaimed();
    }

    /// <summary>떨어뜨린 유물을 치우고 처음 상태로 되돌린다.</summary>
    private void ResetReward()
    {
        Unsubscribe();

        if (dropped != null)
        {
            Destroy(dropped.gameObject);
            dropped = null;
        }

        claimed = false;
    }
}
