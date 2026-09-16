using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성 — 트리거 안에 들어온 대상의 <see cref="Health"/>에 데미지를 전달한다.
/// 플레이어 검과 적 돌진이 같은 컴포넌트를 쓰며, 누가 맞을지는 LayerMask로만 구분한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class DamageHitbox : MonoBehaviour
{
    [SerializeField, Min(1)] private int damage = 1;
    [SerializeField] private LayerMask targetLayers;

    // 추가 생성 — 타격감. 값을 히트박스마다 두는 이유:
    // Health.Damaged에 자동으로 걸면 편하지만, 그러면 잡몹 한 대와 보스를 끝내는 한 대가
    // 똑같이 멈춘다. 무게는 맞은 쪽이 아니라 <b>때린 것</b>이 정하는 값이다.
    [Header("타격감")]
    [Tooltip("맞은 순간 멈출 실시간(초). 0이면 안 멈춘다.")]
    [SerializeField, Min(0f)] private float hitStopSeconds = 0.05f;

    [Tooltip("이 한 대로 죽었을 때 멈출 시간(초). 보통 일반 타격보다 길다.")]
    [SerializeField, Min(0f)] private float killHitStopSeconds = 0.1f;

    // 기본값을 0이 아니라 0.18로 두는 이유: 멈춤만으로는 "뭔가 끊겼다"로 읽히고 타격으로는
    // 안 읽힌다. CameraShake의 maxOffset이 0.6유닛이므로 0.18은 약 0.11유닛 —
    // 32 PPU 기준 3~4픽셀이다. 알아볼 수는 있고 화면이 요동치지는 않는 정도.
    [Tooltip("맞은 순간 화면 흔들림 세기(0~1). 0이면 안 흔든다.")]
    [SerializeField, Range(0f, 1f)] private float shakeStrength = 0.18f;

    [Tooltip("흔들림 시간(초).")]
    [SerializeField, Min(0f)] private float shakeSeconds = 0.12f;

    private readonly HashSet<Health> damagedThisActivation = new HashSet<Health>();
    private Collider2D hitboxCollider;

    /// <summary>
    /// 추가 생성(화살 명중 VFX) — 이 히트박스가 대상에게 데미지를 <b>실제로 넣은 뒤</b> 한 번 울린다.
    ///
    /// 왜 이벤트로 여는가: 화살이 맞은 자리에 불꽃을 터뜨리려면 <see cref="Projectile"/>이
    /// "맞았다"를 알아야 하는데, 판정 규칙(레이어·중복 방지·무적)은 전부 이쪽에 있다.
    /// 투사체가 OnTriggerEnter2D를 따로 받아 같은 검사를 다시 하면, 무적인 적에게도 불꽃이 튀는 식으로
    /// 두 판정이 조용히 갈라진다. 걸러진 결과만 밖으로 알리면 규칙은 여전히 한 곳에 있다.
    ///
    /// 무엇을 보여줄지는 듣는 쪽이 정한다. 검(플레이어)과 돌진(적)은 지금 듣는 곳이 없어서 아무 일도 없다.
    /// </summary>
    public event System.Action<Health> HitLanded;

    // 히트박스는 검·돌진·투사체마다 하나씩이라 여럿이 동시에 산다. 매번 Camera.main을
    // 뒤지지 않도록 한 번 찾은 것을 공유한다. 씬이 바뀌어 카메라가 파괴되면 유니티의
    // 가짜 null 비교에 걸려 다시 찾는다.
    private static CameraShake cachedShake;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider2D>();
        hitboxCollider.isTrigger = true;
        hitboxCollider.enabled = false;
    }

    /// <summary>
    /// 추가 생성 — 이번 판정의 데미지를 바꾼다.
    ///
    /// 스킬마다 데미지가 다르고 유물 보정치도 전투 중에 늘어나므로, 히트박스가 고정값을
    /// 들고 있으면 안 된다. 켜기 직전에 스킬이 이 값을 넣는다.
    /// </summary>
    public void SetDamage(int value) => damage = Mathf.Max(0, value);

    /// <summary>추가 생성 — 새 공격 판정을 시작한다. 이전 공격의 적중 기록은 비운다.</summary>
    public void Activate()
    {
        damagedThisActivation.Clear();
        hitboxCollider.enabled = true;
    }

    /// <summary>추가 생성 — 공격 판정을 즉시 끈다.</summary>
    public void Deactivate()
    {
        if (hitboxCollider != null) hitboxCollider.enabled = false;
    }

    private void OnDisable()
    {
        Deactivate();
        damagedThisActivation.Clear();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((targetLayers.value & (1 << other.gameObject.layer)) == 0) return;

        Health health = other.GetComponentInParent<Health>();
        if (health == null || !damagedThisActivation.Add(health)) return;

        // 추가 생성 — 때린 쪽의 위치를 같이 넘겨 넉백 방향을 정할 수 있게 한다.
        //
        // 히트박스 자신이 아니라 부모(공격자 본체)의 위치를 쓰는 이유: 히트박스는 몸 앞으로
        // 밀어낸 자식이라, 적이 검 끝보다 안쪽에 있으면 히트박스 → 적 방향이 뒤를 가리킨다.
        // 그러면 적이 플레이어 쪽으로 빨려온다. 본체 기준이면 항상 바깥으로 밀린다.
        Transform source = transform.parent != null ? transform.parent : transform;
        health.TakeDamage(damage, source.position);

        // 추가 생성 — 타격감은 데미지가 <b>실제로 들어간 뒤</b>에 준다.
        // 위에서 무적이나 중복 판정으로 걸러졌다면 여기까지 오지 않는다.
        ApplyImpactFeel(health.IsDead);

        // 추가 생성(화살 명중 VFX) — 맞혔다고 알린다. 여기서 만든 이펙트는 SpriteFrameAnimator가
        // Time.deltaTime으로 프레임을 넘기므로, 히트스톱 동안 첫 프레임(흰 섬광)에 멈춰 있다가
        // 시간이 풀리면서 퍼진다. 멈춤과 섬광이 같은 순간에 겹쳐 "꽂혔다"가 한 번에 읽힌다.
        HitLanded?.Invoke(health);
    }

    /// <summary>
    /// 추가 생성 — 맞은 순간의 화면 반응.
    ///
    /// 죽인 한 대를 더 길게 멈추는 이유: 그 한 대만 결과가 다르다. 같은 길이로 멈추면
    /// 마지막 타격이 그냥 또 한 대가 되고, 적이 사라지는 것을 <b>멈춤 없이</b> 보게 된다.
    ///
    /// 흔들림이 히트스톱보다 늦게 보이는 것은 의도다. <see cref="CameraShake"/>가
    /// <c>Time.deltaTime</c>으로 세기 때문에 멈춰 있는 동안은 흔들리지 않고, 시간이 풀리면서
    /// 흔들린다. 멈춤과 흔들림이 겹치면 둘 다 뭉개져서 어느 쪽도 안 읽힌다.
    /// </summary>
    /// <param name="killed">이 한 대로 대상이 죽었는가.</param>
    private void ApplyImpactFeel(bool killed)
    {
        PauseGate.HitStop(killed ? killHitStopSeconds : hitStopSeconds);

        if (shakeStrength <= 0f || shakeSeconds <= 0f) return;

        if (cachedShake == null && Camera.main != null)
            cachedShake = Camera.main.GetComponent<CameraShake>();

        if (cachedShake != null) cachedShake.Shake(shakeStrength, shakeSeconds);
    }
}
