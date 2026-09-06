using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 상자에서 튀어나와 바닥에 떨어지는 유물. 플레이어가 밟으면 먹는다.
///
/// 상자를 열자마자 효과를 주는 것보다 이 방식이 나은 이유:
/// 즉시 적용은 <b>언제 무엇을 받았는지가 화면에 안 남는다.</b> 튀어나온 물건이 바닥에 놓여
/// 있으면 "저기 뭔가 떨어졌다 → 주우러 간다 → 먹었다"가 세 단계로 보인다. 로그라이크에서
/// 상자를 여는 맛이 여기서 나온다.
///
/// 튀어 오르는 연출을 물리가 아니라 코드로 만든 이유: 이 게임은 2D 중력을 전역에서 0으로
/// 꺼놨다(AshProjectSetup). 여기만 중력을 켜면 그 결정이 흐려지고, 탑다운에서 "높이"는
/// 물리 좌표가 아니라 그림의 상하 위치일 뿐이다. 그래서 <b>바닥 위치는 옆으로 밀고,
/// 높이는 자식 스프라이트의 로컬 y로만</b> 흉내 낸다.
/// </summary>
[DisallowMultipleComponent]
public class RelicPickup : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("그림. 튀어 오르는 높이는 이 자식의 로컬 y로 표현한다.")]
    [SerializeField] private Transform visual;

    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("주울 수 있게 되면 켜진다. 떨어지는 도중에는 꺼져 있다.")]
    [SerializeField] private Collider2D pickupCollider;

    [Header("튀어나오기")]
    [Tooltip("상자에서 얼마나 멀리 튀어나갈지(유닛).")]
    [SerializeField, Min(0f)] private float popDistance = 3f;

    [Tooltip("튀어나가 바닥에 닿기까지의 시간(초).")]
    [SerializeField, Min(0.05f)] private float popSeconds = 0.55f;

    [Tooltip("포물선의 최고 높이(유닛). 그림만 올라간다.")]
    [SerializeField, Min(0f)] private float hopHeight = 2f;

    [Header("놓인 뒤")]
    [Tooltip("위아래로 살짝 떠다니는 폭(유닛). 정지해 있으면 배경에 묻힌다.")]
    [SerializeField, Min(0f)] private float bobHeight = 0.25f;

    [Tooltip("떠다니는 주기(초).")]
    [SerializeField, Min(0.1f)] private float bobSeconds = 1.6f;

    private RelicData relic;
    private bool collected;

    // 추가 생성 — 주웠을 때 알려줄 상대. 없으면 아무에게도 안 알린다.
    private Action collectedCallback;

    /// <summary>
    /// 상자가 부른다. 어떤 유물인지와 어느 방향으로 튈지를 정한다.
    ///
    /// 수정(문 개방 시점): 주운 순간을 알려줄 <paramref name="onCollected"/>를 받는다.
    /// 기본값이 있으므로 이미 <see cref="RelicInventory.Gained"/>를 듣는
    /// <see cref="BossRelicReward"/> 같은 기존 호출부는 고칠 필요가 없다.
    ///
    /// 인벤토리의 획득 이벤트가 아니라 픽업이 직접 알리는 쪽을 고른 이유:
    /// 상자 유물은 같은 종류가 다른 곳에서도 나올 수 있어서, 유물 종류로 맞춰보면
    /// "다른 데서 주운 같은 유물"과 구별이 안 된다. 이 픽업이 먹혔는지는 이 픽업이 가장 확실히 안다.
    /// </summary>
    public void Setup(RelicData data, Vector2 direction, Action onCollected = null)
    {
        relic = data;
        collectedCallback = onCollected;

        if (spriteRenderer != null && data != null)
        {
            // 아이콘 그림이 아직 없으면 안 보인다. 그래도 콜라이더는 살아 있어서
            // 먹히기는 하지만, 안 보이는 물건을 밟게 하는 건 좋지 않다.
            spriteRenderer.sprite = data.Icon;

            if (data.Icon == null)
                Debug.LogWarning($"[유물 픽업] {data.DisplayName}에 아이콘이 없어 보이지 않는다.", this);
        }

        if (pickupCollider != null) pickupCollider.enabled = false;

        StartCoroutine(Pop(direction));
    }

    private IEnumerator Pop(Vector2 direction)
    {
        Vector3 start = transform.position;
        Vector3 end = start + (Vector3)(direction.normalized * popDistance);

        float elapsed = 0f;
        while (elapsed < popSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popSeconds);

            // 바닥 위치는 끝으로 갈수록 느려진다. 튀어나온 물건이 미끄러지듯 멈추는 느낌.
            transform.position = Vector3.Lerp(start, end, 1f - (1f - t) * (1f - t));

            // 높이는 포물선. t=0.5에서 최고점, 양 끝에서 0.
            if (visual != null)
                visual.localPosition = new Vector3(0f, 4f * hopHeight * t * (1f - t), 0f);

            yield return null;
        }

        transform.position = end;
        if (visual != null) visual.localPosition = Vector3.zero;

        // 착지한 뒤에야 주울 수 있다. 튀는 도중에 켜두면 상자 앞에 서 있던 플레이어가
        // 물건이 날아가기도 전에 먹어버려서 연출이 통째로 사라진다.
        if (pickupCollider != null) pickupCollider.enabled = true;

        StartCoroutine(Bob());
    }

    private IEnumerator Bob()
    {
        while (!collected)
        {
            if (visual != null)
            {
                float t = Mathf.Sin(Time.time * Mathf.PI * 2f / bobSeconds);
                visual.localPosition = new Vector3(0f, t * bobHeight, 0f);
            }

            yield return null;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected || relic == null) return;

        // 콜라이더가 Pickup 레이어라 충돌 매트릭스에서 Player만 닿는다.
        // 그래도 부모까지 훑는 이유는 플레이어의 콜라이더가 자식에 있을 수 있어서다.
        var inventory = other.GetComponentInParent<RelicInventory>();
        if (inventory == null) return;

        collected = true;
        inventory.Acquire(relic);

        // 추가 생성 — 인벤토리에 들어간 <b>뒤에</b> 알린다.
        // 순서가 뒤바뀌면 상자가 문 종류를 정할 때 방금 먹은 열쇠가 아직 안 세어져 있다.
        collectedCallback?.Invoke();
        collectedCallback = null;

        Destroy(gameObject);
    }
}
