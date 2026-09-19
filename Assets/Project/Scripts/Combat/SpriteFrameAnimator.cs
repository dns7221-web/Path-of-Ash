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

                if (destroyWhenFinished)
                {
                    // 추가 생성(2026-09-17, 플레이어 파티클) — 곁들인 파티클을 먼저 월드로 떼어 낸다.
                    // 같이 지우면 그림이 끝나는 순간 불티까지 한꺼번에 사라져 여운이 안 남는다.
                    // 곁들임이 없는 이펙트에서는 아무 일도 하지 않는다.
                    ParticleGarnish.ReleaseAll(gameObject);
                    Destroy(gameObject);
                }
                enabled = false;
                return;
            }
        }

        spriteRenderer.sprite = frames[index];
    }

    /// <summary>
    /// 추가 생성(2026-09-15, 망령 돌진 예고선) — 첫 프레임부터 다시 재생한다. 전체 재생 시간을 같이 줄 수 있다.
    ///
    /// <b>왜 OnEnable만으로 안 되나.</b> 지금까지의 이펙트는 한 번 만들고 끝나면 지우는 것이라 OnEnable의
    /// 초기화로 충분했다. 예고선은 적에게 붙여 두고 공격할 때마다 다시 켜는데, 반복하지 않는 재생은 끝나면
    /// 이 컴포넌트를 스스로 끈다(위 Update의 enabled = false). 꺼진 컴포넌트는 Update가 돌지 않아서,
    /// 다시 부르지 않으면 <b>두 번째 공격부터 마지막 프레임에 멈춘 그림만</b> 뜬다. 에러도 안 난다.
    ///
    /// <b>왜 재생 시간을 받나.</b> 예고선은 "마지막 프레임이 끝나는 순간 = 공격이 시작되는 순간"이어야 읽힌다.
    /// fps를 인스펙터 숫자로만 두면 적의 예비동작 시간을 바꿀 때 두 숫자를 같이 고쳐야 하고, 한쪽만 고치면
    /// 선이 먼저 끝나거나 공격이 시작된 뒤에도 남는다. 시간을 쥔 쪽(적)이 넘겨주면 둘이 어긋날 수 없다.
    /// </summary>
    /// <param name="durationSeconds">첫 프레임부터 마지막 프레임이 끝날 때까지의 시간(초). 0 이하면 인스펙터의 fps를 그대로 쓴다.</param>
    public void Restart(float durationSeconds = 0f)
    {
        // 한 번도 켜진 적 없는 오브젝트에서 불리면 Awake가 아직 안 돌았을 수 있다.
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

        // 인스턴스의 값만 바뀐다. 프리팹 에셋의 fps는 그대로다.
        if (durationSeconds > 0f && frames != null && frames.Length > 0)
            fps = frames.Length / durationSeconds;

        elapsed = 0f;
        if (frames != null && frames.Length > 0) spriteRenderer.sprite = frames[0];

        // 끝까지 재생해서 스스로 꺼졌을 수 있다. 다시 켜야 Update가 돈다.
        enabled = true;
    }
}
