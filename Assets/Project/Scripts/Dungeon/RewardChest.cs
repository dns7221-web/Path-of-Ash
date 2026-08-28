using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — 방 클리어 뒤 나타나는 보상 상자다.
/// 플레이어가 범위 안에서 F를 누르면 열린 모습으로 바뀌고 문 개방 이벤트를 보낸다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class RewardChest : RoomReward
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
    //
    // 수정(밸런스): 최대 회복량을 3 → 2로 내렸다.
    // 플레이어 최대 체력이 5인데 한 방에 최대 3을 돌려주면, 두 대 맞고 상자를 여는 것만으로
    // 매번 만피가 된다. 그러면 방을 아무리 반복해도 체력이 깎이지 않아서 "무한 반복으로
    // 밸런스를 확인한다"는 목적 자체가 성립하지 않는다. 1~2면 맞은 만큼을 다 돌려받지는
    // 못하므로 판이 진행될수록 체력이 서서히 줄고, 그래야 판이 끝나는 지점이 생긴다.
    [Header("체력 회복 보너스")]
    [Tooltip("상자를 열 때 추첨할 최소 회복량.")]
    [SerializeField, Min(0)] private int minimumHeal = 1;

    [Tooltip("상자를 열 때 추첨할 최대 회복량. 정수 Random.Range의 상한을 포함하도록 처리한다.")]
    [SerializeField, Min(0)] private int maximumHeal = 2;

    [Header("연 뒤 정리")]
    // 추가 생성 — 튜토리얼 방처럼 오래 머무는 곳에서 빈 상자가 계속 남아 있으면 지저분하다.
    //
    // 오브젝트를 파괴하지 않고 그림만 감추는 이유: 이 컴포넌트가 살아 있어야 방을 다시 열 때
    // SetAvailable로 처음 상태로 되돌릴 수 있다. 파괴하면 방 재사용이 깨진다.
    [Tooltip("상자를 연 뒤 이 시간이 지나면 상자를 감춘다(초). 0이면 계속 남는다.")]
    [SerializeField, Min(0f)] private float hideAfterOpenSeconds;

    // 추가 생성 — 보류가 풀리지 않을 때를 대비한 안전장치.
    //
    // 보류를 건 쪽이 어떤 이유로든(픽업이 못 가는 자리에 떨어지는 등) 풀어주지 못하면
    // 문이 영영 안 열려 <b>판을 진행할 수 없는 상태</b>가 된다. 유물 하나를 놓치는 것보다
    // 게임이 멈추는 쪽이 훨씬 나쁘므로, 시간이 지나면 경고를 남기고 문을 연다.
    [Header("보상 보류 안전장치")]
    [Tooltip("보상 보류가 이 시간 안에 안 풀리면 경고를 남기고 문을 연다(초). 0이면 안전장치를 끈다.")]
    [SerializeField, Min(0f)] private float claimHoldTimeoutSeconds = 30f;

    // 감춰진 상태인가. ApplyVisual이 이 값을 존중해야 감춘 뒤 다시 나타나지 않는다.
    private bool hidden;

    private readonly HashSet<Collider2D> playerColliders = new HashSet<Collider2D>();
    private Collider2D interactionTrigger;

    // 수정(보스 방 보상 추가) — 열림 이벤트는 RoomReward.Claimed로 올라갔다.
    // 상자는 "열렸다", 보스 방은 "유물을 주웠다"지만 방이 받는 신호는 하나면 된다.

    // 추가 생성 — 아직 안 풀린 보류의 개수. 0이 되어야 Claimed가 나간다.
    private int claimHolds;

    // 추가 생성 — 보류가 걸린 뒤 Claimed를 이미 내보냈는지. 두 번 열리는 것을 막는다.
    private bool claimRaised;

    /// <summary>
    /// 추가 생성 — 상자가 열린 순간. <b>문이 열리는 시점(Claimed)과 다르다.</b>
    ///
    /// 이 둘을 나눈 이유: 유물을 튀어나오게 하는 <see cref="ChestRelicReward"/>는 "상자가
    /// 열렸다"를 알아야 픽업을 띄울 수 있는데, 방은 "플레이어가 보상을 실제로 손에 넣었다"를
    /// 알아야 문을 연다. 예전에는 두 신호가 하나여서, 유물이 아직 공중에 떠 있는데 문이
    /// 열려 그냥 나가버릴 수 있었다. 더 나쁜 건 네 번째 열쇠를 먹기 <b>전에</b> 문 종류가
    /// 정해져서, 열쇠를 다 모으고도 방을 하나 더 돌아야 부서진 문이 나왔다는 점이다.
    /// </summary>
    public event Action Opened;

    /// <summary>이미 열린 상자인가.</summary>
    public bool IsOpened { get; private set; }

    private void Awake()
    {
        interactionTrigger = GetComponent<Collider2D>();
        interactionTrigger.isTrigger = true;
        ApplyVisual();
    }

    /// <summary>추가 생성 — 상자를 감춘다. 오브젝트는 남겨서 방 재사용이 깨지지 않게 한다.</summary>
    private void HideChest()
    {
        hidden = true;
        ApplyVisual();
        Debug.Log("[보상 상자] 연 상자를 정리했다.", this);
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
    public override void SetAvailable(bool available)
    {
        if (!available)
        {
            IsOpened = false;
            playerColliders.Clear();

            // 추가 생성 — 예약된 정리를 취소하고 감춤도 푼다.
            // 안 풀면 방을 다시 열었을 때 상자가 처음부터 안 보이거나, 열자마자 사라진다.
            CancelInvoke(nameof(HideChest));
            hidden = false;

            // 추가 생성 — 보류 상태도 처음으로 되돌린다.
            // 안 되돌리면 지난 방에서 남은 보류 때문에 다음 방 상자가 문을 못 연다.
            StopCoroutine(nameof(ClaimHoldFailsafe));
            claimHolds = 0;
            claimRaised = false;

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

        // 수정(문 개방 시점): Claimed를 바로 내보내지 않는다.
        // 먼저 Opened로 보상 지급자들을 부르고, 그 안에서 보류가 걸렸으면 기다린다.
        // Opened는 이벤트라 그 자리에서 동기로 돌기 때문에, 아래 줄에 오면 보류 여부가 확정돼 있다.
        Opened?.Invoke();

        if (claimHolds > 0)
        {
            // 추가 생성 — 보류가 안 풀리는 최악의 경우에 대비한다.
            // 문자열 이름으로 시작하는 이유: StopCoroutine(문자열)로 멈추려면 시작도 같은
            // 방식이어야 한다. IEnumerator 참조를 따로 들고 있을 만큼 복잡한 일이 아니다.
            if (claimHoldTimeoutSeconds > 0f) StartCoroutine(nameof(ClaimHoldFailsafe));
        }
        else
        {
            RaiseClaimOnce();
        }

        // 추가 생성 — 설정돼 있으면 잠시 뒤 상자를 치운다.
        // 코루틴 대신 Invoke를 쓴 이유: 대기가 한 번뿐이고 CancelInvoke로 한 줄에 정리된다.
        if (hideAfterOpenSeconds > 0f) Invoke(nameof(HideChest), hideAfterOpenSeconds);
    }

    /// <summary>
    /// 추가 생성 — 문 개방을 잠시 미룬다. 보상을 아직 플레이어가 손에 넣지 않았을 때 쓴다.
    ///
    /// <b>반드시 <see cref="Opened"/> 처리 중에 불러야 한다.</b> 그 뒤에 걸면 이미 문이 열린 뒤다.
    /// 개수를 세는 이유: 나중에 상자에 보상이 둘 이상 붙어도 각자 자기 것만 풀면 되게 하기 위해서다.
    /// </summary>
    public void HoldClaim()
    {
        if (claimRaised) return;
        claimHolds++;
    }

    /// <summary>추가 생성 — 보류를 푼다. 남은 보류가 없으면 그 자리에서 문이 열린다.</summary>
    public void ReleaseClaim()
    {
        if (claimRaised) return;

        claimHolds = Mathf.Max(0, claimHolds - 1);
        if (claimHolds > 0) return;

        StopCoroutine(nameof(ClaimHoldFailsafe));
        RaiseClaimOnce();
    }

    /// <summary>추가 생성 — Claimed를 한 번만 내보낸다. 안전장치와 정상 경로가 겹쳐도 문은 한 번만 열린다.</summary>
    private void RaiseClaimOnce()
    {
        if (claimRaised) return;

        claimRaised = true;
        claimHolds = 0;
        RaiseClaimed();
    }

    /// <summary>
    /// 추가 생성 — 보류가 제때 안 풀리면 경고를 남기고 문을 연다.
    ///
    /// 실제 시간(Realtime)으로 세는 이유: 인벤토리를 열면 timeScale이 0이 되는데,
    /// 그 상태에서 게임 시간으로 세면 안전장치가 영영 안 돈다. 안전장치만큼은
    /// 게임이 멈춰 있어도 흘러야 한다.
    /// </summary>
    private IEnumerator ClaimHoldFailsafe()
    {
        yield return new WaitForSecondsRealtime(claimHoldTimeoutSeconds);

        if (claimRaised) yield break;

        Debug.LogWarning($"[보상 상자] 보상 보류가 {claimHoldTimeoutSeconds}초 안에 안 풀렸다. " +
                         "유물을 못 줍는 자리에 떨어졌을 수 있다. 갇히지 않도록 문을 연다.", this);
        RaiseClaimOnce();
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
        // 수정(연 뒤 정리): hidden이면 둘 다 끈다. 이 조건이 없으면 감춘 뒤에도
        // ApplyVisual이 다시 불릴 때 상자가 되살아난다.
        if (closedVisual != null) closedVisual.SetActive(!hidden && !IsOpened);
        if (openVisual != null) openVisual.SetActive(!hidden && IsOpened);
    }
}
