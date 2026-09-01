using System;
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
        Apply(true);
    }

    /// <summary>스택 상태를 실제 timeScale에 반영한다.</summary>
    private static void Apply(bool wasPaused)
    {
        bool paused = IsPaused;

        // 여기가 이 프로젝트에서 Time.timeScale을 쓰는 유일한 자리다.
        Time.timeScale = paused ? 0f : 1f;

        if (paused != wasPaused) PausedChanged?.Invoke(paused);
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
        Time.timeScale = 1f;
    }
}
