using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 한 판(런)의 결과를 씬 너머로 전달하는 통로.
///
/// 결과는 Game 씬에서 만들어지고 Result 씬에서 읽힌다. 씬이 바뀌면 그 안의 오브젝트는 전부
/// 파괴되므로 값을 넘길 무언가가 필요한데, 여기서 ScriptableObject를 고른 이유는 이렇다.
///
/// - static 필드 / DontDestroyOnLoad 싱글톤은 전역 상태를 만든다. 초기화를 한 군데라도
///   빠뜨리면 다음 판에 이전 값이 새어나오고, 그 버그는 보통 세 번째 판에서야 재현된다.
///   "죽으면 완전 초기화"가 이 게임의 설계라서 특히 위험하다.
/// - ScriptableObject는 유니티가 원래 이 용도로 제공하는 것이고, 에셋이라 인스펙터에서
///   값이 눈에 보인다. 디버깅할 때 Play 중에 값이 어떻게 변하는지 그냥 보인다.
///
/// 주의 — 에디터에서는 Play를 끝내도 여기 적힌 값이 그대로 남는다(에셋이라서). 빌드에서는
/// 원본 에셋 값으로 시작하므로 동작이 달라진다. 그래서 <see cref="Clear"/>를 판 시작 시점에
/// 반드시 부른다. "에디터에선 되는데 빌드에선 이상하다"의 흔한 원인이 이 차이다.
/// </summary>
[CreateAssetMenu(fileName = "RunResultData", menuName = "재의 길/런 결과 데이터")]
public class RunResultData : ScriptableObject
{
    [Header("이 값들은 실행 중에 RunManager가 채운다. 손으로 넣을 필요 없다.")]
    [SerializeField] private float survivedSeconds;
    [SerializeField] private int killCount;

    // 수정(무한 방 진행 도입): floorReached → roomsEntered로 이름을 바꿨다.
    // "층"은 층 개념이 없던 시절의 임시 이름이었고, 실제로 세는 건 이번 판에 들어간 방 수다.
    //
    // FormerlySerializedAs를 붙인 이유: 이름만 바꾸면 유니티는 이걸 "옛 필드 삭제 + 새 필드 추가"로
    // 보고 기존 에셋에 저장된 값을 버린다. 이 속성이 있으면 옛 이름으로 저장된 값을 새 필드로
    // 그대로 읽어온다. 유니티가 이 상황을 위해 제공하는 기능이라 직접 마이그레이션 코드를 짜지 않는다.
    [FormerlySerializedAs("floorReached")]
    [SerializeField] private int roomsEntered;

    [SerializeField] private bool cleared;

    /// <summary>이번 판에서 버틴 시간(초).</summary>
    public float SurvivedSeconds => survivedSeconds;

    /// <summary>처치한 적 수.</summary>
    public int KillCount => killCount;

    /// <summary>
    /// 이번 판에 들어간 방 수. 첫 방에 들어간 시점이 1이다.
    /// 방을 무한 반복하므로 "몇 번째 방까지 버텼는가"가 이 게임의 진행도 지표가 된다.
    /// </summary>
    public int RoomsEntered => roomsEntered;

    /// <summary>죽어서 끝났는지, 클리어해서 끝났는지.</summary>
    public bool Cleared => cleared;

    /// <summary>
    /// 판이 시작될 때 호출한다. 이전 판의 값이 남아 있으면 결과 화면에 그게 그대로 뜬다.
    /// </summary>
    public void Clear()
    {
        survivedSeconds = 0f;
        killCount = 0;
        roomsEntered = 1;
        cleared = false;
    }

    /// <summary>
    /// 판이 끝날 때 호출한다. 값을 하나씩 대입하지 않고 한 번에 받는 이유는, 결과가 반쯤만
    /// 기록된 상태를 만들지 않기 위해서다.
    /// </summary>
    public void Record(float survived, int kills, int rooms, bool isCleared)
    {
        survivedSeconds = survived;
        killCount = kills;
        roomsEntered = rooms;
        cleared = isCleared;
    }

    /// <summary>생존 시간을 "1분 23.4초" 형태로 만든다. 화면에 그대로 쓸 문자열.</summary>
    public string FormatSurvivedTime()
    {
        int minutes = Mathf.FloorToInt(survivedSeconds / 60f);
        float seconds = survivedSeconds - minutes * 60f;

        if (minutes > 0)
            return $"{minutes}분 {seconds:F1}초";

        return $"{seconds:F1}초";
    }
}
