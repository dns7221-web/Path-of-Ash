/// <summary>
/// 추가 생성(2026-09-29, 소리) — 효과음 이름표. <see cref="SoundBank"/>가 이 이름으로 클립을 찾는다.
///
/// <b>문자열 대신 열거형인 이유:</b> 오타가 컴파일 오류로 잡힌다. 문자열은 "ChestOpne"처럼 틀려도 조용히 소리만 안 난다.
///
/// <b>값을 숫자로 직접 적은 이유:</b> 에셋(스킬 데이터·소리 목록)에는 이 값이 숫자로 저장된다. 중간에 새 이름을 끼워 넣으면
/// 뒤쪽 숫자가 밀려서 이미 저장된 에셋이 엉뚱한 소리를 가리킨다. <b>새 이름은 늘 맨 끝에 새 숫자로 붙인다.</b>
/// </summary>
public enum SfxId
{
    /// <summary>소리 없음. 스킬 데이터의 기본값이다.</summary>
    None = 0,

    /// <summary>플레이어 기본 공격 휘두름.</summary>
    PlayerSwing = 1,

    /// <summary>적(과 부술 수 있는 유물·허수아비)이 맞음 — 칼이 들어가는 소리.</summary>
    EnemyHit = 2,

    /// <summary>플레이어가 맞음.</summary>
    PlayerHurt = 3,

    /// <summary>플레이어 사망.</summary>
    PlayerDeath = 4,

    /// <summary>보상 상자 열기.</summary>
    ChestOpen = 5,

    // 추가 생성(2026-09-29, 소리 2차) — 스킬·이동. 규칙대로 끝에 새 숫자로 붙였다.

    /// <summary>Q 1단 — 대검을 바닥에 내려찍는 소리.</summary>
    SkillQSlam = 6,

    /// <summary>Q 2단 — 앞으로 터지는 잿불 폭발.</summary>
    SkillQBurst = 7,

    /// <summary>W — 화살을 놓는 소리.</summary>
    SkillWRelease = 8,

    /// <summary>W — 화살이 맞는 소리.</summary>
    SkillWImpact = 9,

    /// <summary>E — 재 기둥이 솟는 소리.</summary>
    SkillEBurst = 10,

    /// <summary>R — 무릎 꿇고 힘을 모으는 소리(시전 시작).</summary>
    SkillRCharge = 11,

    /// <summary>R — 큰 폭발.</summary>
    SkillRBurst = 12,

    /// <summary>플레이어 걸음.</summary>
    Footstep = 13,

    /// <summary>플레이어 대시.</summary>
    Dash = 14,

    // 추가 생성(2026-10-01, 소리 3차) — 문·적·보스·UI. 규칙대로 끝에 새 숫자로 붙였다.

    /// <summary>방을 클리어하고 보상을 챙겨 문이 열림.</summary>
    DoorOpen = 15,

    /// <summary>보스 열쇠 넷을 다 모아 부서진 문이 열림(보스 방으로 가는 길). 일반 문과 구분되게 무겁고 불길한 소리.</summary>
    BossGateOpen = 16,

    /// <summary>일반 적(망령·사수·자폭병) 사망. 셋 다 "재로 된 것"이라 한 소리를 같이 쓴다.</summary>
    EnemyDeath = 17,

    /// <summary>잿불 망령이 돌진을 시작함.</summary>
    WraithCharge = 18,

    /// <summary>잿불 사수가 화살을 쏨.</summary>
    MarksmanShoot = 19,

    /// <summary>잿불 자폭병이 터짐.</summary>
    BomberExplode = 20,

    /// <summary>보스 내려찍기 — 칼끝이 바닥에 닿는 순간.</summary>
    BossSlam = 21,

    /// <summary>보스 재의 창 — 창 한 발이 날아감(여러 발이라 겹침 제한이 중요하다).</summary>
    BossSpear = 22,

    /// <summary>보스 재 폭발 — 고리가 퍼지는 판정 순간.</summary>
    BossBurst = 23,

    /// <summary>2페이즈 전환 — 갑옷이 무너짐(타임라인 0.875).</summary>
    BossArmorBreak = 24,

    /// <summary>2페이즈 전환 — 알 껍질이 깨짐(타임라인 2.375).</summary>
    BossShellBreak = 25,

    /// <summary>2페이즈 전환 — 진체가 나타남(타임라인 2.875).</summary>
    BossReturn = 26,

    /// <summary>보스 사망.</summary>
    BossDeath = 27,

    /// <summary>UI 버튼 누름.</summary>
    UiClick = 28,

    /// <summary>창 열기(인벤토리·보스 열쇠·설정).</summary>
    UiOpen = 29,

    /// <summary>창 닫기.</summary>
    UiClose = 30,

    /// <summary>게임 시작·재시작처럼 화면이 넘어가는 결정.</summary>
    UiConfirm = 31,
}

/// <summary>
/// 추가 생성(2026-09-29, 소리) — 배경음악 이름표. 값을 숫자로 직접 적은 이유는 <see cref="SfxId"/>와 같다.
/// </summary>
public enum MusicId
{
    /// <summary>음악 없음 — 틀고 있던 곡을 서서히 끈다.</summary>
    None = 0,

    /// <summary>어두운 동굴 분위기(Dark Cavern Ambient) — 타이틀·던전.</summary>
    Ambient = 1,

    /// <summary>보스전(Heavy Dungeon).</summary>
    Boss = 2,

    /// <summary>
    /// 추가 생성(2026-09-29, 타이틀 음악) — 타이틀 화면(Dark Shrine Loop). 던전 곡(Ambient)과 나눠서,
    /// 게임을 시작하면 곡이 바뀌며 "들어섰다"는 느낌이 나게 한다.
    /// </summary>
    Title = 3,

    /// <summary>추가 생성(2026-09-29, 방 음악) — 튜토리얼 방(Safe Room). 싸움이 없는 방이라 조용한 곡.</summary>
    Tutorial = 4,

    /// <summary>추가 생성(2026-09-29, 방 음악) — 일반 던전 방(Dark Place). 튜토리얼을 나가면 이 곡으로 바뀐다.</summary>
    Dungeon = 5,

    /// <summary>추가 생성(2026-10-01, 결과 음악) — 클리어 결과 화면(Cathedral in the forest). "손을 펴다" 연출 뒤라 조용한 곡.</summary>
    ResultClear = 6,

    /// <summary>추가 생성(2026-10-01, 결과 음악) — 사망 결과 화면(Vampire's Piano). 슬픈 피아노 루프.</summary>
    ResultDeath = 7,
}
