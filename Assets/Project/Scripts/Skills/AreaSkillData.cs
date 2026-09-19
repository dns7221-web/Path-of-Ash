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

    // 추가 생성(2026-09-17, E 판정을 그림에 맞춤) — 판정 원을 세로로 누르는 비율.
    //
    // 왜 필요한가: E의 그림은 바닥에 누운 고리라 가로 반경 약 4.7, 세로 약 3.1로 그려져 있다.
    // 그런데 판정은 반경 12인 원이어서, 고리 밖 7유닛 떨어진 적도 맞았다. 반경만 줄이면 가로는 맞아도
    // 세로로는 여전히 고리보다 넓다 — 탑다운 화면에서 바닥의 원은 세로로 눌려 보이기 때문이다.
    //
    // 기본값이 1인 이유: R(왕의 잿불)은 바닥 고리가 아니라 화면에 둥글게 터지는 그림이라 원이 맞다.
    // 값을 넣은 스킬만 타원이 된다.
    [Tooltip("판정 원을 세로로 누르는 비율. 1이면 원. 바닥에 누운 고리 그림(세로가 짧다)에 맞출 때 낮춘다. E는 0.66(3.1 ÷ 4.7).")]
    [SerializeField, Range(0.2f, 1f)] private float verticalScale = 1f;

    // 추가 생성(2026-09-17) — 판정 중심을 이펙트가 놓이는 자리에서 옮길 거리.
    //
    // E 시트는 피벗이 그림의 접지선(아래에서 39px)인데 고리의 중심은 그보다 약 0.9유닛 위에 그려져 있다.
    // 판정을 피벗에 두면 고리 위쪽 가장자리는 안 맞고 고리 아래 바깥은 맞는다.
    // 그림을 옮기지 않고 판정을 옮기는 이유: 지금까지 보던 E의 위치를 바꾸지 않는다.
    [Tooltip("판정 중심을 이펙트 자리에서 옮길 거리(유닛). 그림의 고리 중심이 피벗보다 위에 그려져 있으면 그만큼 올린다. E는 (0, 0.9).")]
    [SerializeField] private Vector2 areaCenterOffset;

    [Tooltip("때릴 대상 레이어.")]
    [SerializeField] private LayerMask targetLayers;

    // 추가 생성 — 타격감. 장판이 여러 마리를 한 번에 때려도 <see cref="PauseGate.HitStop"/>이
    // 겹침을 "더 긴 쪽"으로 처리하므로, 마릿수만큼 시간이 불어나지 않는다.
    [Tooltip("맞은 순간 멈출 실시간(초). 0이면 안 멈춘다.")]
    [SerializeField, Min(0f)] private float hitStopSeconds = 0.07f;

    [Header("타이밍")]
    // 추가 생성(궁극기 연출 확인 후) — 이 값을 정하는 규칙.
    //
    // 원래는 "동작이 꽂히는 프레임"에만 맞추면 된다고 봤다(E는 staff 5프레임 = 4/14).
    // 그런데 R(왕의 잿불)에서 그 기준만으로는 부족한 경우가 나왔다.
    //
    // R은 forwardDistance가 0이라 이펙트가 <b>시전자 발밑에 그대로 깔리고</b>, 판정 반경이
    // 14(지름 28유닛)라 이펙트도 그만큼 크다. 화면 세로가 28유닛이므로 이펙트 한 장이
    // 화면을 통째로 덮는다. 게다가 VFX는 Sorting Layer가 Entity보다 위다(프로젝트 공통 규칙).
    //
    // 그 상태에서 castDelay가 0.3이었다 — Ultimate 클립이 6프레임/10fps/0.6초이므로
    // <b>정확히 모션 한가운데</b>다. 뒷 3프레임이 이펙트에 가려 아무도 못 봤다.
    // "궁극기 모션이 안 보인다"는 증상의 원인이 애니메이션이 아니라 이 숫자였다.
    //
    // 그래서 규칙을 하나 더 붙인다: <b>이펙트가 시전자를 덮는 스킬이면, 덮이기 전에
    // 모션이 거의 끝나 있어야 한다.</b> R은 0.5로 잡아 6프레임 중 5프레임이 보이고
    // 마지막 프레임에서 겹친다 — 덮이되 "내려찍은 결과로 터진다"가 읽히는 지점이다.
    //
    // 이펙트가 몸에서 떨어져 나가는 스킬(E는 forwardDistance 8)에는 이 문제가 없다.
    // 그때는 예전 기준대로 동작 프레임에만 맞추면 된다.
    [Tooltip("시전 시작부터 장판이 깔리기까지(초). 동작이 꽂히는 프레임에 맞춘다. " +
             "단 이펙트가 시전자를 덮는 스킬이면 모션이 거의 끝난 뒤여야 모션이 보인다.")]
    [SerializeField, Min(0f)] private float castDelay = 0.286f;

    [Tooltip("장판이 깔린 뒤 터지기까지(초). 이 시간이 곧 난이도다 — 길수록 피하기 쉽다.")]
    [SerializeField, Min(0f)] private float explodeDelay = 0.5f;

    // 추가 생성(2026-09-17, R 모으기 불티) — 시전을 시작하는 순간 몸에 만드는 이펙트.
    //
    // 장판 이펙트(effectPrefab)는 castDelay 뒤에 깔리므로, 그 전의 기다리는 시간에는 화면에 아무 일도 없었다.
    // R은 그 시간이 0.5초라 "충전하는 중"이 보여야 한 방의 무게가 산다. 비워 두면 예전과 같다.
    [Header("시전 연출 (없어도 동작한다)")]
    [Tooltip("시전을 시작하는 순간 시전자 몸에 만들 이펙트. R의 모으기 불티. 비우면 안 만든다.")]
    [SerializeField] private GameObject castEffectPrefab;

    [Tooltip("시전 이펙트를 발밑에서 화면 위로 띄울 높이(유닛). 불티가 모이는 곳 = 몸통 가운데.")]
    [SerializeField, Min(0f)] private float castEffectHeight = 2.6f;

    public override IEnumerator Execute(SkillContext context)
    {
        // 추가 생성(2026-09-17) — 기다리기 전에 만든다. 기다리는 시간이 곧 이 이펙트가 보이는 시간이다.
        SpawnCastEffect(context);

        yield return new WaitForSeconds(castDelay);

        if (context.Owner == null) yield break;

        // 수정(8방향) — 앞으로 나가는 거리를 바라보는 방향으로 재되, 화면 원근에 맞게 누른다.
        Vector2 facing = context.FacingDirection;
        Vector2 center = (Vector2)context.Owner.position + Forward(facing) * forwardDistance;

        // 이펙트를 먼저 깐다. 이게 "여기가 터진다"는 예고이므로 폭발보다 앞서야 한다.
        GameObject effect = SpawnEffectAt(center);

        // 추가 생성(2026-09-17, R 판정 경계 불티 고리) — 판정 경계를 그리는 파티클에 반경을 넘긴다.
        // 첫 불티가 나오기 전(같은 프레임)이라 시작 속도를 바꿔도 이미 나간 불티가 없다.
        if (effect != null)
        {
            foreach (var ring in effect.GetComponentsInChildren<AreaRingParticles>())
                ring.SetRadius(radius);
        }

        yield return new WaitForSeconds(explodeDelay);

        int damage = Damage + context.BonusDamage;

        // 추가 생성(2026-09-17) — 판정 중심. E는 그림의 고리 중심으로 올리고, R은 그대로다(오프셋 0).
        Vector2 areaCenter = center + areaCenterOffset;

        // 한 대상이 콜라이더를 여러 개 갖고 있으면 중복으로 맞을 수 있다.
        // Health를 기준으로 걸러서 한 번만 때린다.
        //
        // 수정(2026-09-17) — 원의 중심을 areaCenter로 옮겼다. 반경은 타원의 가로 반경이라,
        // 눌린 타원(verticalScale < 1)의 후보를 빠짐없이 모은 뒤 아래 InsideArea로 한 번 더 거른다.
        var hits = Physics2D.OverlapCircleAll(areaCenter, radius, targetLayers);
        var damaged = new System.Collections.Generic.HashSet<Health>();

        // 추가 생성(2026-09-17) — 데미지가 실제로 들어간 대상이 있었는가.
        bool landed = false;

        foreach (var hit in hits)
        {
            // 추가 생성(2026-09-17) — 원 안이어도 눌린 타원 밖이면 맞지 않는다.
            if (!InsideArea(hit, areaCenter)) continue;

            var health = hit.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;

            // 수정(2026-09-17) — 넉백 기준을 판정 중심으로 바꿨다(R은 오프셋이 0이라 그대로).
            // 수정(2026-09-17) — 결과를 본다. 무적·사망으로 막힌 대상은 멈춤의 이유가 되지 않는다.
            if (health.TakeDamage(damage, areaCenter)) landed = true;
        }

        // 추가 생성 — 데미지가 실제로 들어간 뒤에 멈춘다.
        //
        // 수정(2026-09-17) — 예전에는 결과를 버리고 대상마다 바로 멈춰서, 방금 맞아 무적인 적이나 전환 중인 보스를
        // E·R로 쳐도 화면이 멈췄다(09-16에 DamageHitbox에서 고친 것과 같은 문제). 한 대상이라도 실제로 맞았을 때만 한 번 멈춘다.
        // 대상마다 부르던 것을 한 번으로 줄여도 결과는 같다 — PauseGate는 겹친 멈춤을 "더 긴 쪽" 하나로 처리한다.
        if (landed) PauseGate.HitStop(hitStopSeconds);
    }

    /// <summary>
    /// 추가 생성(2026-09-17) — 콜라이더가 눌린 타원 판정 안에 걸치는가.
    ///
    /// 콜라이더에서 판정 중심에 가장 가까운 점을 타원 식에 넣는다. 원(verticalScale 1)이면
    /// OverlapCircleAll이 이미 같은 판정을 끝냈으므로 바로 통과시킨다 — R의 판정은 예전과 똑같다.
    ///
    /// 가장 가까운 점이 "타원 기준으로 가장 가까운 점"과 정확히 같지는 않다. 타원이 눌린 만큼 가장자리에서
    /// 조금 너그럽거나 박해질 수 있지만, 몸 콜라이더가 작아서 눈으로 구별되지 않는 차이다.
    /// 정확한 타원-도형 겹침을 직접 푸는 것보다 유니티의 ClosestPoint 하나로 끝내는 쪽을 골랐다.
    /// </summary>
    private bool InsideArea(Collider2D hit, Vector2 areaCenter)
    {
        if (verticalScale >= 1f) return true;

        Vector2 offset = hit.ClosestPoint(areaCenter) - areaCenter;
        float y = offset.y / verticalScale;
        return offset.x * offset.x + y * y <= radius * radius;
    }

    /// <summary>
    /// 추가 생성(2026-09-17) — 시전 이펙트를 시전자 몸통 높이에 만든다. 없으면 아무 일도 안 한다.
    ///
    /// 자식으로 붙이지 않는 이유: 시전하는 동안 몸은 제자리에 잠겨 있어서 따라갈 일이 없고,
    /// 자식이면 시전 중에 맞아 쓰러질 때 몸과 같이 뒤집히거나 지워진다.
    /// 수명은 프리팹의 Stop Action(Destroy)이 정리한다.
    /// </summary>
    private void SpawnCastEffect(SkillContext context)
    {
        if (castEffectPrefab == null || context.Owner == null) return;

        Vector3 position = context.Owner.position + Vector3.up * castEffectHeight;
        Object.Instantiate(castEffectPrefab, position, Quaternion.identity);
    }

    /// <summary>
    /// 이펙트를 지정 지점에 만든다.
    ///
    /// 부모 클래스의 SpawnEffect는 시전자 앞 고정 거리에 놓는 방식이라 장판에는 안 맞는다.
    /// 장판은 떨어지는 지점이 곧 이펙트 위치다.
    ///
    /// 수정(2026-09-17) — 만든 이펙트를 돌려준다. 판정 경계 파티클에 반경을 넘기려면 인스턴스가 필요하다.
    /// </summary>
    private GameObject SpawnEffectAt(Vector2 position)
    {
        var prefab = EffectPrefab;
        if (prefab == null) return null;

        return Object.Instantiate(prefab, position, Quaternion.identity);
    }
}
