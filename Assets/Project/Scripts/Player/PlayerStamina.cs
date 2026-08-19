using System;
using UnityEngine;

/// <summary>
/// 달리기와 대시가 쓰는 스태미나 게이지.
///
/// 이 컴포넌트를 PlayerController에서 분리한 이유: 스태미나는 "얼마나 남았나"를 UI와 적 AI가
/// 같이 읽어야 하는 값이고, 나중에 아이템으로 최대치나 회복 속도를 바꾸는 대상이 된다.
/// PlayerController 안의 float 변수로 두면 그때마다 PlayerController를 열어야 한다.
///
/// 회복 지연을 코루틴이 아니라 타이머 변수로 둔 이유: 코루틴은 시작할 때마다 객체를 하나
/// 할당하는데, 달리는 동안 매 프레임 스태미나를 쓰므로 매 프레임 코루틴을 다시 시작하는 꼴이
/// 된다. 값을 하나 대입하고 Update에서 빼는 쪽이 할당이 없다.
///
/// <b>고갈 잠금(Exhausted)</b>이 이 컴포넌트의 핵심 규칙이다. 0이 되자마자 다시 달릴 수 있게
/// 두면, 스태미나가 1 회복될 때마다 한 프레임씩 달리는 상태가 되어 걷기와 달리기 애니메이션이
/// 초당 수십 번 번갈아 재생된다. 일정 비율까지 차야 잠금이 풀리게 해서 이걸 막는다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerStamina : MonoBehaviour
{
    [Header("용량")]
    [Tooltip("최대 스태미나. 아래 소모/회복 값들이 전부 이 값 기준이라 여기를 바꾸면 체감이 다 바뀐다.")]
    [SerializeField] private float maxStamina = 100f;

    [Header("소모")]
    [Tooltip("달리는 동안 초당 소모량. 22면 가득 찬 상태에서 약 4.5초 달린다.")]
    [SerializeField] private float runCostPerSecond = 22f;

    [Tooltip("대시 1회 소모량. 25면 가득 찬 상태에서 연속 4회.")]
    [SerializeField] private float dashCost = 25f;

    [Header("회복")]
    [Tooltip("초당 회복량. 18이면 빈 상태에서 가득 차기까지 약 5.5초.")]
    [SerializeField] private float regenPerSecond = 18f;

    [Tooltip("마지막으로 소모한 뒤 회복이 시작되기까지의 시간(초). 이게 0이면 달리기를 " +
             "점멸하듯 끊어 눌러서 소모를 사실상 무시할 수 있다.")]
    [SerializeField] private float regenDelaySeconds = 0.7f;

    [Header("고갈 잠금")]
    [Tooltip("0까지 떨어진 뒤 다시 달릴 수 있게 되는 회복 비율. 0.3이면 30%까지 차야 풀린다.")]
    [Range(0f, 1f)]
    [SerializeField] private float exhaustedReleaseRatio = 0.3f;

    // 회복이 시작되기까지 남은 시간. 0 이하면 회복 중이다.
    private float regenBlockTimer;

    /// <summary>현재 스태미나.</summary>
    public float Current { get; private set; }

    /// <summary>최대 스태미나. UI가 눈금을 그릴 때 쓴다.</summary>
    public float Max => maxStamina;

    /// <summary>0~1로 정규화한 현재량. 게이지 바의 fillAmount에 그대로 넣는 값이다.</summary>
    public float Normalized => maxStamina <= 0f ? 0f : Current / maxStamina;

    /// <summary>고갈 상태인가. 이 동안에는 스태미나가 남아 있어도 달리거나 대시할 수 없다.</summary>
    public bool IsExhausted { get; private set; }

    /// <summary>지금 달릴 수 있는가.</summary>
    public bool CanRun => !IsExhausted && Current > 0f;

    /// <summary>지금 대시할 수 있는가. 대시는 한 번에 목돈이 나가므로 잔량을 미리 본다.</summary>
    public bool CanDash => !IsExhausted && Current >= dashCost;

    /// <summary>대시 1회 비용. UI가 "대시 가능" 눈금을 그릴 때 쓸 수 있다.</summary>
    public float DashCost => dashCost;

    /// <summary>
    /// 값이 바뀔 때마다 불린다. 인자는 정규화된 현재량(0~1).
    ///
    /// UI가 Update에서 매 프레임 Normalized를 읽어도 되지만, 그러면 스태미나를 안 쓰는
    /// 대부분의 시간에도 계속 읽는다. 바뀔 때만 알려주는 쪽이 UI 코드를 단순하게 만든다.
    /// </summary>
    public event Action<float> Changed;

    private void Awake()
    {
        Current = maxStamina;
    }

    private void Update()
    {
        // 소모 직후에는 회복을 막는다.
        if (regenBlockTimer > 0f)
        {
            regenBlockTimer -= Time.deltaTime;
            return;
        }

        if (Current >= maxStamina) return;

        SetCurrent(Current + regenPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// 달리는 동안 매 프레임 호출한다. 프레임 시간을 곱해서 소모하므로 프레임률이 달라도
    /// 초당 소모량이 같다.
    /// </summary>
    public void ConsumeForRun(float deltaTime)
    {
        Consume(runCostPerSecond * deltaTime);
    }

    /// <summary>
    /// 대시 비용을 지불한다. 모자라면 아무것도 안 쓰고 false를 돌려준다.
    ///
    /// "쓸 수 있나 확인"과 "쓰기"를 한 함수로 묶은 이유: 호출하는 쪽에서 CanDash를 확인한 뒤
    /// 따로 소모시키면, 두 줄 사이에 조건이 바뀌는 경우(같은 프레임에 다른 곳에서 소모)에
    /// 잔량이 음수가 된다. 확인과 차감을 한 번에 하면 그 틈이 없다.
    /// </summary>
    public bool TryConsumeDash()
    {
        if (!CanDash) return false;

        Consume(dashCost);
        return true;
    }

    /// <summary>스태미나를 깎고 회복 지연을 다시 건다.</summary>
    private void Consume(float amount)
    {
        if (amount <= 0f) return;

        regenBlockTimer = regenDelaySeconds;
        SetCurrent(Current - amount);

        // 바닥나면 잠근다. 잠금은 아래 SetCurrent에서 비율이 찰 때 풀린다.
        if (Current <= 0f)
            IsExhausted = true;
    }

    /// <summary>
    /// 현재량을 범위 안으로 잘라 넣고, 값이 실제로 바뀐 경우에만 이벤트를 쏜다.
    /// 고갈 잠금 해제도 값이 바뀌는 이 한 곳에서만 판단한다.
    /// </summary>
    private void SetCurrent(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, maxStamina);

        // 부동소수점이라 완전히 같은 경우는 드물지만, 가득 찬 상태에서 회복이 계속 호출되는
        // 동안 이벤트가 매 프레임 나가는 걸 막는다.
        if (Mathf.Approximately(clamped, Current)) return;

        Current = clamped;

        if (IsExhausted && Current >= maxStamina * exhaustedReleaseRatio)
            IsExhausted = false;

        Changed?.Invoke(Normalized);
    }
}
