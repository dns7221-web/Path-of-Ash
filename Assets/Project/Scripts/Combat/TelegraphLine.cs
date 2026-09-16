using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-15) — 공격이 지나갈 길을 바닥에 미리 긋는 예고선. 잿불 망령의 돌진이 처음 쓴다.
///
/// 그림은 <b>오른쪽으로 뻗게</b> 한 줄로 그린 프레임 애니메이션이고, 피벗이 선의 출발점이다
/// (AshVfxSpriteSlicer의 Forward). 이 컴포넌트는 그 그림을 공격 방향으로 돌리고, 공격이 닿는 거리만큼
/// 가로로 늘리고, 정해진 시간 동안 한 번 재생한다.
///
/// <b>적 스크립트에 넣지 않고 따로 뺀 이유.</b> "그림 속 선이 몇 유닛 길이로 그려졌나"는 그림의 성질이라
/// 그림과 같은 프리팹에 있어야 한다. 적 쪽에 두면 시트를 다시 정규화할 때마다 적 프리팹의 숫자를 따라
/// 고쳐야 하는데, 안 고쳐도 에러 없이 선 길이만 판정과 어긋난다. 적은 방향·거리·시간만 넘긴다.
///
/// <b>가로만 늘리는 이유.</b> 선의 굵기는 "얼마나 비켜야 하나"로 읽힌다. 돌진 거리를 조정할 때마다
/// 굵기까지 따라 변하면 같은 적인데 피할 폭이 달라 보인다.
///
/// 수정(2026-09-15) — 굵기도 늘린다. 다만 <b>길이가 아니라 판정의 굵기</b>를 따른다. 위 이유("굵기 = 얼마나 비켜야 하나")를
/// 그대로 지키려면 굵기가 판정 폭과 같아야 하는데, 그림 그대로의 굵기(약 1.9유닛)가 망령 돌진 판정 폭(2.94유닛)보다 가늘어서
/// <b>선 바로 옆에 서도 맞았다.</b> 길이와는 여전히 무관하다 — 돌진 거리를 바꿔도 굵기는 안 변한다.
///
/// 유니티의 LineRenderer를 쓰지 않은 이유: 선의 모양이 프레임마다 다른 픽셀 그림(갈라지는 불꽃, 화살촉)이라
/// 점과 폭으로 그리는 LineRenderer로는 옮길 수 없다. 이미 있는 SpriteFrameAnimator 그림을 돌리고 늘리는 쪽이
/// 다른 VFX와 같은 방식으로 관리된다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteFrameAnimator))]
public class TelegraphLine : MonoBehaviour
{
    [Tooltip("배율 1에서 그림 속 선이 출발점(피벗)부터 가장 먼 끝까지 차지하는 길이(유닛). " +
             "스프라이트 칸 크기가 아니라 실제로 그려진 부분이다. 예고선 빌더가 시트 픽셀을 재서 넣는다.")]
    [SerializeField, Min(0.01f)] private float drawnLength = 1f;

    // 추가 생성(2026-09-15) — 그림 속 선의 굵기. Show가 판정 굵기에 맞춰 세로를 늘릴 때 기준으로 쓴다.
    //
    // 기본값 1.875는 지금 시트(vfx_wraith_charge_telegraph)를 잰 값이다(여섯 프레임의 세로 범위 40·51·57·63·68·84px 중
    // 가운데 두 값의 평균 60px ÷ PPU 32). 새 필드라 이미 있는 예고선 프리팹에는 저장된 값이 없어서 이 기본값이 그대로 들어간다.
    // 1로 두면 빌더를 다시 돌리기 전까지 선이 판정의 1.9배로 두꺼워진다.
    [Tooltip("배율 1에서 그림 속 선의 굵기(유닛). 여섯 프레임의 세로 범위 중 가운데 값 — 가늘게 시작하는 첫 프레임과 " +
             "갈라지며 퍼지는 마지막 프레임에 끌리지 않는다. 예고선 빌더가 시트 픽셀을 재서 넣는다.")]
    [SerializeField, Min(0.01f)] private float drawnThickness = 1.875f;

    private SpriteRenderer spriteRenderer;
    private SpriteFrameAnimator frameAnimator;

    /// <summary>
    /// 예고선을 켜고 첫 프레임부터 재생한다.
    /// </summary>
    /// <param name="direction">공격이 나아갈 방향(월드). 길이는 상관없다.</param>
    /// <param name="length">선 끝이 닿을 거리(유닛). 이 오브젝트의 위치(출발점)부터 잰다.</param>
    /// <param name="thickness">추가 생성(2026-09-15) — 선이 덮어야 할 굵기(유닛). 판정의 폭을 넘긴다. 0 이하면 그림 굵기 그대로 둔다.</param>
    /// <param name="durationSeconds">마지막 프레임이 끝날 때까지의 시간(초). 공격이 시작되는 순간과 맞춘다.</param>
    public void Show(Vector2 direction, float length, float thickness, float durationSeconds)
    {
        CacheComponents();

        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;

        // 로컬이 아니라 월드 회전으로 준다. 부모가 돌아가 있어도 "선이 가리키는 쪽 = 공격이 가는 쪽"이 지켜진다.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // 그림 속 선 길이를 원하는 길이로 늘리는 배율. 세로는 건드리지 않는다(클래스 설명 참고).
        // 수정(2026-09-15) — 세로도 판정 굵기에 맞춰 늘린다(클래스 설명의 수정 참고). 선의 피벗이 세로 가운데라
        // 위아래로 같은 양만큼 퍼지고, 가운데선은 출발점 높이에 그대로 남는다.
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Max(0.01f, length) / drawnLength;
        if (thickness > 0f) scale.y = thickness / drawnThickness;
        transform.localScale = scale;

        spriteRenderer.enabled = true;
        frameAnimator.Restart(durationSeconds);
    }

    /// <summary>
    /// 예고선을 끈다. 공격이 시작됐거나 취소됐을 때 부른다. 이미 꺼져 있어도 괜찮다.
    ///
    /// 오브젝트를 SetActive로 끄지 않고 그림과 재생기만 끄는 이유: 이 함수는 적이 풀로 돌아가며
    /// 꺼지는 도중(OnDisable)에도 불린다. 그때 자식의 활성 상태를 바꾸면 부모의 활성화 처리와 겹친다.
    /// 컴포넌트 스위치는 그런 제약이 없고, 오브젝트가 계속 살아 있어서 다음 Show도 단순하다.
    /// </summary>
    public void Hide()
    {
        CacheComponents();

        spriteRenderer.enabled = false;

        // 재생기도 끈다. 안 보이는 선이 매 프레임 스프라이트를 갈아끼울 이유가 없다.
        frameAnimator.enabled = false;
    }

    /// <summary>
    /// 컴포넌트를 한 번만 찾아 둔다. Awake 대신 여기서 하는 이유: 적이 예고선을 만든 바로 그 프레임에
    /// Show나 Hide를 부르는데, 호출 순서를 Awake에 기대지 않아도 되게 하려는 것이다.
    /// </summary>
    private void CacheComponents()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (frameAnimator == null) frameAnimator = GetComponent<SpriteFrameAnimator>();
    }
}
