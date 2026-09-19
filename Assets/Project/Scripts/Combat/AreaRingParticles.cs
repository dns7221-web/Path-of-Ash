using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-17, R 판정 경계 불티 고리) — 불티가 판정 반경까지 퍼진 뒤 그 자리에 멈추게 시작 속도를 맞춘다.
///
/// 궁극기 판정은 반경 14인데 화면에서는 어디까지 닿았는지 알 수 없었다. 불티가 정확히 판정 경계에 서면
/// "여기까지 맞았다"가 보인다. 반경은 스킬 에셋이 들고 있으므로 스킬이 이펙트를 만든 직후
/// <see cref="SetRadius"/>로 넘긴다 — 숫자를 이펙트 쪽에 따로 적으면 반경을 고칠 때 고리만 옛 크기로 남는다.
///
/// 멈추는 일은 파티클 시스템의 Velocity over Lifetime → Speed Modifier 곡선이 한다(빌더가 넣는다).
/// 곡선은 수명 0에서 1, <see cref="travelFraction"/>에서 0으로 곧게 내려가고 그 뒤로 0이다.
/// 그러면 날아간 거리 = 시작 속도 × 수명 × travelFraction × ½ 이라, 반경에서 거꾸로 속도를 구할 수 있다.
/// 수명이 불티마다 다르면 멈추는 자리도 달라지므로 빌더는 수명을 상수로 둔다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public class AreaRingParticles : MonoBehaviour
{
    [Tooltip("불티가 태어나는 반지름(유닛). 셰이프 모듈(Circle)의 Radius와 같아야 한다.")]
    [SerializeField, Min(0f)] private float startRadius = 1.5f;

    [Tooltip("수명 중 날아가는 구간의 비율. Velocity over Lifetime의 Speed Modifier 곡선이 0이 되는 지점과 같아야 한다.")]
    [SerializeField, Range(0.05f, 1f)] private float travelFraction = 0.35f;

    /// <summary>
    /// 판정 반경을 받아, 불티가 그 반경에서 멈추도록 시작 속도를 바꾼다.
    /// 이펙트를 만든 직후(첫 방출 전에) 부른다. 인스턴스 값만 바뀌고 프리팹 에셋은 그대로다.
    /// </summary>
    public void SetRadius(float radius)
    {
        var particles = GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;

        float lifetime = main.startLifetime.constant;
        if (lifetime <= 0f) return;

        float distance = Mathf.Max(0f, radius - startRadius);

        // 속도 배율이 1에서 0으로 곧게 내려가는 구간의 평균은 ½이다.
        main.startSpeed = distance / (lifetime * travelFraction * 0.5f);
    }
}
