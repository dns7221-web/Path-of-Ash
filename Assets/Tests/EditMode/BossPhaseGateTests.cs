using NUnit.Framework;

/// <summary>
/// 추가 생성(2026-09-28, 포트폴리오 1단계) — 보스 페이즈 전환 테스트.
///
/// <see cref="BossPhaseGate"/>는 <see cref="EnemyBoss"/>가 맞을 때마다 묻는 규칙이다 — 전환(체력이 전환 비율 이하)과
/// 왕관 의식(2페이즈에서 의식 비율 이하)을 지금 시작하나. 보스 프리팹·애니메이터·타임라인 없이 규칙만 시험한다.
///
/// 여기서 실제로 났던 버그 둘을 다시 막는다.
/// <list type="bullet">
/// <item><b>죽이는 한 대가 전환을 켰다</b> — 체력 0이면 비율 검사가 반드시 참이라 사망 대신 전환 모션이 나왔다.</item>
/// <item><b>전환이 두 번 돌았다</b> — "2페이즈인가"로 막았더니 타임라인 신호가 유실되면 다음 한 대에 전환이 처음부터 다시 돌았다.</item>
/// </list>
///
/// <b>비율을 0.75·0.25로 두는 이유:</b> 2진수로 딱 떨어지는 값이라 "정확히 경계"(75%)를 시험해도 소수점 처리 방식에 흔들리지 않는다.
/// 실제 보스 값(0.7·0.35)은 2진수로 딱 떨어지지 않아서, 정확히 70%에서 켜지는지가 실행 환경의 부동소수점 계산에 달릴 수 있다.
/// 규칙(이하에서 시작, 한 번만, 순서)은 비율 값과 상관없이 같다.
/// </summary>
public class BossPhaseGateTests
{
    private const int Max = 100;
    private const float Phase2Ratio = 0.75f;
    private const float RitualRatio = 0.25f;

    /// <summary>시험마다 바뀌는 값만 이름으로 넘기려고 기본값을 둔 도우미. 기본은 "1페이즈, 아직 아무것도 시작 안 함"이다.</summary>
    private static BossPhaseGate.Step Decide(int current, int max = Max, bool canTransition = true,
                                             bool transitionStarted = false, bool isPhase2 = false, bool ritualStarted = false)
        => BossPhaseGate.Decide(current, max, Phase2Ratio, RitualRatio, canTransition, transitionStarted, isPhase2, ritualStarted);

    /// <summary>체력이 정확히 전환 비율(75%)이 되면 전환이 시작된다("이하"의 경계).</summary>
    [Test]
    public void Transition_StartsAtExactlyTheRatio()
    {
        Assert.AreEqual(BossPhaseGate.Step.StartTransition, Decide(75));
    }

    /// <summary>경계보다 한 칸이라도 많으면 아직 전환하지 않는다.</summary>
    [Test]
    public void Transition_DoesNotStartAboveTheRatio()
    {
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(76));
    }

    /// <summary>최대 체력이 달라도 비율로 계산한다(40 × 0.75 = 30).</summary>
    [Test]
    public void Transition_ScalesWithMaxHealth()
    {
        Assert.AreEqual(BossPhaseGate.Step.StartTransition, Decide(30, max: 40));
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(31, max: 40));
    }

    /// <summary>
    /// 전환은 한 번만 시작된다. 타임라인 신호가 유실돼 2페이즈가 아직 안 켜졌어도(isPhase2 거짓) 다시 시작하지 않는다
    /// — 예전 "전환이 두 번 돌았다" 버그.
    /// </summary>
    [Test]
    public void Transition_StartsOnlyOnce_EvenIfPhase2SignalWasLost()
    {
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(50, transitionStarted: true, isPhase2: false));
    }

    /// <summary>죽이는 한 대는 전환을 켜지 않는다(체력 0·음수 모두) — 예전 "죽는 한 대가 전환을 켰다" 버그.</summary>
    [Test]
    public void KillingBlow_NeverStartsTransition()
    {
        Assert.AreEqual(BossPhaseGate.Step.Dead, Decide(0));
        Assert.AreEqual(BossPhaseGate.Step.Dead, Decide(-5), "넘치게 깎여도 죽음이 먼저");
    }

    /// <summary>1페이즈에서 큰 한 대로 의식 비율 아래까지 떨어져도 전환이 먼저다. 의식은 그 뒤 피격에서 시작한다.</summary>
    [Test]
    public void BigHitInPhase1_StartsTransitionBeforeRitual()
    {
        Assert.AreEqual(BossPhaseGate.Step.StartTransition, Decide(20));
    }

    /// <summary>2페이즈에서 체력이 정확히 의식 비율(25%)이 되면 의식이 시작된다.</summary>
    [Test]
    public void Ritual_StartsInPhase2AtExactlyTheRatio()
    {
        Assert.AreEqual(BossPhaseGate.Step.StartRitual, Decide(25, transitionStarted: true, isPhase2: true));
    }

    /// <summary>의식 비율보다 많으면 아직 의식을 시작하지 않는다.</summary>
    [Test]
    public void Ritual_DoesNotStartAboveTheRatio()
    {
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(26, transitionStarted: true, isPhase2: true));
    }

    /// <summary>의식은 한 번만 시작된다.</summary>
    [Test]
    public void Ritual_StartsOnlyOnce()
    {
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(10, transitionStarted: true, isPhase2: true, ritualStarted: true));
    }

    /// <summary>전환은 시작했지만 2페이즈가 아직 안 켜졌으면(연출 중) 의식을 기다린다 — 연출이 겹치지 않게.</summary>
    [Test]
    public void Ritual_WaitsUntilPhase2HasArrived()
    {
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(20, transitionStarted: true, isPhase2: false));
    }

    /// <summary>2페이즈 애니메이터가 연결되지 않은 보스는 전환하지 않는다(그래서 의식도 오지 않는다).</summary>
    [Test]
    public void BossWithoutPhase2_NeverTransitions()
    {
        Assert.AreEqual(BossPhaseGate.Step.None, Decide(20, canTransition: false));
    }
}
