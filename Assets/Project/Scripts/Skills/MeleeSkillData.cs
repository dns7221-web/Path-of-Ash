using System.Collections;
using UnityEngine;

/// <summary>
/// 근접 스킬. 시전자 앞의 히트박스를 잠깐 켰다 끈다. Q(잿불 베기)가 이것이다.
///
/// 히트박스를 스킬이 <b>새로 만들지 않고</b> 시전자가 이미 들고 있는 것을 켜는 이유:
/// 검 판정의 크기와 위치는 캐릭터의 몸 크기에서 나오는 값이라 프리팹이 정할 일이다.
/// 스킬 에셋이 크기까지 들고 있으면, 캐릭터 PPU를 바꿀 때 프리팹과 스킬 에셋 두 군데를
/// 고쳐야 하고 한쪽을 잊으면 판정만 옛날 크기로 남는다.
/// </summary>
[CreateAssetMenu(fileName = "Skill_Melee", menuName = "재의 길/스킬/근접")]
public class MeleeSkillData : SkillData
{
    [Header("근접")]
    [Tooltip("시전 시작부터 판정이 켜지기까지의 시간(초). 검을 들어올리는 동안은 안 맞아야 한다.")]
    [SerializeField, Min(0f)] private float hitboxDelay = 0.12f;

    [Tooltip("판정이 켜져 있는 시간(초). 길수록 맞히기 쉽다.")]
    [SerializeField, Min(0.01f)] private float hitboxDuration = 0.18f;

    public override IEnumerator Execute(SkillContext context)
    {
        var hitbox = context.MeleeHitbox;
        if (hitbox == null)
        {
            Debug.LogWarning($"[{DisplayName}] 근접 히트박스가 연결돼 있지 않다. 아무 일도 일어나지 않는다.");
            yield break;
        }

        yield return new WaitForSeconds(hitboxDelay);

        // 데미지를 켜기 직전에 넣는 이유: 유물로 얻은 보정치가 전투 중에 늘어날 수 있다.
        // 시작할 때 한 번만 계산하면 방금 먹은 유물이 이번 판에 반영되지 않는다.
        hitbox.SetDamage(Damage + context.BonusDamage);

        SpawnEffect(context);
        hitbox.Activate();

        yield return new WaitForSeconds(hitboxDuration);

        hitbox.Deactivate();
    }
}
