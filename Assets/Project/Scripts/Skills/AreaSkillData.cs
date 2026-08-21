using System.Collections;
using UnityEngine;

/// <summary>
/// 지정한 지점에서 잠시 뒤 터지는 장판 스킬. E(잿불 기둥)가 이것이다.
///
/// 판정을 히트박스가 아니라 <see cref="Physics2D.OverlapCircle"/> 한 번으로 하는 이유:
/// 장판은 "그 순간 그 원 안에 있던 대상"만 때리면 된다. 히트박스는 콜라이더를 켜두고
/// 들어오는 것을 기다리는 방식이라, 폭발이 끝난 뒤 들어온 적까지 맞는다.
/// 순간 판정은 한 줄이면 되고 켜고 끄는 타이밍을 관리할 필요도 없다.
///
/// 터지기까지 시간을 두는 것이 이 스킬의 전부다. 즉시 터지면 조준할 이유가 없고,
/// 지연이 있어야 "적이 저기로 올 것"을 읽는 플레이가 된다.
/// </summary>
[CreateAssetMenu(fileName = "Skill_Area", menuName = "재의 길/스킬/장판")]
public class AreaSkillData : SkillData
{
    [Header("조준")]
    [Tooltip("시전자 앞 몇 유닛 지점에 떨어뜨릴지.")]
    [SerializeField, Min(0f)] private float forwardDistance = 8f;

    [Tooltip("폭발 반경(유닛). 4면 적 여러 마리를 한 번에 덮는다.")]
    [SerializeField, Min(0.1f)] private float radius = 4f;

    [Tooltip("때릴 대상 레이어.")]
    [SerializeField] private LayerMask targetLayers;

    [Header("타이밍")]
    [Tooltip("시전 시작부터 장판이 깔리기까지(초). staff 클립 5프레임 = 4/14 = 0.286초.")]
    [SerializeField, Min(0f)] private float castDelay = 0.286f;

    [Tooltip("장판이 깔린 뒤 터지기까지(초). 이 시간이 곧 난이도다 — 길수록 피하기 쉽다.")]
    [SerializeField, Min(0f)] private float explodeDelay = 0.5f;

    public override IEnumerator Execute(SkillContext context)
    {
        yield return new WaitForSeconds(castDelay);

        if (context.Owner == null) yield break;

        // 수정(8방향) — 앞으로 나가는 거리를 바라보는 방향으로 재되, 화면 원근에 맞게 누른다.
        Vector2 facing = context.FacingDirection;
        Vector2 center = (Vector2)context.Owner.position + Forward(facing) * forwardDistance;

        // 이펙트를 먼저 깐다. 이게 "여기가 터진다"는 예고이므로 폭발보다 앞서야 한다.
        SpawnEffectAt(center);

        yield return new WaitForSeconds(explodeDelay);

        int damage = Damage + context.BonusDamage;

        // 한 대상이 콜라이더를 여러 개 갖고 있으면 중복으로 맞을 수 있다.
        // Health를 기준으로 걸러서 한 번만 때린다.
        var hits = Physics2D.OverlapCircleAll(center, radius, targetLayers);
        var damaged = new System.Collections.Generic.HashSet<Health>();

        foreach (var hit in hits)
        {
            var health = hit.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;

            health.TakeDamage(damage, center);
        }
    }

    /// <summary>
    /// 이펙트를 지정 지점에 만든다.
    ///
    /// 부모 클래스의 SpawnEffect는 시전자 앞 고정 거리에 놓는 방식이라 장판에는 안 맞는다.
    /// 장판은 떨어지는 지점이 곧 이펙트 위치다.
    /// </summary>
    private void SpawnEffectAt(Vector2 position)
    {
        var prefab = EffectPrefab;
        if (prefab == null) return;

        Object.Instantiate(prefab, position, Quaternion.identity);
    }
}
