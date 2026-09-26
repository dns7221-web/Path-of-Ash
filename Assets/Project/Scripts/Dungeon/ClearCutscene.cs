using System.Collections;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-26) — 클리어 연출 「손을 펴다」.
///
/// 보스 방 문을 나가면 결과 화면으로 넘어가기 전에 한 번 튼다. 플레이어가 걸음을 멈추고
/// 클리어 유물 "길의 매듭"을 쥔 손을 가슴으로 가져와 바라본 뒤, 앞으로 내밀며 천천히 편다.
/// 그동안 카메라가 플레이어 쪽으로 살짝 다가간다. 핵심 동작은 "꽉 쥔 손을 펴는 것" 하나다.
///
/// <b>누가 무엇을 소유하는가.</b>
/// <list type="bullet">
/// <item>모션의 시간표 — 애니메이션 클립(8방향 빌더의 ClearOpenHand, 장별 시간)이 소유한다.
/// 이 스크립트는 "끝났는가"를 애니메이터 상태(normalizedTime)로 물어볼 뿐 시간을 따로 적지 않는다.
/// 같은 시간을 두 곳에 적으면 어긋난다(페이즈 전환 0.75 대 0.875 사고).</item>
/// <item>판을 끝내는 일 — RoomSequenceController가 소유한다. 이 스크립트는 연출만 하고 돌아가며,
/// <see cref="Play"/>가 끝나면 부른 쪽이 EndRun을 부른다.</item>
/// <item>플레이어 붙잡기 — PlayerController.BeginScripted(왕관 의식과 같은 길).
/// 입력 잠금·무적·R 무릎 꿇기 풀기(ReleasePose)가 같이 따라온다.</item>
/// </list>
///
/// <b>끝 이벤트(ScriptedPoseEnd)를 쓰지 않는 이유.</b> 그 이벤트는 조작을 돌려준다(EndScripted).
/// 이 연출 뒤에는 결과 화면으로 가야 하므로 붙잡은 채로 둔다. 마지막 장(풀린 자세)은 애니메이터가 붙든다 —
/// 빌더가 이 상태에는 Idle로 돌아가는 전환을 만들지 않는다(HoldLastFrame). 나가는 전환이 없는 상태는
/// 마지막 장에서 멈춘다(사망 모션과 같은 원리).
/// </summary>
[DisallowMultipleComponent]
public class ClearCutscene : MonoBehaviour
{
    [Header("모션")]
    [Tooltip("틀 모션의 트리거이자 애니메이터 상태 이름. 8방향 빌더가 둘을 같은 이름으로 만든다.")]
    [SerializeField] private string motionName = "ClearOpenHand";

    [Tooltip("모션이 끝난 걸 알아채지 못했을 때 그냥 넘어가기까지의 시간(초). 모션 자체는 약 2.7초다.")]
    [SerializeField, Min(0.5f)] private float timeoutSeconds = 5f;

    [Header("카메라 (살짝 다가간다)")]
    [Tooltip("다가간 뒤의 화면 크기 비율. 0.75면 화면에 담기는 범위가 3/4이 된다(약 1.33배 확대).")]
    [SerializeField, Range(0.4f, 1f)] private float zoomRatio = 0.75f;

    [Tooltip("다가가는 데 걸리는 시간(초). 모션의 '가슴으로 가져오기'가 끝날 즈음 멈추게 잡았다.")]
    [SerializeField, Min(0f)] private float zoomSeconds = 1.2f;

    [Tooltip("다가가는 빠르기 곡선(가로 0~1 = 시간, 세로 0~1 = 진행). 기본은 천천히 출발해 천천히 멈춘다.")]
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("화면 가운데를 발에서 이만큼 위(유닛)로 잡는다. 캐릭터 키가 약 6유닛이라 3이면 손과 가슴 높이다.")]
    [SerializeField] private float focusHeight = 3f;

    /// <summary>
    /// 연출을 틀고 모션이 끝날 때까지 기다린다. 부른 쪽이 <c>yield return</c>으로 기다린 뒤 판을 끝낸다.
    /// 플레이어가 없거나 이미 죽었으면 연출 없이 바로 돌아간다 — 연출보다 판을 끝내는 게 먼저다.
    /// </summary>
    public IEnumerator Play()
    {
        PlayerController player = FindFirstObjectByType<PlayerController>();

        // 화면 쪽(아래)을 보게 하고 모션을 튼다. 이 모션은 8방향 모두 앞모습이라 방향은 그림에 영향이 없지만,
        // 연출이 끝난 뒤 남는 방향 값도 화면 쪽이 자연스럽다.
        if (player == null || !player.BeginScripted(Vector2.down, motionName))
        {
            Debug.LogWarning("[클리어 연출] 붙잡을 플레이어가 없어 연출 없이 끝낸다.", this);
            yield break;
        }

        Animator animator = player.GetComponentInChildren<Animator>();

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 focus = player.transform.position + Vector3.up * focusHeight;
            StartCoroutine(ZoomTowards(cam, focus));
        }

        // 모션 상태에 들어가 끝(normalizedTime 1)까지 재생되기를 기다린다.
        // 게임 시간(Time.deltaTime)으로 세는 이유: 설정 창을 열어 시간이 멈추면 애니메이터도 멈추므로,
        // 기다리는 시간도 같이 멈춰야 모션이 끝나기 전에 넘어가지 않는다.
        float waited = 0f;
        while (!IsMotionFinished(animator))
        {
            waited += Time.deltaTime;
            if (waited >= timeoutSeconds)
            {
                // 안전망은 조용히 돌면 버그를 가린다(09-26 왕관 의식에서 배운 것) — 넘어갈 때 반드시 남긴다.
                Debug.LogWarning($"[클리어 연출] {timeoutSeconds}초 안에 '{motionName}' 모션이 끝나지 않아 그냥 넘어간다. " +
                                 "Tools → 재의 길 → 애니메이션 → 8방향 플레이어 애니메이션 생성 을 실행했는지 확인해라.", this);
                break;
            }

            yield return null;
        }
    }

    /// <summary>
    /// 애니메이터가 이 모션 상태에 들어가 끝까지 재생했는가.
    /// 트리거를 켠 그 프레임에는 아직 옛 상태라서, 상태 이름부터 확인해야 옛 상태의 끝을 잘못 읽지 않는다.
    /// 유니티 내장 AnimatorStateInfo로 묻는다 — 모션 길이를 이 스크립트에 따로 적지 않기 위해서다.
    /// </summary>
    private bool IsMotionFinished(Animator animator)
    {
        if (animator == null) return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsName(motionName) && state.normalizedTime >= 1f;
    }

    /// <summary>
    /// 카메라를 focus 쪽으로 다가가게 한다. 화면 크기(orthographicSize)를 줄이고 중심을 옮긴다.
    ///
    /// <b>중심이 움직일 수 있는 범위를 "줄어든 만큼"으로 막는 이유.</b> 방은 한 화면짜리라 카메라가 고정이고,
    /// 보스 방 문은 방 위쪽 가장자리에 있다. 그대로 문을 향해 다가가면 확대한 화면이 방 바깥(빈 곳)을 비춘다.
    /// 줄어든 반폭만큼만 움직이면 확대한 화면은 늘 원래 화면 안에 들어간다.
    ///
    /// <b>위치를 덮어쓰지 않고 이동량만 더하는 이유.</b> CameraShake는 LateUpdate에서 "지난 흔들림을 빼고
    /// 새 흔들림을 더하는" 상대 방식으로 움직인다. 여기서 절대 위치를 넣으면 흔들림이 두 번 빠져 카메라가 흘러간다.
    /// </summary>
    private IEnumerator ZoomTowards(Camera cam, Vector3 focus)
    {
        float startSize = cam.orthographicSize;
        float endSize = startSize * zoomRatio;

        Vector3 startCenter = cam.transform.position;
        float maxY = startSize - endSize;
        float maxX = maxY * cam.aspect;
        Vector3 toFocus = focus - startCenter;

        // z는 그리는 범위(near/far)를 정하는 값이라 건드리지 않는다.
        Vector3 endCenter = startCenter + new Vector3(
            Mathf.Clamp(toFocus.x, -maxX, maxX),
            Mathf.Clamp(toFocus.y, -maxY, maxY),
            0f);

        Vector3 lastCenter = startCenter;
        float duration = Mathf.Max(0.01f, zoomSeconds);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = zoomCurve.Evaluate(Mathf.Clamp01(elapsed / duration));

            cam.orthographicSize = Mathf.Lerp(startSize, endSize, progress);

            Vector3 center = Vector3.Lerp(startCenter, endCenter, progress);
            cam.transform.position += center - lastCenter;
            lastCenter = center;

            yield return null;
        }
    }
}
