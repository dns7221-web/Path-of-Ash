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

    // 추가 생성(2026-09-29, 소리 2차) — 칼이 실제로 들어갔을 때의 소리.
    // 처음에는 "누가 맞든" 칼 소리를 냈는데(CombatSounds), 그러면 Q·E·R 같은 불·재 스킬에 맞아도 칼 소리가 났다.
    // 칼 소리는 칼이 맞혔을 때만 — 이 스킬의 판정이 맞힌 순간에만 낸다.
    [Tooltip("이 공격의 판정이 실제로 맞혔을 때 낼 효과음 이름표. 한 번에 여럿을 맞혀도 소리 목록의 최소 간격 때문에 한 번만 난다.")]
    [SerializeField] private SfxId hitSfx = SfxId.None;

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

        // 추가 생성(2026-09-29, 소리 2차) — 판정이 켜져 있는 동안 "맞혔다"를 듣는다.
        // 먼저 빼고 거는 이유: 맞아서 시전이 끊기면(StopAllCoroutines) 아래 빼는 줄까지 못 오고 구독이 남는다.
        // 다음 공격에서 한 겹 더 걸리지 않게 늘 한 겹만 유지한다. 같은 히트박스는 이 스킬만 쓰므로 남아 있어도 뜻은 같다.
        if (hitSfx != SfxId.None)
        {
            hitbox.HitLanded -= OnHitLanded;
            hitbox.HitLanded += OnHitLanded;
        }

        hitbox.Activate();

        yield return new WaitForSeconds(hitboxDuration);

        hitbox.Deactivate();

        // 추가 생성(2026-09-29, 소리 2차)
        hitbox.HitLanded -= OnHitLanded;
    }

    /// <summary>추가 생성(2026-09-29, 소리 2차) — 칼이 맞힌 순간. 맞은 대상은 상관없다(적·보스·유물·허수아비 모두 같은 소리).</summary>
    private void OnHitLanded(Health target) => SoundPlayer.Play(hitSfx);
}
