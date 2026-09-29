using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-29, 소리) — 누가 맞고 죽었는지 보고 전투 효과음을 고르는 규칙.
///
/// <b>체력(Health)마다 소리 컴포넌트를 붙이지 않은 이유:</b> 플레이어·적 여러 종·보스·유물·허수아비가 전부 Health를 쓴다.
/// 프리팹마다 소리를 붙이면 프리팹을 다시 만드는 빌더가 돌 때마다 빠진다(파티클이 그랬다). 대신 Health가
/// "누군가 맞았다"를 한 곳으로 알리고(<see cref="Health.AnyDamaged"/>), 여기서 대상을 보고 소리를 고른다.
/// 적이 풀에서 다시 나와도 구독을 걸고 풀 필요가 없다.
///
/// 무적으로 막힌 공격은 Health가 알리지 않으므로 소리도 나지 않는다 — 막혔는데 맞은 소리가 나면 거짓말이 된다.
/// </summary>
public static class CombatSounds
{
    /// <summary>게임이 켜질 때 한 번 건다. 두 번 걸리지 않게 먼저 뺀다(도메인 리로드를 끈 플레이 설정 대비).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        Health.AnyDamaged -= OnAnyDamaged;
        Health.AnyDamaged += OnAnyDamaged;
        Health.AnyDied -= OnAnyDied;
        Health.AnyDied += OnAnyDied;
    }

    private static void OnAnyDamaged(Health health)
    {
        // 수정(2026-09-29, 소리 2차) — 적이 맞는 칼 소리를 여기서 뺐다. "누가 맞든" 칼 소리를 내니 Q·E·R 같은 불·재 스킬에
        // 맞아도 칼 소리가 났다. 칼 소리는 기본 공격 판정이 맞혔을 때(MeleeSkillData.hitSfx), 스킬은 스킬마다 터지는 소리를 낸다.
        // 여기는 무엇에 맞았든 같은 소리가 맞는 플레이어만 맡는다.
        if (!IsPlayer(health)) return;

        // 죽는 한 대는 사망 소리가 대신한다. 둘이 겹치면 비명이 두 번 들린다.
        if (!health.IsDead) SoundPlayer.Play(SfxId.PlayerHurt);
    }

    private static void OnAnyDied(Health health)
    {
        // 적 사망 소리는 아직 없다 — 받아 둔 팩의 후보(오크 목소리)가 재의 망령과 어울리는지 들어 보고 정한다.
        if (IsPlayer(health)) SoundPlayer.Play(SfxId.PlayerDeath);
    }

    /// <summary>플레이어인가. 태그 대신 컴포넌트로 본다 — 태그는 문자열이라 틀려도 조용히 넘어간다.</summary>
    private static bool IsPlayer(Health health)
        => health != null && health.TryGetComponent(out PlayerController _);
}
