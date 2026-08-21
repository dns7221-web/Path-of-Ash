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
    /// <summary>
    /// 이펙트를 지면에서 살짝 띄우는 높이(유닛).
    ///
    /// 이제 Spawn이 그림의 보이는 중심을 지정 지점에 맞추므로, 이 값이 곧 그림 중심 높이다.
    /// 0이면 절반이 바닥에 묻히므로 정강이 높이쯤으로 살짝 띄운다.
    /// </summary>
    private const float EffectGroundLift = 1.2f;

    [Header("공통")]
    [SerializeField] private LayerMask targetLayers;

    [Header("1단 — 근접 충격 (4프레임)")]
    [Tooltip("검이 바닥에 꽂히는 시점(초). 6프레임 / 14fps에서 4번 프레임 = 3/14.")]
    [SerializeField, Min(0f)] private float nearDelay = 0.214f;

    // 추가 생성 — 1단이 몸에서 앞으로 나가는 거리.
    //
    // 왜 필요한가: 예전에는 이 값이 없어서 1단 판정과 이펙트가 <b>플레이어 몸 안에서</b> 터졌다.
    // 대검을 앞으로 내려찍는 동작인데 이펙트가 몸에 겹쳐 있으니 무엇이 터진 건지 안 보였다.
    // 발밑에서 터지는 게 맞는 스킬(제자리 충격파)이라면 0으로 두면 된다.
    [Tooltip("1단이 몸에서 앞으로 나가는 거리(유닛). 0이면 발밑에서 터진다.")]
    [SerializeField, Min(0f)] private float nearDistance = 2.5f;

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

        // 수정(8방향) — 2단 판정이 나가는 곳을 바라보는 방향으로 잡는다.
        Vector2 facing = context.FacingDirection;
        Vector2 feet = context.Owner.position;

        // 1단 — 발밑. 판정 중심을 몸 높이 절반쯤으로 올린다. 피벗이 발밑이라
        // 그대로 쓰면 판정이 바닥 아래로 반쯤 내려간다.
        // 수정(1단 위치): 앞으로 nearDistance만큼 내보낸다.
        // 이게 없으면 판정도 이펙트도 몸 안에서 터져서 무엇이 일어났는지 읽히지 않는다.
        Vector2 nearGround = feet + Forward(facing) * nearDistance;
        Vector2 nearCenter = nearGround + new Vector2(0f, nearSize.y * 0.5f);

        // 수정(이펙트 높이): 이펙트는 판정 중심이 아니라 <b>지면 가까이</b> 놓는다.
        //
        // 판정 상자는 중심이 위에 있어도 아래 절반이 지면부터 시작한다(중심 = 바닥 + 높이/2).
        // 즉 지면이 곧 판정의 바닥이라 여기 놓아도 어긋나지 않는다. 반대로 중심에 놓으면
        // 대검을 바닥에 내려찍는데 충격파가 가슴 높이에 떠서 따로 논다.
        Spawn(nearEffect, nearGround + new Vector2(0f, EffectGroundLift), facing);
        ApplyDamage(nearCenter, nearSize, nearDamage + context.BonusDamage);

        yield return new WaitForSeconds(Mathf.Max(0f, farDelay - nearDelay));
        if (context.Owner == null) yield break;

        // 2단 — 앞으로. 이펙트는 발밑 높이에 놓고(바닥에서 솟는 그림이라) 판정만 띄운다.
        // 수정(8방향 + 이펙트 위치): 거리를 화면 원근에 맞게 누르고, 이펙트를 판정 중심에 놓는다.
        // Forward를 안 쓰면 위/아래로 쓸 때만 2단이 몸에서 훨씬 멀리 떨어져 따로 논다.
        Vector2 farGround = feet + Forward(facing) * farDistance;
        Vector2 farCenter = farGround + new Vector2(0f, farSize.y * 0.5f);
        Spawn(farEffect, farGround + new Vector2(0f, EffectGroundLift), facing);
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

    /// <summary>이펙트를 지정 지점에 만들고 바라보는 방향으로 돌린다.</summary>
    private static void Spawn(GameObject prefab, Vector2 position, Vector2 facing)
    {
        if (prefab == null) return;

        var effect = Object.Instantiate(prefab, position, Quaternion.identity);

        // 수정(8방향) — 좌우 반전에서 회전으로 바꿨다. 반전으로는 위아래를 표현할 수 없어서
        // 위를 보고 내려찍어도 균열이 옆으로 뻗었다.
        if (facing.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            effect.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // 수정(이펙트 높이) — 그림의 <b>보이는 중심</b>을 지정 지점에 맞춘다.
        //
        // 왜 필요한가: 이펙트 시트의 피벗이 캐릭터 규칙(아래에서 15%)으로 잡혀 있는데
        // 그림은 프레임 위쪽에 그려져 있다. 그래서 피벗을 지면에 두면 그림 중심이
        // <b>3유닛 위, 즉 가슴 높이</b>에 떠버렸다.
        //
        // 상수로 빼지 않고 매번 재는 이유: 1단과 2단은 스케일도 그림 위치도 다르다.
        // 같은 숫자를 쓰면 한쪽은 맞고 한쪽은 어긋난다. 렌더러가 실제 경계를 알고 있으므로
        // 거기서 가져오면 그림을 바꾸거나 크기를 조절해도 저절로 따라온다.
        var renderer = effect.GetComponentInChildren<SpriteRenderer>();
        if (renderer != null && renderer.sprite != null)
        {
            float centerOffset = renderer.bounds.center.y - effect.transform.position.y;
            effect.transform.position -= new Vector3(0f, centerOffset, 0f);
        }
    }
}
