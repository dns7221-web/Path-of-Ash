using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-22, 사용자 요청 "방을 넘어갈 때마다 소품이 완전 새롭게") — 던전 방에 들어갈 때마다
/// 소품을 새로 골라 새 자리에 흩뿌린다. 방의 Props 오브젝트에 붙는다.
///
/// <b>왜 필요한가.</b> 던전 방 둘(Room_01·02)은 물론 튜토리얼·보스 방까지 <b>같은 소품 12개가 같은 자리</b>에
/// 복사돼 있었다. 방을 몇 개 지나도 같은 방에 다시 들어온 것처럼 보여서, 로그라이크의 "새 방에 들어섰다"가 안 났다.
///
/// <b>하는 일</b> — 방이 켜질 때마다(<see cref="OnEnable"/>):
/// <list type="number">
/// <item>종류 묶음(<see cref="PropGroup"/>)마다 몇 개를 쓸지 최솟값~최댓값에서 뽑고, 그 수만큼 무작위로 골라 켠다.
/// 나머지는 끈다. 그래서 자리뿐 아니라 무엇이 몇 개 나오는지도 매번 달라진다.</item>
/// <item>고른 소품을 큰 것부터 바닥 안의 무작위 자리에 놓는다. 적이 나오는 곳·입장 지점·상자·출구 주변과
/// 벽 가장자리는 비우고, 소품끼리는 사람이 지나갈 틈을 둔다. 자리를 못 찾은 소품은 이번엔 안 나온다.</item>
/// <item>좌우 뒤집기를 섞는다. 같은 그림이라도 방향이 바뀌면 다른 물건처럼 읽힌다.</item>
/// </list>
///
/// <b>왜 방 진행 코드를 안 고치나.</b> 방은 들어갈 때 켜지고(SetActive) 나갈 때 꺼진다. 켜지는 순간 Props의
/// OnEnable이 불리므로 "들어갈 때마다"가 이미 공짜로 온다. 그래서 흩뿌리지 않을 방(튜토리얼·보스)은 이 컴포넌트를
/// 안 붙이기만 하면 된다 — 보스 방은 왕관 의식의 유물 자리와 보스 동선이 걸려 있어 지금 배치를 지킨다.
///
/// <b>소품을 실행 중에 만들지 않고 씬에 미리 둔 것을 켜고 끄는 이유.</b> 방에 들어가는 한 프레임은 이미 적 생성·방
/// 초기화로 바쁘다(입장 계측 로그가 있는 이유). 소품을 그때 Instantiate하면 그 프레임이 더 무거워진다.
/// 필요한 수만큼 구성 도구가 미리 깔아 두고, 여기서는 켜고 끄고 옮기기만 한다.
///
/// 무작위는 <see cref="UnityEngine.Random"/>을 쓴다. 판마다 씨앗이 달라서 같은 판 안에서도, 판끼리도 겹치지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class RoomPropScatter : MonoBehaviour
{
    /// <summary>같은 종류의 소품 묶음. 이 중에서 매번 min~max개를 골라 켠다.</summary>
    [Serializable]
    private class PropGroup
    {
        // 초기값을 주는 이유: 인스펙터(직렬화)로만 채워지는 필드라, 비워 두면 컴파일러가 "값을 안 넣는다"(CS0649)고 경고한다.
        [Tooltip("알아보기 쉬운 이름(예: 부서진 기둥).")]
        public string label = "";

        [Tooltip("이 묶음에 속한 소품들. 구성 도구가 Props 아래 자식을 이름으로 모아 채운다.")]
        public GameObject[] items = Array.Empty<GameObject>();

        [Tooltip("방에 들어갈 때마다 적어도 이만큼은 놓는다. 기둥처럼 싸움에 필요한 것은 1 이상으로 둔다.")]
        [Min(0)] public int min = 1;

        [Tooltip("방에 들어갈 때마다 많아야 이만큼 놓는다. 묶음의 소품 수보다 크면 소품 수까지만 쓴다.")]
        [Min(0)] public int max = 3;
    }

    [Header("소품 (구성 도구가 채운다)")]
    [Tooltip("종류별 묶음. 최솟값·최댓값은 인스펙터에서 조정한다.")]
    [SerializeField] private PropGroup[] groups = Array.Empty<PropGroup>();

    [Header("바닥")]
    [Tooltip("걸을 수 있는 바닥(벽 안쪽 면으로 둘러싸인 영역, 월드 좌표). 구성 도구가 벽에서 구해 넣는다.")]
    [SerializeField] private Rect floor = new Rect(-24f, -13.5f, 48f, 23f);

    [Tooltip("옆 벽에서 띄우는 거리(유닛). 소품 크기에 더해진다.")]
    [SerializeField, Min(0f)] private float sideMargin = 1.5f;

    [Tooltip("위 벽에서 띄우는 거리(유닛). 소품 그림은 발밑(피벗)에서 위로 솟아서, 위 벽에 붙이면 그림이 벽 그림을 덮는다. 그래서 아래보다 크게 둔다.")]
    [SerializeField, Min(0f)] private float topMargin = 3f;

    [Tooltip("아래 벽에서 띄우는 거리(유닛).")]
    [SerializeField, Min(0f)] private float bottomMargin = 1.5f;

    [Header("비울 자리")]
    [Tooltip("소품을 두면 안 되는 자리 — 적이 나오는 곳, 플레이어 입장 지점, 보상 상자, 출구. 구성 도구가 채운다.")]
    [SerializeField] private Transform[] keepClear = Array.Empty<Transform>();

    [Tooltip("비울 자리 둘레의 반지름(유닛). 적이 나오자마자 소품에 끼면 밀려나며 엉뚱한 쪽으로 튄다(소품 배치 도구 주석 참고).")]
    [SerializeField, Min(0f)] private float keepClearRadius = 4.5f;

    [Header("간격")]
    [Tooltip("소품과 소품 사이에 남길 틈(유닛). 플레이어 몸통 폭보다 넉넉해야 사이로 지나갈 수 있다.")]
    [SerializeField, Min(0f)] private float minGap = 2.5f;

    [Tooltip("소품 하나의 자리를 찾는 시도 횟수. 다 실패하면 그 소품은 이번엔 안 나온다.")]
    [SerializeField, Min(1)] private int triesPerProp = 30;

    [Tooltip("좌우 뒤집기를 섞는다.")]
    [SerializeField] private bool randomFlip = true;

    // 놓인 소품의 자리와 크기. 다음 소품이 겹치지 않는지 볼 때 쓴다. 매번 새로 만들지 않게 들고 있는다.
    private readonly List<(Vector2 position, float radius)> placed = new List<(Vector2, float)>();

    // 이번에 고른 소품과 그 크기. 크기는 고를 때 한 번만 재서, 정렬하는 동안 부품을 몇 번씩 다시 찾지 않게 한다.
    // 꼭 놓을 것(묶음의 최솟값 몫)과 더 놓을 것을 나눠 담는다 — 아래 Scatter 2번 주석 참고.
    private readonly List<(GameObject item, float radius)> required = new List<(GameObject, float)>();
    private readonly List<(GameObject item, float radius)> optional = new List<(GameObject, float)>();

    private void OnEnable()
    {
        Scatter();
    }

    /// <summary>
    /// 소품을 새로 고르고 흩뿌린다. 방이 켜질 때 저절로 불린다.
    /// </summary>
    public void Scatter()
    {
        required.Clear();
        optional.Clear();
        placed.Clear();

        // 1. 묶음마다 몇 개를 쓸지 정하고 고른다. 안 고른 것은 끈다.
        foreach (PropGroup group in groups)
        {
            if (group == null || group.items == null || group.items.Length == 0) continue;

            Shuffle(group.items);

            int upper = Mathf.Min(group.max, group.items.Length);
            int lower = Mathf.Min(group.min, upper);
            int count = UnityEngine.Random.Range(lower, upper + 1);

            for (int i = 0; i < group.items.Length; i++)
            {
                GameObject item = group.items[i];
                if (item == null) continue;

                if (i < lower) required.Add((item, FootprintRadius(item)));
                else if (i < count) optional.Add((item, FootprintRadius(item)));
                else item.SetActive(false);
            }
        }

        // 2. 꼭 놓을 것 먼저, 그 안에서는 큰 것부터 놓는다.
        //
        // 크기 순서만 쓰면 잔해·깨진 항아리(기둥보다 넓다)가 먼저 자리를 차지해서, 방이 붐비는 판에는 기둥이 밀려난다.
        // 1만 번 흉내 내 보니 기둥이 둘 미만인 방이 1% 나왔다. 기둥은 엄폐물이라 최솟값을 둔 것이므로, 최솟값 몫을
        // 먼저 놓아 그 약속부터 지킨다. 더 놓을 것은 남은 자리에 큰 것부터 넣는다.
        PlaceAll(required);
        PlaceAll(optional);
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 목록을 큰 것부터 놓는다. 작은 항아리가 먼저 자리를 차지하면 큰 것이 들어갈 곳이 사라진다.
    /// </summary>
    private void PlaceAll(List<(GameObject item, float radius)> items)
    {
        items.Sort((a, b) => b.radius.CompareTo(a.radius));

        foreach ((GameObject item, float radius) in items)
        {
            if (TryFindSpot(radius, out Vector2 spot))
            {
                Vector3 position = item.transform.position;
                item.transform.position = new Vector3(spot.x, spot.y, position.z);
                placed.Add((spot, radius));

                if (randomFlip) Flip(item, UnityEngine.Random.value < 0.5f);
                item.SetActive(true);
            }
            else
            {
                // 방이 이미 꽉 찼다. 억지로 겹쳐 놓느니 이번엔 빼는 편이 낫다.
                item.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 바닥 안에서 비울 자리와 다른 소품을 피한 무작위 자리를 찾는다.
    /// </summary>
    private bool TryFindSpot(float radius, out Vector2 spot)
    {
        float xMin = floor.xMin + sideMargin + radius;
        float xMax = floor.xMax - sideMargin - radius;
        float yMin = floor.yMin + bottomMargin + radius;
        float yMax = floor.yMax - topMargin - radius;

        spot = Vector2.zero;
        if (xMin > xMax || yMin > yMax) return false;

        for (int attempt = 0; attempt < triesPerProp; attempt++)
        {
            var candidate = new Vector2(UnityEngine.Random.Range(xMin, xMax), UnityEngine.Random.Range(yMin, yMax));
            if (!IsClear(candidate, radius)) continue;

            spot = candidate;
            return true;
        }

        return false;
    }

    /// <summary>이 자리가 비울 자리와 이미 놓인 소품에서 충분히 떨어져 있는가.</summary>
    private bool IsClear(Vector2 candidate, float radius)
    {
        foreach (Transform point in keepClear)
        {
            if (point == null) continue;

            float need = keepClearRadius + radius;
            if (((Vector2)point.position - candidate).sqrMagnitude < need * need) return false;
        }

        foreach ((Vector2 position, float other) in placed)
        {
            float need = radius + other + minGap;
            if ((position - candidate).sqrMagnitude < need * need) return false;
        }

        return true;
    }

    /// <summary>
    /// 소품이 바닥에서 차지하는 반지름. 충돌체가 있으면 그 크기로, 없으면 그림 폭으로 어림한다.
    ///
    /// 그림이 아니라 충돌체를 먼저 보는 이유: 소품 그림은 256px 칸 전체라 여백이 크고 위로 솟은 부분까지 들어 있다.
    /// 그걸로 재면 기둥 둘이 한참 떨어져 있어도 겹친다고 판단한다. 충돌체가 실제로 바닥을 막는 크기다.
    /// 긴 잔해도 원으로 어림한다(긴 변의 절반). 조금 넉넉하게 떨어뜨릴 뿐 겹치지는 않는다.
    /// </summary>
    private static float FootprintRadius(GameObject item)
    {
        var box = item.GetComponentInChildren<BoxCollider2D>(true);
        if (box != null)
        {
            Vector3 scale = box.transform.lossyScale;
            return 0.5f * Mathf.Max(box.size.x * Mathf.Abs(scale.x), box.size.y * Mathf.Abs(scale.y));
        }

        var circle = item.GetComponentInChildren<CircleCollider2D>(true);
        if (circle != null) return circle.radius * Mathf.Abs(circle.transform.lossyScale.x);

        var sprite = item.GetComponentInChildren<SpriteRenderer>(true);
        if (sprite != null && sprite.sprite != null)
            return 0.35f * sprite.sprite.bounds.size.x * Mathf.Abs(sprite.transform.lossyScale.x);

        return 1f;
    }

    /// <summary>
    /// 그림만 좌우로 뒤집는다. 충돌체는 가운데 기준 대칭이라 그대로 둬도 맞는다.
    /// Transform 배율을 뒤집지 않는 이유: 음수 배율은 충돌체와 자식 전부에 번져서, 한쪽으로 치우친 충돌체가 생기면 어긋난다.
    /// </summary>
    private static void Flip(GameObject item, bool flip)
    {
        foreach (var renderer in item.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.flipX = flip;
    }

    /// <summary>배열을 제자리에서 섞는다(피셔-예이츠). 매번 같은 소품만 먼저 뽑히지 않게 한다.</summary>
    private static void Shuffle(GameObject[] items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 이 오브젝트를 고르면 바닥 범위와 비울 자리를 씬 뷰에 그린다. 인스펙터 숫자만으로는 어디가
    /// 비는지 감이 안 온다. 빌드에는 안 들어간다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        var inner = Rect.MinMaxRect(floor.xMin + sideMargin, floor.yMin + bottomMargin,
                                    floor.xMax - sideMargin, floor.yMax - topMargin);
        Gizmos.DrawWireCube(inner.center, inner.size);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        foreach (Transform point in keepClear)
            if (point != null) Gizmos.DrawWireSphere(point.position, keepClearRadius);
    }
#endif
}
