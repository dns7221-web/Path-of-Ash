using System;
using UnityEngine;

/// <summary>
/// 체력을 소유하고 피격·사망을 알리는 컴포넌트. 플레이어와 적이 <b>같은 것을</b> 쓴다.
///
/// 하나로 합친 이유: 체력이 줄고, 무적 시간이 있고, 0이 되면 죽는다는 규칙은 양쪽이 완전히
/// 같다. 따로 만들면 "적은 무적 시간이 있는데 플레이어는 없어서 같은 프레임에 두 번 맞는"
/// 식의 차이가 조용히 생긴다.
///
/// 이 컴포넌트는 <b>죽으면 어떻게 되는지를 모른다.</b> 플레이어는 사망 모션 후 결과 화면으로
/// 가고, 적은 잿더미가 되어 사라진다. 그 차이는 <see cref="Died"/>를 구독하는 쪽이 정한다.
/// 여기서 분기하면 적 종류가 늘 때마다 이 파일을 열어야 한다.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("체력")]
    [Tooltip("최대 체력. 플레이어 5, 잿불 망령 3, 재 무리 1이 기획 수치다.")]
    [SerializeField] private int maxHealth = 3;

    [Header("무적")]
    [Tooltip("피격 직후 무적 시간(초). 0이면 한 번의 돌진에 여러 프레임 연속으로 맞는다. " +
             "플레이어 피격 경직(0.2초)보다 살짝 길어야 경직이 풀리는 순간 또 맞지 않는다.")]
    [SerializeField] private float invulnerableSecondsAfterHit = 0.35f;

    // 남은 무적 시간. 0 이하면 맞을 수 있다.
    private float invulnerableTimer;

    /// <summary>현재 체력.</summary>
    public int Current { get; private set; }

    /// <summary>최대 체력. UI가 하트 개수를 그릴 때 읽는다.</summary>
    public int Max => maxHealth;

    /// <summary>죽었는가. 죽은 뒤에는 더 이상 데미지를 받지 않는다.</summary>
    public bool IsDead => Current <= 0;

    /// <summary>
    /// 외부가 거는 무적. 대시 회피가 이 값을 켠다.
    ///
    /// 피격 후 무적 타이머와 따로 둔 이유: 둘은 켜지는 조건이 다르다. 하나로 합치면
    /// 대시가 끝나는 순간 피격 무적까지 같이 풀려서, 대시 직후 한 프레임이 무방비가 된다.
    /// </summary>
    public bool IsInvulnerableExternally { get; set; }

    /// <summary>지금 데미지를 받을 수 없는 상태인가.</summary>
    public bool IsInvulnerable => IsDead || IsInvulnerableExternally || invulnerableTimer > 0f;

    /// <summary>데미지를 받았을 때. 인자는 (남은 체력, 최대 체력).</summary>
    public event Action<int, int> Damaged;

    /// <summary>체력이 0이 됐을 때. 한 번만 불린다.</summary>
    public event Action Died;

    /// <summary>
    /// 추가 생성 — 마지막으로 맞은 방향(때린 쪽 → 나). 넉백을 어느 쪽으로 밀지에 쓴다.
    ///
    /// 방향만 기록하고 <b>넉백을 여기서 적용하지 않는 이유</b>: 밀려나는 방식이 대상마다 다르다.
    /// 적은 뒤로 미끄러지지만 플레이어는 경직만 있고 밀리지 않으며, 나중에 나올 거대 보스는
    /// 아예 안 밀려야 한다. Health가 밀어버리면 그 차이를 여기서 분기해야 하고, 그러면
    /// 적 종류가 늘 때마다 이 파일을 열게 된다. 기록만 하고 판단은 맞은 쪽이 한다.
    /// </summary>
    public Vector2 LastHitDirection { get; private set; } = Vector2.right;

    private void Awake()
    {
        Current = maxHealth;
    }

    private void Update()
    {
        if (invulnerableTimer > 0f)
            invulnerableTimer -= Time.deltaTime;
    }

    /// <summary>
    /// 추가 생성 — 체력을 최대치로 되돌리고 모든 무적 상태를 해제한다.
    ///
    /// 적 오브젝트 풀은 같은 인스턴스를 다시 꺼내 쓰므로 Awake가 다시 호출되지 않는다.
    /// 풀에서 재사용할 때 이 함수를 호출하지 않으면 죽은 체력 0 상태로 다시 등장한다.
    /// </summary>
    public void RestoreFull()
    {
        Current = maxHealth;
        invulnerableTimer = 0f;
        IsInvulnerableExternally = false;
    }

    /// <summary>
    /// 데미지를 준다. 무적 중이거나 이미 죽었으면 아무 일도 일어나지 않는다.
    ///
    /// 무적 판정을 호출하는 쪽이 아니라 여기서 하는 이유: 데미지를 주는 곳이 여러 개(적의 돌진,
    /// 플레이어의 검, 나중에 함정)인데 각자 무적을 확인하게 하면 한 곳이 빠뜨린다.
    /// 그 한 곳이 "대시 중인데 가끔 맞는" 버그가 된다.
    /// </summary>
    /// <returns>실제로 데미지가 들어갔으면 true.</returns>
    public bool TakeDamage(int amount) => TakeDamage(amount, null);

    /// <summary>
    /// 추가 생성 — 때린 위치를 같이 받는 판. 넉백 방향을 기록하는 것 외에는 위와 같다.
    /// </summary>
    /// <param name="sourcePosition">때린 쪽의 월드 좌표. null이면 방향을 갱신하지 않는다.</param>
    public bool TakeDamage(int amount, Vector2? sourcePosition)
    {
        if (amount <= 0) return false;
        if (IsInvulnerable) return false;

        // 방향은 데미지가 실제로 들어갈 때만 갱신한다. 무적 중에 스친 공격까지 방향을 바꾸면,
        // 다음에 진짜로 맞았을 때 엉뚱한 쪽으로 밀려난다.
        if (sourcePosition.HasValue)
        {
            Vector2 delta = (Vector2)transform.position - sourcePosition.Value;

            // 정확히 겹쳐 있으면 방향을 못 정한다. 그때는 직전 방향을 유지한다.
            if (delta.sqrMagnitude > 0.0001f) LastHitDirection = delta.normalized;
        }

        Current = Mathf.Max(0, Current - amount);
        invulnerableTimer = invulnerableSecondsAfterHit;

        Damaged?.Invoke(Current, maxHealth);

        if (Current <= 0)
            Died?.Invoke();

        return true;
    }

    /// <summary>회복. 최대치를 넘지 않는다. 죽은 뒤에는 살아나지 않는다.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;

        Current = Mathf.Min(maxHealth, Current + amount);
        Damaged?.Invoke(Current, maxHealth); // UI 갱신 경로가 하나뿐이라 회복도 같은 이벤트를 쓴다
    }
}
