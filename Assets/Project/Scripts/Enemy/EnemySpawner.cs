using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 추가 생성 — 잿불 망령을 Unity 내장 <see cref="ObjectPool{T}"/>로 재사용한다.
/// 방의 모든 적이 죽으면 문을 열고 RunManager 처치 수를 갱신한다.
/// </summary>
[DisallowMultipleComponent]
public class EnemySpawner : MonoBehaviour
{
    [SerializeField] private EnemyWraith enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField, Min(1)] private int spawnCount = 3;
    [SerializeField, Min(1)] private int defaultPoolCapacity = 4;
    [SerializeField, Min(1)] private int maxPoolSize = 12;
    [SerializeField] private RoomDoorState roomDoor;
    [SerializeField] private RunManager runManager;

    private ObjectPool<EnemyWraith> pool;
    private readonly HashSet<EnemyWraith> activeEnemies = new HashSet<EnemyWraith>();
    private int nextSpawnPoint;

    private void Awake()
    {
        if (roomDoor == null) roomDoor = FindFirstObjectByType<RoomDoorState>();
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
        if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[적 스포너] 적 프리팹 또는 스폰 지점이 비어 있다.", this);
            return;
        }

        roomDoor?.SetState(RoomDoorState.DoorState.Closed);
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
        pool.Release(enemy);

        if (activeEnemies.Count == 0)
            roomDoor?.SetState(RoomDoorState.DoorState.Open);
    }
}
