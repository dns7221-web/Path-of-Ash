using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 판정이 두 번 나가는 근접 스킬. Q(잿불 대검 내려찍기)가 이것이다.
///
/// 기획: 4프레임에 검이 바닥에 꽂히며 좁은 근접 충격, 5프레임에 그 지점에서 앞으로
/// 넓은 잿불 폭발. 붙어 있으면 둘 다 맞아 더 아프다.
///
/// 히트박스를 켜고 끄는 방식 대신 <see cref="Physics2D.OverlapBoxAll"/> 순간 판정을 쓴 이유:
/// 단계가 둘이라 히트박스도 둘이 필요한데, 그러면 프리팹에 자식을 더 달고 좌우 반전까지
/// 따로 챙겨야 한다. 순간 판정은 "그 시점 그 범위"만 보면 되고 방향은 계산으로 뒤집힌다.
/// 장판(<see cref="AreaSkillData"/>)이 같은 판단을 한 것과 같은 이유다.
/// </summary>
[CreateAssetMenu(fileName = "Skill_GroundSlam", menuName = "재의 길/스킬/내려찍기")]
public class GroundSlamSkillData : SkillData
{
    [Header("공통")]
    [SerializeField] private LayerMask targetLayers;

    [Header("1단 — 근접 충격 (4프레임)")]
    [Tooltip("검이 바닥에 꽂히는 시점(초). 6프레임 / 14fps에서 4번 프레임 = 3/14.")]
    [SerializeField, Min(0f)] private float nearDelay = 0.214f;

    [Tooltip("발밑 판정 크기(유닛). 좁고 짧다 — 코앞에 붙은 적만 맞는다.")]
    [SerializeField] private Vector2 nearSize = new Vector2(4f, 3f);

    [Tooltip("1단 데미지. 2단보다 작다. 붙어 있으면 둘 다 맞아 합계가 커진다.")]
    [SerializeField, Min(0)] private int nearDamage = 2;

    [Tooltip("검이 박히는 지점의 충격파 이펙트.")]
    [SerializeField] private GameObject nearEffect;

    [Header("2단 — 전방 폭발 (5프레임)")]
    [Tooltip("앞으로 터지는 시점(초). 5번 프레임 = 4/14.")]
    [SerializeField, Min(0f)] private float farDelay = 0.286f;

    [Tooltip("폭발 중심을 몸 앞 몇 유닛에 둘지.")]
    [SerializeField] private float farDistance = 5f;

    [Tooltip("전방 판정 크기(유닛). 1단보다 넓다 — 주 데미지원이다.")]
    [SerializeField] private Vector2 farSize = new Vector2(9f, 4f);

    [SerializeField, Min(0)] private int farDamage = 5;

    [Tooltip("앞으로 터지는 잿불 폭발 이펙트.")]
    [SerializeField] private GameObject farEffect;

    public override IEnumerator Execute(SkillContext context)
    {
        yield return new WaitForSeconds(nearDelay);
        if (context.Owner == null) yield break;

        float facing = context.FacingRight ? 1f : -1f;
        Vector2 feet = context.Owner.position;

        // 1단 — 발밑. 판정 중심을 몸 높이 절반쯤으로 올린다. 피벗이 발밑이라
        // 그대로 쓰면 판정이 바닥 아래로 반쯤 내려간다.
        Vector2 nearCenter = feet + new Vector2(0f, nearSize.y * 0.5f);
        Spawn(nearEffect, feet, facing);
        ApplyDamage(nearCenter, nearSize, nearDamage + context.BonusDamage);

        yield return new WaitForSeconds(Mathf.Max(0f, farDelay - nearDelay));
        if (context.Owner == null) yield break;

        // 2단 — 앞으로. 이펙트는 발밑 높이에 놓고(바닥에서 솟는 그림이라) 판정만 띄운다.
        Vector2 farGround = feet + new Vector2(farDistance * facing, 0f);
        Vector2 farCenter = farGround + new Vector2(0f, farSize.y * 0.5f);
        Spawn(farEffect, farGround, facing);
        ApplyDamage(farCenter, farSize, farDamage + context.BonusDamage);
    }

    /// <summary>범위 안의 대상을 한 번씩만 때린다.</summary>
    private void ApplyDamage(Vector2 center, Vector2 size, int amount)
    {
        var hits = Physics2D.OverlapBoxAll(center, size, 0f, targetLayers);
        var damaged = new HashSet<Health>();

        foreach (var hit in hits)
        {
            var health = hit.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;

            health.TakeDamage(amount, center);
        }
    }

    /// <summary>이펙트를 지정 지점에 만든다. 왼쪽을 볼 때는 뒤집는다.</summary>
    private static void Spawn(GameObject prefab, Vector2 position, float facing)
    {
        if (prefab == null) return;

        var effect = Object.Instantiate(prefab, position, Quaternion.identity);

        if (facing < 0f)
        {
            Vector3 scale = effect.transform.localScale;
            scale.x = -Mathf.Abs(scale.x);
            effect.transform.localScale = scale;
        }
    }
}
