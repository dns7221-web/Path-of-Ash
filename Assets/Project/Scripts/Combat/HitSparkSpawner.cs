using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-17, 기본 공격 명중 불똥) — 히트박스가 데미지를 넣은 자리에 불똥을 튀긴다.
///
/// 플레이어 검 판정(AttackHitbox)에 붙는다. 기본 공격은 맞았을 때 참격 그림 말고는 반응이 없어서,
/// 히트스톱이 걸려도 "어디에 맞았는지"가 안 보였다. 불똥이 맞은 자리에서 공격 방향으로 튀면
/// 멈춤과 같은 순간에 위치와 방향이 같이 읽힌다.
///
/// <see cref="DamageHitbox"/>의 명중 알림만 듣는 이유: 무적·중복 판정은 전부 그쪽에 있다. 여기서
/// 트리거를 따로 받으면 대시로 흘린 공격이나 무적인 적에게도 불똥이 튀어, 09-16에 고친 히트스톱 규칙과
/// 갈라진다. 걸러진 결과만 들으면 "불똥이 튀었다 = 데미지가 들어갔다"가 항상 맞다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageHitbox))]
public class HitSparkSpawner : MonoBehaviour
{
    [Tooltip("맞은 자리에 만들 불똥 프리팹(HitSparks). 오른쪽으로 튀게 만든 것을 공격 방향으로 돌려 놓는다. 비우면 아무것도 안 한다.")]
    [SerializeField] private GameObject sparkPrefab;

    // 적의 몸 콜라이더는 발치(높이 0~1.65)에 있어서, 맞은 점을 그대로 쓰면 불똥이 발목에서 튄다.
    // 참격 그림은 가슴 높이(기본 공격 Effect Height 3)에 그려지므로 그 근처로 올린다.
    // 보스처럼 몸 전체가 콜라이더인 적도 판정 중심(발치 높이)에서 가장 가까운 점이 발치 쪽이라 같은 값이 맞는다.
    [Tooltip("맞은 점에서 화면 위로 올릴 높이(유닛). 적 몸통 가운데쯤에 맞춘다. 눈으로 맞추는 값이다.")]
    [SerializeField] private float sparkHeight = 2.2f;

    private DamageHitbox hitbox;

    private void Awake()
    {
        hitbox = GetComponent<DamageHitbox>();
    }

    // 켜질 때 구독하고 꺼질 때 푼다. Projectile의 명중 이펙트와 같은 이유 — 껐다 켜도 구독이 한 겹이다.
    private void OnEnable()
    {
        if (hitbox != null) hitbox.HitLandedAt += OnHitLanded;
    }

    private void OnDisable()
    {
        if (hitbox != null) hitbox.HitLandedAt -= OnHitLanded;
    }

    /// <summary>
    /// 맞은 점 위에 불똥을 만든다. 방향은 이 히트박스가 바라보는 쪽이다.
    ///
    /// PlayerController가 히트박스를 바라보는 방향으로 돌려 두므로 transform.right가 곧 공격 방향이다.
    /// 공격자 → 적 벡터를 따로 구하지 않는 이유: 적이 검 끝보다 안쪽에 겹쳐 있으면 그 벡터가 옆이나
    /// 뒤를 가리켜 불똥이 엉뚱한 쪽으로 튄다. 휘두른 방향은 항상 하나다.
    /// </summary>
    private void OnHitLanded(Health target, Vector2 contact)
    {
        if (sparkPrefab == null) return;

        Vector3 position = (Vector3)contact + Vector3.up * sparkHeight;

        Vector2 direction = transform.right;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        // 수명은 프리팹의 Stop Action(Destroy)이 정리한다. 여기서 Destroy 타이머를 따로 걸지 않는다.
        Instantiate(sparkPrefab, position, Quaternion.Euler(0f, 0f, angle));
    }
}
