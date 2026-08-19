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

    private readonly HashSet<Health> damagedThisActivation = new HashSet<Health>();
    private Collider2D hitboxCollider;

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
    }
}
