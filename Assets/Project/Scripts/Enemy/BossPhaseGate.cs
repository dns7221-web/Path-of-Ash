/// <summary>
/// 추가 생성(2026-09-28, 포트폴리오 1단계 테스트) — 보스가 맞았을 때 "페이즈 전환이나 왕관 의식을 지금 시작하나"를 정하는 규칙.
///
/// <b>왜 <see cref="EnemyBoss"/>에서 빼냈나.</b> 보스는 애니메이터·타임라인·코루틴에 묶여 있어 통째로 띄워 시험하기 어렵다.
/// 판단만 여기로 옮기면 씬 없이 테스트로 지킬 수 있다(PathOfAsh.Tests.EditMode의 BossPhaseGateTests).
/// 이 판단에서 실제로 났던 버그 둘 — <b>죽이는 한 대가 전환을 켠 것</b>, <b>타임라인 신호가 유실돼 전환이 두 번 돈 것</b> —
/// 을 테스트가 다시 막는다. 판단 순서와 조건은 EnemyBoss.OnDamaged에 있던 그대로이고, 그 이유 주석도 거기에 남아 있다.
///
/// MonoBehaviour가 아닌 정적 클래스인 이유: 상태가 없다. 보스가 들고 있는 값(체력, 이미 시작했나)을 넘겨받아 답만 한다.
/// </summary>
public static class BossPhaseGate
{
    /// <summary>이번 피해 뒤에 보스가 할 일.</summary>
    public enum Step
    {
        /// <summary>이 피해로 죽었다 — 여기서 할 일이 없다. 죽음은 OnDied가 처리한다.</summary>
        Dead,

        /// <summary>2페이즈 전환을 시작한다.</summary>
        StartTransition,

        /// <summary>왕관 의식을 시작한다.</summary>
        StartRitual,

        /// <summary>아무것도 시작하지 않는다 — 평소 피격 처리(경직 등)로 넘어간다.</summary>
        None,
    }

    /// <summary>
    /// 맞은 뒤 체력과 지금까지의 진행으로 다음 할 일을 정한다. <b>검사 순서가 곧 우선순위다</b> — 죽음 → 전환 → 의식.
    /// </summary>
    /// <param name="current">맞은 뒤 체력.</param>
    /// <param name="max">최대 체력.</param>
    /// <param name="phase2Ratio">전환을 시작하는 체력 비율(이하에서 시작, 예: 0.7).</param>
    /// <param name="ritualRatio">의식을 시작하는 체력 비율(이하에서 시작, 예: 0.35).</param>
    /// <param name="canTransition">전환할 수 있는 보스인가(2페이즈 애니메이터가 연결돼 있나).</param>
    /// <param name="transitionStarted">전환을 한 번이라도 시작했나. <b>"지금 2페이즈인가"와 다르다</b> — 아래 설명.</param>
    /// <param name="isPhase2">지금 2페이즈인가(전환 연출 중간에 켜진다).</param>
    /// <param name="ritualStarted">의식을 한 번이라도 시작했나.</param>
    public static Step Decide(int current, int max, float phase2Ratio, float ritualRatio,
                              bool canTransition, bool transitionStarted, bool isPhase2, bool ritualStarted)
    {
        // 죽이는 한 대도 피격 알림을 먼저 거친다. 체력이 0이면 아래 비율 검사가 반드시 참이 되어 전환이 켜졌었다.
        if (current <= 0) return Step.Dead;

        // "시작했나"(transitionStarted)로 막는다. "2페이즈인가"(isPhase2)로 막으면, 연출 중간에 오는 신호가
        // 유실됐을 때 isPhase2가 계속 거짓이라 다음 한 대에 전환이 처음부터 다시 돌았다.
        if (!transitionStarted && canTransition && current <= max * phase2Ratio) return Step.StartTransition;

        // 의식은 2페이즈에서만 한 번. 1페이즈에서 큰 한 대로 35% 아래까지 떨어져도 전환이 먼저고, 의식은 그 뒤 피격에서 시작한다.
        if (isPhase2 && !ritualStarted && current <= max * ritualRatio) return Step.StartRitual;

        return Step.None;
    }
}
