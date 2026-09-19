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
///
/// <b>주의 — 이 스킬은 <see cref="SkillData.Damage"/>를 쓰지 않는다.</b>
///
/// 데미지가 1단·2단으로 갈라져 있어서 <see cref="nearDamage"/>와 <see cref="farDamage"/>가
/// 전부를 담당한다. 상속받은 damage는 <b>한 번도 읽히지 않는</b> 죽은 값이고, 그래서
/// Skill_Q_GroundSlam.asset에는 0으로 들어 있다. 나머지 네 스킬(근접·투사체·장판)은
/// 그 값을 실제로 쓰므로 0이 아니다.
///
/// 인스펙터에서 "Damage 0"을 보고 버그로 오해하기 쉬운 자리다. <b>거기를 채워도 게임에서는
/// 아무것도 안 바뀐다</b> — 동작하는 것처럼 보이는 값이 하나 늘 뿐이라 오히려 나쁘다.
/// 이 스킬의 데미지를 조절하려면 아래 nearDamage / farDamage를 고쳐야 한다.
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

    // 추가 생성 — 타격감. 내려찍기는 근·원 두 판정이 연달아 도는데, 겹침을 "더 긴 쪽"으로
    // 처리하므로 두 번 맞아도 한 번 멈춘 것처럼 보인다. 그게 의도다.
    //
    // 수정(2026-09-17, 사용자 결정) — 이제 <b>2단에서만</b> 멈춘다. 1단(0.25초)과 2단(0.42초)이 0.17초 차이라
    // 1단에서 멈추면 멈춤이 두 번 이어져 한 방이 아니라 끊긴 두 방으로 읽혔다. 무게가 실린 2단 폭발에 한 번 멈춘다.
    [Tooltip("2단(전방 폭발)이 맞은 순간 멈출 실시간(초). 1단에서는 멈추지 않는다. 0이면 안 멈춘다.")]
    [SerializeField, Min(0f)] private float hitStopSeconds = 0.08f;

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

    [Tooltip("1단 데미지. 2단보다 작다. 붙어 있으면 둘 다 맞아 합계가 커진다. " +
             "이 스킬의 데미지는 위 Damage가 아니라 여기와 farDamage가 정한다.")]
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

        // 추가 생성(2026-09-17) — 그림을 놓는 방식(반전·회전·길이)을 한 번만 구해 그림과 판정이 같이 쓴다.
        ArtPose pose = PoseFor(facing);

        // 1단 — 발밑. 판정 중심을 몸 높이 절반쯤으로 올린다. 피벗이 발밑이라
        // 그대로 쓰면 판정이 바닥 아래로 반쯤 내려간다.
        // 수정(1단 위치): 앞으로 nearDistance만큼 내보낸다.
        // 이게 없으면 판정도 이펙트도 몸 안에서 터져서 무엇이 일어났는지 읽히지 않는다.
        // 수정(2026-09-17) — "몸 높이 절반쯤 올리기"는 ApplyDamage 안으로 옮겼다. 그림이 돌면 올리는 방향도 같이 돌아야 한다.
        Vector2 nearGround = feet + Reach(facing) * nearDistance;

        // 수정(이펙트 높이): 이펙트는 판정 중심이 아니라 <b>지면 가까이</b> 놓는다.
        //
        // 판정 상자는 중심이 위에 있어도 아래 절반이 지면부터 시작한다(중심 = 바닥 + 높이/2).
        // 즉 지면이 곧 판정의 바닥이라 여기 놓아도 어긋나지 않는다. 반대로 중심에 놓으면
        // 대검을 바닥에 내려찍는데 충격파가 가슴 높이에 떠서 따로 논다.
        Spawn(nearEffect, nearGround + new Vector2(0f, EffectGroundLift), pose);

        // 수정(2026-09-17, 사용자 결정) — 1단은 멈추지 않는다. 멈춤은 2단에서 한 번.
        // 수정(2026-09-17, 사용자 결정) — 1단은 피격 무적도 걸지 않는다. 걸면 붙어 있던 적이 0.17초 뒤의 2단을 무시한다.
        ApplyDamage(nearGround, nearSize, pose, nearDamage + context.BonusDamage,
                    hitStop: false, grantInvulnerability: false);

        yield return new WaitForSeconds(Mathf.Max(0f, farDelay - nearDelay));
        if (context.Owner == null) yield break;

        // 2단 — 앞으로. 이펙트는 발밑 높이에 놓고(바닥에서 솟는 그림이라) 판정만 띄운다.
        // 수정(8방향 + 이펙트 위치): 거리를 화면 원근에 맞게 누르고, 이펙트를 판정 중심에 놓는다.
        // Forward를 안 쓰면 위/아래로 쓸 때만 2단이 몸에서 훨씬 멀리 떨어져 따로 논다.
        Vector2 farGround = feet + Reach(facing) * farDistance;
        Spawn(farEffect, farGround + new Vector2(0f, EffectGroundLift), pose);
        ApplyDamage(farGround, farSize, pose, farDamage + context.BonusDamage,
                    hitStop: true, grantInvulnerability: true);
    }

    /// <summary>
    /// 추가 생성(2026-09-17) — 이펙트 그림을 놓는 방식. 그림과 판정 상자가 같은 값을 쓴다.
    /// </summary>
    private struct ArtPose
    {
        /// <summary>그림을 좌우 반전하는가(왼쪽을 볼 때).</summary>
        public bool Flipped;

        /// <summary>그림의 최종 회전(도). 반전한 경우 부호까지 반영된 값이다.</summary>
        public float Angle;

        /// <summary>균열이 뻗는 축(그림의 X)의 원근 배율. 좌우로 칠 때 1, 위·아래로 칠 때 VerticalSquash.</summary>
        public float LengthScale;
    }

    /// <summary>
    /// 추가 생성(2026-09-17) — 바라보는 방향에서 그림의 반전·회전·길이를 구한다.
    ///
    /// 예전에는 <see cref="Spawn"/> 안에서만 계산해서 <b>그림만 돌고 판정 상자는 늘 가로 그대로</b>였다.
    /// 위로 칠 때 그림은 90도 돌아 세로 균열(불길은 왼쪽)이 되는데 판정은 가로 띠여서, 보이는 곳과 맞는 곳이 달랐다
    /// (2단 기준 그림 x -10.1~0.9 · y 4.0~12.2, 판정 x ±9.45 · y 8.1~16.5). 계산을 여기로 빼서 둘이 같은 값을 쓴다.
    ///
    /// 계산 자체는 예전 Spawn의 것과 같다. 좌우로 칠 때는 회전 0·길이 1이라 판정이 예전과 똑같다.
    /// </summary>
    private ArtPose PoseFor(Vector2 facing)
    {
        var pose = new ArtPose { Flipped = facing.x < 0f, Angle = 0f, LengthScale = 1f };

        if (facing.sqrMagnitude > 0.0001f)
        {
            Vector2 dir = facing.normalized;
            float px = Mathf.Abs(dir.x);
            float py = dir.y * VerticalSquash;

            float angle = Mathf.Atan2(py, px) * Mathf.Rad2Deg;
            pose.Angle = pose.Flipped ? -angle : angle;
            pose.LengthScale = Mathf.Sqrt(px * px + py * py);
        }

        return pose;
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

    /// <summary>
    /// 범위 안의 대상을 한 번씩만 때린다.
    ///
    /// 수정(2026-09-17) — 판정 상자를 그림과 같이 놓는다. 인자가 상자 중심에서 <b>지면 점</b>으로 바뀌었다.
    /// <list type="bullet">
    /// <item>길이(size.x)에 그림의 원근 배율을 곱한다 — 위·아래로 칠 때 짧아진 균열만큼.</item>
    /// <item>"지면에서 높이 절반만큼 올린 중심"을 그림의 회전만큼 돌린다 — 그림의 위쪽(불길이 솟는 쪽)과 같은 쪽으로.</item>
    /// <item>상자도 같은 각도로 돌린다(OverlapBoxAll의 각도 인자 — 회전한 사각형 겹침은 유니티가 푼다).</item>
    /// </list>
    /// 좌우로 칠 때는 각도 0·배율 1이라 예전과 같은 상자다. 반전은 상자가 좌우 대칭이라 따로 볼 필요가 없다.
    /// </summary>
    /// <param name="ground">그림이 놓이는 지면 점.</param>
    /// <param name="size">좌우로 칠 때 기준의 상자 크기(가로 = 균열 방향, 세로 = 불길 높이).</param>
    /// <param name="pose">그림을 놓은 방식.</param>
    /// <param name="amount">데미지.</param>
    /// <param name="hitStop">맞은 대상이 있으면 멈출지. 추가 생성(2026-09-17) — 1단은 false, 2단은 true.</param>
    /// <param name="grantInvulnerability">
    /// 추가 생성(2026-09-17) — 맞은 대상에게 피격 무적을 걸지. 1단은 false라야 0.17초 뒤의 2단이 붙어 있는 적에게 들어간다
    /// (<see cref="Health.TakeDamage(int, Vector2?, bool)"/> 설명 참고). 2단은 true — 동작이 끝났으니 평소 규칙대로 건다.
    /// </param>
    private void ApplyDamage(Vector2 ground, Vector2 size, ArtPose pose, int amount, bool hitStop, bool grantInvulnerability)
    {
        Vector2 boxSize = new Vector2(size.x * pose.LengthScale, size.y);
        Vector2 center = ground + Rotate(new Vector2(0f, size.y * 0.5f), pose.Angle);

        var hits = Physics2D.OverlapBoxAll(center, boxSize, pose.Angle, targetLayers);
        var damaged = new HashSet<Health>();

        // 추가 생성(2026-09-17) — 이번 판정에서 데미지가 실제로 들어간 대상이 있었는가.
        bool landed = false;

        foreach (var hit in hits)
        {
            var health = hit.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;

            // 수정(2026-09-17) — 결과를 본다. 무적·사망으로 막힌 대상은 멈춤의 이유가 되지 않는다.
            if (health.TakeDamage(amount, center, grantInvulnerability)) landed = true;
        }

        // 추가 생성 — 데미지가 실제로 들어간 뒤에 멈춘다.
        //
        // 수정(2026-09-17) — 예전에는 대상마다 TakeDamage의 결과를 버리고 바로 멈춰서, 막힌 대상(피격 무적·전환 중인 보스)을
        // 쳐도 화면이 멈췄다. 09-16에 DamageHitbox에서 고친 것과 같은 문제다. 이제 한 대상이라도 실제로 맞았을 때만,
        // 그리고 멈출 단계(2단)에서만 한 번 멈춘다. 대상마다 부르던 것을 한 번으로 줄여도 결과는 같다 —
        // PauseGate는 겹친 멈춤을 "더 긴 쪽" 하나로 처리한다.
        if (hitStop && landed) PauseGate.HitStop(hitStopSeconds);
    }

    /// <summary>추가 생성(2026-09-17) — 벡터를 degrees만큼 반시계로 돌린다. Transform 회전(Euler z)과 같은 방향이다.</summary>
    private static Vector2 Rotate(Vector2 value, float degrees)
    {
        return (Vector2)(Quaternion.Euler(0f, 0f, degrees) * value);
    }

    /// <summary>
    /// 이펙트를 지정 지점에 만들고 방향을 맞춘다.
    ///
    /// static을 뗀 이유: 원근 압축에 <see cref="SkillData.VerticalSquash"/>가 필요한데
    /// 그건 인스턴스 값이다. 같은 화면 각도를 상수로 또 적으면 인스펙터에서 조절할 때
    /// 거리와 그림이 서로 다른 각도를 쓰게 된다.
    ///
    /// 수정(2026-09-17) — 바라보는 방향 대신 <see cref="PoseFor"/>가 구한 값을 받는다. 판정 상자가 같은 값을 쓰게 하려고
    /// 계산을 밖으로 뺐다. 아래 주석들은 그 계산이 왜 그런 모양인지에 대한 설명이라 그대로 둔다.
    /// </summary>
    private void Spawn(GameObject prefab, Vector2 position, ArtPose pose)
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
        // 수정(2026-09-17) — 반전 여부는 PoseFor가 정한다(facing.x < 0 그대로).
        bool flipped = pose.Flipped;
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
        //
        // 수정(2026-09-17) — 각도·배율 계산(px, py, Atan2, 투영 길이)은 PoseFor로 옮겼다. 여기서는 적용만 한다.
        effect.transform.rotation = Quaternion.Euler(0f, 0f, pose.Angle);

        Vector3 scale = effect.transform.localScale;
        scale.x *= pose.LengthScale;
        effect.transform.localScale = scale;

        // 추가 생성(2026-09-17, 플레이어 파티클) — 곁들인 파티클도 그림과 같이 뒤집고 줄인다.
        //
        // 파티클은 flipX를 모르고, Scaling Mode가 Local이라 위 배율도 안 먹는다. 그대로 두면 왼쪽으로 칠 때
        // 불티가 오른쪽부터 번지고(Q-3), 위·아래로 칠 때 짧아진 균열 밖까지 불티가 솟는다.
        // 회전은 부모를 따라가므로 따로 맞출 필요가 없다.
        ParticleGarnish.MatchSprite(effect, flipped, pose.LengthScale);

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
