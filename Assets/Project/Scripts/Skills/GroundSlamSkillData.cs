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
    /// 이펙트를 지면에서 띄우는 높이(유닛).
    ///
    /// 수정(시트 바닥선 정규화): 기본값을 1.2에서 0으로 내렸다.
    /// 시트를 NormalizeVfxSheets로 정리하면서 <b>피벗이 그림의 접지선</b>이 됐다.
    /// 그래서 지정 지점에 그냥 놓으면 갈라진 바닥이 그 자리에 정확히 앉는다.
    /// 예전 1.2는 피벗이 엉뚱한 곳(아래에서 15%)에 있던 걸 눈으로 때우던 값이다.
    ///
    /// 0인데도 남겨둔 이유: 접지선을 슬래브 덩어리의 가장 넓은 행으로 잡았는데
    /// 슬래브에는 두께가 있어서, 눈으로 보면 몇 픽셀 어긋나 보일 수 있다.
    /// verticalSquash와 같은 성격의 눈대중 값이라 조절할 자리를 남긴다.
    /// </summary>
    private const float EffectGroundLift = 0f;

    /// <summary>
    /// 이펙트 그림이 오른쪽을 향해 그려져 있다고 볼 때의 기준.
    ///
    /// 회전을 <b>오른쪽 절반에서만</b> 계산하고 왼쪽은 좌우 반전으로 처리하는 것이
    /// 이 스킬에서 가장 중요한 규칙이다. 예전에 바라보는 각도를 그대로 넣었더니
    /// 왼쪽을 볼 때 180도가 걸려 <b>그림이 통째로 뒤집혔다.</b> 바닥이 위로 가고 파편이
    /// 아래로 뻗어서 솟는 게 아니라 쏟아지는 것으로 보였다.
    ///
    /// 각도를 -90~90으로 묶어두면 그 경우가 구조적으로 생기지 않는다.
    /// 대신 정면(위/아래)에서는 파편이 옆을 향하게 되는데, 그림이 한 종류뿐인 이상
    /// "균열이 공격 방향으로 뻗는 것"과 "파편이 늘 위를 향하는 것" 둘 다는 만족시킬 수 없다.
    /// 방향이 읽히는 쪽을 택했다.
    /// </summary>
    private const bool ArtFacesRight = true;

    [Header("공통")]
    [SerializeField] private LayerMask targetLayers;

    // 추가 생성 — 위/아래로 칠 때 거리를 얼마나 유지할지.
    //
    // 왜 SkillData의 원근 압축(Forward)을 그대로 안 쓰는가:
    // 그쪽은 세로 거리에 0.55를 곱한다. 화면 원근으로는 맞지만, 위로 치면 8.1이 4.46이 되어
    // <b>플레이어 몸통 한가운데(키 5.94유닛)에 이펙트가 겹친다.</b> 뭘 쳤는지 안 읽힌다.
    //
    // 그래서 이 스킬은 원근을 <b>그림에만</b> 적용하고 배치에는 적용하지 않는다.
    // 회전과 길이는 여전히 VerticalSquash로 눌러 멀어 보이게 하되, 몸에서 떨어뜨리는 거리는
    // 가독성 기준으로 따로 잡는다. 1이면 좌우로 칠 때와 같은 거리만큼 위로도 나간다.
    [Tooltip("위/아래로 칠 때 유지할 거리 비율. 1이면 좌우와 같은 거리. 낮추면 몸에 붙는다.")]
    [SerializeField, Range(0.3f, 1.5f)] private float verticalReach = 1f;

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
        Vector2 nearGround = feet + Reach(facing) * nearDistance;
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
        Vector2 farGround = feet + Reach(facing) * farDistance;
        Vector2 farCenter = farGround + new Vector2(0f, farSize.y * 0.5f);
        Spawn(farEffect, farGround + new Vector2(0f, EffectGroundLift), facing);
        ApplyDamage(farCenter, farSize, farDamage + context.BonusDamage);
    }

    /// <summary>
    /// 추가 생성 — 이펙트와 판정을 몸에서 밀어낼 방향과 비율.
    ///
    /// <see cref="SkillData.Forward"/>와 갈라진 지점이다. 그쪽은 화면 원근을 그대로 반영하는
    /// 반면 이쪽은 <see cref="verticalReach"/>로 세로 거리를 따로 잡는다.
    /// 판정도 같은 값을 쓰므로 <b>보이는 곳과 맞는 곳이 어긋나지 않는다.</b>
    /// </summary>
    private Vector2 Reach(Vector2 facing)
    {
        return new Vector2(facing.x, facing.y * verticalReach);
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

    /// <summary>
    /// 이펙트를 지정 지점에 만들고 방향을 맞춘다.
    ///
    /// static을 뗀 이유: 원근 압축에 <see cref="SkillData.VerticalSquash"/>가 필요한데
    /// 그건 인스턴스 값이다. 같은 화면 각도를 상수로 또 적으면 인스펙터에서 조절할 때
    /// 거리와 그림이 서로 다른 각도를 쓰게 된다.
    /// </summary>
    private void Spawn(GameObject prefab, Vector2 position, Vector2 facing)
    {
        if (prefab == null) return;

        var effect = Object.Instantiate(prefab, position, Quaternion.identity);

        var renderer = effect.GetComponentInChildren<SpriteRenderer>();

        // 수정(2단이 거꾸로 솟던 문제): 각도 회전을 걷어내고 좌우 반전으로 되돌린다.
        //
        // 이전 주석이 말한 "반전으로는 위아래를 표현할 수 없다"는 <b>베기 궤적</b> 이야기였다.
        // 궤적은 오른쪽을 향해 그려진 방향성 그림이라 각도만큼 돌리는 게 맞다.
        // 그런데 Q의 두 이펙트는 성격이 다르다 — 1단은 바닥이 파이며 파편이 튀는 그림이고,
        // 2단은 갈라진 바닥에서 잿불 파편이 <b>위로</b> 뻗는 그림이다. 방향이 아니라
        // 중력이 기준인 그림이라 "위"가 언제나 화면 위여야 한다.
        //
        // 각도로 돌리면 왼쪽을 볼 때 180도가 걸려 그림이 통째로 뒤집혔다. 바닥 슬래브가
        // 위로 가고 파편이 아래로 뻗어서, 솟아오르는 게 아니라 <b>위에서 아래로 쏟아지는</b>
        // 것처럼 보였다. 위/아래를 볼 때도 90도로 눕었다. 오른쪽을 볼 때(각도 0)만
        // 우연히 맞아서 문제가 늦게 드러났다.
        //
        // 이펙트가 어느 쪽에 놓일지는 호출부가 이미 Forward(facing)로 위치를 밀어서 정한다.
        // 그래서 그림까지 돌릴 이유가 없고, 좌우 기울기만 맞춰주면 된다.
        bool flipped = facing.x < 0f;
        if (renderer != null) renderer.flipX = flipped;

        // 추가 생성 — 균열이 공격 방향으로 뻗도록 회전시키고, 원근만큼 길이를 줄인다.
        //
        // 바닥에 누운 방향 벡터를 화면에 투영하는 계산이다.
        // 세로 성분에 VerticalSquash를 곱하면 그게 곧 화면에서 보이는 방향이 되고,
        // 그 벡터의 길이가 곧 <b>짧아 보이는 정도</b>다. 위를 보고 치면 0.55배로 줄어든다.
        //
        // 가로 성분에 Abs를 쓰는 이유는 위 ArtFacesRight 주석에 적었다 — 각도를 -90~90에
        // 묶어 그림이 뒤집히는 경우를 없앤다. 왼쪽은 회전이 아니라 반전이 담당한다.
        //
        // scale.x를 줄이는 게 맞는 이유: 회전을 준 뒤의 x축은 <b>균열이 뻗어나가는 축</b>이다.
        // 그래서 여기를 줄이면 두께가 아니라 길이가 짧아진다.
        if (facing.sqrMagnitude > 0.0001f)
        {
            Vector2 dir = facing.normalized;
            float px = Mathf.Abs(dir.x);
            float py = dir.y * VerticalSquash;

            float angle = Mathf.Atan2(py, px) * Mathf.Rad2Deg;
            effect.transform.rotation = Quaternion.Euler(0f, 0f, flipped ? -angle : angle);

            float projected = Mathf.Sqrt(px * px + py * py);
            Vector3 scale = effect.transform.localScale;
            scale.x *= projected;
            effect.transform.localScale = scale;
        }

        // 수정(시트 바닥선 정규화) — 높이 보정을 걷어냈다.
        //
        // 걷어낸 이유: 시트 피벗이 이제 그림의 접지선이라 Instantiate가 놓은 자리가 곧
        // 갈라진 바닥이 앉을 자리다. 보정이 필요 없어졌다.
        //
        // 그리고 그 보정은 애초에 주석대로 동작한 적이 없었다. "보이는 중심"을 맞춘다고
        // 적혀 있지만 시트가 spriteMeshType 1(Full Rect)이라 renderer.bounds는 그려진
        // 부분이 아니라 <b>빈 여백까지 포함한 256x256 셀 전체</b>를 잡는다. 그래서 실제로는
        // 셀 중심을 맞추고 있었고, 그림이 셀 위쪽에 몰려 있는 만큼 이펙트가 위로 떴다.
    }
}
