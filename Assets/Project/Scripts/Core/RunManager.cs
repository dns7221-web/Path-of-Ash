using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 한 판(런)의 수명과 상태를 소유한다. Game 씬에만 존재한다.
///
/// 이 프로젝트에서 가장 중요한 설계가 여기 있다 — <b>런의 상태를 이 컴포넌트의 수명에 묶는다.</b>
/// 재시작은 "값을 0으로 되돌리는 것"이 아니라 Game 씬을 다시 로드하는 것이고, 그러면 이
/// 오브젝트가 통째로 파괴되고 새로 생긴다. 초기화해야 할 변수를 하나 빠뜨리는 실수가
/// 애초에 불가능해진다.
///
/// 그래서 여기에는 static 필드도 DontDestroyOnLoad도 없다. 씬 밖으로 나가야 하는 값은
/// RunResultData 에셋 하나뿐이고, 그것도 판 시작 시점에 Clear()로 비운다.
/// </summary>
[DisallowMultipleComponent]
public class RunManager : MonoBehaviour
{
    /// <summary>한 판 안에서의 상태. 일시정지는 나중에 여기 추가한다.</summary>
    public enum RunState
    {
        Playing,  // 조작이 먹히는 정상 상태
        GameOver, // 죽었고 결과 화면으로 넘어가는 중. 입력을 더 받으면 안 된다
    }

    [Header("참조")]
    [Tooltip("결과를 기록할 ScriptableObject 에셋. Assets/Project/Data 에 만들어 넣는다.")]
    [SerializeField] private RunResultData result;

    [Header("연출")]
    [Tooltip("죽고 나서 결과 화면으로 넘어가기까지의 시간(초). 사망 연출이 들어갈 자리다.")]
    [SerializeField] private float resultDelaySeconds = 0.6f;

    [Header("임시 — 플레이어가 생기면 지운다")]
    [Tooltip("사망을 강제로 발생시키는 키. 아직 적도 체력도 없어서 죽을 방법이 없다.")]
    [SerializeField] private Key debugDeathKey = Key.K;

    // 판이 시작된 시각. Time.time은 timeScale의 영향을 받으므로, 나중에 일시정지나
    // 히트스톱으로 timeScale을 0으로 만들면 생존 시간도 같이 멈춘다. 그게 맞는 동작이다.
    private float runStartTime;

    private RunState state;

    /// <summary>지금까지 버틴 시간(초). 판이 끝난 뒤에는 끝난 시점의 값에서 멈춘다.</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>이번 판에서 처치한 적 수.</summary>
    public int KillCount { get; private set; }

    /// <summary>현재 판 상태. 다른 시스템이 "지금 조작을 받아도 되는지" 판단할 때 읽는다.</summary>
    public RunState State => state;

    private void Awake()
    {
        // 결과 에셋을 먼저 비운다. 에디터에서는 이전 판의 값이 그대로 남아 있기 때문에,
        // 이걸 빠뜨리면 새 판을 시작하자마자 지난 판 기록을 들고 시작하는 셈이 된다.
        if (result != null)
        {
            result.Clear();
        }
        else
        {
            Debug.LogError("[RunManager] RunResultData가 비어 있다. 인스펙터에서 에셋을 연결해라.", this);
        }
    }

    private void Start()
    {
        runStartTime = Time.time;
        state = RunState.Playing;
        KillCount = 0;
        ElapsedSeconds = 0f;
    }

    private void Update()
    {
        // 죽은 뒤에는 시간도 멈추고 입력도 안 받는다.
        if (state != RunState.Playing) return;

        ElapsedSeconds = Time.time - runStartTime;

        // 임시: 적과 체력이 생기기 전까지 사망을 확인할 방법이 이것뿐이다.
        // 키보드가 없는 환경(패드만 연결)에서 Keyboard.current가 null일 수 있어 먼저 확인한다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[debugDeathKey].wasPressedThisFrame)
        {
            EndRun(false);
        }
    }

    /// <summary>적을 처치했을 때 호출한다. 나중에 적의 사망 처리에서 부른다.</summary>
    public void AddKill()
    {
        if (state != RunState.Playing) return;
        KillCount++;
    }

    /// <summary>
    /// 판을 끝낸다. 죽었으면 isCleared가 false, 최종 보스를 잡았으면 true.
    /// 두 번 호출되어도 한 번만 처리된다 — 여러 개의 데미지가 같은 프레임에 들어와
    /// 사망이 중복으로 발생하는 경우를 막는다.
    /// </summary>
    public void EndRun(bool isCleared)
    {
        if (state == RunState.GameOver) return;

        state = RunState.GameOver;
        ElapsedSeconds = Time.time - runStartTime;

        if (result != null)
            result.Record(ElapsedSeconds, KillCount, 1, isCleared);

        StartCoroutine(GoToResultAfterDelay());
    }

    /// <summary>
    /// 잠깐 기다렸다가 결과 화면으로 넘어간다.
    ///
    /// WaitForSeconds가 아니라 Realtime을 쓰는 이유: 사망 순간에 히트스톱으로 timeScale을
    /// 0으로 만들 예정인데, 그러면 WaitForSeconds는 영원히 안 끝난다. 화면 전환은 게임 시간이
    /// 아니라 실제 시간으로 세야 한다.
    /// </summary>
    private IEnumerator GoToResultAfterDelay()
    {
        yield return new WaitForSecondsRealtime(resultDelaySeconds);
        GameFlow.LoadResult();
    }
}
