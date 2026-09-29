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
}
