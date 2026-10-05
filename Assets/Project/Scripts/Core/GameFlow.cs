using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 전환만 담당한다. 어디서 어디로 갈 수 있는지를 한 곳에 모아둔 곳.
///
/// static인데도 안전한 이유: 이 클래스는 <b>아무 상태도 기억하지 않는다.</b> 씬 이름 상수와
/// 전환 함수만 있다. 전역 상태를 두지 않는 게 이 프로젝트의 핵심 규칙이고(죽으면 완전 초기화),
/// 기억하는 게 없는 static은 그 규칙을 어기지 않는다. 한 판의 상태는 RunManager가 들고 있고
/// 그건 씬과 함께 파괴된다.
///
/// 씬을 빌드 인덱스(숫자)가 아니라 이름으로 로드하는 이유: 빌드 설정에서 씬 순서를 바꾸면
/// 인덱스는 조용히 다른 씬을 가리키게 된다. 이름은 바뀌면 바로 에러로 드러난다.
/// </summary>
public static class GameFlow
{
    public const string TitleScene = "Title";
    public const string GameScene = "Game";
    public const string ResultScene = "Result";

    /// <summary>타이틀 화면으로 돌아간다.</summary>
    public static void LoadTitle()
    {
        SceneManager.LoadScene(TitleScene);
    }

    /// <summary>
    /// 새 판을 시작한다. Game 씬을 다시 로드하므로 이전 판의 오브젝트는 전부 파괴된다.
    /// "재시작"을 위한 별도 초기화 함수가 없는 게 의도다 — 씬 로드가 곧 초기화다.
    /// </summary>
    public static void StartNewRun()
    {
        // 추가 생성(2026-10-05, 이어하기) — 새 판을 시작하면 저장한 판은 끝이다. 타이틀의 [처음부터]와 결과 화면의 재시작이 모두 여기를 지난다.
        // 지우지 않으면 새 판의 튜토리얼 도중에 끄고 다시 켰을 때 "옛 판 이어하기"가 뜬다.
        continueRequested = false;
        RunSaveStore.Delete();
        SceneManager.LoadScene(GameScene);
    }

    // 추가 생성(2026-10-05, 이어하기) — 타이틀에서 [이어하기]를 눌렀다는 표시. Game 씬의 RoomSequenceController.Start가 한 번 꺼내 읽는다.
    //
    // static으로 둔 이유: 씬을 넘어 전해야 하는 값이 bool 하나뿐이다. 저장 내용 자체는 파일에 있으니 여기엔 "이어할 것인가"만 둔다.
    // RunManager가 static을 안 두는 원칙(판의 상태는 씬 수명에 묶는다)과 부딪히지 않는다 — 이건 판의 상태가 아니라 씬 사이의 전달이다.
    private static bool continueRequested;

    // 도메인 리로드를 끈 에디터 설정(Enter Play Mode Options)에서는 static 값이 다음 플레이까지 남는다.
    // Health가 static 이벤트를 비우는 것과 같은 방식으로 플레이를 시작할 때마다 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        continueRequested = false;
    }

    /// <summary>
    /// 추가 생성(2026-10-05, 이어하기) — 저장한 판을 이어서 시작한다. Game 씬을 새로 로드하는 것은 새 판과 똑같고(씬 로드가 곧 초기화),
    /// 깨끗해진 씬 위에 RoomSequenceController가 저장 내용을 덮어씌운다.
    /// </summary>
    public static void ContinueRun()
    {
        continueRequested = true;
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>추가 생성(2026-10-05, 이어하기) — 이어하기 요청을 꺼내고 지운다. 한 번만 true가 나온다.</summary>
    public static bool ConsumeContinueRequest()
    {
        bool requested = continueRequested;
        continueRequested = false;
        return requested;
    }

    /// <summary>결과 화면으로 넘어간다. 결과 값은 RunResultData 에셋에 이미 기록돼 있어야 한다.</summary>
    public static void LoadResult()
    {
        SceneManager.LoadScene(ResultScene);
    }

    /// <summary>게임을 종료한다. 에디터에서는 Application.Quit이 아무 일도 안 하므로 따로 처리한다.</summary>
    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
