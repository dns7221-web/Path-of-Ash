using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal; // 추가 생성(2026-09-26, 연출 v2) — 사라질 때 플레이어 조명(Light2D)도 같이 끄려고

/// <summary>
/// 추가 생성(2026-09-26) — 클리어 연출 「손을 펴다」.
///
/// 보스 방 문을 나가면 결과 화면으로 넘어가기 전에 한 번 튼다. 플레이어가 걸음을 멈추고
/// 클리어 유물 "길의 매듭"을 쥔 손을 가슴으로 가져와 바라본 뒤, 앞으로 내밀며 천천히 편다.
/// 그동안 카메라가 플레이어 쪽으로 살짝 다가간다. 핵심 동작은 "꽉 쥔 손을 펴는 것" 하나다.
///
/// 수정(2026-09-26, 연출 v2) — 흐름을 <b>문에 닿음 → 걸어 나옴 → 손을 펴다 → 문으로 걸어 들어가 사라짐</b>으로 바꿨다.
/// 첫 판은 문에 닿은 그 자리에서 모션을 틀었다. 보스 방 문은 방 맨 위 가장자리라서, 문에 닿은 순간 머리가 이미
/// 화면 위로 나가 있었고(발 약 10.6 + 키 약 6 = 머리 약 16.6, 화면 위 끝 15.9) 그 잘린 채로 카메라가 다가갔다.
/// 이제 몸 전체가 화면에 드는 곳까지 몇 걸음 걸어 나와서 모션을 틀고, 끝나면 돌아서 문으로 걸어 들어가며 흐려진다.
///
/// <b>누가 무엇을 소유하는가.</b>
/// <list type="bullet">
/// <item>모션의 시간표 — 애니메이션 클립(8방향 빌더의 ClearOpenHand, 장별 시간)이 소유한다.
/// 이 스크립트는 "끝났는가"를 애니메이터 상태로 물어볼 뿐 시간을 따로 적지 않는다.
/// 같은 시간을 두 곳에 적으면 어긋난다(페이즈 전환 0.75 대 0.875 사고).</item>
/// <item>판을 끝내는 일 — RoomSequenceController가 소유한다. 이 스크립트는 연출만 하고 돌아가며,
/// <see cref="Play"/>가 끝나면 부른 쪽이 EndRun을 부른다.</item>
/// <item>플레이어 붙잡기 — PlayerController.BeginScripted(왕관 의식과 같은 길).
/// 입력 잠금·무적·R 무릎 꿇기 풀기(ReleasePose)가 같이 따라온다.
/// 수정(2026-09-26, 연출 v2) — 붙잡은 채로 걷게 하는 일도 PlayerController가 한다(SetScriptedMove).
/// 여기서 위치를 직접 옮기지 않는 이유는 그 함수 주석에 있다.</item>
/// </list>
///
/// <b>끝 이벤트(ScriptedPoseEnd)를 쓰지 않는 이유.</b> 그 이벤트는 조작을 돌려준다(EndScripted).
/// 이 연출 뒤에는 결과 화면으로 가야 하므로 붙잡은 채로 둔다.
/// 수정(2026-09-26, 연출 v2) — 예전에는 마지막 장(풀린 자세)을 애니메이터가 붙들게 했다(빌더의 HoldLastFrame).
/// 이제 모션 뒤에 걸어 들어가야 하므로 다른 액션처럼 끝나면 Idle로 돌아가고, 걸음은 속도에 따라 저절로 걷기가 된다.
/// </summary>
[DisallowMultipleComponent]
public class ClearCutscene : MonoBehaviour
{
    [Header("모션")]
    [Tooltip("틀 모션의 트리거이자 애니메이터 상태 이름. 8방향 빌더가 둘을 같은 이름으로 만든다.")]
    [SerializeField] private string motionName = "ClearOpenHand";

    [Tooltip("모션이 끝난 걸 알아채지 못했을 때 그냥 넘어가기까지의 시간(초). 모션 자체는 약 2.7초다.")]
    [SerializeField, Min(0.5f)] private float timeoutSeconds = 5f;

    // 추가 생성(2026-09-26, 연출 v2) — 문에서 걸어 나왔다가 다시 걸어 들어가는 값들
    [Header("걷기 (문에서 나왔다가 다시 들어간다)")]
    [Tooltip("문에 닿은 자리에서 모션을 틀 자리까지의 방향과 거리(유닛). 보스 방 문은 방 위쪽 가장자리라 아래(화면 쪽)로 " +
             "걸어 나온다. 캐릭터 키가 약 6유닛이라 4.5면 머리까지 화면 안에 든다.")]
    [SerializeField] private Vector2 walkOutOffset = new Vector2(0f, -4.5f);

    [Tooltip("연출 중에 걷는 빠르기(유닛/초). 평소 이동(14)의 절반으로 천천히 걷는다.")]
    [SerializeField, Min(0.5f)] private float walkSpeed = 7f;

    [Tooltip("들어갈 때 문에 닿았던 자리보다 이만큼(유닛) 더 안쪽까지 걷는다. 문턱을 넘어 사라지게 보이려는 것이다. " +
             "너무 크면 다 사라지기 전에 머리가 화면 위로 나간다. 보스 방에서 0.5면 머리 끝(약 17.1)이 방 그림 위 끝(17.18) 안에 머문다.")]
    [SerializeField, Min(0f)] private float walkInExtra = 0.5f;

    [Tooltip("들어가는 걸음의 마지막 이 시간(초) 동안 몸과 몸에 붙은 조명이 흐려져 사라진다.")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.6f;

    [Header("카메라 (살짝 다가간다)")]
    // 수정(2026-09-26, 연출 v2) — 0.75 → 0.6. 걸어 나온 뒤에는 몸 전체가 화면 한가운데 쪽에 오므로 조금 더 다가가도
    // 잘리지 않는다. 손과 팔의 움직임이 작은 캐릭터에서도 읽히도록 크게 보이게 하려는 것이다(0.6 = 약 1.67배).
    [Tooltip("다가간 뒤의 화면 크기 비율. 0.6이면 화면에 담기는 범위가 0.6배가 된다(약 1.67배 확대).")]
    [SerializeField, Range(0.4f, 1f)] private float zoomRatio = 0.6f;

    // 수정(2026-09-26, 연출 v2) — 설명만 고쳤다. 이제 걸어 나오는 동안 다가가기 시작한다.
    [Tooltip("다가가는 데 걸리는 시간(초). 걸어 나오는 동안 시작해 모션 앞부분(멈춰 섬)에서 멈추게 잡았다.")]
    [SerializeField, Min(0f)] private float zoomSeconds = 1.2f;

    [Tooltip("다가가는 빠르기 곡선(가로 0~1 = 시간, 세로 0~1 = 진행). 기본은 천천히 출발해 천천히 멈춘다.")]
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("화면 가운데를 발에서 이만큼 위(유닛)로 잡는다. 캐릭터 키가 약 6유닛이라 3이면 손과 가슴 높이다.")]
    [SerializeField] private float focusHeight = 3f;

    /// <summary>
    /// 연출을 끝까지 틀고 돌아온다. 부른 쪽이 <c>yield return</c>으로 기다린 뒤 판을 끝낸다.
    /// 플레이어가 없거나 이미 죽었으면 연출 없이 바로 돌아간다 — 연출보다 판을 끝내는 게 먼저다.
    ///
    /// 수정(2026-09-26, 연출 v2) — 방을 받는다. 카메라가 방 그림 밖(빈 곳)을 비추지 않게 막는 데 쓴다(FindViewLimit).
    /// 흐름: ① 붙잡기 → ② 문에서 걸어 나옴(카메라가 다가가기 시작) → ③ 손을 펴다 → ④ 문으로 걸어 들어가며 사라짐.
    /// </summary>
    /// <param name="room">연출이 벌어지는 방(보스 방). 비우면 지금 화면 안으로만 카메라를 움직인다.</param>
    public IEnumerator Play(Transform room)
    {
        PlayerController player = FindFirstObjectByType<PlayerController>();

        // ① 붙잡는다 — 모션은 아직 틀지 않는다(트리거 null). 입력·스킬이 막히고 무적이 된 채로 걸어 나오게 하려는 것이다.
        // 화면 쪽(아래)을 보게 하는 이유: 곧 화면 쪽으로 걸어 나오므로, 문을 보던 뒷모습이 한 프레임 남지 않게.
        if (player == null || !player.BeginScripted(Vector2.down, null))
        {
            Debug.LogWarning("[클리어 연출] 붙잡을 플레이어가 없어 연출 없이 끝낸다.", this);
            yield break;
        }

        Animator animator = player.GetComponentInChildren<Animator>();

        // 문에 닿은 자리를 기억한다 — 모션 뒤에 이 자리(보다 조금 더 안쪽)로 걸어 들어간다.
        Vector2 door = player.transform.position;
        Vector2 stage = door + walkOutOffset;

        // 카메라는 "모션을 틀 자리"를 향해 미리 다가가기 시작한다. 걸어 나오는 동안 같이 움직여야 한 번에 이어져 보인다.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 focus = stage + Vector2.up * focusHeight;
            StartCoroutine(ZoomTowards(cam, focus, FindViewLimit(room, cam)));
        }

        // ② 문에서 걸어 나온다.
        yield return WalkTo(player, stage);

        // ③ 화면 쪽을 보고 손을 편다. 이미 붙잡혀 있으므로 이번 호출은 멈춰 세우고, 방향을 돌리고, 모션을 트는 일만 한다.
        // 이 모션은 8방향 모두 앞모습이라 방향은 그림에 영향이 없지만, 들어갈 때 돌아서는 방향의 기준이 된다.
        player.BeginScripted(Vector2.down, motionName);
        yield return WaitForMotion(animator);

        // ④ 돌아서서 문으로 걸어 들어가며 사라진다. 나온 길을 거꾸로 되짚어, 문에 닿았던 자리보다 조금 더 안쪽까지 간다.
        Vector2 intoDoor = -walkOutOffset.normalized;
        yield return WalkIntoDoor(player, door + intoDoor * walkInExtra);
    }

    /// <summary>
    /// 추가 생성(2026-09-26, 연출 v2) — target까지 걸어간다. 도착하면(또는 막혀서 시간이 다 되면) 멈춰 세운다.
    /// </summary>
    private IEnumerator WalkTo(PlayerController player, Vector2 target)
    {
        // 소품이나 벽에 막혀 영영 못 닿는 경우를 대비한 한도 — 걸리는 시간보다 조금 넉넉하게 준다.
        float limit = Vector2.Distance(player.transform.position, target) / walkSpeed + 0.5f;
        float elapsed = 0f;

        while (!StepTowards(player, target) && elapsed < limit)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        player.SetScriptedMove(Vector2.zero);
    }

    /// <summary>
    /// 추가 생성(2026-09-26, 연출 v2) — 이번 프레임에 target 쪽으로 걷게 한다. 이미 닿았으면 멈추고 true를 돌려준다.
    /// </summary>
    private bool StepTowards(PlayerController player, Vector2 target)
    {
        Vector2 toTarget = target - (Vector2)player.transform.position;

        // 물리 한 스텝(Time.fixedDeltaTime)에 걷는 거리보다 가까우면 도착으로 본다.
        // 더 밀면 목표를 지나쳤다가 되돌아오기를 반복하며 제자리에서 떤다.
        if (toTarget.magnitude <= walkSpeed * Time.fixedDeltaTime)
        {
            player.SetScriptedMove(Vector2.zero);
            return true;
        }

        player.SetScriptedMove(toTarget.normalized * walkSpeed);
        return false;
    }

    /// <summary>
    /// 추가 생성(2026-09-26, 연출 v2) — 문으로 걸어 들어가며 사라진다. 끝나면 몸과 조명이 완전히 꺼져 있다.
    ///
    /// 걸음은 위치로(도착하면 멈춤), 흐려짐은 시간으로 센다. 걷는 시간이 흐려지는 시간보다 짧아도
    /// (문 바로 앞에서 끝났을 때) 흐려짐은 제 시간을 다 쓰게 하려는 것이다.
    ///
    /// <b>색을 "원래 값 × 남은 비율"로 넣는 이유.</b> 그림자처럼 처음부터 반투명한 그림이 있으면 1 → 0으로 덮어쓸 때
    /// 첫 프레임에 오히려 진해진다. 시작할 때의 값을 기억해 두고 거기에 곱한다. 조명 세기도 같은 방식이다.
    /// </summary>
    private IEnumerator WalkIntoDoor(PlayerController player, Vector2 target)
    {
        // 흐릴 대상 — 플레이어 밑의 모든 그림(자식 포함)과 몸에 붙은 조명(HeroLight). 몸만 사라지고 불빛이 허공에
        // 남으면 어색하므로 같이 끈다. 한 번만 찾는다(매 프레임 GetComponents를 부르면 배열을 매번 새로 만든다).
        SpriteRenderer[] sprites = player.GetComponentsInChildren<SpriteRenderer>();
        Color[] startColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) startColors[i] = sprites[i].color;

        Light2D[] lights = player.GetComponentsInChildren<Light2D>();
        float[] startIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) startIntensities[i] = lights[i].intensity;

        // 남은 비율(1 = 원래대로, 0 = 사라짐)을 그림과 조명에 한꺼번에 넣는다.
        void SetVisible(float visible)
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color color = startColors[i];
                color.a *= visible;
                sprites[i].color = color;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null) lights[i].intensity = startIntensities[i] * visible;
            }
        }

        float walkSeconds = Vector2.Distance(player.transform.position, target) / walkSpeed;
        float duration = Mathf.Max(walkSeconds, fadeSeconds);
        float fadeStart = duration - fadeSeconds;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            StepTowards(player, target);

            // 마지막 fadeSeconds 동안 1 → 0. fadeSeconds가 0이면 걷는 동안은 그대로 두었다가 끝에서 한 번에 끈다.
            if (fadeSeconds > 0f) SetVisible(1f - Mathf.Clamp01((elapsed - fadeStart) / fadeSeconds));

            yield return null;
        }

        player.SetScriptedMove(Vector2.zero);
        SetVisible(0f);
    }

    /// <summary>
    /// 모션이 끝날 때까지 기다린다. 유니티 내장 AnimatorStateInfo로 묻는다 — 모션 길이를 이 스크립트에 따로 적지 않기 위해서다.
    /// 트리거를 켠 그 프레임에는 아직 옛 상태라서, 상태 이름부터 확인해야 옛 상태의 끝을 잘못 읽지 않는다.
    ///
    /// 수정(2026-09-26, 연출 v2) — "이 상태에 들어갔다가 나왔으면 끝"으로 바꿨다(옛 IsMotionFinished를 대신한다).
    /// 예전 조건(이 상태이면서 normalizedTime ≥ 1)은 마지막 장을 붙드는, 나가는 전환이 없는 상태에서만 맞는다.
    /// 이제 모션이 끝나면 Idle로 돌아가므로, normalizedTime이 1에 닿는 그 프레임에 이미 Idle로 넘어가 있어 한 번도
    /// 못 볼 수 있다. 그래서 "한 번이라도 들어갔는가"를 기억해 두고, 들어간 뒤 다른 상태가 되면 끝으로 본다.
    /// normalizedTime ≥ 1도 같이 보는 이유: 빌더를 다시 돌리기 전의 컨트롤러(마지막 장을 붙드는)에서도 멈추지 않게.
    /// </summary>
    private IEnumerator WaitForMotion(Animator animator)
    {
        if (animator == null)
        {
            Debug.LogWarning("[클리어 연출] 플레이어에 애니메이터가 없어 모션 없이 넘어간다.", this);
            yield break;
        }

        bool entered = false;
        float waited = 0f;

        // 게임 시간(Time.deltaTime)으로 세는 이유: 설정 창을 열어 시간이 멈추면 애니메이터도 멈추므로,
        // 기다리는 시간도 같이 멈춰야 모션이 끝나기 전에 넘어가지 않는다.
        while (waited < timeoutSeconds)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool inMotion = state.IsName(motionName);
            if (inMotion) entered = true;

            if (entered && (!inMotion || state.normalizedTime >= 1f)) yield break;

            waited += Time.deltaTime;
            yield return null;
        }

        // 안전망은 조용히 돌면 버그를 가린다(09-26 왕관 의식에서 배운 것) — 넘어갈 때 반드시 남긴다.
        Debug.LogWarning($"[클리어 연출] {timeoutSeconds}초 안에 '{motionName}' 모션이 끝나지 않아 그냥 넘어간다. " +
                         "Tools → 재의 길 → 애니메이션 → 8방향 플레이어 애니메이션 생성 을 실행했는지 확인해라.", this);
    }

    /// <summary>
    /// 추가 생성(2026-09-26, 연출 v2) — 카메라가 비춰도 되는 범위를 구한다. 방에서 가장 큰 그림(= 방 배경)의 테두리다.
    ///
    /// 방 배경은 원래 화면보다 조금 크게 그려져 있다(보스 방: 60×33.75 대 화면 56.5×31.8). 확대한 화면이 원래 화면
    /// 위 끝(15.9)을 조금 넘어가도 빈 곳이 아니라 방 그림(위 끝 17.18)이 보인다. 그 여유 덕에 문으로 걸어 들어가는
    /// 캐릭터의 머리까지 화면에 담긴다. 예전처럼 "원래 화면 안"으로만 막으면 위 끝이 15.9에 묶여 머리가 다시 잘린다.
    ///
    /// 방이 없거나 방에 그림이 하나도 없으면 지금 화면을 범위로 쓴다 — 원래 보이던 곳 밖은 비추지 않는다.
    /// </summary>
    private static Bounds FindViewLimit(Transform room, Camera cam)
    {
        float halfHeight = cam.orthographicSize;
        Bounds limit = new Bounds(cam.transform.position, new Vector3(halfHeight * 2f * cam.aspect, halfHeight * 2f, 0f));
        if (room == null) return limit;

        float largestArea = 0f;
        foreach (SpriteRenderer sprite in room.GetComponentsInChildren<SpriteRenderer>())
        {
            // Renderer.bounds — 유니티가 계산해 주는 월드 기준 테두리 상자(위치·크기·스케일이 다 반영된다).
            // 보스 방에서 가장 큰 것은 배경(Room_raw, 약 2000제곱유닛)이고 소품들은 수백 이하라 헷갈리지 않는다.
            Bounds bounds = sprite.bounds;
            float area = bounds.size.x * bounds.size.y;
            if (area > largestArea)
            {
                largestArea = area;
                limit = bounds;
            }
        }

        return limit;
    }

    /// <summary>
    /// 카메라를 focus 쪽으로 다가가게 한다. 화면 크기(orthographicSize)를 줄이고 중심을 옮긴다.
    ///
    /// <b>중심이 움직일 수 있는 범위를 막는 이유.</b> 방은 한 화면짜리라 카메라가 고정이고,
    /// 보스 방 문은 방 위쪽 가장자리에 있다. 그대로 문을 향해 다가가면 확대한 화면이 방 바깥(빈 곳)을 비춘다.
    /// 수정(2026-09-26, 연출 v2) — 막는 범위를 "원래 화면"에서 "방 그림(limit)"으로 바꿨다(FindViewLimit 주석 참고).
    /// 확대한 화면의 반폭·반높이만큼 안쪽으로 중심을 막으면, 확대한 화면은 늘 limit 안에 들어간다.
    ///
    /// <b>위치를 덮어쓰지 않고 이동량만 더하는 이유.</b> CameraShake는 LateUpdate에서 "지난 흔들림을 빼고
    /// 새 흔들림을 더하는" 상대 방식으로 움직인다. 여기서 절대 위치를 넣으면 흔들림이 두 번 빠져 카메라가 흘러간다.
    /// </summary>
    private IEnumerator ZoomTowards(Camera cam, Vector3 focus, Bounds limit)
    {
        float startSize = cam.orthographicSize;
        float endSize = startSize * zoomRatio;

        Vector3 startCenter = cam.transform.position;

        // z는 그리는 범위(near/far)를 정하는 값이라 건드리지 않는다.
        Vector3 endCenter = new Vector3(
            ClampCenter(focus.x, limit.min.x, limit.max.x, endSize * cam.aspect),
            ClampCenter(focus.y, limit.min.y, limit.max.y, endSize),
            startCenter.z);

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

    /// <summary>
    /// 추가 생성(2026-09-26, 연출 v2) — 반폭 halfExtent인 화면이 [min, max] 밖을 비추지 않도록 중심 한 축을 막는다.
    /// 범위가 화면보다 좁으면 어디에 둬도 밖이 보이므로, 가운데에 둬서 양쪽에 똑같이 나눈다(Mathf.Clamp는 min > max일 때 뜻이 없다).
    /// </summary>
    private static float ClampCenter(float focus, float min, float max, float halfExtent)
    {
        if (max - min <= halfExtent * 2f) return (min + max) * 0.5f;
        return Mathf.Clamp(focus, min + halfExtent, max - halfExtent);
    }
}
