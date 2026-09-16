using UnityEngine;

/// <summary>
/// 날아가는 투사체. 잿가루 화살이 이것이다.
///
/// 데미지 판정을 직접 하지 않고 <see cref="DamageHitbox"/>를 붙여 쓰는 이유:
/// 레이어 판정, 같은 대상 중복 타격 방지, 데미지 전달이 근접 공격과 완전히 같은 규칙이다.
/// 여기서 다시 짜면 "검은 한 번만 때리는데 화살은 두 번 때리는" 식의 차이가 조용히 생긴다.
/// 이 컴포넌트는 <b>움직이고 사라지는 것</b>만 맡는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    [Tooltip("초당 이동 거리(월드 유닛). 플레이어 이동 14보다 훨씬 빨라야 견제기로 쓰인다.")]
    [SerializeField, Min(0f)] private float speed = 34f;

    [Tooltip("이 시간이 지나면 사라진다(초). 34 x 0.6 = 약 20유닛, 화면 가로의 40%가 사거리다.")]
    [SerializeField, Min(0.05f)] private float lifetime = 0.6f;

    [Tooltip("데미지를 전달할 히트박스. 비우면 아무도 못 때린다.")]
    [SerializeField] private DamageHitbox hitbox;

    // 추가 생성(화살 명중 VFX) — 맞힌 자리에 남길 이펙트.
    //
    // 스킬 에셋(ProjectileSkillData)이 아니라 투사체 프리팹이 들고 있는 이유: 명중은 발사한 뒤
    // 한참 지나 <b>투사체가 날아간 곳에서</b> 일어난다. 스킬은 발사하는 순간 할 일이 끝나서
    // 그때 어디에 맞을지 모르고, 알고 있는 것은 날아가는 화살 자신뿐이다.
    // 같은 화살 프리팹을 다른 스킬이 쏴도 명중 그림은 따라간다.
    [Tooltip("무언가를 맞혔을 때 화살촉 자리에 만들 이펙트. 비우면 아무것도 안 남긴다.")]
    [SerializeField] private GameObject impactEffectPrefab;

    /// <summary>
    /// 추가 생성 — 명중 이펙트가 스스로 안 사라질 때 강제로 지우기까지의 시간(초).
    ///
    /// 지금 이펙트는 SpriteFrameAnimator가 재생을 마치면 스스로 지운다(6프레임 / 20fps = 0.3초).
    /// 그래도 따로 두는 이유는 SkillData.SpawnEffect와 같다 — 루프로 잘못 설정된 프리팹을 끼우면
    /// 화살을 쏠 때마다 불꽃이 방에 쌓인다. 관통하는 화살이라 한 발에 여럿이 생길 수 있어 더 빨리 쌓인다.
    /// </summary>
    private const float ImpactEffectMaxLifetime = 2f;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();

        // 투사체는 물리에 밀리면 안 된다. 벽에 부딪혀도 튕기지 않고, 적을 밀지도 않는다.
        body.bodyType = RigidbodyType2D.Kinematic;

        if (hitbox == null) hitbox = GetComponentInChildren<DamageHitbox>();
    }

    // 추가 생성(화살 명중 VFX) — 히트박스의 "맞혔다"를 듣는다.
    // 켜질 때 구독하고 꺼질 때 푸는 이유: 나중에 투사체를 오브젝트 풀로 돌리면 파괴 없이
    // 껐다 켜기를 반복한다. 그때도 구독이 한 겹으로 유지되어 한 번 맞혔는데 불꽃이 둘 생기지 않는다.
    private void OnEnable()
    {
        if (hitbox != null) hitbox.HitLanded += OnHitLanded;
    }

    private void OnDisable()
    {
        if (hitbox != null) hitbox.HitLanded -= OnHitLanded;
    }

    /// <summary>
    /// 추가 생성 — 맞힌 순간 화살촉 자리에 명중 이펙트를 남긴다.
    ///
    /// 맞은 적의 콜라이더가 아니라 <b>화살 자신의 위치</b>에 놓는 이유: 일반 적의 몸 콜라이더는 발밑
    /// (높이 0~1.65)에 있고 보스는 몸 전체(0~7.5)라, 적 쪽 좌표(ClosestPoint 등)를 쓰면 적 종류에 따라
    /// 불꽃 높이가 들쭉날쭉하고 날아가던 화살 선에서 벗어난다. 화살 시트의 피벗이 촉 끝이라
    /// transform.position이 곧 촉이 닿은 자리다.
    ///
    /// 회전도 화살과 같게 준다. 명중 그림은 "왼쪽에서 날아와 맞았다"로 그려져 있어서
    /// 날아온 방향으로 돌리면 파편이 진행 방향(앞)으로 튄다.
    /// </summary>
    private void OnHitLanded(Health target)
    {
        if (impactEffectPrefab == null) return;

        var effect = Instantiate(impactEffectPrefab, transform.position, transform.rotation);
        Destroy(effect, ImpactEffectMaxLifetime);
    }

    /// <summary>
    /// 발사한다. 방향·데미지는 스킬이 정한다.
    ///
    /// 히트박스를 여기서 켜는 이유: 프리팹 상태에서는 꺼져 있어야 한다. 켜진 채로 생성되면
    /// 생성 위치에 겹쳐 있던 적이 화살이 날아가기도 전에 맞는다.
    /// </summary>
    public void Launch(Vector2 launchDirection, int damage)
    {
        direction = launchDirection.sqrMagnitude > 0.0001f
            ? launchDirection.normalized
            : Vector2.right;

        // 화살 그림이 오른쪽을 향해 그려져 있으므로, 왼쪽으로 쏠 때는 뒤집는다.
        // 수정(8방향) — 좌우 반전 대신 날아가는 방향으로 회전시킨다.
        // 반전은 위아래를 표현할 수 없어서, 위로 쏜 화살이 옆으로 누워 날아갔다.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (hitbox != null)
        {
            hitbox.SetDamage(damage);
            hitbox.Activate();
        }

        // 사거리를 거리가 아니라 시간으로 재는 이유: 속도를 바꾸면 사거리가 같이 따라와서
        // 두 값을 따로 맞출 필요가 없다.
        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        body.linearVelocity = direction * speed;
    }
}
