using UnityEngine;

/// <summary>
/// 추가 생성 — 튜토리얼 방의 허수아비. 맞으면 피격 그림으로 잠깐 바뀌고 절대 죽지 않는다.
///
/// 왜 적(EnemyWraith)을 쓰지 않는가:
/// 잡몹은 다가오고 때리고 죽는다. 처음 조작을 배우는 자리에서 그러면 배우기 전에 죽고,
/// 죽여버리면 연습할 대상이 사라진다. 허수아비는 <b>가만히 서서 계속 맞아주는 것</b>이
/// 유일한 일이라 별도 컴포넌트가 맞다.
///
/// 오브젝트 두 개를 껐다 켜지 않고 스프라이트만 바꾸는 이유:
/// 상자(닫힘/열림)는 두 오브젝트가 크기도 콜라이더도 달라서 나눌 이유가 있었다. 허수아비는
/// 같은 자리에 같은 크기로 그림만 바뀌므로, 오브젝트를 나누면 위치를 두 번 맞춰야 하고
/// 한쪽만 옮기는 실수가 생긴다.
///
/// 체력을 무한으로 만들지 않고 <b>낮아지면 되돌리는</b> 이유:
/// Health를 무적으로 두면 피격 판정 자체가 무시되어 맞는 느낌(피격 그림·넉백)이 사라진다.
/// 실제로 맞되 죽기 전에 회복시키면 타격감은 그대로 살아 있다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class TrainingDummy : MonoBehaviour
{
    [Header("그림")]
    [Tooltip("허수아비 그림을 그리는 렌더러. 비우면 자식에서 찾는다.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("평소 그림. 비우면 시작할 때 렌더러에 있는 것을 쓴다.")]
    [SerializeField] private Sprite idleSprite;

    [Tooltip("맞았을 때 잠깐 보여줄 그림.")]
    [SerializeField] private Sprite hitSprite;

    [Tooltip("피격 그림을 보여줄 시간(초). 너무 길면 다음 타격과 겹쳐 계속 피격 그림만 보인다.")]
    [SerializeField, Min(0.02f)] private float hitFlashSeconds = 0.18f;

    [Header("체력")]
    // 비율로 두는 이유: 최대 체력을 999에서 바꿔도 이 값은 그대로 유효하다.
    [Tooltip("체력이 최대치의 이 비율 아래로 내려가면 가득 채운다. 허수아비가 죽지 않게 하는 장치다.")]
    [SerializeField, Range(0.05f, 0.9f)] private float restoreThreshold = 0.3f;

    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);

        // 평소 그림을 안 꽂았으면 지금 렌더러에 있는 것을 기준으로 삼는다.
        // 이게 없으면 한 번 맞은 뒤 되돌릴 그림이 없어서 피격 그림에 그대로 굳는다.
        if (idleSprite == null && spriteRenderer != null) idleSprite = spriteRenderer.sprite;
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        ShowIdle();
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;

        // 방이 꺼질 때 예약을 지운다. 안 지우면 다음 입장 때 피격 그림으로 시작한다.
        CancelInvoke(nameof(ShowIdle));
        ShowIdle();
    }

    /// <summary>
    /// 맞았을 때. 피격 그림으로 바꾸고 예약을 새로 건다.
    ///
    /// 매번 CancelInvoke로 예약을 갱신하는 이유: 연타로 맞으면 첫 타격의 예약이 먼저 터져서
    /// 아직 맞는 중인데 평소 그림으로 돌아간다. 마지막 타격 기준으로 다시 재야 한다.
    /// </summary>
    private void OnDamaged(int current, int max)
    {
        // 타격이 실제로 들어왔는지 콘솔로 확인할 수 있게 남긴다.
        // 피격 그림이 안 보일 때 "안 맞은 것"인지 "맞았는데 그림만 안 바뀐 것"인지가 여기서 갈린다.
        Debug.Log($"[허수아비] 피격 — {current}/{max}", this);

        // hitSprite가 비어 있으면 그림을 바꾸지 않는다.
        // 예전에 여기서 null을 그대로 꽂아 허수아비가 통째로 사라진 적이 있다 —
        // SpriteRenderer는 sprite가 null이면 에러 없이 아무것도 안 그린다.
        if (spriteRenderer != null && hitSprite != null)
        {
            spriteRenderer.sprite = hitSprite;
            CancelInvoke(nameof(ShowIdle));
            Invoke(nameof(ShowIdle), hitFlashSeconds);
        }
        else if (hitSprite == null)
        {
            Debug.LogWarning("[허수아비] 피격 그림이 비어 있다. Tools → 재의 길 → 허수아비 세팅 을 다시 실행해라.", this);
        }

        // 죽기 전에 되돌린다. 0이 된 뒤에 채우면 Died가 이미 나가버려서
        // 사망 처리를 듣는 쪽이 허수아비를 잡은 것으로 착각한다.
        if (max > 0 && current <= max * restoreThreshold)
        {
            health.RestoreFull();
            Debug.Log("[허수아비] 체력을 되돌렸다. 계속 연습할 수 있다.", this);
        }
    }

    private void ShowIdle()
    {
        if (spriteRenderer != null && idleSprite != null) spriteRenderer.sprite = idleSprite;
    }
}
