using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

/// <summary>
/// 추가 생성 — 방의 적을 Unity 내장 <see cref="ObjectPool{T}"/>로 재사용한다.
/// 방의 모든 적이 죽으면 전투 종료 이벤트를 보내고 RunManager 처치 수를 갱신한다.
/// 문과 보상은 <see cref="RoomController"/>가 순서대로 처리한다.
///
/// 수정(보스 방 추가): 이 클래스가 EnemyWraith 전용이라 보스를 꽂을 수 없었다.
/// 방 진행이 기대하는 "전투 시작 / 전투 종료" 규약을 <see cref="RoomEncounter"/>로 올리고
/// 이 클래스는 그 구현 중 하나가 됐다. 보스 방은 <see cref="BossEncounter"/>가 맡는다.
///
/// <b>수정(적 종류 추가): 한 방에 여러 종류를 섞을 수 있게 했다.</b>
///
/// 예전에는 프리팹 하나와 마릿수 하나뿐이라 방마다 한 종류만 나왔다. 적을 늘려도
/// 스포너를 복제해 방에 두 개를 붙이는 수밖에 없었는데, 그러면 전투 종료 판정이
/// 스포너마다 따로 돌아서 <b>한쪽 적이 다 죽으면 문이 열리는</b> 상태가 된다.
/// 종류별 풀을 이 안에 두면 종료 판정은 하나로 남는다.
///
/// 옛 설정은 그대로 살아난다 — 아래 <see cref="enemyPrefab"/> 참고.
/// </summary>
[DisallowMultipleComponent]
public class EnemySpawner : RoomEncounter
{
    /// <summary>
    /// 이 방에 나올 수 있는 적 한 종류와 그 비율.
    ///
    /// struct가 아니라 class인 이유: 필드에 초기값을 줄 수 있다. 값을 채우는 것은
    /// 유니티의 직렬화라서 컴파일러는 "아무도 안 채운다"고 보고 CS0649로 경고하는데,
    /// 초기값이 있으면 그 경고가 사라진다. 경고를 끄는 것보다 값을 주는 편이 낫다.
    ///
    /// <b>다만 초기값이 인스펙터까지 살아오지는 않는다.</b> 여기 적힌 <c>= 1</c>은 배열 크기를
    /// 늘려 만든 항목에서는 <b>0으로 보인다.</b> 유니티는 객체를 만든 뒤 직렬화된 값으로 덮는데,
    /// 새 항목의 직렬화된 값이 전부 0이라 초기값이 그 자리에 안 남는다. C# 초기값이 직렬화
    /// 앞에서 지는 것은 프리팹 필드에서 겪은 것과 같은 일이다. 그래서 <b>줄을 추가하면 값을
    /// 직접 적어야 하고</b>, 안 적으면 그 줄은 없는 것과 같다. <see cref="BuildSpawnPlan"/>이
    /// 그 상태를 경고로 남긴다.
    /// </summary>
    [Serializable]
    private class EnemyEntry
    {
        [Tooltip("스폰할 적 프리팹.")]
        public EnemyBase prefab = null;

        /// <summary>
        /// 수정(구성 무작위화) — "몇 마리"에서 "얼마나 자주"로 뜻이 바뀌었다.
        ///
        /// 옛 이름(count)을 <see cref="FormerlySerializedAsAttribute"/>로 이어받는 이유:
        /// 이름을 바꾸면 유니티는 다른 필드로 보고 씬에 적어둔 값을 버린다. 그러면 방마다
        /// 다시 채워야 하고, 한 방만 빠뜨려도 "그 방만 적이 안 나오는" 형태로 늦게 드러난다.
        /// 게다가 예전에 적어둔 마릿수(망령 3, 사수 1)는 그대로 비율로 읽어도 말이 된다.
        /// </summary>
        [Tooltip("뽑힐 가중치. 클수록 자주 나온다. 3과 1이면 3번에 한 번꼴로 뒤엣것이 나온다. " +
                 "0이면 이 방에 안 나온다.")]
        [FormerlySerializedAs("count")]
        [Min(0)] public int weight = 1;

        [Tooltip("한 방에 나올 수 있는 이 종류의 최대 마릿수. 0이면 제한 없음. " +
                 "원거리 적이 방을 다 채우면 쫓아다니기만 하는 방이 되므로 그런 적에 걸어둔다.")]
        [Min(0)] public int maxCount = 0;

        /// <summary>이 종류를 더 뽑을 수 있는가. 최대치가 0이면 제한이 없다.</summary>
        public bool CanTakeMore(int taken) => maxCount <= 0 || taken < maxCount;
    }

    [Header("적 구성")]
    [Tooltip("이 방에 나올 수 있는 적들. 마릿수는 아래 스폰 지점 개수로 정해지고, " +
             "그 자리를 여기 적힌 가중치대로 뽑아 채운다. 비워두면 '옛 설정'을 대신 쓴다.")]
    [SerializeField] private EnemyEntry[] enemies;

    // ── 옛 설정 ───────────────────────────────────────────────────────────
    //
    // 지우지 않고 남긴 이유: 던전 방마다 이 값이 이미 채워져 있다. 지우면 씬에 있는 방
    // 전부가 적 없는 방이 되고, 사람이 하나씩 다시 꽂아야 한다. 그 작업은 한 방만
    // 빠뜨려도 "그 방만 문이 바로 열리는" 형태로 나중에 드러난다.
    //
    // 타입을 EnemyWraith에서 EnemyBase로 넓혔지만 필드 이름이 같아서 저장된 참조는
    // 그대로 살아난다 — 유니티는 이름으로 직렬화 값을 찾는다.

    [Header("옛 설정 (위가 비어 있을 때만 쓰인다)")]
    [Tooltip("적 프리팹 하나만 쓰던 시절의 설정.")]
    [SerializeField] private EnemyBase enemyPrefab;

    [Tooltip("적 프리팹 하나만 쓰던 시절의 마릿수.")]
    [SerializeField, Min(1)] private int spawnCount = 3;

    [Header("배치")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("풀")]
    [SerializeField, Min(1)] private int defaultPoolCapacity = 4;
    [SerializeField, Min(1)] private int maxPoolSize = 12;

    [Header("참조")]
    [SerializeField] private RunManager runManager;

    // 추가 생성 — 처치 시 채울 재 게이지. 플레이어가 프리팹 인스턴스라 미리 못 걸어둔다.
    private AshGauge ashGauge;

    // 종류별 풀과 그 종류의 설정.
    //
    // 수정(구성 무작위화) — 예전에는 여기에 마릿수까지 들어 있었다. 마릿수는 이제 전투를
    // 시작할 때마다 다시 뽑으므로 <b>여기 담아두면 안 된다.</b> 풀은 만드는 비용이 있고
    // 살아 있는 적을 기억하고 있어서 방을 다시 들어가도 그대로 써야 하지만, 구성은 매번
    // 달라야 한다. 그래서 "오래 사는 것(풀)"과 "매번 바뀌는 것(마릿수)"을 갈랐다.
    private readonly List<(ObjectPool<EnemyBase> pool, EnemyEntry entry)> spawnPlan =
        new List<(ObjectPool<EnemyBase>, EnemyEntry)>();

    // 이번 전투에서 종류별로 뽑힌 마릿수. spawnPlan과 순서가 같다.
    // 매번 새로 만들지 않고 재사용한다 — 방을 드나들 때마다 쓰레기가 쌓일 이유가 없다.
    private readonly List<int> rolledCounts = new List<int>();

    // 스폰 순서. 종류를 섞어 담는다.
    private readonly List<int> spawnOrder = new List<int>();

    // 옛 설정으로 도는 방인가. 그 방은 마릿수가 고정이라 뽑지 않는다.
    private bool usesLegacySetup;

    // 살아 있는 적이 어느 풀에서 나왔는지. 돌려보낼 때 이 표를 본다.
    //
    // 적에게 자기 풀을 들려주지 않는 이유: 그러면 적이 "누가 나를 관리하는가"를 알아야 하고,
    // 풀 없이 씬에 직접 놓아둔 적은 그 값이 비어서 다른 코드가 된다.
    private readonly Dictionary<EnemyBase, ObjectPool<EnemyBase>> poolOfEnemy =
        new Dictionary<EnemyBase, ObjectPool<EnemyBase>>();

    private readonly HashSet<EnemyBase> activeEnemies = new HashSet<EnemyBase>();
    private int nextSpawnPoint;
    private bool encounterStarted;

    // 수정(보스 방 추가) — 전투 종료 이벤트는 RoomEncounter로 올라갔다.
    // 문을 직접 열지 않고 방 진행 담당자에게 알리기만 한다는 원칙은 그대로다.

    private void Awake()
    {
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();

        BuildSpawnPlan();
    }

    /// <summary>
    /// 인스펙터 설정에서 종류별 풀을 만든다.
    ///
    /// 새 목록이 비어 있으면 옛 설정(프리팹 하나 + 마릿수)을 한 종류짜리 목록으로 본다.
    /// 그래서 이미 만들어둔 방은 아무것도 안 고쳐도 예전과 똑같이 동작한다.
    /// </summary>
    private void BuildSpawnPlan()
    {
        spawnPlan.Clear();

        if (enemies != null && enemies.Length > 0)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyEntry entry = enemies[i];

                // 인스펙터에서 항목만 늘리고 안 채운 경우 entry 자체가 비어 있을 수 있다.
                //
                // 수정 — 조용히 넘기지 않고 무엇을 왜 뺐는지 남긴다. 예전에는 그냥 continue였고,
                // 그러면 인스펙터에 줄이 보이는데 게임에는 그 적이 안 나오는 상태가 된다.
                // 화면에는 나머지 적이 멀쩡히 돌아다니므로 "안 나온다"는 것조차 늦게 알아챈다.
                if (entry == null || entry.prefab == null)
                {
                    Debug.LogWarning($"[적 스포너] {name}의 적 구성 {i}번에 프리팹이 없다. 그 줄은 건너뛴다.", this);
                    continue;
                }

                if (entry.weight <= 0)
                {
                    Debug.LogWarning($"[적 스포너] {name}의 적 구성 {i}번({entry.prefab.name})이 " +
                                     "가중치 0이라 뽑히지 않는다. 그 줄은 건너뛴다.", this);
                    continue;
                }

                spawnPlan.Add((CreatePool(entry.prefab), entry));
            }

            // 줄은 있는데 하나도 못 쓴 경우. 이때 옛 설정으로 넘어가지 않는 이유:
            // 사람이 새 구성을 적어둔 방인데 도구가 옛 구성을 대신 꺼내면, 화면에는 적이 나오므로
            // 잘못 적은 줄을 영영 안 고치게 된다. 비어 있는 편이 낫다 — BeginEncounter가 오류를 낸다.
            if (spawnPlan.Count == 0)
            {
                Debug.LogError($"[적 스포너] {name}의 적 구성이 {enemies.Length}줄인데 쓸 수 있는 줄이 " +
                               "하나도 없다. 프리팹과 마릿수를 확인해라.", this);
            }
        }
        else if (enemyPrefab != null)
        {
            // 옛 설정은 종류가 하나뿐이라 뽑을 것이 없다. 마릿수도 spawnCount로 고정이다 —
            // 튜토리얼처럼 <b>일부러</b> 지점 수보다 적게 잡아둔 방이 있어서, 여기까지
            // 무작위로 바꾸면 그런 방의 의도가 조용히 뒤집힌다.
            usesLegacySetup = true;
            spawnPlan.Add((CreatePool(enemyPrefab), new EnemyEntry { prefab = enemyPrefab }));
        }
    }

    /// <summary>
    /// 추가 생성 — 이번 전투에 나올 적을 뽑아 스폰 순서를 만든다.
    ///
    /// <b>마릿수는 스폰 지점 개수가 정한다.</b> 한 지점에 한 마리라 겹쳐 태어나는 일이 없고,
    /// "몇 마리 나오는 방인가"를 씬에서 눈으로 보고 정할 수 있다. 마릿수를 따로 적게 하면
    /// 지점 넷에 여섯 마리 같은 설정이 가능해지는데, 그러면 두 마리가 같은 자리에서 시작해
    /// 서로를 밀어낸다.
    /// </summary>
    private void BuildSpawnOrder()
    {
        spawnOrder.Clear();

        if (usesLegacySetup)
        {
            for (int i = 0; i < spawnCount; i++) spawnOrder.Add(0);
            return;
        }

        RollComposition(spawnPoints.Length);

        for (int i = 0; i < rolledCounts.Count; i++)
        {
            for (int n = 0; n < rolledCounts[i]; n++) spawnOrder.Add(i);
        }

        Shuffle(spawnOrder);
    }

    /// <summary>
    /// 추가 생성 — 자리를 하나씩 가중치로 뽑아 종류별 마릿수를 정한다.
    ///
    /// 비율로 한 번에 나누지 않고 <b>한 자리씩</b> 뽑는 이유: 최대치에 찬 종류를 그때그때
    /// 빼야 하기 때문이다. 사수가 최대 1인 방에서 비율로 나누면 2마리가 배정될 수 있고,
    /// 그걸 나중에 깎으면 남는 자리를 누가 가져갈지 또 정해야 한다.
    /// </summary>
    private void RollComposition(int total)
    {
        rolledCounts.Clear();
        for (int i = 0; i < spawnPlan.Count; i++) rolledCounts.Add(0);

        for (int filled = 0; filled < total; filled++)
        {
            // 아직 더 뽑을 수 있는 종류의 가중치 합. 최대치에 찬 종류가 빠지므로 매번 다시 센다.
            int weightSum = 0;
            for (int i = 0; i < spawnPlan.Count; i++)
            {
                EnemyEntry entry = spawnPlan[i].entry;
                if (entry.CanTakeMore(rolledCounts[i])) weightSum += entry.weight;
            }

            if (weightSum <= 0)
            {
                Debug.LogWarning($"[적 스포너] {name}의 자리 {total}개 중 {filled}개만 채웠다. " +
                                 "종류별 최대 마릿수의 합이 스폰 지점 개수보다 적다.", this);
                return;
            }

            // UnityEngine.Random으로 적는 이유: 이 파일은 using System을 같이 쓰고 있어서
            // 그냥 Random이라고 쓰면 System.Random과 겹쳐 컴파일이 안 된다.
            int roll = UnityEngine.Random.Range(0, weightSum);

            for (int i = 0; i < spawnPlan.Count; i++)
            {
                EnemyEntry entry = spawnPlan[i].entry;
                if (!entry.CanTakeMore(rolledCounts[i])) continue;

                roll -= entry.weight;
                if (roll >= 0) continue;

                rolledCounts[i]++;
                break;
            }
        }
    }

    /// <summary>
    /// 추가 생성 — 순서를 섞는다(피셔–예이츠).
    ///
    /// 안 섞으면 종류별로 뭉쳐서 나온다. 스폰 지점을 순서대로 쓰는 구조라 그렇게 두면
    /// <b>사수가 항상 마지막 지점에 선다.</b> 방을 몇 개만 돌아도 플레이어가 그 자리를 외운다.
    /// 목록을 섞는 내장 기능은 유니티에 없어서 직접 돌린다 — 난수만 내장을 쓴다.
    /// </summary>
    private static void Shuffle(List<int> order)
    {
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
    }

    /// <summary>프리팹 한 종류의 풀을 만든다.</summary>
    private ObjectPool<EnemyBase> CreatePool(EnemyBase prefab)
    {
        // 풀 변수를 먼저 선언하고 람다 안에서 쓰는 이유: 생성 함수가 "어느 풀에서 나왔는지"를
        // 기록해야 하는데, 그 풀은 아직 만들어지는 중이다. 람다는 나중에 실행되므로
        // 그때는 값이 채워져 있다.
        ObjectPool<EnemyBase> pool = null;

        pool = new ObjectPool<EnemyBase>(
            () => CreateEnemy(prefab, pool),
            OnTakeFromPool,
            OnReturnedToPool,
            OnDestroyPooledEnemy,
            collectionCheck: true,
            defaultCapacity: defaultPoolCapacity,
            maxSize: maxPoolSize);

        return pool;
    }

    private void Start()
    {
        BeginEncounter();
    }

    /// <summary>
    /// 추가 생성 — 이 방의 적을 배치하고 전투를 시작한다.
    /// 같은 전투가 진행 중일 때 다시 호출해도 적이 중복 생성되지 않는다.
    /// </summary>
    public override void BeginEncounter()
    {
        if (encounterStarted || activeEnemies.Count > 0) return;

        if (spawnPlan.Count == 0 || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[적 스포너] 적 구성 또는 스폰 지점이 비어 있다.", this);
            return;
        }

        encounterStarted = true;
        nextSpawnPoint = 0;

        // 수정(구성 무작위화) — 전투를 시작할 때마다 다시 뽑는다.
        // 방 오브젝트는 재사용되므로, 같은 방을 다시 들어가도 구성이 달라진다.
        BuildSpawnOrder();

        foreach (int index in spawnOrder) spawnPlan[index].pool.Get();
    }

    private void OnDestroy()
    {
        foreach (var (pool, _) in spawnPlan) pool?.Clear();
        spawnPlan.Clear();
    }

    private EnemyBase CreateEnemy(EnemyBase prefab, ObjectPool<EnemyBase> pool)
    {
        EnemyBase enemy = Instantiate(prefab, transform);
        enemy.DespawnRequested += ReleaseEnemy;
        enemy.gameObject.SetActive(false);

        poolOfEnemy[enemy] = pool;
        return enemy;
    }

    private void OnTakeFromPool(EnemyBase enemy)
    {
        // 스폰 지점을 종류와 상관없이 돌려 쓴다. 종류별로 자리를 나누면 방마다
        // "이 자리에는 이 적"이 고정되어 구성이 바뀌어도 배치가 안 바뀐다.
        Transform point = spawnPoints[nextSpawnPoint % spawnPoints.Length];
        nextSpawnPoint++;

        enemy.transform.SetPositionAndRotation(point.position, Quaternion.identity);
        activeEnemies.Add(enemy);
        enemy.gameObject.SetActive(true);
    }

    private void OnReturnedToPool(EnemyBase enemy)
    {
        enemy.gameObject.SetActive(false);
    }

    private void OnDestroyPooledEnemy(EnemyBase enemy)
    {
        if (enemy == null) return;

        enemy.DespawnRequested -= ReleaseEnemy;
        poolOfEnemy.Remove(enemy);
        Destroy(enemy.gameObject);
    }

    private void ReleaseEnemy(EnemyBase enemy)
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

        if (poolOfEnemy.TryGetValue(enemy, out ObjectPool<EnemyBase> pool))
        {
            pool.Release(enemy);
        }
        else
        {
            // 풀을 모르는 적은 돌려보낼 곳이 없다. 그냥 끄면 다음 전투에서 되살아나지 않고
            // 씬에 남으므로, 왜 이렇게 됐는지 알 수 있게 경고를 남긴다.
            Debug.LogWarning($"[적 스포너] {enemy.name}의 풀을 못 찾아 그냥 껐다.", enemy);
            enemy.gameObject.SetActive(false);
        }

        if (activeEnemies.Count == 0)
        {
            encounterStarted = false;
            RaiseEncounterCleared();
        }
    }
}
