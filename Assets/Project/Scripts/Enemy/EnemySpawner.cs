using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 추가 생성 — 잿불 망령을 Unity 내장 <see cref="ObjectPool{T}"/>로 재사용한다.
/// 방의 모든 적이 죽으면 전투 종료 이벤트를 보내고 RunManager 처치 수를 갱신한다.
/// 문과 보상은 <see cref="RoomController"/>가 순서대로 처리한다.
/// </summary>
[DisallowMultipleComponent]
public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private EnemyWraith enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField, Min(1)] private int spawnCount = 3;
    [SerializeField, Min(1)] private int defaultPoolCapacity = 4;
    [SerializeField, Min(1)] private int maxPoolSize = 12;
    [SerializeField] private RunManager runManager;

    // 추가 생성 — 처치 시 채울 재 게이지. 플레이어가 프리팹 인스턴스라 미리 못 걸어둔다.
    private AshGauge ashGauge;

    private ObjectPool<EnemyWraith> pool;
    private readonly HashSet<EnemyWraith> activeEnemies = new HashSet<EnemyWraith>();
    private int nextSpawnPoint;
    private bool encounterStarted;

    // 추가 생성 — 문을 직접 열지 않고 방 진행 담당자에게 전투 종료만 알린다.
    public event Action EncounterCleared;

    private void Awake()
    {
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();

        pool = new ObjectPool<EnemyWraith>(
            CreateEnemy,
            OnTakeFromPool,
            OnReturnedToPool,
            OnDestroyPooledEnemy,
            collectionCheck: true,
            defaultCapacity: defaultPoolCapacity,
            maxSize: maxPoolSize);
    }

    private void Start()
    {
        BeginEncounter();
    }

    /// <summary>
    /// 추가 생성 — 이 방의 적을 배치하고 전투를 시작한다.
    /// 같은 전투가 진행 중일 때 다시 호출해도 적이 중복 생성되지 않는다.
    /// </summary>
    public void BeginEncounter()
    {
        if (encounterStarted || activeEnemies.Count > 0) return;

        if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[적 스포너] 적 프리팹 또는 스폰 지점이 비어 있다.", this);
            return;
        }

        encounterStarted = true;
        nextSpawnPoint = 0;
        for (int i = 0; i < spawnCount; i++) pool.Get();
    }

    private void OnDestroy()
    {
        pool?.Clear();
    }

    private EnemyWraith CreateEnemy()
    {
        EnemyWraith enemy = Instantiate(enemyPrefab, transform);
        enemy.DespawnRequested += ReleaseEnemy;
        enemy.gameObject.SetActive(false);
        return enemy;
    }

    private void OnTakeFromPool(EnemyWraith enemy)
    {
        Transform point = spawnPoints[nextSpawnPoint % spawnPoints.Length];
        nextSpawnPoint++;
        enemy.transform.SetPositionAndRotation(point.position, Quaternion.identity);
        activeEnemies.Add(enemy);
        enemy.gameObject.SetActive(true);
    }

    private void OnReturnedToPool(EnemyWraith enemy)
    {
        enemy.gameObject.SetActive(false);
    }

    private void OnDestroyPooledEnemy(EnemyWraith enemy)
    {
        if (enemy == null) return;
        enemy.DespawnRequested -= ReleaseEnemy;
        Destroy(enemy.gameObject);
    }

    private void ReleaseEnemy(EnemyWraith enemy)
    {
        if (!activeEnemies.Remove(enemy)) return;

        runManager?.AddKill();

        // 추가 생성 — 처치할 때마다 재 게이지가 찬다. 열 마리면 필살기 한 번.
        //
        // 여기서 부르는 이유: 적이 죽는 순간을 이미 이 자리에서 잡고 있다. 적 쪽에
        // 게이지 참조를 들려주면 적 프리팹마다 그걸 연결해야 하고, 하나 빠뜨리면
        // 그 적만 게이지를 안 채우는 버그가 된다.
        if (ashGauge == null) ashGauge = FindFirstObjectByType<AshGauge>();
        if (ashGauge != null) ashGauge.AddKillCharge();
        pool.Release(enemy);

        if (activeEnemies.Count == 0)
        {
            encounterStarted = false;
            EncounterCleared?.Invoke();
        }
    }
}
