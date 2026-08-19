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

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();

        // 투사체는 물리에 밀리면 안 된다. 벽에 부딪혀도 튕기지 않고, 적을 밀지도 않는다.
        body.bodyType = RigidbodyType2D.Kinematic;

        if (hitbox == null) hitbox = GetComponentInChildren<DamageHitbox>();
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
        transform.localScale = new Vector3(direction.x < 0f ? -1f : 1f, 1f, 1f);

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
