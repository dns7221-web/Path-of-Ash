#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — `F7`/`F8`/`F9`로 게임 시간을 늦추고, 멈추고, 한 프레임씩 넘기는 조사용 도구.
///
/// 왜 필요한가: 이 프로젝트에서 반복해서 나온 버그는 전부 <b>"몇 초에 무엇이 보이는가"</b>였다.
/// 클립 길이와 코드의 동작 시간이 어긋나고(애니메이션 타이밍), 이펙트가 캐릭터를 덮고(궁극기),
/// 전환 연출의 마지막 프레임을 아무도 못 보고(2페이즈). 공통점은 <b>0.1초 단위로 벌어지는 일이라
/// 실시간으로는 보이지 않는다</b>는 것이다.
///
/// 지금까지 그걸 찾은 방법은 플레이를 녹화해서 ffmpeg으로 프레임을 뜯어보는 것이었다.
/// 정확하지만 <b>고치고 다시 확인하는 데 한 판이 통째로 든다.</b> 늦춰서 그 자리에서 보면
/// 그 왕복이 사라진다.
///
/// <b>파일 전체를 <c>#if UNITY_EDITOR</c>로 감쌌다.</b> <see cref="BossKeyDebugGrant"/>와 같은 이유다 —
/// 인스펙터 체크박스로 끄는 방식은 켜둔 채로 빌드하는 실수가 언젠가 반드시 나온다.
///
/// 씬에 손대지 않고 실행 시점에 스스로 붙는다. 조사용 오브젝트를 씬에 넣으면 나중에 그걸
/// 빼는 것을 잊고 그대로 커밋된다.
///
/// <b>PauseGate와 부딪히지 않게 만드는 것이 이 도구에서 제일 조심한 부분이다.</b>
/// <see cref="PauseGate"/>의 주석은 "여기가 이 프로젝트에서 Time.timeScale을 쓰는 유일한 자리"라고
/// 못 박고 있고, 실제로 화면을 닫을 때 <c>Time.timeScale = 1f</c>로 <b>하드 설정</b>한다.
/// 그래서 이 도구가 배속을 걸어둔 채 인벤토리를 열었다 닫으면 배속이 조용히 풀린다.
///
/// 해결은 "이벤트로 한 번 되돌리기"가 아니라 <b>활성일 때 매 프레임 다시 먹이는 것</b>이다.
/// 이벤트 구독은 순서에 기대게 되고, 누가 timeScale을 또 만지면 다시 어긋난다. 매 프레임
/// 쓰면 무엇이 먼저 돌든 다음 프레임에 반드시 수렴한다.
///
/// 그리고 그 계산에 PauseGate의 상태를 <b>그대로 존중</b>한다 — 화면이 열려 있으면 0,
/// 아니면 내 배속. 즉 이 도구는 PauseGate를 이기는 게 아니라 그 위에 얹힌다.
///
/// <b>도구가 꺼져 있으면(1배속·정지 아님) timeScale을 아예 쓰지 않는다.</b> 안 그러면 조사용
/// 도구가 평소 실행에까지 매 프레임 개입하게 되고, 나중에 시간 관련 버그가 났을 때 용의자가
/// 하나 늘어난다.
///
/// <b>Time.fixedDeltaTime은 일부러 안 건드린다.</b> 느린 화면에서 물리를 부드럽게 하려면 같이
/// 줄이는 것이 정석이지만, 그 값은 플레이 모드를 나가도 에디터 세션에 남는 <b>전역 상태</b>다.
/// 되돌리기를 한 번 놓치면 그 뒤 모든 실행의 물리가 이상해지고, 원인을 찾기 아주 어렵다.
/// 이 도구가 보려는 것은 스프라이트 애니메이션과 이펙트 타이밍(전부 Update 기반)이라
/// 물리 부드러움은 목적이 아니다. 위험을 살 이유가 없다.
/// </summary>
[DisallowMultipleComponent]
public class TimeScaleDebugControl : MonoBehaviour
{
    private const Key CycleKey = Key.F7;
    private const Key PauseKey = Key.F8;
    private const Key StepKey = Key.F9;

    /// <summary>
    /// 순환할 배속. 1이 먼저인 이유는 <b>끄는 것이 가장 자주 하는 동작</b>이기 때문이다 —
    /// 느리게 보고 나면 항상 정상 속도로 돌아온다.
    ///
    /// 0.1까지 둔 이유: 10fps 클립(궁극기가 그렇다)의 프레임 하나가 0.1초다. 0.1배속이면
    /// 그 한 프레임이 실시간 1초가 되어 <b>눈으로 셀 수 있다.</b>
    /// </summary>
    private static readonly float[] Scales = { 1f, 0.5f, 0.25f, 0.1f };

    private int scaleIndex;
    private bool paused;

    /// <summary>이번 프레임만 시간을 흘려보낸다(F9). 다음 프레임에 다시 멈춘다.</summary>
    private bool stepRequested;

    /// <summary>
    /// 오버레이 스타일. 한 번 만들어 두고 재사용한다.
    ///
    /// 왜 캐시하는가: <see cref="OnGUI"/>는 프레임당 <b>여러 번</b> 불린다(Layout과 Repaint가
    /// 따로 온다). 거기서 GUIStyle과 RectOffset을 새로 만들면 조사용 도구가 오히려 GC를
    /// 만들어내고, 그러면 <b>렉을 조사하려고 켠 도구가 렉의 원인이 된다.</b>
    ///
    /// GUI.skin은 OnGUI 밖에서 읽으면 안 되므로 생성자나 Awake가 아니라 첫 호출 때 만든다.
    /// </summary>
    private GUIStyle overlayStyle;

    private GUIStyle OverlayStyle
    {
        get
        {
            if (overlayStyle != null) return overlayStyle;

            overlayStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 6, 6),
            };
            overlayStyle.normal.textColor = Color.white;

            return overlayStyle;
        }
    }

    /// <summary>도구가 timeScale을 잡고 있는가. 꺼져 있으면 아무것도 쓰지 않는다.</summary>
    private bool Active => paused || Scales[scaleIndex] != 1f;

    private float TargetScale => Scales[scaleIndex];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[TimeScaleDebugControl]");
        go.AddComponent<TimeScaleDebugControl>();
        DontDestroyOnLoad(go);

        Debug.Log("[조사용] 시간 제어 — <b>F7</b> 배속 순환(1 → 0.5 → 0.25 → 0.1), " +
                  "<b>F8</b> 멈춤/재개, <b>F9</b> 한 프레임 진행(멈춘 상태에서).");
    }

    private void Update()
    {
        ReadKeys();
        ApplyTimeScale();
    }

    /// <summary>
    /// 키 입력을 읽는다.
    ///
    /// <b>멈춘 상태에서도 이 함수가 도는 것이 이 설계의 핵심이다.</b>
    /// 유니티 내장 정지(<c>EditorApplication.isPaused</c>)를 쓰지 않은 이유가 여기 있다 —
    /// 그쪽은 플레이어 루프 자체를 세우기 때문에 <b>Update가 안 돌고, 그러면 풀어줄 키를
    /// 읽을 방법이 없다.</b> 에디터 툴바 버튼으로만 풀 수 있게 된다.
    ///
    /// <c>Time.timeScale = 0</c>은 다르다. Update는 계속 돌고 애니메이션·코루틴·물리만 선다.
    /// 손을 이동 키에 둔 채로 멈추고 넘길 수 있어야 조사 도구로 쓸모가 있다.
    /// (툴바의 Pause/Step이 필요 없다는 뜻은 아니다. 그쪽은 에디터 전체를 세우므로
    ///  인스펙터 값을 뜯어볼 때 쓰면 된다. 두 도구의 용도가 다르다.)
    /// </summary>
    private void ReadKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[CycleKey].wasPressedThisFrame)
        {
            scaleIndex = (scaleIndex + 1) % Scales.Length;
            Debug.Log($"[조사용] 배속 {TargetScale:0.##}x" +
                      (TargetScale == 1f ? " (원래 속도)" : ""));
        }

        if (keyboard[PauseKey].wasPressedThisFrame)
        {
            paused = !paused;
            stepRequested = false;
            Debug.Log(paused
                ? "[조사용] 시간 <b>멈춤</b> (F8 재개, F9 한 프레임)"
                : "[조사용] 시간 재개");
        }

        // 멈춘 상태에서만 뜻이 있다. 흐르는 중에 눌러도 티가 안 나서 오해를 부른다.
        if (paused && keyboard[StepKey].wasPressedThisFrame) stepRequested = true;
    }

    /// <summary>
    /// 계산한 배속을 실제로 먹인다.
    ///
    /// PauseGate가 열려 있으면 <b>무조건 0</b>이다. 이 도구는 그 판단을 뒤집지 않는다 —
    /// 인벤토리를 열어둔 채 시간이 흐르면 그건 이 도구의 버그가 아니라 게임의 버그가 된다.
    /// </summary>
    private void ApplyTimeScale()
    {
        if (!Active)
        {
            // 방금 꺼졌다면 PauseGate의 판단으로 한 번 되돌려주고 손을 뗀다.
            // 매 프레임 쓰지 않는 이유는 클래스 주석에 적었다.
            if (!Mathf.Approximately(Time.timeScale, PauseGate.IsPaused ? 0f : 1f))
                Time.timeScale = PauseGate.IsPaused ? 0f : 1f;

            return;
        }

        if (PauseGate.IsPaused)
        {
            Time.timeScale = 0f;
            return;
        }

        if (paused)
        {
            // F9로 요청이 들어온 프레임만 시간을 흘린다. 딱 한 프레임이 지나가고
            // 다음 프레임에는 요청이 꺼져 있으므로 다시 0이 된다.
            Time.timeScale = stepRequested ? TargetScale : 0f;
            stepRequested = false;
            return;
        }

        Time.timeScale = TargetScale;
    }

    /// <summary>
    /// 도구가 붙어 있는 채로 실행이 끝나거나 오브젝트가 사라질 때 시간을 되돌린다.
    ///
    /// 이게 없으면 0.1배속이나 멈춤 상태로 플레이 모드를 빠져나갔다가 다시 들어왔을 때
    /// <b>"게임이 안 움직인다"로 보인다.</b> 원인이 조사용 도구라는 것을 떠올리기까지가 멀다.
    /// </summary>
    private void OnDisable()
    {
        Time.timeScale = PauseGate.IsPaused ? 0f : 1f;
    }

    /// <summary>
    /// 화면 왼쪽 위에 현재 상태를 적는다.
    ///
    /// <b>도구가 켜져 있을 때만 그린다.</b> 평소에도 떠 있으면 화면을 가리고, 무엇보다
    /// 스크린샷이나 녹화에 조사용 글자가 섞여 들어간다.
    ///
    /// OnGUI를 쓴 이유: 캔버스에 붙이면 이 도구가 씬 구조에 개입하게 되고, HUD 빌더가
    /// 다시 돌 때 정리 대상에 걸린다. OnGUI는 그런 흔적을 하나도 안 남긴다.
    /// </summary>
    private void OnGUI()
    {
        if (!Active) return;

        string label = paused
            ? $"■ 멈춤   (F8 재개 / F9 한 프레임 · {TargetScale:0.##}x)"
            : $"▶ {TargetScale:0.##}x   (F7 배속 / F8 멈춤)";

        GUI.Label(new Rect(10f, 10f, 320f, 30f), label, OverlayStyle);
    }
}
#endif
