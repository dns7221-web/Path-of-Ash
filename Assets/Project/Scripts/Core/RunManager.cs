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
    // 수정(2026-09-13, 새 사망 모션) — 0.6 → 1.4.
    //
    // 사망 클립이 6프레임 / 8fps = 0.75초이고, 결정타가 히트박스면 그 앞에 히트스톱 0.1초가 붙는다.
    // 0.6이면 씬이 넘어갈 때 클립이 0.5초 지점이라 쓰러지는 4번 프레임까지만 보이고,
    // 몸이 재로 타들어가는 5번과 잿더미만 남는 6번은 한 번도 화면에 나오지 않았다.
    // 1.4면 잿더미(6번)가 약 0.73초에 나오고 0.67초 동안 보인 뒤 결과 화면으로 넘어간다.
    //
    // 이 값은 Game.unity에 직렬화되어 있어서 기본값만 고치면 게임에 반영되지 않는다. 씬 값도 같이 고쳤다.
    [Tooltip("죽고 나서 결과 화면으로 넘어가기까지의 시간(초). 사망 클립 0.75초 + 히트스톱 + 잿더미를 보여줄 여유.")]
    [SerializeField] private float resultDelaySeconds = 1.4f;

#if UNITY_EDITOR
    // 수정(빌드 유출): 조사용 사망 키를 에디터에서만 컴파일되게 감쌌다.
    //
    // 예전에는 이 필드와 아래 Update의 입력 확인이 그대로 빌드에 들어가서, 완성된 게임에서
    // K를 누르면 즉사했다. 인스펙터 체크박스로 끄는 방식은 <b>켜둔 채로 빌드하는 실수</b>가
    // 언젠가 반드시 나오므로, BossKeyDebugGrant와 같이 아예 컴파일에서 빼는 쪽을 택했다.
    //
    // 직렬화 필드가 조건부가 되면 빌드에서 이 값은 저장되지 않지만, 빌드에는 쓰는 곳도
    // 없으므로 상관없다.
    [Header("조사용 — 에디터 전용")]
    [Tooltip("사망을 강제로 발생시키는 키. 에디터에서만 동작하며 빌드에는 포함되지 않는다.")]
    [SerializeField] private Key debugDeathKey = Key.K;
#endif

    // 판이 시작된 시각. Time.time은 timeScale의 영향을 받으므로, 나중에 일시정지나
    // 히트스톱으로 timeScale을 0으로 만들면 생존 시간도 같이 멈춘다. 그게 맞는 동작이다.
    private float runStartTime;

    private RunState state;

    /// <summary>지금까지 버틴 시간(초). 판이 끝난 뒤에는 끝난 시점의 값에서 멈춘다.</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>이번 판에서 처치한 적 수.</summary>
    public int KillCount { get; private set; }

    /// <summary>
    /// 추가 생성 — 이번 판에 들어간 방 수. 첫 방에 들어간 시점이 1이다.
    ///
    /// RoomSequenceController가 자기 카운터를 들고 있는데도 여기에 또 두는 이유:
    /// <b>판의 기록은 RunManager가 소유한다</b>는 규칙을 깨지 않기 위해서다. 결과를 남기는
    /// 시점(EndRun)에 던전 쪽 오브젝트를 찾아가 값을 물어보게 만들면, 방 시스템이 통째로
    /// 교체될 때 EndRun도 같이 고쳐야 한다. 적 처치 수를 EnemySpawner가 AddKill로
    /// 밀어 넣는 것과 똑같은 방향이다 — 아는 쪽이 알려주고, 기록은 여기 쌓인다.
    /// </summary>
    public int RoomsEntered { get; private set; } = 1;

    /// <summary>현재 판 상태. 다른 시스템이 "지금 조작을 받아도 되는지" 판단할 때 읽는다.</summary>
    public RunState State => state;

    /// <summary>
    /// 추가 생성(2026-09-26, 클리어 연출) — 이번 판이 클리어로 끝났는가. 끝나기 전에는 false다.
    ///
    /// State만으로는 "끝났다"만 알 수 있고 어떻게 끝났는지는 모른다. PlayerController가 "판이 끝나면 죽는다"를
    /// 패배일 때만 하도록 가르는 데 쓴다 — 가르지 않으면 클리어 직후 결과 대기 동안 사망 모션이 나온다.
    /// </summary>
    public bool IsCleared { get; private set; }

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

        // 추가 생성 — 방 진행 시스템이 첫 방을 알려주기 전의 기본값. 방이 없는 씬에서도 1이다.
        RoomsEntered = 1;
    }

    private void Update()
    {
        // 죽은 뒤에는 시간도 멈추고 입력도 안 받는다.
        if (state != RunState.Playing) return;

        ElapsedSeconds = Time.time - runStartTime;

#if UNITY_EDITOR
        // 조사용: 보스전이나 결과 화면을 확인할 때 죽는 과정을 건너뛰기 위해 남겨둔 키다.
        // 키보드가 없는 환경(패드만 연결)에서 Keyboard.current가 null일 수 있어 먼저 확인한다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[debugDeathKey].wasPressedThisFrame)
        {
            EndRun(false);
        }
#endif
    }

    /// <summary>적을 처치했을 때 호출한다. 나중에 적의 사망 처리에서 부른다.</summary>
    public void AddKill()
    {
        if (state != RunState.Playing) return;
        KillCount++;
    }

    /// <summary>
    /// 추가 생성 — 새 방에 들어갔을 때 방 진행 시스템이 호출한다.
    ///
    /// 증가(++)가 아니라 <b>방 번호를 받아서 최댓값만 남기는</b> 이유가 두 가지 있다.
    /// - 방 개수를 세는 주체는 RoomSequenceController다. 양쪽이 각자 세면 언젠가 어긋나고,
    ///   어긋났을 때 어느 쪽이 맞는지 판단할 근거가 없다. 번호를 통째로 받으면 답이 하나다.
    /// - Awake/Start 실행 순서는 오브젝트마다 보장되지 않는다. RunManager.Start가 나중에
    ///   돌아 1로 초기화해도, 이미 더 큰 번호가 들어왔으면 그 값이 살아남는다.
    /// </summary>
    /// <param name="roomNumber">이번 판에서 몇 번째로 들어간 방인지. 첫 방이 1.</param>
    public void ReportRoomEntered(int roomNumber)
    {
        if (state != RunState.Playing) return;
        if (roomNumber > RoomsEntered) RoomsEntered = roomNumber;
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
        IsCleared = isCleared; // 추가 생성(2026-09-26) — 어떻게 끝났는지도 남긴다(IsCleared 주석 참고)
        ElapsedSeconds = Time.time - runStartTime;

        // 수정(무한 방 진행 도입): 층 수를 1로 하드코딩하던 자리에 실제 방 수를 넣는다.
        // 방을 열 개 돌아도 결과 화면에 항상 1이 뜨던 문제를 고친다.
        if (result != null)
            result.Record(ElapsedSeconds, KillCount, RoomsEntered, isCleared);

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
