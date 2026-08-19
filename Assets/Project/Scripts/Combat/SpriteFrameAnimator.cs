using UnityEngine;

/// <summary>
/// SpriteRenderer의 스프라이트를 일정 간격으로 갈아끼우는 가벼운 프레임 재생기.
///
/// Animator를 안 쓴 이유: 이펙트 하나에 AnimatorController 에셋과 상태 머신을 만드는 건
/// 과하다. 이펙트는 "프레임을 순서대로 넘긴다"가 전부고 전이 조건도 파라미터도 없다.
/// 그리고 나중에 오브젝트 풀로 재사용할 때 Animator는 Rebind로 상태를 되돌려줘야 하는데,
/// 이쪽은 인덱스를 0으로 되돌리면 끝이라 다루기 쉽다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFrameAnimator : MonoBehaviour
{
    [Tooltip("순서대로 재생할 프레임. 비어 있으면 아무 일도 하지 않는다.")]
    [SerializeField] private Sprite[] frames;

    [Tooltip("초당 프레임 수.")]
    [SerializeField, Min(1f)] private float fps = 16f;

    [Tooltip("끝까지 재생한 뒤 처음으로 돌아갈지. 화살 비행처럼 계속 도는 것은 켠다.")]
    [SerializeField] private bool loop = true;

    [Tooltip("루프가 아닐 때, 재생이 끝나면 이 오브젝트를 지울지. 명중 이펙트에 쓴다.")]
    [SerializeField] private bool destroyWhenFinished;

    private SpriteRenderer spriteRenderer;
    private float elapsed;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        // 풀에서 다시 꺼내 쓸 때를 대비해 처음으로 되돌린다.
        elapsed = 0f;
        if (frames != null && frames.Length > 0) spriteRenderer.sprite = frames[0];
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0) return;

        elapsed += Time.deltaTime;

        int index = Mathf.FloorToInt(elapsed * fps);

        if (index >= frames.Length)
        {
            if (loop)
            {
                index %= frames.Length;
            }
            else
            {
                // 마지막 프레임에서 멈춘다. 지우라고 했으면 지운다.
                index = frames.Length - 1;
                spriteRenderer.sprite = frames[index];

                if (destroyWhenFinished) Destroy(gameObject);
                enabled = false;
                return;
            }
        }

        spriteRenderer.sprite = frames[index];
    }
}
