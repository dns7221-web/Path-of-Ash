using System.Collections;
using UnityEngine;

/// <summary>
/// 추가 생성 — 보스 한 마리로 이루어진 방 전투.
///
/// <see cref="EnemySpawner"/>와 같은 <see cref="RoomEncounter"/>를 상속하므로
/// <see cref="RoomController"/> 입장에서는 잡몹 방과 완전히 똑같이 보인다.
/// 방 진행 로직을 한 줄도 고치지 않고 보스 방을 끼워 넣을 수 있다.
///
/// 오브젝트 풀을 쓰지 않는 이유:
/// 잡몹은 방마다 여러 마리가 반복해서 나오니 <c>ObjectPool</c>이 이득이지만, 보스는 방에 한 마리뿐이라
/// 재사용으로 아낄 게 없다. 오히려 보스는 2페이즈에서 <b>AnimatorController를 통째로 갈아끼우고</b>
/// 체력·상태·코루틴이 전부 바뀌기 때문에, 재사용하면 이전 판의 2페이즈 상태가 그대로 남는다.
/// 매번 새로 만들고 지우는 쪽이 안전하고 코드도 짧다.
/// </summary>
[DisallowMultipleComponent]
public class BossEncounter : RoomEncounter
{
    [Header("보스")]
    [SerializeField] private EnemyBoss bossPrefab;
    [Tooltip("보스가 등장할 위치. 비우면 이 오브젝트 자리에 놓는다.")]
    [SerializeField] private Transform spawnPoint;

    [Header("연출")]
    [Tooltip("보스가 죽고 나서 상자가 나오기까지의 시간. 사망 모션이 끝까지 보이게 하는 용도.")]
    [SerializeField, Min(0f)] private float clearedDelay = 1.6f;

    [Header("기록")]
    [Tooltip("처치 수를 기록할 RunManager. 비우면 씬에서 찾는다.")]
    [SerializeField] private RunManager runManager;

    // 추가 생성(2026-09-22) — 보스 궁극기 "왕관 의식"의 연출.
    [Header("왕관 의식")]
    [Tooltip("보스 궁극기 연출. 비우면 같은 오브젝트에서 찾는다. 없으면 보스가 의식 없이 이어 싸운다. " +
             "Tools → 재의 길 → 씬·세팅 → 왕관 의식 구성 이 붙인다.")]
    [SerializeField] private CrownRitual crownRitual;

    private EnemyBoss activeBoss;
    private Health bossHealth;
    private bool encounterStarted;

    // 추가 생성 — 처치 시 채울 재 게이지. 플레이어가 프리팹 인스턴스라 미리 못 걸어둔다.
    // EnemySpawner가 쓰는 방식과 같다.
    private AshGauge ashGauge;

    // 추가 생성 — 보스 체력바. 반대 방향으로 같은 문제가 있어 여기서 잇는다.
    //
    // 재 게이지는 <b>플레이어가</b> 프리팹이라 HUD가 미리 못 걸고, 이쪽은 <b>보스가</b>
    // 프리팹이라 HUD가 미리 못 건다. 어느 쪽이든 씬에 저장된 참조로는 이을 수 없어서,
    // 둘 다 실행 중에 방이 찾아서 연결한다.
    private BossHealthBar healthBar;

    // 추가 생성(2026-09-20) — 보스 시전 바. 체력바와 같은 이유로 여기서 잇는다.
    private BossCastBar castBar;

    // 추가 생성(2026-09-22) — 유물을 뽑힐 플레이어. 플레이어도 프리팹이라 씬에 미리 못 걸어서,
    // 의식이 시작될 때 한 번 찾는다(판마다 한 번이라 찾는 비용은 무시할 수 있다).
    private PlayerController player;

    private void Awake()
    {
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();

        // 추가 생성(2026-09-22) — 구성 도구가 같은 오브젝트에 붙이므로, 비어 있으면 거기서 찾는다.
        if (crownRitual == null) crownRitual = GetComponent<CrownRitual>();
    }

    private void OnDisable()
    {
        // 방이 꺼질 때 예약된 전투 종료 알림을 취소한다.
        // 이게 없으면 보스를 잡자마자 방을 나갔을 때 다음 방에서 상자가 튀어나온다.
        //
        // 수정(시간 정지 문제): CancelInvoke → StopAllCoroutines. 아래 대기가 Invoke에서
        // 코루틴으로 바뀌면서 취소 수단도 같이 바뀌어야 한다. 오브젝트가 꺼지면 코루틴은
        // 어차피 멈추지만, 컴포넌트만 꺼지는 경로도 있어서 명시적으로 끊는다.
        StopAllCoroutines();
        Cleanup();
    }

    /// <summary>
    /// 보스를 등장시킨다.
    ///
    /// 이미 전투 중이면 아무것도 하지 않는다. EnemySpawner의 같은 이름 함수와 규칙을 맞춘 것으로,
    /// 진행 관리자가 실수로 두 번 불러도 보스가 두 마리 나오지 않게 하는 방어다.
    /// </summary>
    public override void BeginEncounter()
    {
        if (encounterStarted || activeBoss != null) return;

        if (bossPrefab == null)
        {
            Debug.LogError("[보스 방] 보스 프리팹이 비어 있다.", this);
            return;
        }

        encounterStarted = true;

        Transform point = spawnPoint != null ? spawnPoint : transform;
        activeBoss = Instantiate(bossPrefab, point.position, Quaternion.identity, transform);

        // 보스의 사망 신호는 Health가 낸다. EnemyBoss는 같은 신호로 사망 모션만 재생하고,
        // 방 진행은 여기서 따로 듣는다 — 적 스크립트가 방 구조를 몰라도 되게 하려는 분리다.
        bossHealth = activeBoss.GetComponent<Health>();
        if (bossHealth == null)
        {
            Debug.LogError("[보스 방] 보스 프리팹에 Health가 없다. 전투가 끝나지 않는다.", activeBoss);
            return;
        }

        bossHealth.Died += OnBossDied;

        // 추가 생성 — 화면 위 보스 체력바를 이 보스에 물린다.
        //
        // 눈금 위치를 보스에게 물어서 넘긴다. 여기에 0.5를 적어두면 보스의 전환 비율을
        // 바꿨을 때 눈금만 옛 자리에 남는데, 그건 에러 없이 "전환이 눈금과 다른 데서
        // 온다"로만 나타나서 버그가 아니라 밸런스 문제로 오해하게 된다.
        if (healthBar == null) healthBar = FindFirstObjectByType<BossHealthBar>();

        if (healthBar != null)
        {
            healthBar.Bind(bossHealth, activeBoss.Phase2HealthRatio);
            activeBoss.EnteredPhase2 += OnBossEnteredPhase2;
        }
        else
        {
            // 경고에 그친다. 체력바가 없어도 보스전은 성립한다 — 화면에 안 보일 뿐이다.
            // 여기서 return하면 HUD를 아직 안 만든 테스트 씬에서 보스가 아예 안 나온다.
            Debug.LogWarning("[보스 방] 씬에서 BossHealthBar를 못 찾았다. " +
                             "Tools → 재의 길 → 화면 → 게임 HUD 생성 을 실행해라.", this);
        }

        // 추가 생성(2026-09-20) — 시전 바를 보스의 시전 신호에 잇는다.
        //
        // 체력바와 달리 없어도 경고하지 않는다. 시전 바는 예고용이라 없으면 예고만 사라지고,
        // 패턴은 그대로 돈다 — HUD를 안 만든 테스트 씬에서 콘솔만 시끄러워질 이유가 없다.
        if (castBar == null) castBar = FindFirstObjectByType<BossCastBar>();

        if (castBar != null)
        {
            castBar.HideImmediate();
            activeBoss.CastStarted += OnBossCastStarted;
            activeBoss.CastEnded += OnBossCastEnded;
        }

        // 추가 생성(2026-09-22) — 왕관 의식을 보스의 신호에 잇는다.
        //
        // 의식이 없는 방에서는 <b>구독하지 않는다.</b> 보스는 듣는 쪽이 없으면 경고를 남기고 의식 없이
        // 이어 싸운다. 구독만 해 두고 아무것도 안 하면 보스가 무적인 채로 영영 서 있어서, 에러 없이
        // "보스가 안 죽는다"로만 보이는 막힘이 된다.
        if (crownRitual != null)
        {
            activeBoss.CrownRitualStarted += OnCrownRitualStarted;
            crownRitual.Finished += OnCrownRitualFinished;

            // 추가 생성(2026-09-22, 보스 파티클 1부) — 의식의 순간들을 보스 동작으로 전한다(손 뻗기 → 의식 자세, 움찔).
            crownRitual.RelicsTorn += OnRitualRelicsTorn;
            crownRitual.RelicBroken += OnRitualRelicBroken;
        }
    }

    /// <summary>추가 생성(2026-09-22) — 유물이 뽑혔다. 보스를 손 뻗기에서 의식 자세로 넘긴다.</summary>
    private void OnRitualRelicsTorn()
    {
        if (activeBoss != null) activeBoss.RitualRelicsTorn();
    }

    /// <summary>추가 생성(2026-09-22) — 유물 하나가 깨졌다. 보스가 움찔한다.</summary>
    private void OnRitualRelicBroken()
    {
        if (activeBoss != null) activeBoss.RitualFlinch();
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 보스가 왕관 의식을 시작했을 때. 연출을 돌린다.
    ///
    /// 보스가 의식을 직접 돌리지 않고 방이 받아 넘기는 이유는 시전 바와 같다. 의식에 필요한 것 —
    /// 네 귀퉁이, 유물, 시전 바, 플레이어 — 이 전부 방 쪽에 있다. 보스는 "시작했다"만 알린다.
    /// </summary>
    private void OnCrownRitualStarted()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();

        // 플레이어를 못 찾아도 넘긴다. 의식 쪽이 연출 없이 곧바로 끝내고 Finished를 울려,
        // 보스가 무적인 채로 멈춰 있지 않게 한다.
        // 수정(2026-09-22) — 보스 가슴 자리를 같이 넘긴다. 유물 실이 닿고, 못 막았을 때 유물이 빨려 들어가는 곳이다.
        crownRitual.Begin(player, castBar, activeBoss != null ? activeBoss.RitualChest : (Vector3?)null);
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 의식이 끝났을 때. 보스를 싸움으로 돌려보낸다.
    /// 유물을 다 부숴 의식을 막았으면 보스를 그로기로 보낸다 — 의식은 보스를 모르므로 방이 전한다.
    /// </summary>
    /// <param name="bossGroggy">유물을 다 부숴 의식을 막았는가.</param>
    private void OnCrownRitualFinished(bool bossGroggy)
    {
        if (activeBoss != null) activeBoss.EndCrownRitual(bossGroggy);
    }

    /// <summary>
    /// 추가 생성(2026-09-20) — 보스가 예고가 필요한 패턴을 시작했을 때. 그대로 시전 바에 넘긴다.
    /// </summary>
    private void OnBossCastStarted(string patternName, float seconds)
    {
        castBar?.Show(patternName, seconds);
    }

    /// <summary>추가 생성(2026-09-20) — 시전이 끝났거나 끊겼을 때.</summary>
    private void OnBossCastEnded()
    {
        castBar?.Hide();
    }

    /// <summary>
    /// 추가 생성 — 보스가 2페이즈에 들어갔을 때. 체력바에 그대로 넘긴다.
    ///
    /// 방이 중간에서 받는 이유는 사망 처리와 같다. 보스가 HUD를 직접 찾으면 적 스크립트가
    /// 화면 구조를 알게 되고, HUD가 없는 씬에 보스를 놓는 순간 보스 쪽이 경고를 뱉는다.
    /// </summary>
    private void OnBossEnteredPhase2()
    {
        healthBar?.MarkPhase2();
    }

    /// <summary>
    /// 보스가 죽었을 때. 바로 방을 끝내지 않고 사망 모션이 보일 시간을 준다.
    ///
    /// 수정(시간 정지 문제): <c>Invoke</c>를 코루틴으로 바꿨다.
    ///
    /// <c>Invoke</c>는 대기 한 번뿐일 때 가장 짧은 코드지만 <b>스케일 시간</b>으로 센다.
    /// 인벤토리(<c>I</c>)와 보스 열쇠 화면(<c>T</c>)이 열려 있는 동안 <c>Time.timeScale</c>이
    /// 0이라, 보스를 잡은 직후 1.6초 안에 둘 중 하나를 열면 <b>화면을 닫을 때까지 클리어 유물이
    /// 나오지 않는다.</b> 무엇을 받았는지 확인하려고 연 창이 보상을 막는 셈이다.
    ///
    /// <see cref="RunManager"/>가 결과 화면 전환에 <c>WaitForSecondsRealtime</c>을 쓰는 것과
    /// 같은 이유다. <b>연출 대기는 게임 시간이 아니라 실제 시간으로 세야 한다.</b>
    /// </summary>
    private void OnBossDied()
    {
        runManager?.AddKill();

        // 추가 생성 — 보스도 처치 시 재 게이지를 채운다. 잡몹과 규칙을 맞춘다.
        if (ashGauge == null) ashGauge = FindFirstObjectByType<AshGauge>();
        ashGauge?.AddKillCharge();

        Debug.Log("[보스 방] 보스 처치 — 잠시 뒤 보상 상자.", this);
        StartCoroutine(FinishAfterDeathMotion());
    }

    /// <summary>추가 생성 — 사망 모션이 보일 시간을 실제 시간으로 기다린다.</summary>
    private IEnumerator FinishAfterDeathMotion()
    {
        yield return new WaitForSecondsRealtime(clearedDelay);

        FinishEncounter();
    }

    /// <summary>사망 연출이 끝난 뒤 방 진행에 전투 종료를 알린다.</summary>
    private void FinishEncounter()
    {
        encounterStarted = false;
        Cleanup();
        RaiseEncounterCleared();
    }

    /// <summary>
    /// 남아 있는 보스를 정리한다.
    ///
    /// 이벤트 구독을 반드시 먼저 끊는다. Destroy는 프레임 끝에 처리되므로, 끊지 않은 채로
    /// 방을 다시 열면 죽은 보스의 Health가 아직 살아 있어 신호가 두 번 올 수 있다.
    /// </summary>
    private void Cleanup()
    {
        if (bossHealth != null)
        {
            bossHealth.Died -= OnBossDied;
            bossHealth = null;
        }

        // 추가 생성 — 체력바를 먼저 뗀다. activeBoss를 지운 뒤에 떼려고 하면
        // 이미 파괴된 보스의 이벤트를 해제하게 된다.
        if (activeBoss != null) activeBoss.EnteredPhase2 -= OnBossEnteredPhase2;
        if (healthBar != null) healthBar.Unbind();

        // 추가 생성(2026-09-20) — 시전 바도 같은 순서로 뗀다. 방이 끝나는 순간 시전 중이었다면
        // 가득 차다 만 바가 화면에 남으므로 즉시 감춘다.
        if (activeBoss != null)
        {
            activeBoss.CastStarted -= OnBossCastStarted;
            activeBoss.CastEnded -= OnBossCastEnded;
        }

        if (castBar != null) castBar.HideImmediate();

        // 추가 생성(2026-09-22) — 왕관 의식도 같은 순서로 뗀다. 도는 중이었다면 끊는다 — 떠 있는 유물을
        // 지우고 붙잡힌 플레이어를 놓아준다. 끊을 때는 Finished가 안 울리므로 보스를 풀어 줄 일도 없다
        // (보스는 바로 아래에서 지운다).
        if (activeBoss != null) activeBoss.CrownRitualStarted -= OnCrownRitualStarted;
        if (crownRitual != null)
        {
            crownRitual.Finished -= OnCrownRitualFinished;
            crownRitual.RelicsTorn -= OnRitualRelicsTorn;
            crownRitual.RelicBroken -= OnRitualRelicBroken;
            crownRitual.Abort();
        }

        if (activeBoss != null)
        {
            Destroy(activeBoss.gameObject);
            activeBoss = null;
        }

        encounterStarted = false;
    }
}
