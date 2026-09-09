using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성 — 시간을 멈추는 화면들의 <b>중재자</b>. Time.timeScale을 만지는 곳을 여기 하나로 모은다.
///
/// 왜 필요한가:
/// 예전에는 인벤토리와 보스 열쇠 화면이 각자 <c>Time.timeScale = open ? 0f : 1f</c>를 실행하고,
/// "여는 쪽이 상대를 닫는다"는 규칙으로 충돌을 피했다. 화면이 둘일 때만 성립하는 규칙이다.
/// 일시정지와 설정이 더해져 넷이 되면 서로를 아는 조합이 여섯 개로 늘고, 그중 하나라도
/// 빠뜨리면 <b>화면을 닫았는데 시간이 안 흐르는</b> 버그가 난다. 그 버그는 에러도 로그도 안 남긴다.
///
/// 어떻게 푸는가:
/// 열린 화면을 스택으로 세고, <b>스택이 빌 때만</b> 시간을 되돌린다. 화면끼리는 서로를 몰라도 되고,
/// 화면 위에 화면을 겹쳐 열어도(일시정지 → 설정) 시간이 한 프레임도 안 흐른다.
///
/// static인데 안전한 이유는 <see cref="GameFlow"/>와 다르다. 저쪽은 상태가 없어서 안전하고,
/// 이쪽은 상태가 있지만 <b>씬 안의 살아 있는 화면만 담고</b> 씬이 바뀌면 각 화면이 스스로 빠진다.
/// 그래도 남을 수 있는 경우를 대비해 <see cref="CloseAll"/>과 부팅 시 초기화를 둔다.
/// </summary>
public static class PauseGate
{
    // 열려 있는 화면들. 마지막 원소가 맨 위(가장 나중에 열린 화면)다.
    // List로 쓰는 이유: 스택 한가운데 있는 화면이 먼저 닫히는 경우(씬 정리 순서 등)를
    // Stack<T>로는 처리할 수 없다. 개수가 많아야 서넛이라 검색 비용은 문제되지 않는다.
    private static readonly List<MonoBehaviour> openScreens = new List<MonoBehaviour>();

    /// <summary>지금 시간이 멈춰 있는가. 열린 화면이 하나라도 있으면 참이다.</summary>
    public static bool IsPaused => openScreens.Count > 0;

    /// <summary>열려 있는 화면 수. 디버그용.</summary>
    public static int OpenCount => openScreens.Count;

    /// <summary>멈춤 상태가 바뀔 때 알린다. 나중에 사운드를 줄이거나 할 때 쓴다.</summary>
    public static event Action<bool> PausedChanged;

    // 추가 생성 — 히트스톱이 끝나는 <b>실시간</b> 시각. 0이면 히트스톱 중이 아니다.
    //
    // 남은 시간을 매 프레임 깎지 않고 끝나는 시각을 들고 있는 이유: 히트스톱 중에는
    // Time.deltaTime이 0이라 깎을 수가 없다. Time.unscaledTime은 timeScale과 무관하게 흐른다.
    private static float hitStopEndsAt;

    /// <summary>지금 히트스톱 중인가.</summary>
    public static bool IsHitStopped => hitStopEndsAt > Time.unscaledTime;

    /// <summary>히트스톱이 끝나기까지 남은 실시간(초). 디버그 패널에서도 쓴다.</summary>
    public static float HitStopRemaining => Mathf.Max(0f, hitStopEndsAt - Time.unscaledTime);

    /// <summary>
    /// 추가 생성 — 타격이 꽂히는 순간 아주 짧게 시간을 멈춘다.
    ///
    /// <b>왜 여기에 두는가.</b> <see cref="Apply"/>가 <c>Time.timeScale</c>을 하드 설정한다.
    /// 히트스톱이 밖에서 timeScale을 만지면 <b>화면을 여닫는 순간 지워진다</b> — 인벤토리를
    /// 열었다 닫으면 히트스톱이 통째로 날아가고, 반대로 히트스톱이 끝나면서 1로 되돌리면
    /// 열려 있는 인벤토리 위로 시간이 흐른다. timeScale의 주인은 하나여야 한다.
    ///
    /// <b>화면이 열려 있으면 무시한다.</b> 멈춘 화면 위에서 히트스톱이 끝나면 timeScale이
    /// 1로 돌아가 게임이 인벤토리 뒤에서 움직인다. 2026-09-04 소프트락의 정반대 사고다.
    ///
    /// <b>겹치면 더 긴 쪽을 남긴다.</b> 더하면 여러 적을 한 번에 때렸을 때 시간이 눈덩이처럼
    /// 불어난다. <see cref="CameraShake.Shake"/>가 "겹치면 더 센 쪽"으로 같은 판단을 해뒀다.
    /// </summary>
    /// <param name="seconds">멈출 실시간(초). 0 이하면 아무 일도 안 한다.</param>
    public static void HitStop(float seconds)
    {
        if (seconds <= 0f) return;
        if (IsPaused) return;

        float endsAt = Time.unscaledTime + seconds;
        if (endsAt <= hitStopEndsAt) return;

        bool wasHitStopped = IsHitStopped;
        hitStopEndsAt = endsAt;
        ApplyTimeScale();

        // 이미 돌고 있으면 새로 시작하지 않는다. 위에서 끝나는 시각만 늘렸으므로
        // 도는 중인 코루틴이 늘어난 만큼 더 기다린다.
        if (!wasHitStopped) HitStopRunner.Begin();
    }

    /// <summary>
    /// 추가 생성 — 히트스톱을 실시간으로 재는 러너.
    ///
    /// <b>왜 코루틴인가.</b> 남은 시간을 직접 누적하는 대신 유니티가 이미 가진
    /// <see cref="WaitForSecondsRealtime"/>을 쓴다. timeScale이 0이어도 실시간으로 깨어난다.
    ///
    /// <b>왜 숨은 오브젝트인가.</b> <see cref="PauseGate"/>는 static이라 코루틴을 못 돌린다.
    /// 씬에 컴포넌트를 두면 씬마다 배선해야 하고 빠뜨리면 히트스톱만 조용히 안 걸린다.
    /// 조사용 도구들이 쓰는 <c>RuntimeInitializeOnLoadMethod</c> 방식과 같은 이유로,
    /// 실행 시점에 스스로 붙고 씬을 넘어가도 살아남는다.
    /// </summary>
    private sealed class HitStopRunner : MonoBehaviour
    {
        private static HitStopRunner instance;
        private bool running;

        public static void Begin()
        {
            if (instance == null)
            {
                var host = new GameObject("[히트스톱 러너]");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<HitStopRunner>();
            }

            if (!instance.running) instance.StartCoroutine(instance.Wait());
        }

        private IEnumerator Wait()
        {
            running = true;

            // 자는 동안 더 긴 히트스톱이 들어오면 남은 시간이 늘어난다.
            // 그래서 한 번 자고 끝인지 다시 본다.
            while (HitStopRemaining > 0f)
                yield return new WaitForSecondsRealtime(HitStopRemaining);

            hitStopEndsAt = 0f;
            running = false;
            ApplyTimeScale();
        }
    }

    /// <summary>
    /// 화면을 연다. 이미 들어 있으면 아무 일도 하지 않는다.
    ///
    /// 중복을 무시하는 이유: 같은 화면이 두 번 들어가면 닫을 때 한 번만 빠져서
    /// 스택이 영영 안 비고, 게임이 멈춘 채로 남는다.
    /// </summary>
    public static void Open(MonoBehaviour screen)
    {
        if (screen == null) return;
        if (openScreens.Contains(screen)) return;

        bool wasPaused = IsPaused;
        openScreens.Add(screen);
        Apply(wasPaused);
    }

    /// <summary>화면을 닫는다. 스택이 비면 시간이 다시 흐른다.</summary>
    public static void Close(MonoBehaviour screen)
    {
        if (screen == null) return;

        bool wasPaused = IsPaused;
        if (!openScreens.Remove(screen)) return;
        Apply(wasPaused);
    }

    /// <summary>이 화면이 지금 맨 위에 있는가. ESC를 누구가 처리할지 정할 때 쓴다.</summary>
    public static bool IsTop(MonoBehaviour screen)
    {
        if (screen == null || openScreens.Count == 0) return false;
        return openScreens[openScreens.Count - 1] == screen;
    }

    /// <summary>이 화면이 열려 있는가.</summary>
    public static bool IsOpen(MonoBehaviour screen)
        => screen != null && openScreens.Contains(screen);

    /// <summary>
    /// 전부 닫고 시간을 되돌린다. <b>씬을 넘기기 직전에 부른다.</b>
    ///
    /// 씬을 로드하면 화면 오브젝트가 파괴되면서 각자 OnDisable에서 빠지긴 한다. 하지만
    /// 파괴 순서는 보장되지 않고, 로드가 시작된 뒤에 timeScale이 0인 프레임이 끼면
    /// 새 씬이 멈춘 채로 시작한다. 넘기는 쪽에서 먼저 정리하는 편이 확실하다.
    /// </summary>
    public static void CloseAll()
    {
        if (openScreens.Count == 0) return;

        openScreens.Clear();

        // 추가 생성 — 히트스톱도 같이 끈다. 씬을 넘기는 순간 남아 있으면
        // ApplyTimeScale이 0을 유지해서 새 씬이 멈춘 채로 시작한다.
        hitStopEndsAt = 0f;

        Apply(true);
    }

    /// <summary>스택 상태를 실제 timeScale에 반영한다.</summary>
    private static void Apply(bool wasPaused)
    {
        bool paused = IsPaused;
        ApplyTimeScale();

        // 히트스톱은 이 알림을 안 쏜다. 이건 "화면이 떠서 게임이 멈췄다"를 알리는 신호라
        // 사운드를 줄이거나 하는 쪽이 듣는다. 0.05초짜리 타격감까지 여기 실으면
        // 때릴 때마다 소리가 끊긴다.
        if (paused != wasPaused) PausedChanged?.Invoke(paused);
    }

    /// <summary>
    /// 추가 생성 — 화면 스택과 히트스톱을 합쳐 실제 timeScale을 정한다.
    ///
    /// <b>여기가 이 프로젝트에서 Time.timeScale을 쓰는 유일한 자리다.</b>
    /// 둘 중 하나라도 걸려 있으면 멈춘다.
    /// </summary>
    private static void ApplyTimeScale()
    {
        Time.timeScale = (IsPaused || IsHitStopped) ? 0f : 1f;
    }

    /// <summary>
    /// 플레이 모드를 다시 시작할 때 정적 상태를 비운다.
    ///
    /// 에디터의 "Enter Play Mode Options"로 도메인 리로드를 꺼두면 static 필드가
    /// 이전 실행의 값을 그대로 들고 시작한다. 그러면 이전 판에서 열려 있던 화면이
    /// 스택에 유령으로 남아 <b>시작하자마자 시간이 멈춘 게임</b>이 된다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        openScreens.Clear();
        PausedChanged = null;

        // 추가 생성 — 히트스톱도 비운다. 도메인 리로드를 꺼두면 지난 판에서 멈춰 있던
        // 시각이 그대로 남아 시작하자마자 멈춘 게임이 된다.
        hitStopEndsAt = 0f;

        Time.timeScale = 1f;
    }
}
