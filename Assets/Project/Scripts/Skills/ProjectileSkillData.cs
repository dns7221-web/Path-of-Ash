using System.Collections;
using UnityEngine;

/// <summary>
/// 투사체를 쏘는 스킬. W(잿가루 화살)가 이것이다.
///
/// 근접 스킬과 달리 시전자의 히트박스를 쓰지 않는다. 판정이 몸에서 떨어져 나가 따로
/// 날아가므로, 데미지도 투사체가 들고 간다.
/// </summary>
[CreateAssetMenu(fileName = "Skill_Projectile", menuName = "재의 길/스킬/투사체")]
public class ProjectileSkillData : SkillData
{
    [Header("투사체")]
    [Tooltip("발사할 투사체 프리팹.")]
    [SerializeField] private Projectile projectilePrefab;

    [Tooltip("시전 시작부터 발사까지의 시간(초). 활을 당기는 동안은 안 나가야 한다. " +
             "bow 클립이 6프레임 / 14fps이고 5번 프레임에서 놓으므로 4/14 = 0.286초.")]
    [SerializeField, Min(0f)] private float releaseDelay = 0.286f;

    [Tooltip("발사 위치를 몸 앞으로 얼마나 밀지(유닛). 몸에 겹쳐 생성되면 자기 옆의 적을 " +
             "즉시 때려서 근접기가 된다.")]
    [SerializeField] private float forwardOffset = 2.5f;

    [Tooltip("발사 높이(유닛). 피벗이 발밑이라 0이면 바닥에서 쏜다. 적 몸통 높이에 맞춘다.")]
    [SerializeField] private float launchHeight = 1.2f;

    public override IEnumerator Execute(SkillContext context)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"[{DisplayName}] 투사체 프리팹이 비어 있다.");
            yield break;
        }

        yield return new WaitForSeconds(releaseDelay);

        // 대기하는 동안 죽거나 맞아서 시전이 끊겼을 수 있다. 그래도 화살이 나가면
        // "맞고 쓰러졌는데 화살은 날아가는" 그림이 된다.
        if (context.Owner == null) yield break;

        float facing = context.FacingRight ? 1f : -1f;
        Vector3 spawn = context.Owner.position +
                        new Vector3(forwardOffset * facing, launchHeight, 0f);

        SpawnEffect(context);

        var projectile = Object.Instantiate(projectilePrefab, spawn, Quaternion.identity);
        projectile.Launch(new Vector2(facing, 0f), Damage + context.BonusDamage);
    }
}
