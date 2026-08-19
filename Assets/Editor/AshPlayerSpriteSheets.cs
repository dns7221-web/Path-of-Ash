using UnityEngine;

/// <summary>
/// 캐릭터(플레이어 / 적) 스프라이트 시트의 규격을 한 곳에 모아둔 정의표.
///
/// 이 파일이 따로 있는 이유: 슬라이스(<see cref="AshPlayerSpriteSlicer"/>)와 애니메이션 클립
/// 생성(<see cref="AshPlayerAnimationBuilder"/>)이 <b>같은 표를 읽어야</b> 하기 때문이다.
/// 두 스크립트가 프레임 개수를 각자 들고 있으면, 시트를 다시 뽑아 프레임이 6개에서 8개로
/// 바뀌었을 때 한쪽만 고치고 다른 쪽을 잊는다. 그러면 슬라이스는 8개인데 클립은 6개만 쓰는,
/// 에러 없이 조용히 틀린 상태가 된다.
///
/// 수정(적 추가 시점): 플레이어 전용이던 것을 <see cref="CharacterSet"/>으로 묶어 적도
/// 같은 도구를 쓰게 했다. 적 시트를 <b>플레이어와 완전히 같은 좌표 규격</b>(셀 256,
/// 가로 중심 x=128, 발끝 y=216)으로 뽑았기 때문에 슬라이서와 클립 생성기를 그대로 재사용할
/// 수 있다. 프롬프트 단계에서 규격을 통일해둔 것이 여기서 코드 중복을 없앤다.
///
/// 시트 규격(생성 프롬프트에 못 박고 실측으로 검증한 값):
/// - 셀 256x256, 가로로만 나열, 셀 사이 여백 0
/// - 가로 중심은 셀 정중앙 (전 프레임 오프셋 0)
/// - 발끝 y=216 (셀 위쪽 기준). 캐릭터 키는 세트마다 다르다(플레이어 160px, 적 141px)
/// </summary>
public static class AshPlayerSpriteSheets
{
    /// <summary>셀 한 칸의 크기(px). 가로 세로 같다.</summary>
    public const int CellSize = 256;

    /// <summary>
    /// 지면선의 y좌표(셀 위쪽 기준, px). 발끝 마지막 불투명 픽셀이 216행이므로 그 바로 아래다.
    ///
    /// 이 값이 중요한 이유: 모든 프레임의 피벗을 여기에 두면 애니메이션이 바뀔 때 캐릭터의
    /// 발 높이가 안 바뀐다. 플레이어와 적이 같은 값을 쓰므로 둘이 같은 바닥에 선다.
    /// </summary>
    public const int GroundLineY = 217;

    /// <summary>피벗의 세로 위치(0~1, 아래가 0). 유니티 텍스처 좌표는 아래가 원점이라 뒤집는다.</summary>
    public const float GroundPivotY = (CellSize - GroundLineY) / (float)CellSize;

    /// <summary>플레이어의 픽셀 키(머리 y=57 ~ 발끝 y=216). 콜라이더 계산에 쓴다.</summary>
    public const int CharacterPixelHeight = 160;

    /// <summary>적의 픽셀 키(실측 141px). 플레이어의 88%라 한 화면에 놓았을 때 덜 위협적으로 읽힌다.</summary>
    public const int EnemyPixelHeight = 141;

    /// <summary>
    /// 시트 한 장 안의 연속된 프레임 묶음 하나 = 애니메이션 하나.
    ///
    /// 시트와 애니메이션을 1:1로 두지 않은 이유: 적의 hit_death 시트 한 장에 피격 2프레임과
    /// 사망 4프레임이 같이 들어 있다. 시트 단위로만 다루면 "피격 뒤에 죽는 그림이 이어서
    /// 재생되는" 상태가 된다.
    /// </summary>
    public struct Segment
    {
        public string Name;      // 애니메이션 이름. 스프라이트/클립 이름에 그대로 쓰인다
        public int StartCell;    // 시트에서 몇 번째 칸부터인지(0부터)
        public int FrameCount;   // 몇 칸을 쓰는지
        public int Fps;          // 초당 프레임 수. 클립 길이 = FrameCount / Fps
        public bool Loop;        // 반복 재생 여부

        public Segment(string name, int startCell, int frameCount, int fps, bool loop)
        {
            Name = name; StartCell = startCell; FrameCount = frameCount; Fps = fps; Loop = loop;
        }
    }

    /// <summary>시트 파일 한 장.</summary>
    public struct Sheet
    {
        public string FileName;     // 확장자를 뺀 파일 이름
        public int CellCount;       // 시트 전체 칸 수. 텍스처 실제 가로폭 검증에 쓴다
        public Segment[] Segments;  // 이 시트에서 뽑아낼 애니메이션들

        public Sheet(string fileName, int cellCount, params Segment[] segments)
        {
            FileName = fileName; CellCount = cellCount; Segments = segments;
        }

        /// <summary>이 시트가 있어야 할 가로 폭(px).</summary>
        public int ExpectedWidth => CellCount * CellSize;
    }

    /// <summary>
    /// 캐릭터 한 종류의 전체 정의. 폴더, 이름 접두사, 시트 목록을 묶는다.
    ///
    /// 접두사를 세트마다 두는 이유: 스프라이트와 클립 이름이 프로젝트 전체에서 유일해야
    /// 검색이 명확하다. player_walk와 wraith_walk가 섞이면 애니메이션 창에서 어느 게 누구
    /// 것인지 이름만 봐선 모른다.
    /// </summary>
    public struct CharacterSet
    {
        public string DisplayName;  // 로그에 찍을 이름
        public string FolderPath;   // 시트가 들어 있는 폴더
        public string NamePrefix;   // 스프라이트/클립 이름 접두사
        public string AnimationFolder; // 클립과 컨트롤러를 만들 폴더
        public string ControllerName;  // 만들 AnimatorController 이름
        public Sheet[] Sheets;

        public CharacterSet(string displayName, string folderPath, string namePrefix,
                            string animationFolder, string controllerName, Sheet[] sheets)
        {
            DisplayName = displayName; FolderPath = folderPath; NamePrefix = namePrefix;
            AnimationFolder = animationFolder; ControllerName = controllerName; Sheets = sheets;
        }

        /// <summary>스프라이트 이름. 예: player_idle_00 / wraith_walk_00</summary>
        public string SpriteName(string segmentName, int frameIndex)
            => $"{NamePrefix}{segmentName}_{frameIndex:00}";

        /// <summary>애니메이션 클립 이름. 예: player_idle / wraith_walk</summary>
        public string ClipName(string segmentName) => $"{NamePrefix}{segmentName}";

        public string ControllerPath => $"{AnimationFolder}/{ControllerName}.controller";
    }

    /// <summary>
    /// 플레이어 시트 목록.
    ///
    /// Fps 근거: idle 4(숨쉬기라 느려야 한다 — 8은 움찔거림이 심했다), walk 10, run 12,
    /// attack 14(타격감은 프레임이 빨리 넘어갈 때 생긴다), dash 16, hit 10, death 8.
    /// </summary>
    public static readonly CharacterSet Player = new CharacterSet(
        "플레이어",
        "Assets/Project/Art/Sprites/Player",
        "player_",
        "Assets/Project/Animations/Player",
        "Player",
        new[]
        {
            new Sheet("player_idle_6frames_1536x256", 6, new Segment("idle", 0, 6, 4, true)),
            new Sheet("player_walk_8frames_2048x256", 8, new Segment("walk", 0, 8, 10, true)),
            new Sheet("player_run_6frames_1536x256", 6, new Segment("run", 0, 6, 12, true)),
            // 기본 공격(마우스 우클릭). 스킬이 아니라 항상 쓰는 평타다.
            new Sheet("player_attack_6frames_1536x256", 6,
                new Segment("attack", 0, 6, 14, false)),

            // Q 스킬 — 잿불 대검 내려찍기. 기본 공격과 <b>별개 애니메이션</b>이라
            // 세그먼트 이름도 따로 둔다. Animator에 SwordSlam 상태가 따로 생긴다.
            new Sheet("player_sword_slam_6frames_1536x256", 6,
                new Segment("sword_slam", 0, 6, 14, false)),

            // 활 스킬(W)용. 아직 상태 머신에 연결하지 않았다 — 스프라이트와 클립만 만들어두고,
            // W 스킬을 구현할 때 Animator에 상태를 추가한다.
            new Sheet("player_bow_6frames_1536x256", 6,
                new Segment("bow", 0, 6, 14, false)),

            // E 스킬(지팡이)용.
            new Sheet("player_staff_6frames_1536x256", 6,
                new Segment("staff", 0, 6, 14, false)),

            // R 필살기. 다른 스킬(14fps)보다 느린 10fps로 둔다 — 0.6초짜리 큰 동작이라
            // 천천히 봐야 무게가 실린다.
            new Sheet("player_ultimate_6frames_1536x256", 6,
                new Segment("ultimate", 0, 6, 10, false)),
            new Sheet("player_dash_hit_6frames_1536x256", 6,
                new Segment("dash", 0, 4, 16, false),
                new Segment("hit", 4, 2, 10, false)),
            new Sheet("player_death_6frames_1536x256", 6, new Segment("death", 0, 6, 8, false)),
        });

    /// <summary>
    /// 잿불 망령(일반 적) 시트 목록.
    ///
    /// Fps를 기획서의 동작 시간에 맞춰 정했다. 클립 길이와 코드의 상태 지속시간이 어긋나면
    /// "모션이 끝났는데 아직 못 움직이거나" 그 반대가 되므로, 여기 숫자가 곧 기획 수치다.
    /// - walk 8  : 6프레임 / 8 = 0.75초. 어슬렁거리는 느린 배회
    /// - windup 10: 4프레임 / 10 = <b>0.4초</b>. 기획의 예비동작 시간
    /// - charge 12: 4프레임 / 12 = <b>0.33초</b>. 기획의 돌진 시간(0.35초)에 가장 가까운 정수 fps
    /// - hit 10   : 2프레임 / 10 = 0.2초
    /// - death 8  : 4프레임 / 8 = 0.5초
    /// </summary>
    public static readonly CharacterSet Wraith = new CharacterSet(
        "잿불 망령",
        "Assets/Project/Art/Sprites/Enemy",
        "wraith_",
        "Assets/Project/Animations/Enemy",
        "Wraith",
        new[]
        {
            new Sheet("ash_ember_wraith_walk_6frames_1536x256", 6,
                new Segment("walk", 0, 6, 8, true)),
            new Sheet("ash_ember_wraith_windup_4frames_1024x256", 4,
                new Segment("windup", 0, 4, 10, false)),
            new Sheet("ash_ember_wraith_charge_4frames_1024x256", 4,
                new Segment("charge", 0, 4, 12, false)),
            // 한 장에 피격 2프레임(0~1) + 사망 4프레임(2~5)이 들어 있다.
            new Sheet("ash_ember_wraith_hit_death_6frames_1536x256", 6,
                new Segment("hit", 0, 2, 10, false),
                new Segment("death", 2, 4, 8, false)),
        });

    /// <summary>도구가 순회할 전체 세트.</summary>
    public static readonly CharacterSet[] AllSets = { Player, Wraith };

    /// <summary>셀 하나가 텍스처 안에서 차지하는 사각형. 유니티 텍스처 좌표라 아래가 y=0이다.</summary>
    public static Rect CellRect(int cellIndex)
        => new Rect(cellIndex * CellSize, 0f, CellSize, CellSize);

    /// <summary>모든 프레임이 공유하는 피벗. 가로는 정중앙, 세로는 지면선.</summary>
    public static Vector2 Pivot => new Vector2(0.5f, GroundPivotY);
}
