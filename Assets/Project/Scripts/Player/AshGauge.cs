using System;
using UnityEngine;

/// <summary>
/// 필살기(R)를 쓰기 위해 모으는 재 게이지.
///
/// <b>쿨다운이 아니라 게이지로 만든 이유:</b> 쿨다운은 시간이 주는 것이고 게이지는 싸워서
/// 버는 것이다. 필살기가 "가만히 있어도 20초 뒤에 또 쓸 수 있는 것"이면 판을 뒤집는 무게가
/// 안 실린다. 적을 열 마리 잡아야 한 번 쓸 수 있으면 쓰는 순간이 여기까지 온 대가가 된다.
///
/// 실전에서도 차이가 난다 — <b>위기에 몰릴수록 게이지가 안 찬다.</b> 적을 못 잡고 도망만
/// 다니면 필살기도 못 쓴다. 쿨다운은 도망쳐도 차오른다.
///
/// 충전원을 처치 하나로 둔 이유: "준 데미지만큼", "맞은 만큼", "방 클리어 보너스" 같은 안도
/// 있지만 넣지 않았다. 공식이 복잡해지면 플레이어가 게이지를 보며 뭘 기대해야 할지 알 수 없다.
/// 상자 회복량을 균등 1~2로 확정한 것과 같은 판단이다. 밸런스가 실제로 무너지면 그때 바꾼다.
///
/// 판이 끝나면 초기화할 필요가 없다 — 씬을 다시 로드하면 이 컴포넌트가 통째로 새로 생긴다.
/// RunManager가 정한 "재시작은 값을 되돌리는 게 아니라 씬을 다시 로드하는 것" 규칙을 그대로 탄다.
/// </summary>
[DisallowMultipleComponent]
public class AshGauge : MonoBehaviour
{
    [Tooltip("가득 찬 것으로 볼 양.")]
    [SerializeField, Min(1f)] private float maxCharge = 100f;

    [Tooltip("적 하나를 처치할 때 차는 양. 10이면 열 마리에 한 번 쓸 수 있다.")]
    [SerializeField, Min(0f)] private float chargePerKill = 10f;

    /// <summary>현재 모인 양.</summary>
    public float Current { get; private set; }

    /// <summary>0~1로 정규화한 값. 게이지 바가 읽는다.</summary>
    public float Normalized => maxCharge <= 0f ? 0f : Current / maxCharge;

    /// <summary>지금 필살기를 쓸 수 있는가.</summary>
    public bool IsFull => Current >= maxCharge;

    /// <summary>값이 바뀔 때마다 불린다. 인자는 정규화된 값(0~1).</summary>
    public event Action<float> Changed;

    /// <summary>추가 생성 — 유물로 얻은 처치당 충전량 보정.</summary>
    public float BonusChargePerKill { get; set; }

    /// <summary>적을 처치했을 때 부른다.</summary>
    public void AddKillCharge() => Add(chargePerKill + BonusChargePerKill);

    /// <summary>
    /// 가득 찼으면 전부 쓰고 true. 아니면 아무것도 하지 않고 false.
    ///
    /// 확인과 소모를 한 함수로 묶은 이유는 PlayerStamina.TryConsumeDash와 같다 —
    /// 두 줄로 나누면 그 사이에 값이 바뀌는 경우를 놓친다.
    /// </summary>
    public bool TryConsumeAll()
    {
        if (!IsFull) return false;

        Current = 0f;
        Changed?.Invoke(Normalized);
        return true;
    }

    private void Add(float amount)
    {
        if (amount <= 0f) return;

        float clamped = Mathf.Clamp(Current + amount, 0f, maxCharge);
        if (Mathf.Approximately(clamped, Current)) return;

        Current = clamped;
        Changed?.Invoke(Normalized);
    }
}
