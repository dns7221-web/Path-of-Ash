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

    private EnemyBoss activeBoss;
    private Health bossHealth;
    private bool encounterStarted;

    // 추가 생성 — 처치 시 채울 재 게이지. 플레이어가 프리팹 인스턴스라 미리 못 걸어둔다.
    // EnemySpawner가 쓰는 방식과 같다.
    private AshGauge ashGauge;

    private void Awake()
    {
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();
    }

    private void OnDisable()
    {
        // 방이 꺼질 때 예약된 전투 종료 알림을 취소한다.
        // 이게 없으면 보스를 잡자마자 방을 나갔을 때 다음 방에서 상자가 튀어나온다.
        CancelInvoke();
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
    }

    /// <summary>
    /// 보스가 죽었을 때. 바로 방을 끝내지 않고 사망 모션이 보일 시간을 준다.
    ///
    /// 코루틴 대신 <c>Invoke</c>를 쓴 이유: 대기 한 번뿐이라 코루틴을 만들 이유가 없고,
    /// <c>CancelInvoke</c>로 OnDisable에서 한 줄로 정리된다.
    /// </summary>
    private void OnBossDied()
    {
        runManager?.AddKill();

        // 추가 생성 — 보스도 처치 시 재 게이지를 채운다. 잡몹과 규칙을 맞춘다.
        if (ashGauge == null) ashGauge = FindFirstObjectByType<AshGauge>();
        ashGauge?.AddKillCharge();

        Debug.Log("[보스 방] 보스 처치 — 잠시 뒤 보상 상자.", this);
        Invoke(nameof(FinishEncounter), clearedDelay);
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

        if (activeBoss != null)
        {
            Destroy(activeBoss.gameObject);
            activeBoss = null;
        }

        encounterStarted = false;
    }
}
