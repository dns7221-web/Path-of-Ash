using UnityEngine;

/// <summary>
/// 추가 생성 — 카메라를 잠깐 흔든다. 보스 2페이즈 전환이 첫 사용처지만
/// 내려찍기와 재 폭발에도 그대로 쓸 것이라 보스 전용 이름을 피했다.
///
/// <b>Cinemachine Impulse를 안 쓴 이유:</b> 패키지는 설치돼 있지만 Game 씬은 Cinemachine을
/// 전혀 안 쓴다. 방 하나가 곧 한 화면이라 추종 카메라가 필요 없어서 Main Camera 하나가
/// 고정으로 서 있다. 흔들림 하나 때문에 CinemachineBrain과 가상 카메라를 씬에 들이면
/// <b>카메라 위치의 주인이 바뀐다</b> — 지금 인스펙터로 잡아둔 화면 구도가 가상 카메라 쪽으로
/// 넘어가고, 그 뒤로는 구도를 고칠 때마다 두 곳을 봐야 한다. 흔들림은 오프셋 하나면 된다.
///
/// <b>기준 위치를 Awake에 저장하지 않는 이유:</b> 매 프레임 지난 오프셋을 되돌리고 새로
/// 더한다. 기준을 한 번 찍어두면 나중에 카메라가 따라다니게 됐을 때 흔들림이 끝나는 순간
/// 카메라가 옛 자리로 순간이동한다. 이 방식은 위치의 주인이 누구든 그 위에 얹힌다.
/// </summary>
[DisallowMultipleComponent]
public class CameraShake : MonoBehaviour
{
    [Tooltip("흔들림 최대 반경(유닛). 강도 1일 때 이만큼 흔들린다.")]
    [SerializeField, Min(0f)] private float maxOffset = 0.6f;

    // 이번 흔들림의 세기(0~1)와 남은 시간. 둘 다 Shake가 채운다.
    private float strength;
    private float remaining;
    private float duration;

    // 지난 프레임에 카메라에 더해둔 오프셋. 다음 프레임 첫머리에서 이걸 빼서 원래 자리로
    // 되돌린 뒤 새 오프셋을 더한다. 이 값을 안 들고 있으면 흔들림이 누적돼 카메라가 흘러간다.
    private Vector3 appliedOffset;

    /// <summary>
    /// 흔들기 시작. 이미 흔들리는 중이면 <b>더 센 쪽을 남기고 시간은 새로 준다.</b>
    ///
    /// 덮어쓰지 않는 이유: 전환 연출은 시작(강)과 껍질이 깨지는 순간(중)에 연달아 부른다.
    /// 나중 것으로 덮으면 큰 흔들림 도중에 약한 흔들림이 끼어들어 <b>세기가 뚝 떨어진다.</b>
    /// 겹쳤을 때 화면이 더 조용해지는 것은 어떤 경우에도 의도가 아니다.
    /// </summary>
    /// <param name="newStrength">세기(0~1). 1이면 maxOffset만큼 흔들린다.</param>
    /// <param name="seconds">지속 시간(초).</param>
    public void Shake(float newStrength, float seconds)
    {
        if (seconds <= 0f) return;

        strength = Mathf.Max(strength, Mathf.Clamp01(newStrength));
        duration = seconds;
        remaining = seconds;
    }

    /// <summary>
    /// 흔들림을 즉시 멈추고 카메라를 제자리에 돌려놓는다.
    /// 연출이 중간에 끊길 때(보스 사망, 씬 전환) 흔들린 채로 멈추지 않게 한다.
    /// </summary>
    public void StopShake()
    {
        remaining = 0f;
        strength = 0f;
    }

    /// <summary>
    /// LateUpdate에서 처리하는 이유: 카메라 위치를 정하는 다른 코드(추종, 연출)가
    /// Update에서 돌 수 있다. 그보다 늦게 얹어야 오프셋이 덮이지 않는다.
    ///
    /// Time.deltaTime(스케일 시간)을 쓰는 것도 의도다. 인벤토리(I)나 보스 열쇠 화면(T)으로
    /// PauseGate가 시간을 멈추면 흔들림도 같이 멈춰야 한다. 멈춘 화면만 떨리면 정지 화면이
    /// 아니라 고장으로 보인다.
    /// </summary>
    private void LateUpdate()
    {
        // 지난 프레임 오프셋을 먼저 되돌린다. 아래에서 일찍 return해도 원위치는 보장된다.
        transform.position -= appliedOffset;
        appliedOffset = Vector3.zero;

        if (remaining <= 0f) return;

        remaining -= Time.deltaTime;

        if (remaining <= 0f)
        {
            strength = 0f;
            return;
        }

        // 선형 감쇠. 지수 감쇠를 안 쓴 이유는 체력바 재충전과 같다 — 목표에 가까울수록
        // 느려져서 <b>끝이 언제인지 안 읽힌다.</b> 흔들림은 딱 끝나야 다음 연출이 산다.
        float damper = remaining / duration;

        Vector2 random = Random.insideUnitCircle * (maxOffset * strength * damper);

        // z는 건드리지 않는다. 직교 카메라에서 z는 그리는 범위(near/far)를 정하는 값이라
        // 흔들면 화면에서 갑자기 사라지는 오브젝트가 생긴다.
        appliedOffset = new Vector3(random.x, random.y, 0f);
        transform.position += appliedOffset;
    }

    /// <summary>
    /// 컴포넌트가 꺼지거나 씬이 바뀔 때 얹어둔 오프셋을 정리한다.
    /// 안 하면 흔들린 좌표가 그대로 저장된 채 남는다.
    /// </summary>
    private void OnDisable()
    {
        transform.position -= appliedOffset;
        appliedOffset = Vector3.zero;
        StopShake();
    }
}
