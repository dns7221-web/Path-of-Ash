using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 추가 생성(2026-09-24) — 투사체(플레이어 화살, 사수 화살, 보스 잿불 파도) 풀.
///
/// <b>왜 풀링하나.</b> 투사체는 쏠 때마다 Instantiate, 사거리 끝에서 Destroy를 했다. 보스 파도는 한 번에 여러 발,
/// 사수는 방마다 여럿이 연사한다. 매번 새로 만들고 지우면 GC 할당과 순간 끊김이 쌓인다
/// (프로파일러 스파이크 보고서에서 할당 출처로 잡혔던 종류다). 한 번 만든 것을 꺼 두었다가 다시 쓴다.
///
/// <b>유니티 내장 <see cref="ObjectPool{T}"/>를 쓴다.</b> 잡몹 풀(EnemySpawner)과 같은 도구라 읽는 법이 같다.
/// 직접 Queue로 짜지 않은 이유: 이중 반납 검사(collectionCheck)와 최대 보관 수 제한이 이미 들어 있다.
///
/// <b>프리팹마다 풀이 따로다.</b> 화살과 파도는 모양·속도·이펙트가 다르므로 섞으면 안 된다. 프리팹을 열쇠로 풀을 찾는다.
/// 쓰는 쪽은 Instantiate 대신 <see cref="Spawn"/>만 부르면 되고, 돌려놓는 것은 투사체가 스스로 한다(<see cref="Projectile"/>).
/// </summary>
public static class ProjectilePool
{
    // 프리팹 → 그 프리팹의 풀.
    private static readonly Dictionary<Projectile, ObjectPool<Projectile>> Pools =
        new Dictionary<Projectile, ObjectPool<Projectile>>();

    // 풀에 보관할 최대 개수. 넘으면 반납할 때 지운다. 보스 파도 한 번(여러 발) + 사수 여럿을 넉넉히 덮는 값.
    private const int MaxKeptPerPrefab = 64;

    /// <summary>
    /// 플레이 시작마다 비운다. "도메인 리로드 끄기"(Enter Play Mode 옵션)를 쓰면 static이 이전 플레이의 값을 들고 남아,
    /// 이미 지워진 오브젝트를 꺼내려다 오류가 난다. 유니티 내장 RuntimeInitializeOnLoadMethod로 매번 초기화한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() => Pools.Clear();

    /// <summary>
    /// 투사체 하나를 꺼내 position에 놓고 켠다. 없으면 새로 만든다. 꺼낸 뒤 <see cref="Projectile.Launch"/>를 부르면 된다.
    /// </summary>
    public static Projectile Spawn(Projectile prefab, Vector3 position)
    {
        if (prefab == null) return null;

        if (!Pools.TryGetValue(prefab, out ObjectPool<Projectile> pool))
        {
            pool = CreatePool(prefab);
            Pools.Add(prefab, pool);
        }

        // 씬을 다시 불러오면(R 재시작) 풀에 있던 것이 씬과 함께 지워진다. 지워진 것은 버리고 다시 꺼낸다.
        // actionOnGet에서 켜지 않고 여기서 켜는 이유가 이것이다 — 지워진 오브젝트를 만지면 거기서 예외가 난다.
        Projectile projectile = pool.Get();
        while (projectile == null) projectile = pool.Get();

        projectile.transform.SetPositionAndRotation(position, Quaternion.identity);
        projectile.gameObject.SetActive(true);
        return projectile;
    }

    private static ObjectPool<Projectile> CreatePool(Projectile prefab)
    {
        ObjectPool<Projectile> pool = null;
        pool = new ObjectPool<Projectile>(
            createFunc: () =>
            {
                Projectile created = Object.Instantiate(prefab);
                created.gameObject.SetActive(false);
                // 돌아갈 풀을 기억시킨다. 투사체가 사거리 끝에서 스스로 반납한다.
                created.AssignPool(pool);
                return created;
            },
            actionOnGet: null,
            actionOnRelease: projectile => projectile.gameObject.SetActive(false),
            actionOnDestroy: projectile =>
            {
                if (projectile != null) Object.Destroy(projectile.gameObject);
            },
            collectionCheck: true,
            defaultCapacity: 8,
            maxSize: MaxKeptPerPrefab);
        return pool;
    }
}
