using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — 방 클리어 뒤 나타나는 보상 상자다.
/// 플레이어가 범위 안에서 F를 누르면 열린 모습으로 바뀌고 문 개방 이벤트를 보낸다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class RewardChest : MonoBehaviour
{
    [Header("상자 모습")]
    [Tooltip("닫힌 상자 오브젝트.")]
    [SerializeField] private GameObject closedVisual;

    [Tooltip("열린 상자 오브젝트.")]
    [SerializeField] private GameObject openVisual;

    [Header("상호작용")]
    [Tooltip("상자를 여는 키. E는 플레이어 스킬이므로 기본값은 F다.")]
    [SerializeField] private Key interactionKey = Key.F;

    // 추가 생성 — 상자는 나중에 룬 아이템을 주더라도 방 사이 생존을 보조하는 회복을 함께 준다.
    [Header("체력 회복 보너스")]
    [Tooltip("상자를 열 때 추첨할 최소 회복량.")]
    [SerializeField, Min(0)] private int minimumHeal = 1;

    [Tooltip("상자를 열 때 추첨할 최대 회복량. 정수 Random.Range의 상한을 포함하도록 처리한다.")]
    [SerializeField, Min(0)] private int maximumHeal = 3;

    private readonly HashSet<Collider2D> playerColliders = new HashSet<Collider2D>();
    private Collider2D interactionTrigger;

    /// <summary>상자가 실제로 열렸을 때 한 번 발생한다.</summary>
    public event Action Opened;

    /// <summary>이미 열린 상자인가.</summary>
    public bool IsOpened { get; private set; }

    private void Awake()
    {
        interactionTrigger = GetComponent<Collider2D>();
        interactionTrigger.isTrigger = true;
        ApplyVisual();
    }

    private void OnDisable()
    {
        playerColliders.Clear();
    }

    private void Update()
    {
        if (IsOpened || playerColliders.Count == 0) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[interactionKey].wasPressedThisFrame)
            Open();
    }

    /// <summary>
    /// 방 진행 상태에 맞춰 상자를 보이거나 숨긴다.
    /// 다시 숨길 때 닫힌 상태로 초기화하므로 재사용해도 안전하다.
    /// </summary>
    public void SetAvailable(bool available)
    {
        if (!available)
        {
            IsOpened = false;
            playerColliders.Clear();
            ApplyVisual();
        }

        gameObject.SetActive(available);

        if (available && interactionTrigger != null)
            interactionTrigger.enabled = true;
    }

    /// <summary>추가 생성 — 상자를 열고 방에 문 개방을 요청한다.</summary>
    public void Open()
    {
        if (IsOpened) return;

        // 상호작용 범위를 비우기 전에 플레이어를 확보해야 회복 대상을 잃지 않는다.
        PlayerController player = FindNearbyPlayer();

        IsOpened = true;
        playerColliders.Clear();

        if (interactionTrigger != null)
            interactionTrigger.enabled = false;

        ApplyVisual();
        ApplyRandomHealing(player);
        Debug.Log("[보상 상자] F 상호작용 — 상자를 열었다.", this);
        Opened?.Invoke();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (FindPlayer(other) != null)
            playerColliders.Add(other);
    }

    // 수정(상호작용 누락 방지): 상자가 플레이어와 겹친 자리에서 활성화되는 경우
    // Enter 이벤트를 놓칠 수 있으므로 Stay에서도 같은 플레이어를 보강 등록한다.
    private void OnTriggerStay2D(Collider2D other)
    {
        if (FindPlayer(other) != null)
            playerColliders.Add(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        playerColliders.Remove(other);
    }

    /// <summary>프리팹 자식 콜라이더가 들어와도 플레이어 루트까지 확인한다.</summary>
    private static PlayerController FindPlayer(Collider2D other)
    {
        if (other == null) return null;
        return other.GetComponentInParent<PlayerController>();
    }

    /// <summary>현재 상호작용 범위 안에 있는 플레이어 한 명을 찾는다.</summary>
    private PlayerController FindNearbyPlayer()
    {
        foreach (Collider2D playerCollider in playerColliders)
        {
            PlayerController player = FindPlayer(playerCollider);
            if (player != null) return player;
        }

        return null;
    }

    /// <summary>
    /// 추가 생성 — 상자를 연 플레이어에게 설정된 범위의 무작위 체력을 즉시 회복한다.
    /// 실제 회복량은 최대 체력에서 잘릴 수 있으며, 룬 보상과는 독립적인 보너스 효과다.
    /// </summary>
    private void ApplyRandomHealing(PlayerController player)
    {
        if (player == null)
        {
            Debug.LogWarning("[보상 상자] 상자를 연 플레이어를 찾지 못해 체력을 회복하지 못했다.", this);
            return;
        }

        Health health = player.GetComponent<Health>();
        if (health == null)
        {
            Debug.LogWarning("[보상 상자] 플레이어에 Health가 없어 체력을 회복하지 못했다.", player);
            return;
        }

        int lower = Mathf.Min(minimumHeal, maximumHeal);
        int upper = Mathf.Max(minimumHeal, maximumHeal);
        int rolledAmount = UnityEngine.Random.Range(lower, upper + 1);
        int before = health.Current;

        health.Heal(rolledAmount);

        int actualAmount = health.Current - before;
        if (actualAmount > 0)
        {
            Debug.Log(
                $"[보상 상자] 체력 {actualAmount} 회복 (추첨 {rolledAmount}) — " +
                $"현재 {health.Current}/{health.Max}",
                this);
        }
        else
        {
            Debug.Log($"[보상 상자] 회복량 {rolledAmount} 추첨 — 이미 최대 체력이다.", this);
        }
    }

#if UNITY_EDITOR
    /// <summary>인스펙터에서 최대값을 최소값보다 작게 입력해도 유효한 범위로 보정한다.</summary>
    private void OnValidate()
    {
        minimumHeal = Mathf.Max(0, minimumHeal);
        maximumHeal = Mathf.Max(minimumHeal, maximumHeal);
    }
#endif

    /// <summary>닫힘/열림 두 오브젝트 중 현재 상태에 맞는 하나만 표시한다.</summary>
    private void ApplyVisual()
    {
        if (closedVisual != null) closedVisual.SetActive(!IsOpened);
        if (openVisual != null) openVisual.SetActive(IsOpened);
    }
}
