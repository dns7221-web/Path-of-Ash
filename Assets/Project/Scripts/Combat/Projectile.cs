using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

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

    // 추가 생성(2026-09-19, 사수-3 화살 재 부서짐) — 사거리 끝에서 사라질 때 남길 이펙트.
    // 아무것도 못 맞힌 화살이 허공에서 그냥 꺼지면 "사라졌다"가 아니라 "안 보이게 됐다"로 읽힌다. 비우면 예전처럼 조용히 사라진다.
    [Tooltip("사거리 끝에서 사라질 때 화살촉 자리에 만들 이펙트. 비우면 아무것도 안 남긴다.")]
    [SerializeField] private GameObject expireEffectPrefab;

    /// <summary>
    /// 추가 생성 — 명중 이펙트가 스스로 안 사라질 때 강제로 지우기까지의 시간(초).
    ///
    /// 지금 이펙트는 SpriteFrameAnimator가 재생을 마치면 스스로 지운다(6프레임 / 20fps = 0.3초).
    /// 그래도 따로 두는 이유는 SkillData.SpawnEffect와 같다 — 루프로 잘못 설정된 프리팹을 끼우면
    /// 화살을 쏠 때마다 불꽃이 방에 쌓인다. 관통하는 화살이라 한 발에 여럿이 생길 수 있어 더 빨리 쌓인다.
    /// </summary>
    private const float ImpactEffectMaxLifetime = 2f;

    // 추가 생성(2026-09-17, 사수 화살 높이) — 그림만 따로 띄울 자식.
    //
    // 이 게임의 y는 바닥 위치와 화면 높이를 겸한다. 판정(이 오브젝트의 콜라이더)은 몸 콜라이더가 있는 발치 높이로
    // 날아야 같은 줄의 대상을 맞히는데, 그림까지 거기 있으면 화살이 활이 아니라 발목에서 나간다.
    // 그림을 자식으로 두고 그것만 위로 올리면 판정은 그대로 두고 보이는 높이만 활에 맞출 수 있다.
    [Tooltip("그림(SpriteRenderer)이 붙은 자식. 비우면 그림이 판정과 같은 자리에 있다(플레이어 화살).")]
    [SerializeField] private Transform visual;

    // 추가 생성(2026-09-17) — 그림을 판정 위로 띄울 화면 높이(유닛). 쏘는 쪽이 Launch 전에 넣는다.
    private float visualLift;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;

    // ── 추가 생성(2026-09-24, 투사체 풀링) ─────────────────────────────
    // 돌아갈 풀. ProjectilePool.Spawn으로 만든 것만 들고 있다 — 없으면(예전처럼 Instantiate로 만든 것) 끝에 지운다.
    private IObjectPool<Projectile> pool;

    // 사거리 끝을 지나 반납을 기다리는 중인가(꼬리 불티가 꺼지기를 기다린다).
    private bool expiring;

    // 반납 대기 중에 끄고, 다시 꺼낼 때 켤 그림들과 곁들임 파티클. Awake에서 한 번만 모아 둔다(매번 찾으면 할당).
    private SpriteRenderer[] renderers;
    private bool[] rendererWasEnabled;
    private ParticleSystem[] garnishes;

    /// <summary>추가 생성 — 풀이 새로 만든 직후 한 번 부른다. 이게 있으면 사거리 끝에서 지우지 않고 풀에 돌려놓는다.</summary>
    public void AssignPool(IObjectPool<Projectile> owner) => pool = owner;

    /// <summary>
    /// 추가 생성(2026-09-17, 사수 화살 높이) — 그림을 판정보다 화면에서 얼마나 위에 그릴지 정한다.
    /// <see cref="Launch"/>보다 먼저 부른다. 그림 자식(visual)이 없으면 아무 일도 안 한다.
    /// </summary>
    public void SetVisualLift(float lift) => visualLift = lift;

    /// <summary>추가 생성(2026-09-17) — 화면에서 화살이 보이는 자리. 명중 이펙트를 여기에 놓는다.</summary>
    private Vector3 VisualPosition => visual != null ? visual.position : transform.position;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();

        // 투사체는 물리에 밀리면 안 된다. 벽에 부딪혀도 튕기지 않고, 적을 밀지도 않는다.
        body.bodyType = RigidbodyType2D.Kinematic;

        if (hitbox == null) hitbox = GetComponentInChildren<DamageHitbox>();

        // 추가 생성(2026-09-24, 풀링) — 반납 대기 때 끌 것들을 모아 둔다.
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        rendererWasEnabled = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) rendererWasEnabled[i] = renderers[i].enabled;

        var garnishMarks = GetComponentsInChildren<ParticleGarnish>(true);
        garnishes = new ParticleSystem[garnishMarks.Length];
        for (int i = 0; i < garnishMarks.Length; i++) garnishes[i] = garnishMarks[i].GetComponent<ParticleSystem>();
    }

    // 추가 생성(화살 명중 VFX) — 히트박스의 "맞혔다"를 듣는다.
    // 켜질 때 구독하고 꺼질 때 푸는 이유: 나중에 투사체를 오브젝트 풀로 돌리면 파괴 없이
    // 껐다 켜기를 반복한다. 그때도 구독이 한 겹으로 유지되어 한 번 맞혔는데 불꽃이 둘 생기지 않는다.
    private void OnEnable()
    {
        // 추가 생성(2026-09-24, 풀링) — 풀에서 다시 꺼낸 것이면 지난번 반납 대기 때 꺼 둔 것을 되살린다.
        if (expiring) ResetForReuse();
        visualLift = 0f;

        if (hitbox != null) hitbox.HitLanded += OnHitLanded;
    }

    private void OnDisable()
    {
        // 추가 생성(2026-09-24, 풀링) — 꺼진 채 예약된 사거리 끝이 나중에 불리지 않게 끊는다(재사용 때 두 번 불린다).
        CancelInvoke(nameof(Expire));

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

        // 수정(2026-09-17) — transform.position → VisualPosition. 그림을 띄운 화살(사수)은 보이는 촉 자리에서 터져야 한다.
        // 그림 자식이 없는 화살(플레이어)은 두 값이 같다.
        var effect = Instantiate(impactEffectPrefab, VisualPosition, transform.rotation);
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

        // 추가 생성(2026-09-17, 사수 화살 높이) — 회전을 정한 뒤에 그림을 화면 위쪽(월드 +y)으로 띄운다.
        // 로컬 좌표로 바꿔 넣는 이유: 화살이 날아가는 방향으로 돌아 있어서, 로컬 +y로 올리면 왼쪽으로 쏠 때는
        // 아래로, 위로 쏠 때는 옆으로 밀린다. 월드 +y를 이 오브젝트 기준으로 되돌려야 어느 방향이든 위로 뜬다.
        if (visual != null)
            visual.localPosition = transform.InverseTransformVector(new Vector3(0f, visualLift, 0f));

        if (hitbox != null)
        {
            hitbox.SetDamage(damage);
            hitbox.Activate();
        }

        // 사거리를 거리가 아니라 시간으로 재는 이유: 속도를 바꾸면 사거리가 같이 따라와서
        // 두 값을 따로 맞출 필요가 없다.
        //
        // 수정(2026-09-17, 화살 불티 꼬리) — Destroy(gameObject, lifetime) → Invoke(Expire).
        // 예약된 Destroy는 끼어들 틈이 없어서, 화살에 붙은 꼬리 파티클이 화살과 같이 한순간에 사라졌다.
        // 사라지기 직전에 꼬리를 떼어 내려면 지우는 순간을 이 컴포넌트가 쥐고 있어야 한다.
        // Invoke도 Destroy의 지연처럼 게임 시간(timeScale)으로 세므로 사거리는 그대로다.
        Invoke(nameof(Expire), lifetime);
    }

    /// <summary>
    /// 추가 생성(2026-09-17) — 사거리 끝. 곁들인 파티클(꼬리)을 월드에 남기고 화살만 지운다.
    /// 꼬리는 남은 불티가 다 꺼지면 스스로 사라진다(<see cref="ParticleGarnish"/>).
    /// </summary>
    private void Expire()
    {
        // 추가 생성(2026-09-19, 사수-3) — 보이는 촉 자리에서 부서진다. 명중 이펙트와 같은 안전 수명을 건다.
        if (expireEffectPrefab != null)
        {
            var effect = Instantiate(expireEffectPrefab, VisualPosition, transform.rotation);
            Destroy(effect, ImpactEffectMaxLifetime);
        }

        // 추가 생성(2026-09-24, 풀링) — 풀에서 온 것이면 지우지 않고 돌려놓는다. 이유는 ReturnToPool 참고.
        if (pool != null)
        {
            StartCoroutine(ReturnToPool());
            return;
        }

        ParticleGarnish.ReleaseAll(gameObject);
        Destroy(gameObject);
    }

    /// <summary>
    /// 추가 생성(2026-09-24, 풀링) — 화살을 숨기고, 꼬리 불티가 다 꺼진 뒤에 풀에 돌려놓는다.
    ///
    /// 풀이 아닐 때는 꼬리를 월드에 떼어 내고(ParticleGarnish.ReleaseAll) 화살을 지웠다. 풀에서는 그러면 안 된다 —
    /// 떼어 낸 꼬리는 스스로 사라지므로 <b>다시 꺼낸 화살에는 꼬리가 없다.</b>
    /// 그래서 꼬리를 붙인 채로 방출만 멈추고, 남은 불티가 다 꺼질 때까지(보통 1초 안) 그림과 판정만 끈 채 기다린다.
    /// 곁들임 파티클은 월드 공간이라 화살이 멈춰 있어도 이미 나간 불티는 제자리에서 꺼진다.
    /// </summary>
    private IEnumerator ReturnToPool()
    {
        expiring = true;
        body.linearVelocity = Vector2.zero;
        if (hitbox != null) hitbox.Deactivate();

        foreach (SpriteRenderer spriteRenderer in renderers) spriteRenderer.enabled = false;
        foreach (ParticleSystem particles in garnishes) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        while (AnyGarnishAlive()) yield return null;

        pool.Release(this);
    }

    private bool AnyGarnishAlive()
    {
        foreach (ParticleSystem particles in garnishes)
            if (particles != null && particles.IsAlive(true)) return true;
        return false;
    }

    /// <summary>추가 생성(2026-09-24, 풀링) — 다시 꺼냈을 때 처음 만든 모습으로 되돌린다.</summary>
    private void ResetForReuse()
    {
        expiring = false;

        for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = rendererWasEnabled[i];

        // 지난번 불티를 지우고 처음부터 다시 뿜는다. 켜질 때 저절로 도는(Play On Awake) 것만 — 원래 그렇게 쓰이던 것만 되살린다.
        foreach (ParticleSystem particles in garnishes)
        {
            particles.Clear(true);
            if (particles.main.playOnAwake) particles.Play(true);
        }
    }

    private void FixedUpdate()
    {
        // 추가 생성(2026-09-24, 풀링) — 반납을 기다리는 동안은 제자리에 둔다.
        if (expiring) return;

        body.linearVelocity = direction * speed;
    }
}
