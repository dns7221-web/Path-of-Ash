using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GPT가 뽑아준 원본 그림을 프로젝트 규격의 스프라이트 시트로 바꾸는 도구.
///
/// 메뉴: Tools → 재의 길 → 그림 → 원본 시트 정규화
///
/// <b>왜 만들었나.</b> 프롬프트에 좌표 규칙(셀 256, 발끝 y=216, 중심 x=128)을 적어 GPT가
/// 맞춰주기를 기대했지만, 캔버스 비율·배경·시점·좌표가 한꺼번에 어긋나는 일이 반복됐다.
/// 1536x256은 가로세로 6:1인데 이미지 생성 모델은 정해진 몇 가지 비율만 출력할 수 있어서
/// 애초에 못 맞추는 경우가 많다.
///
/// 그래서 규격은 코드가 책임진다. GPT에게는 세 가지만 요구한다 — 단색 초록 배경,
/// 프레임마다 같은 키, 같은 바닥선. 저 셋만 코드로 고칠 수 없기 때문이다.
///
/// 처리 순서: 배경 제거 → 프레임 분리 → 공통 배율 계산 → 셀에 재배치.
/// </summary>
public static class AshSpriteSheetNormalizer
{
    private const string PlayerFolder = "Assets/Project/Art/Sprites/Player";
    private const string VfxFolder = "Assets/Project/Art/Sprites/VFX";
    private const string EnemyFolder = "Assets/Project/Art/Sprites/Enemy";

    /// <summary>추가 생성 — 보스(재의 왕) 시트 폴더. 원본은 그 아래 Raw/에 있다.</summary>
    private const string AshKingFolder = "Assets/Project/Art/Characters/Boss/AshKing";

    /// <summary>
    /// 프레임 나누기를 빈 구간이 아니라 <b>균등 분할</b>로 강제할 시트들.
    ///
    /// 빈 구간 순위로 나누는 방식은 프레임마다 그림이 충분히 있을 때만 맞는다.
    /// 보스 2페이즈 사망 시트는 뒤쪽 프레임이 <b>거의 비어 있어서</b>(왕이 재로 흩어진다)
    /// 칸 사이 빈 구간과 그림 안의 빈 구간이 구별되지 않는다. 실제로 두 칸이 하나로 합쳐지고
    /// 나머지가 폭 1~5px짜리 부스러기가 됐다.
    ///
    /// 원본이 같은 간격으로 그려져 있으면 균등 분할이 더 정확하다. 판정을 똑똑하게 만드는
    /// 대신 "이 시트는 균등이다"라고 적어두는 쪽이 확실하다 — 빈 프레임을 자동으로 알아보는
    /// 판정은 만들 수는 있어도 다른 시트를 망가뜨릴 위험이 더 크다.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> ForceEqualSplit =
        new System.Collections.Generic.HashSet<string>
        {
            "ash-king-phase2-hit-death.png",

            // 수정(2026-09-21) — 전환 이펙트 셋(gather·egg·shatter)은 여기서 빼서 CellGridSheets로 옮겼다.
            // 이유는 CellGridSheets 주석에 적었다.
        };

    /// <summary>
    /// 추가 생성(2026-09-21, 알·깨짐 잘림) — <b>원본 칸 격자대로</b> 자르고, 칸 안의 자리를 그대로 지키는 시트.
    ///
    /// 전환 이펙트 원본은 2172×724 그림에 <b>362px 칸 여섯 개</b>로 그려져 있다. 예전에는 위
    /// ForceEqualSplit으로 "그림이 있는 범위"(예: x 79~2154)를 6등분했는데, 그 범위는 칸 격자와
    /// 시작점이 다르고 폭도 달라서 경계가 칸마다 16~30px씩 밀렸다. 그래서 알·깨짐의 공이
    /// <b>한쪽이 곧게 잘리고, 옆 칸 조각이 칸 오른쪽에 비쳤다.</b>
    ///
    /// 여기 적힌 시트는 두 가지가 다르다.
    /// <list type="bullet">
    /// <item><b>자르기</b> — 칸 경계(width/프레임 수의 배수) 근처 ±<see cref="CellCutSearch"/>px에서
    /// 그림이 가장 적은 세로줄을 찾아 자른다. 소용돌이 팔이 칸 경계를 살짝 넘어도 안 잘린다.</item>
    /// <item><b>자리</b> — 프레임마다 그림 크기로 가운데를 다시 잡지 않고, <b>원본 칸 안의 자리를
    /// 그대로</b> 옮긴다(가로는 칸 가운데 기준, 세로는 모든 프레임 공통 바닥 기준). 예전에는 팔이 한쪽으로
    /// 뻗을 때마다 중심이 밀려 모임 효과가 6장 동안 54px 흘러갔다.</item>
    /// </list>
    ///
    /// 칸을 지켜 그린 원본에만 맞는 방식이라 목록으로 적어 둔다. 자동으로 판정하면 캐릭터 시트처럼
    /// 칸 없이 그려진 원본까지 흔들 수 있다.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CellGridSheets =
        new System.Collections.Generic.HashSet<string>
        {
            "vfx_ashking_transition_gather_6frames_1536x256.png",
            "vfx_ashking_transition_egg_6frames_1536x256.png",
            "vfx_ashking_transition_shatter_6frames_1536x256.png",
        };

    /// <summary>추가 생성(2026-09-21) — 칸 경계에서 가장 빈 줄을 찾을 거리(px). 원본 칸(362px)의 약 1/8.</summary>
    private const int CellCutSearch = 48;

    /// <summary>
    /// 배치 방식. 기준점이 서로 다르다.
    ///
    /// 캐릭터는 발끝이 지면선에 닿아야 하고, 바닥 이펙트는 접지면이 같은 지면선에 놓여야
    /// 캐릭터 발 위치에 그냥 겹쳐 놓을 수 있다. 공중에 뜨는 것(투사체)만 정중앙이다.
    ///
    /// 처음엔 이펙트를 전부 프레임별 정중앙에 놓았는데, 원본을 재보니 두 검 이펙트 모두
    /// 공통 바닥선을 갖고 있었다(forward_burst는 6프레임 전부 y=488). 중앙을 맞추면
    /// 그 바닥선이 깨져서 이펙트가 위아래로 떠다닌다.
    ///
    /// 전방 폭발은 가로도 다르다. 검이 박힌 지점에서 앞으로 자라는 그림이라, 프레임마다
    /// 중앙을 맞추면 커질수록 시작점이 뒤로 밀려 폭발이 뒤로 미끄러져 보인다.
    /// </summary>
    private enum Mode
    {
        Character,     // 발끝 y=216, 다리 중심 x=128
        GroundCenter,  // 바닥 y=216, 가로 중앙 — 그 자리에서 사방으로 퍼지는 것
        GroundForward, // 바닥 y=216, 왼쪽 끝 고정 — 바닥을 따라 앞으로 자라는 것
        FloatCenter,   // 셀 정중앙 — 공중에 뜬 것(투사체, 공중 폭발)
        TipRight,      // 오른쪽 끝(촉)을 고정 x에, 세로는 중앙 — 앞으로 날아가는 화살
    }

    /// <summary>
    /// 정규화할 파일 목록.
    ///
    /// <b>targetHeight</b>는 가장 큰 프레임을 몇 픽셀로 맞출지다. 0이면 기본값
    /// (캐릭터 160px / 이펙트 200px)을 쓴다.
    ///
    /// 시트마다 따로 줄 수 있게 만든 이유: 내려찍기는 대검을 머리 위로 치켜드는 프레임이
    /// 가장 높은데, 그 높이의 상당 부분이 몸이 아니라 검이다. 전부 160px에 맞추면 검까지
    /// 160 안에 들어가느라 몸이 다른 애니메이션보다 작아진다. 목표를 키우면 몸이 다시
    /// 160 근처가 된다.
    /// </summary>
    private static readonly (string folder, string source, string output,
                             int frames, Mode mode, int targetHeight)[] Jobs =
    {
        // ── 재의 왕(보스) ──
        //
        // 원본이 이미 1536x256 격자에 들어와 있지만 그래도 정규화를 태운다.
        // 격자만 맞고 <b>칸 안에서의 위치와 키는 안 맞기</b> 때문이다. 발끝 높이가 프레임마다
        // 몇 픽셀씩 달라서, 그대로 쓰면 대기 동작 중에 보스가 바닥에서 위아래로 떤다.
        //
        // 목표 키 200: 플레이어가 160이므로 1.25배다. 한 화면에 같이 놓았을 때 확실히 크되,
        // 셀(256) 위쪽에 왕관과 치켜든 검이 들어갈 여유를 남긴다.
        //
        // 내려찍기(slam)와 페이즈 전환만 214로 올린다. 검을 머리 위로 드는 프레임이 있어서
        // 200에 맞추면 <b>몸이 200 안에 들어가려고 작아진다</b> — 플레이어 sword_slam에서 겪은
        // 것과 같은 문제다.
        //
        // 214가 상한이다. 발끝이 y=216이므로 위로 쓸 수 있는 것이 216px뿐이고, 그보다 크게
        // 잡으면 <b>셀 위쪽에서 검 끝이 잘린다.</b> 처음에 240으로 잡았다가 실제로 잘렸다
        // (실측 로그에 "세로 0~216"으로 찍혀서 알았다). 여유 2px은 슬라이스 경계용이다.
        (AshKingFolder, "Raw/PlayerLike/ash-king-idle-raw.png",
                        "ash-king-idle.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-walk-raw.png",
                        "ash-king-walk.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-slam-raw.png",
                        "ash-king-slam.png", 6, Mode.Character, 214),
        (AshKingFolder, "Raw/PlayerLike/ash-king-ember-wave-raw.png",
                        "ash-king-ember-wave.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-hit-death-raw.png",
                        "ash-king-hit-death.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-phase-transition-raw.png",
                        "ash-king-phase-transition.png", 6, Mode.Character, 214),

        (AshKingFolder, "Raw/PlayerLike/ash-king-phase2-idle-raw.png",
                        "ash-king-phase2-idle.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-phase2-walk-raw.png",
                        "ash-king-phase2-walk.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-phase2-slam-raw.png",
                        "ash-king-phase2-slam.png", 6, Mode.Character, 214),
        (AshKingFolder, "Raw/PlayerLike/ash-king-phase2-ember-wave-raw.png",
                        "ash-king-phase2-ember-wave.png", 6, Mode.Character, 200),
        (AshKingFolder, "Raw/PlayerLike/ash-king-phase2-hit-death-raw.png",
                        "ash-king-phase2-hit-death.png", 6, Mode.Character, 200),

        // 2페이즈 전환 연출. 보스의 발 위치에서 재가 응축한 중심핵이 맥동하다 흩어지고,
        // 빈 중심 자리를 2페이즈 스프라이트가 받는다. 생성 원본은 칸 사이가 흰 선이거나 빈 프레임이
        // 넓어 자동 간격 판정이 흔들릴 수 있어, 위 ForceEqualSplit으로 6등분을 고정했다.
        // 전부 접지형 VFX라 보스의 발 위치와 같은 기준선에 놓는다.
        (VfxFolder, "vfx_ashking_transition_gather_6frames_raw.png",
                    "vfx_ashking_transition_gather_6frames_1536x256.png", 6, Mode.GroundCenter, 0),
        (VfxFolder, "vfx_ashking_transition_egg_6frames_raw.png",
                    "vfx_ashking_transition_egg_6frames_1536x256.png", 6, Mode.GroundCenter, 0),
        (VfxFolder, "vfx_ashking_transition_shatter_6frames_raw.png",
                    "vfx_ashking_transition_shatter_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 추가 생성(2026-09-20) — 보스의 재의 창. 앞으로 날아가는 투사체라 화살과 같은 TipRight다.
        // 촉을 같은 x에 고정해야 6프레임이 흔들리지 않고, 회전시켰을 때 촉이 진행 방향에 온다.
        (VfxFolder, "vfx_ashking_ash_spear_6frames_raw.png",
                    "vfx_ashking_ash_spear_6frames_1536x256.png", 6, Mode.TipRight, 0),

        (PlayerFolder, "player_bow_6frames_raw.png",
                       "player_bow_6frames_1536x256.png", 6, Mode.Character, 0),

        // 210 = 몸 160 + 머리 위로 치켜든 검 약 50. 여전히 작으면 더 올린다.
        (PlayerFolder, "player_sword_slam_6frames_raw.png",
                       "player_sword_slam_6frames_1536x256.png", 6, Mode.Character, 210),

        // 지팡이도 머리 위로 치켜드는 프레임이 가장 높다. 내려찍기와 같은 이유로 목표를 키운다.
        (PlayerFolder, "player_staff_6frames_raw.png",
                       "player_staff_6frames_1536x256.png", 6, Mode.Character, 200),

        // 유물 아이콘 3개.
        ("Assets/Project/Art/UI", "relic-icons-ember-set.png",
                                  "relic_icons_3frames_768x256.png", 3, Mode.FloatCenter, 0),

        // 스킬 아이콘 5개. 캐릭터도 이펙트도 아니지만 처리는 같다 — 초록 배경을 걷고
        // 균등한 칸에 가운데 정렬해서 담는다. 아이콘은 바닥 개념이 없으므로 FloatCenter.
        ("Assets/Project/Art/UI", "skill-icons-ember-set.png",
                                 "skill_icons_5frames_1280x256.png", 5, Mode.FloatCenter, 0),

        // 수정(2026-09-15, 일반 몬스터 새 그림) — 사수 4줄·자폭병 3줄을 걷어냈다. 결과 파일 이름은 그대로 쓰지만
        // <b>그림은 새 원본(Art/NewMonsterImages/…)에서 Tools/NormalizeGeneratedStrip.ps1로 만들었다.</b>
        // 망령 시트는 원래 이 표에 없었다(PowerShell 도구로 만든 시트다).
        //
        //   ash_marksman_walk_6frames_raw       → ..._1536x256  Character 160(기본)
        //   ash_marksman_aim_4frames_raw        → ..._1024x256  Character 160(기본)
        //   ash_marksman_shoot_4frames_raw      → ..._1024x256  Character 160(기본)  (3번 프레임의 화살이 빈 구간에 걸쳐 경계가 밀릴 수 있었다)
        //   ash_marksman_hit_death_6frames_raw  → ..._1536x256  Character 160(기본)  (피격 2 + 사망 4)
        //   ash_bomber_walk_6frames_raw         → ..._1536x256  Character 141
        //   ash_bomber_fuse_4frames_raw         → ..._1024x256  Character 214(셀 상한)
        //   ash_bomber_hit_death_6frames_raw    → ..._1536x256  Character 149
        //
        // 사수가 기본값(160)이었던 이유: 활을 든 인간형이라 덩치로 위협하는 적이 아니고, 크기 차이는 보스(200)가 맡는다.
        // 자폭병의 141/214/149는 옛 점화 시트가 마지막 프레임에 1.48배로 부풀어서, 셀 상한 214를 넘지 않게 세 시트에
        // 배율 0.379를 공통으로 걸려고 거꾸로 계산한 값이었다(걷기를 160으로 두면 점화가 시작될 때 몸이 쪼그라들었다).
        // 새 점화는 부풀기 전인 2번 프레임을 걷기와 같은 키 140에 맞춰 정규화해서 같은 문제가 없다.
        //
        // 줄을 남겨두면 안 되는 이유: "원본 시트 정규화"(전체)를 누르는 순간 옛 원본으로 결과를 다시 써서
        // <b>새 그림이 에러 없이 옛 그림으로 돌아간다.</b> 옛 원본 PNG가 폴더에 남아 있어 실패도 안 한다.
        // 2026-09-14 VFX 다섯 줄을 걷어낸 것과 같은 사정이다. 옛 결과 시트는 Enemy/Raw/PreNewMonsterV1에 백업했다.
        // 수정(2026-09-15, 몬스터 VFX) — 사수 화살과 자폭병 폭발 줄도 같은 날 새 그림으로 바꿔 걷어냈다(아래 두 곳 주석).

        // 수정(2026-09-15, 몬스터 VFX) — 자폭병 폭발 줄을 걷어냈다. 원래 줄과 설명은 이랬다.
        //
        //   vfx_bomber_blast_6frames_raw  → ..._1536x256  GroundCenter
        //   "자폭병의 폭발. 발밑에서 사방으로 퍼진다 — 왕의 잿불과 같은 성격이라 같은 방식이다.
        //    실제 크기는 EnemyBomber의 Explosion Effect Scale이 정한다. 여기서는 셀에 꽉 차지
        //    않게만 두고, 판정 반경(6유닛)에 맞추는 일은 프리팹 쪽 배율 한 곳에서만 한다."
        //
        // 새 원본(Art/NewMonsterImages/Bomber/bomber_vfx_blast_v1_raw.png)은 배경이 투명해서 이 도구가 못 읽는다.
        // Tools/NormalizeVfxStrip.ps1 -AnchorX BoxCenter -AnchorY BoxCenter -PivotX 128 -PivotY 128로 만들었다.
        // 기준도 지면선이 아니라 <b>셀 정중앙</b>으로 바꿨다 — 이유는 AshVfxSpriteSlicer 표의 같은 시트 주석에 있다.
        // 크기를 판정에 맞추는 곳이 프리팹 쪽 배율 한 곳이라는 원칙은 그대로다(Explosion Effect Scale 1.8).
        // 옛 결과 시트는 Sprites/VFX/Raw/PreNewMonsterV1에 백업했다.

        // R 필살기. 검을 머리 위로 치켜드는 프레임이 있어 목표를 키운다.
        (PlayerFolder, "player_ultimate_kings_ember_execution_6poses_v3_raw.png",
                       "player_ultimate_6frames_1536x256.png", 6, Mode.Character, 210),

        // 왕의 잿불 폭발. 발밑에서 사방으로 퍼진다.
        (VfxFolder, "vfx_kings_ember_full_room_6frames_raw.png",
                    "vfx_kings_ember_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 수정(2026-09-14, 새 캐릭터 VFX) — 여기 있던 다섯 줄을 걷어냈다. 결과 파일은 그대로 쓰지만
        // <b>원본이 바뀌었고, 이 도구는 새 원본을 읽지 못한다.</b>
        //
        //   vfx_ash_staff_ground_spell_6frames_raw      → ..._1536x256  GroundCenter  (지팡이 주문 — 바닥에서 솟는 잿불 기둥)
        //   vfx_sword_slam_impact_6frames_raw           → ..._1536x256  GroundCenter  (검이 박힌 지점의 충격파)
        //   vfx_sword_slam_forward_burst_6frames_raw    → ..._1536x256  GroundForward (박힌 지점에서 앞으로, 시작점 고정)
        //   vfx_ember_arrow_flight_6frames_raw          → ..._1536x256  FloatCenter   (공중을 나는 화살)
        //   vfx_ember_arrow_impact_6frames_raw          → ..._1536x256  FloatCenter
        //
        // 새 원본(Art/NewPlayerImages/…)은 배경이 이미 투명한데, 이 도구의 배경 제거는 초록이 아닌 픽셀을
        // 전부 불투명으로 만든다(RemoveBackground). 그래서 새 그림은 Tools/NormalizeVfxStrip.ps1로 만들었다.
        //
        // 줄을 남겨두면 안 되는 이유: "원본 시트 정규화"(전체)를 누르는 순간 옛 원본으로 결과를 다시 써서
        // <b>새 그림이 에러 없이 옛 그림으로 돌아간다.</b> 옛 원본 PNG는 폴더에 남아 있어 실패도 안 한다.
        // 되돌려야 할 때는 Raw/PreNewCharacterVfx의 백업을 쓴다.

        // 수정(2026-09-15, 몬스터 VFX) — 사수 화살 줄을 걷어냈다. 원래 줄과 설명은 이랬다.
        //
        //   ash_marksman_ember_arrow_raw  → ash_marksman_ember_arrow_1frame_256x256  TipRight
        //   "사수가 쏘는 화살. 한 장짜리라 프레임이 1개다.
        //    촉 끝을 기준으로 놓아야 맞는 지점과 눈에 보이는 촉이 일치한다."
        //
        // 새 원본(Art/NewMonsterImages/Marksman/marksman_vfx_arrow_v1_raw.png)도 배경이 투명해서
        // Tools/NormalizeVfxStrip.ps1 -Frames 1 -AnchorX RightEdge -AnchorY BoxCenter -PivotX 228 -PivotY 128로 만들었다.
        // 촉 끝 자리 228은 TipRightInset과 같아서 붙는 기준은 그대로다. 옛 결과 시트는 Sprites/VFX/Raw/PreNewMonsterV1에 백업했다.

        // 추가 생성 — 기본 공격(Ctrl, 잿불 베기)의 검 궤적.
        //
        // 위에서 "vfx_ember_slash_A/B는 검 스킬이 내려찍기로 바뀌면서 뺐다"고 적었던 것과
        // <b>다른 물건</b>이다. 그때 사라진 건 Q였고, 이건 그 뒤에 따로 생긴 기본 공격이다.
        // Q는 대검을 바닥에 내려찍으므로 이펙트가 바닥에서 솟지만(GroundCenter), 기본 공격은
        // 검을 허공에 휘두르는 것이라 궤적이 공중에 뜬다.
        //
        // FloatCenter인 이유: 여섯 프레임의 세로 중심이 원본에서 전부 y=352로 같다(초승달이
        // 자라고 흩어질 뿐 위치는 안 움직인다). 바닥선 기준으로 놓으면 초승달이 프레임마다
        // 커지는 만큼 위로 자라 올라가서, 한 자리에서 번쩍이는 게 아니라 위로 솟는 것으로 보인다.
        (VfxFolder, "vfx_ember_slash_6frames_raw.png",
                    "vfx_ember_slash_6frames_1536x256.png", 6, Mode.FloatCenter, 0),
    };

    /// <summary>
    /// 결과 PNG를 파일로 쓴다. 실패하면 false.
    ///
    /// <b>왜 그냥 WriteAllBytes를 안 쓰나.</b> 이미 임포트된 텍스처를 덮어쓰려 하면
    /// 윈도우가 <c>IOException: Win32 IO returned 1224</c>로 거절한다. 1224는
    /// ERROR_USER_MAPPED_FILE — <b>유니티가 그 파일을 메모리에 매핑해 들고 있다</b>는 뜻이다.
    /// 처음 만들 때는 안 나고, 같은 시트를 두 번째로 정규화할 때부터 난다.
    ///
    /// 그리고 이 예외가 나면 <b>정규화 전체가 그 자리에서 멈춘다.</b> 뒤에 남은 시트들은
    /// 손도 못 대는데, 로그만 보면 "몇 장은 됐고 몇 장은 안 됐다"가 한눈에 안 들어온다.
    ///
    /// 그래서 두 가지를 한다 — 쓰기 전에 유니티가 쥔 파일 핸들을 놓게 하고,
    /// 그래도 실패하면 <b>예외를 밖으로 던지지 않고</b> 그 시트만 건너뛴다.
    /// 한 장 때문에 나머지 스물여덟 장을 못 만드는 것이 더 나쁘다.
    /// </summary>
    private static bool WritePng(string outputPath, byte[] png)
    {
        // 유니티가 쥐고 있는 파일 핸들을 놓게 한다. 대부분 이 한 줄로 풀린다.
        AssetDatabase.ReleaseCachedFileHandles();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllBytes(outputPath, png);
                return true;
            }
            catch (IOException e)
            {
                // 마지막 시도까지 실패하면 포기하고 알린다.
                if (attempt == 2)
                {
                    Debug.LogError($"[시트 정규화] 파일을 못 썼다: {outputPath} — {e.Message} " +
                                   "유니티가 이 텍스처를 쓰는 중이다. 인스펙터에서 다른 것을 고르거나 " +
                                   "에디터를 껐다 켠 뒤 다시 실행해라. 나머지 시트는 계속 처리한다.");
                    return false;
                }

                // 짧게 기다렸다 다시 시도한다. 임포트가 끝나는 순간을 노린다.
                System.Threading.Thread.Sleep(120);
                AssetDatabase.ReleaseCachedFileHandles();
            }
        }

        return false;
    }

    /// <summary>앞으로 자라는 이펙트의 시작점(셀 왼쪽에서의 거리, px).</summary>
    private const int ForwardEffectLeftInset = 28;

    /// <summary>
    /// 화살촉 끝을 놓을 자리(셀 왼쪽에서의 거리, px).
    ///
    /// <b>화살은 한가운데가 아니라 촉이 기준이어야 한다.</b> 정중앙에 두면 그림의 절반이
    /// 진행 방향 앞에 남는데, 맞는 지점은 오브젝트 위치라서 <b>촉이 아직 안 닿았는데 맞거나
    /// 이미 지나갔는데 안 맞는다.</b> 꼬리 불씨가 뒤로 길게 붙을수록 그 어긋남이 커진다.
    ///
    /// 오른쪽에 28px을 남기는 것은 다른 이펙트와 같은 이유다 — 셀 경계에 딱 붙으면
    /// 슬라이스한 뒤 옆 칸 픽셀이 한 줄 비쳐 보인다.
    /// </summary>
    private const int TipRightInset = 228;

    /// <summary>
    /// 이펙트가 셀 안에서 차지할 최대 크기(px). 256 셀에 양옆 28px씩 여유를 둔 값이다.
    /// 셀 경계에 딱 붙으면 슬라이스한 뒤 옆 프레임 픽셀이 한 줄 비쳐 보인다.
    /// </summary>
    private const int EffectTargetSize = 200;

    // ── 배경 판정 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 초록기 = G - max(R, B). 이 값이 클수록 배경이다.
    ///
    /// "G가 크면 배경"으로 하면 밝은 회색(R=G=B=200)도 배경이 되어 캐릭터에 구멍이 뚫린다.
    /// R·B와의 차이를 보면 순수 초록만 걸러진다. 프롬프트에서 "캐릭터에 초록을 쓰지 마라"를
    /// 요구한 것이 여기서 값을 한다.
    /// </summary>
    private const int BackgroundGreenness = 110;

    /// <summary>이 값 아래면 완전 불투명. 사이 구간은 안티에일리어싱된 가장자리다.</summary>
    private const int OpaqueGreenness = 45;

    // ── 프레임 분리 ───────────────────────────────────────────────────────

    // 고정 임계값(예: "30픽셀 이하 틈은 같은 프레임")은 쓰지 않는다. 활 시트의 5·6번 인물
    // 사이가 12픽셀이라 둘이 합쳐졌고, 임계값을 12 아래로 낮추면 이번엔 인물 안의 빈틈
    // (활과 몸 사이 1~3픽셀)에서 갈라진다. 어떤 고정값도 전부를 만족시키지 못한다.
    //
    // 대신 프레임 수를 제약으로 쓴다. 6프레임을 원하면 경계는 정확히 5개이므로, 빈 구간 중
    // 가장 넓은 5개를 경계로 삼는다. 간격의 절대값이 아니라 순위로 판단하므로 조정할 값이 없다.

    /// <summary>이 픽셀 수보다 얇은 세로줄은 프레임으로 세지 않는다. 흩날린 티끌을 거른다.</summary>
    private const int MinColumnPixels = 3;

    /// <summary>
    /// 몸 중심을 찾을 때 볼 아래쪽 비율.
    ///
    /// 바운딩박스 중앙을 쓰면 안 되는 이유: 활이 오른쪽으로 뻗어 있어서 중심이 왼쪽으로
    /// 밀린다. 다리와 발은 무기처럼 튀어나오지 않으므로, 아래쪽 20%의 가로 중심이
    /// 몸이 실제로 서 있는 위치를 그대로 나타낸다.
    /// </summary>
    private const float LegBandRatio = 0.2f;

    [MenuItem("Tools/재의 길/그림/원본 시트 정규화")]
    public static void NormalizeAll()
    {
        foreach (var (folder, source, output, frames, mode, targetHeight) in Jobs)
            Normalize(folder, source, output, frames, mode, targetHeight);

        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 추가 생성 — 프로젝트 창에서 고른 원본 PNG만 정규화한다.
    ///
    /// 전체 실행과 나눈 이유: 위 메뉴는 목록의 시트를 <b>전부 다시 만든다.</b> 시트 한 장을
    /// 새로 넣었을 때도 보스와 플레이어까지 같이 바뀌어서, 그날 한 일과 상관없는 것까지
    /// 확인해야 한다. 정렬 규칙을 고친 직후에는 특히 나쁘다 — 무엇이 왜 달라진 건지
    /// 구별할 수 없다. PowerShell 정규화 도구에 -Only를 넣었던 것과 같은 판단이다.
    /// </summary>
    /// <summary>
    /// 추가 생성(2026-09-21, 페이즈 전환 고치기) — 보스 1페이즈 시트들. 초록 번짐만 빼고 배치는 그대로 둔다.
    /// </summary>
    private static readonly string[] BossPhase1Sheets =
    {
        "ash-king-idle.png",
        "ash-king-walk.png",
        "ash-king-slam.png",
        "ash-king-ember-wave.png",
        "ash-king-hit-death.png",
        "ash-king-phase-transition.png",
    };

    /// <summary>
    /// 추가 생성(2026-09-26, 2페이즈 초록 번짐) — 보스 2페이즈 시트들. 1페이즈와 같은 이유로 색만 고친다.
    ///
    /// 09-21에 초록 번짐을 뺄 때 위 목록에 1페이즈 여섯 장만 넣어서, Despill이 생기기 전(09-02)에 만든
    /// 2페이즈 시트 다섯 장은 초록 테두리를 그대로 달고 있었다(장마다 12,000~16,000픽셀).
    /// 망토 끝·머리카락 가장자리와 발밑 그림자가 초록 선으로 보였고, 왕관 의식처럼 보스가
    /// 한 자세로 멈춰 있을 때 특히 잘 보였다(의식 자세 = 내려찍기 3번째 장 ashking2_slam_02).
    ///
    /// 넣지 않은 것: 궁극기 시트(ash-king-phase2-ultimate.png)는 처음부터 초록이 없다.
    /// ash-king-phase2-ultimate-playerlike.png는 씬·프리팹·애니메이션 어디서도 안 쓰는 시안이다.
    /// </summary>
    private static readonly string[] BossPhase2Sheets =
    {
        "ash-king-phase2-idle.png",
        "ash-king-phase2-walk.png",
        "ash-king-phase2-slam.png",
        "ash-king-phase2-ember-wave.png",
        "ash-king-phase2-hit-death.png",
    };

    /// <summary>
    /// 추가 생성(2026-09-21, 페이즈 전환 고치기) — 전환에 필요한 그림만 한 번에 고친다.
    ///
    /// <list type="bullet">
    /// <item><b>전환 이펙트 3장</b>(모임·알·깨짐) — 원본에서 새 방식(칸 격자로 자르기·칸 안 자리 지키기·
    /// 초록 번짐 빼기)으로 다시 만든다.</item>
    /// <item><b>보스 1페이즈 시트 6장</b> — 이미 만들어진 결과 시트에서 <b>초록 번짐만</b> 뺀다.
    /// 칼날 전체가 초록이던 1페이즈 칼이 잿빛이 된다(사용자 선택 "번진 것이라 회색으로").</item>
    /// </list>
    ///
    /// 1페이즈 시트를 원본부터 다시 정규화하지 않는 이유: 배치까지 다시 하면 발 위치·크기가 조금이라도
    /// 달라질 수 있고, 그러면 애니메이션·콜라이더를 전부 다시 확인해야 한다. 색만 고치면 그 걱정이 없다.
    /// 전체 정규화 메뉴를 안 쓰는 이유는 NormalizeSelected 주석과 같다 — 오늘 한 일과 상관없는 시트까지 바뀐다.
    /// </summary>
    [MenuItem("Tools/재의 길/그림/보스 전환 그림 고치기")]
    public static void FixBossTransitionArt()
    {
        int remade = 0;
        foreach (var (folder, source, output, frames, mode, targetHeight) in Jobs)
        {
            if (!CellGridSheets.Contains(output)) continue;

            Normalize(folder, source, output, frames, mode, targetHeight);
            remade++;
        }

        int cleaned = 0;
        foreach (string sheet in BossPhase1Sheets)
        {
            if (DespillInPlace($"{AshKingFolder}/{sheet}")) cleaned++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[시트 정규화] 보스 전환 그림 고치기: 전환 이펙트 {remade}/{CellGridSheets.Count}장을 다시 만들고, " +
                  $"보스 1페이즈 시트 {cleaned}/{BossPhase1Sheets.Length}장에서 초록 번짐을 뺐다.\n" +
                  "다음: Tools → 재의 길 → 애니메이션 → 보스 애니메이션만 생성 → " +
                  "프리팹 → 보스 전환 다시 만들기 (타임라인 포함)");
    }

    /// <summary>
    /// 추가 생성(2026-09-26, 2페이즈 초록 번짐) — 보스 시트 전부(1·2페이즈)에서 <b>초록 번짐만</b> 뺀다.
    ///
    /// "보스 전환 그림 고치기"와 따로 둔 이유: 그 메뉴는 전환 이펙트 3장을 원본부터 다시 만든다.
    /// 색만 고치려는 날에 상관없는 그림까지 다시 쓰지 않도록 색 고치기만 하는 입구를 하나 더 둔다.
    ///
    /// 여러 번 돌려도 결과가 같다 — 이미 뺀 시트는 바뀌는 픽셀이 없어서 DespillInPlace가 파일을
    /// 건드리지 않는다. 그래서 "0장"이 찍히면 전부 이미 깨끗하다는 확인이 된다.
    /// 슬라이스는 .meta에 그대로 남으므로 애니메이션·프리팹을 다시 만들 필요가 없다.
    /// </summary>
    [MenuItem("Tools/재의 길/그림/보스 초록 번짐 빼기")]
    public static void DespillBossSheets()
    {
        int cleaned = 0;
        int total = 0;

        // 1페이즈 목록과 2페이즈 목록을 차례로 돈다. 목록을 합치지 않은 이유는 각 목록 주석에 적은
        // "언제, 왜 들어갔나"를 따로 남기기 위해서다.
        foreach (string[] sheets in new[] { BossPhase1Sheets, BossPhase2Sheets })
        {
            foreach (string sheet in sheets)
            {
                total++;
                if (DespillInPlace($"{AshKingFolder}/{sheet}")) cleaned++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[시트 정규화] 보스 초록 번짐 빼기: 보스 시트 {cleaned}/{total}장을 다시 썼다. " +
                  "0장이면 전부 이미 빠져 있다는 뜻이다. 슬라이스는 그대로라 다른 메뉴를 돌릴 필요가 없다.");
    }

    // ── 추가 생성(2026-09-26, 대기 모션에서 발이 뜨던 것) ─────────────────────────

    /// <summary>추가 생성(2026-09-26) — 발을 땅에 고정할 플레이어 대기 시트. 6장 × 8방향, 칸 256px.</summary>
    private const string PlayerIdleSheet = "Assets/Project/Art/Sprites/Player/Topdown35/Production8Dir/player_idle.png";

    /// <summary>
    /// 추가 생성(2026-09-26, 대기 모션에서 발이 뜨던 것) — 대기 시트에서 "몸 전체를 들어 올린" 장을
    /// "발은 땅에 붙이고 몸만 늘인" 장으로 바꾼다.
    ///
    /// 앞모습(S)을 뺀 일곱 방향의 대기 2~6번째 장은 첫 장을 통째로 0·1·2·2·1·0px 올린 복사본이었다(픽셀 차이 0).
    /// 숨쉬기를 몸 전체 이동으로 만들어서 발까지 같이 떴고, 바닥의 접지 그림자와 틈이 생겨 공중에 뜬 것처럼 보였다.
    /// 걷기 시트는 이미 "전체를 올리면 발바닥이 벗어나니 상체만 올린다"(Tools/FixWalkCycleSheet.ps1)를 지키고 있었다.
    ///
    /// 고치는 방식: 방향(줄)마다 가장 낮은 발을 땅으로 삼는다. 떠 있는 장은 발을 땅에 붙이고 머리 높이는 그대로 둔다.
    /// 그 사이의 몸은 떠 있던 만큼(1~2px) 세로로 늘인다. 늘어나는 줄은 몸 높이에 고르게 나눠 넣는다 —
    /// 한 곳에 몰면 칼 같은 대각선에 2px 계단이 생긴다. 앞모습은 숨쉬기를 따로 그린 장이라 발이 이미 같은 줄에 있어 안 바뀐다.
    ///
    /// 여러 번 돌려도 결과가 같다 — 발이 전부 땅에 붙은 시트는 고칠 장이 없어 파일을 건드리지 않는다.
    /// 그래서 "0장"이 찍히면 이미 고쳐져 있다는 확인이 된다.
    /// </summary>
    [MenuItem("Tools/재의 길/그림/대기 모션 발 고정")]
    public static void PlantPlayerIdleFeet()
    {
        int changed = PlantFeetInPlace(PlayerIdleSheet, 6, 8, 256);

        AssetDatabase.Refresh();
        Debug.Log($"[시트 정규화] 대기 모션 발 고정: {changed}장을 고쳤다. 0장이면 발이 이미 전부 땅에 붙어 있다는 뜻이다. " +
                  "슬라이스·피벗은 .meta 그대로라 애니메이션을 다시 만들 필요가 없다.");
    }

    /// <summary>
    /// 추가 생성(2026-09-26) — 격자 시트의 줄(방향)마다 떠 있는 장의 발을 땅에 고정한다.
    /// 좌표는 텍스처 기준이다(아래가 0). 바뀐 장이 없으면 파일을 건드리지 않는다.
    /// </summary>
    /// <returns>고친 장 수.</returns>
    private static int PlantFeetInPlace(string path, int columns, int rows, int cell)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[시트 정규화] 발을 고정할 시트를 못 찾았다: {path}");
            return 0;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Object.DestroyImmediate(texture);
            Debug.LogError($"[시트 정규화] PNG 디코딩 실패: {path}");
            return 0;
        }

        int width = texture.width;
        int height = texture.height;
        Color32[] source = texture.GetPixels32();
        Color32[] output = (Color32[])source.Clone();
        int changed = 0;

        for (int row = 0; row < rows; row++)
        {
            // 이미지의 맨 위 줄이 row 0이다. 텍스처는 아래가 0이라 뒤집어서 이 줄의 아래 끝을 구한다.
            int originY = height - (row + 1) * cell;

            // 1. 장마다 발(가장 낮은 줄)과 머리(가장 높은 줄)를 찾고, 가장 낮은 발을 이 방향의 땅으로 삼는다.
            var feet = new int[columns];
            var heads = new int[columns];
            int ground = int.MaxValue;
            for (int column = 0; column < columns; column++)
            {
                FindFeetAndHead(source, width, column * cell, originY, cell, out feet[column], out heads[column]);
                if (feet[column] >= 0) ground = Mathf.Min(ground, feet[column]);
            }

            if (ground == int.MaxValue) continue; // 빈 줄

            // 2. 떠 있는 장만 고친다.
            for (int column = 0; column < columns; column++)
            {
                int foot = feet[column];
                int head = heads[column];
                int lift = foot - ground; // 이 장이 땅에서 뜬 높이(px)
                if (foot < 0 || lift <= 0) continue;

                int bodyHeight = head - foot;
                int originX = column * cell;

                for (int y = 0; y < cell; y++)
                {
                    int sourceY;
                    if (y < ground)
                    {
                        // 발 아래(빈 곳)는 발과 같이 내린다.
                        sourceY = y + lift;
                    }
                    else if (y <= head)
                    {
                        // 몸: 땅(t = 0)에서는 발 줄을, 머리(t = 몸 높이 + lift)에서는 머리 줄을 가져오고,
                        // 그 사이는 몸 높이를 lift만큼 늘인 비율로 가져온다. (2t + 1) / 2는 가운데 반올림이다 —
                        // 그냥 내림하면 늘어나는 줄이 전부 발 쪽 한 곳에 몰린다.
                        // 정수로만 계산해서 어느 환경에서 돌려도 같은 결과가 나온다.
                        int t = y - ground;
                        sourceY = foot + ((2 * t + 1) * bodyHeight) / (2 * (bodyHeight + lift));
                    }
                    else
                    {
                        // 머리 위(빈 곳)는 그대로.
                        sourceY = y;
                    }

                    for (int x = 0; x < cell; x++)
                    {
                        int to = (originY + y) * width + originX + x;
                        output[to] = sourceY >= 0 && sourceY < cell
                            ? source[(originY + sourceY) * width + originX + x]
                            : new Color32(0, 0, 0, 0);
                    }
                }

                changed++;
            }
        }

        if (changed == 0)
        {
            Object.DestroyImmediate(texture);
            return 0;
        }

        texture.SetPixels32(output);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        if (!WritePng(path, png)) return 0;

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return changed;
    }

    /// <summary>
    /// 추가 생성(2026-09-26) — 한 칸에서 알파가 40을 넘는 가장 낮은 줄(발)과 가장 높은 줄(머리)을 칸 안 좌표로 돌려준다.
    /// 빈 칸이면 둘 다 -1이다. 40 이하는 가장자리의 옅은 번짐이라 발로 치지 않는다.
    /// </summary>
    private static void FindFeetAndHead(Color32[] pixels, int width, int originX, int originY, int cell,
                                        out int feet, out int head)
    {
        feet = -1;
        head = -1;
        for (int y = 0; y < cell; y++)
        {
            for (int x = 0; x < cell; x++)
            {
                if (pixels[(originY + y) * width + originX + x].a <= 40) continue;

                // 아래에서부터 올라가므로 처음 찾은 줄이 발, 마지막으로 찾은 줄이 머리다.
                if (feet < 0) feet = y;
                head = y;
                break;
            }
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-21) — 이미 만들어진 시트 PNG에서 초록 번짐만 빼서 같은 자리에 다시 쓴다.
    /// 슬라이스 정보는 .meta에 있어서 그대로 남는다. 바뀐 픽셀이 없으면 파일을 건드리지 않는다.
    /// </summary>
    /// <returns>파일을 다시 썼으면 true.</returns>
    private static bool DespillInPlace(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[시트 정규화] 초록 번짐을 뺄 시트를 못 찾았다: {path}");
            return false;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Object.DestroyImmediate(texture);
            Debug.LogError($"[시트 정규화] PNG 디코딩 실패: {path}");
            return false;
        }

        Color32[] pixels = texture.GetPixels32();
        Color32[] before = (Color32[])pixels.Clone();
        Despill(pixels);

        bool changed = false;
        for (int i = 0; i < pixels.Length && !changed; i++)
        {
            if (pixels[i].g != before[i].g) changed = true;
        }

        if (!changed)
        {
            Object.DestroyImmediate(texture);
            return false;
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        if (!WritePng(path, png)) return false;

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return true;
    }

    [MenuItem("Tools/재의 길/그림/원본 시트 정규화 (고른 것만)")]
    public static void NormalizeSelected()
    {
        var picked = new HashSet<string>();
        foreach (UnityEngine.Object obj in Selection.objects)
        {
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath)) picked.Add(Path.GetFileName(assetPath));
        }

        if (picked.Count == 0)
        {
            Debug.LogError("[시트 정규화] 프로젝트 창에서 원본 PNG(..._raw.png)를 고르고 실행해라.");
            return;
        }

        int done = 0;
        foreach (var (folder, source, output, frames, mode, targetHeight) in Jobs)
        {
            if (!picked.Contains(Path.GetFileName(source))) continue;

            Normalize(folder, source, output, frames, mode, targetHeight);
            done++;
        }

        if (done == 0)
        {
            Debug.LogError("[시트 정규화] 고른 파일이 정규화 목록에 없다. Jobs 표에 줄을 먼저 추가해라.");
            return;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[시트 정규화] 고른 {done}장을 다시 만들었다.");
    }

    private static void Normalize(string folder, string sourceName, string outputName,
                                  int expectedFrames, Mode mode, int targetHeight)
    {
        string sourcePath = $"{folder}/{sourceName}";
        if (!File.Exists(sourcePath))
        {
            Debug.LogError($"[시트 정규화] 원본을 못 찾았다: {sourcePath}");
            return;
        }

        // 임포트 설정(Read/Write)에 상관없이 읽으려고 파일에서 직접 디코딩한다.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(sourcePath)))
        {
            Debug.LogError($"[시트 정규화] PNG 디코딩 실패: {sourcePath}");
            Object.DestroyImmediate(texture);
            return;
        }

        int width = texture.width;
        int height = texture.height;
        Color32[] pixels = texture.GetPixels32();
        Object.DestroyImmediate(texture);

        // 수정(2026-09-21, 투명 원본) — 원본이 이미 투명 배경이면 초록 빼기를 건너뛴다.
        //
        // 이 도구는 초록 배경 원본만 생각하고 만들어졌다. RemoveBackground는 "초록이 아닌 픽셀은
        // 전부 그림"으로 보고 불투명하게 칠하는데, 투명 배경 원본의 빈 칸은 색이 (0,0,0)이라
        // 초록이 아니다. 그래서 <b>빈 칸 전체가 불투명한 검정</b>이 됐다. 재의 창 원본이 그렇게
        // 망가졌다 — 창마다 검은 네모가 붙고, 칸 전체가 그림으로 잡혀서 그걸 셀에 맞춰 줄이느라
        // 창이 1/3 크기가 됐다.
        //
        // 초록 빼기를 고치지 않고 원본 종류를 먼저 가르는 이유: 투명 원본은 배경을 뺄 필요가
        // 애초에 없다. 이미 있는 투명도가 정답이고, 거기에 초록 판정을 덧씌우면 그림 속 초록기
        // 있는 픽셀에 구멍만 뚫린다. 초록 배경 원본은 전부 불투명이라 이 검사를 통과하지 못하므로
        // 지금까지 만든 시트는 결과가 그대로다.
        if (HasTransparentBackground(pixels))
        {
            Debug.Log($"[시트 정규화] {sourceName}은 이미 투명 배경이다 — 초록 빼기를 건너뛰고 원본 투명도를 쓴다.");
        }
        else
        {
            RemoveBackground(pixels);

            // 추가 생성(2026-09-21, 초록 번짐) — 초록 배경을 뺀 뒤 가장자리에 남은 초록기를 누른다.
            Despill(pixels);
        }

        // 수정(2026-09-21) — 칸 격자 시트인지 한 번만 판단해서 자르기와 배치 양쪽에 같이 넘긴다.
        // 둘 중 한쪽만 칸 기준이면 자른 조각을 엉뚱한 자리에 놓게 된다.
        bool cellGrid = CellGridSheets.Contains(outputName);

        var figures = FindFigures(pixels, width, height, expectedFrames,
                                  ForceEqualSplit.Contains(outputName), cellGrid);
        if (figures.Count != expectedFrames)
        {
            Debug.LogError($"[시트 정규화] {sourceName}에서 프레임을 {figures.Count}개 만들었는데 " +
                           $"{expectedFrames}개를 기대했다. 원본에 내용이 없거나 배경 판정이 잘못됐다.");
            return;
        }

        Compose(pixels, width, height, figures, folder, outputName, mode, targetHeight, cellGrid);
    }

    // ── 1단계: 배경 제거 ──────────────────────────────────────────────────

    /// <summary>
    /// 추가 생성(2026-09-21) — 이 픽셀 비율보다 많이 투명하면 "이미 투명 배경인 원본"으로 본다.
    ///
    /// 비율로 가르는 이유: 초록 배경 원본은 PNG여도 알파가 전부 255라 투명 픽셀이 0개다.
    /// 투명 원본은 그림 사이 빈 칸이 대부분이라 보통 80~95%가 투명하다(재의 창 원본은 92.8%).
    /// 둘 사이가 이렇게 멀어서 기준을 20%에 두면 어느 쪽도 헷갈리지 않는다.
    /// </summary>
    private const float TransparentBackgroundRatio = 0.2f;

    /// <summary>
    /// 추가 생성(2026-09-21) — 원본이 이미 투명 배경인가.
    ///
    /// 거의 투명한 픽셀(알파 8 미만)을 센다. 생성 그림은 빈 칸에 알파 1~2짜리 먼지가 섞여
    /// 나오기도 해서, 0만 세면 투명 배경인데도 비율이 낮게 나올 수 있다.
    /// </summary>
    private static bool HasTransparentBackground(Color32[] pixels)
    {
        int transparent = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a < 8) transparent++;
        }

        return transparent > pixels.Length * TransparentBackgroundRatio;
    }

    /// <summary>
    /// 초록 배경을 알파로 바꾼다.
    ///
    /// 가장자리에서 섞인 초록을 역산하는 이유: 경계 픽셀은 캐릭터 색이 초록과 섞인 상태다.
    /// 그대로 두면 어두운 던전 배경 위에 얹었을 때 외곽에 초록 테두리가 남는다.
    /// </summary>
    private static void RemoveBackground(Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            int greenness = p.g - Mathf.Max(p.r, p.b);

            if (greenness >= BackgroundGreenness)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
                continue;
            }

            if (greenness <= OpaqueGreenness)
            {
                pixels[i] = new Color32(p.r, p.g, p.b, 255);
                continue;
            }

            float alpha = (BackgroundGreenness - greenness) /
                          (float)(BackgroundGreenness - OpaqueGreenness);

            // 수정(2026-09-21) 메모 — 아래 두 줄은 계산하면 <b>아무것도 안 바꾼다.</b>
            // (g − (1−α)·g) / α = α·g / α = g. 섞인 초록을 빼려던 식인데, 빼는 양을 배경 초록이 아니라
            // 이 픽셀 자신의 g로 잡아서 항등식이 됐다. 전환 그림의 보이는 픽셀 10~21%에 초록기가 남은
            // 원인이다. 실제로 초록을 누르는 일은 이 함수 다음에 불리는 Despill이 한다 — 식을 고치는
            // 대신 따로 둔 이유는 Despill 주석에 적었다.
            float bleed = (1f - alpha) * p.g;
            byte g = (byte)Mathf.Clamp((p.g - bleed) / alpha, 0f, 255f);

            pixels[i] = new Color32(p.r, g, p.b, (byte)Mathf.RoundToInt(alpha * 255f));
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-21, 초록 번짐) — G를 R·B 중 큰 값 아래로 누른다.
    ///
    /// 초록 배경 앞에서 그린 그림은 가장자리와 반사광에 초록이 스민다. 배경색을 역산해 빼는
    /// 정석은 배경 초록이 정확히 한 색이어야 하는데, 생성 그림의 배경은 칸마다 조금씩 다르다.
    /// <b>이 게임 팔레트에는 초록이 없으므로</b> "초록이 R·B보다 튀어나온 만큼은 전부 번진 것"으로
    /// 보고 잘라내는 쪽이 어떤 배경에서도 맞는다. 회색·주황·흰색은 G가 원래 max(R,B) 이하라
    /// 바뀌지 않는다.
    ///
    /// 1페이즈 칼(칼날 전체가 초록이던 것)도 여기서 잿빛 돌칼이 된다 — 2026-09-21 사용자가
    /// "번진 것이라 회색으로"를 골랐다.
    ///
    /// 수정(2026-09-26) — private → internal. 유물 아이콘 다듬기(AshRelicIconProcessor)도 같은 초록 배경 원본을 다루는데
    /// 이 단계가 없어서 아이콘마다 가장자리에 초록 픽셀이 100~600개씩 남아 있었다. 규칙을 두 벌로 적으면 한쪽만 고치게 되므로
    /// 이 함수 하나를 같이 쓴다.
    /// </summary>
    internal static void Despill(Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            if (p.a == 0) continue;

            byte limit = (byte)Mathf.Max(p.r, p.b);
            if (p.g > limit) pixels[i] = new Color32(p.r, limit, p.b, p.a);
        }
    }

    // ── 2단계: 프레임 분리 ────────────────────────────────────────────────

    private struct Figure
    {
        public int MinX, MaxX, MinY, MaxY;
        public int LegCenterX;

        // 추가 생성(2026-09-21) — 칸 격자 시트에서 이 프레임이 원래 있던 원본 칸의 가운데(x).
        // 배치할 때 "칸 가운데에서 얼마나 떨어져 있었나"를 그대로 옮기는 데 쓴다.
        public float CellCenterX;

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
    }

    /// <summary>
    /// 세로줄마다 불투명 픽셀 수를 세어 프레임을 나눈다.
    /// 원하는 프레임 수가 N이면 경계는 N-1개다. 빈 구간을 넓은 순서로 정렬해 위쪽 N-1개를 쓴다.
    /// </summary>
    private static List<Figure> FindFigures(
        Color32[] pixels, int width, int height, int expectedFrames, bool forceEqualSplit,
        bool cellGrid = false)
    {
        var columnCounts = new int[width];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[row + x].a > 16) columnCounts[x]++;
            }
        }

        int contentStart = -1;
        int contentEnd = -1;
        for (int x = 0; x < width; x++)
        {
            if (columnCounts[x] < MinColumnPixels) continue;

            if (contentStart < 0) contentStart = x;
            contentEnd = x;
        }

        var figures = new List<Figure>();
        if (contentStart < 0) return figures;

        // 추가 생성(2026-09-21) — 칸 격자 시트는 원본 칸 경계 근처의 가장 빈 줄에서 자른다.
        // 이유는 CellGridSheets 주석 참고.
        if (cellGrid) return SplitByCellGrid(pixels, width, height, expectedFrames, columnCounts);

        var gaps = new List<(int start, int end)>();
        int gapStart = -1;

        for (int x = contentStart; x <= contentEnd; x++)
        {
            if (columnCounts[x] < MinColumnPixels)
            {
                if (gapStart < 0) gapStart = x;
                continue;
            }

            if (gapStart >= 0)
            {
                gaps.Add((gapStart, x - 1));
                gapStart = -1;
            }
        }

        int needed = expectedFrames - 1;

        if (gaps.Count < needed || forceEqualSplit)
        {
            // 프레임끼리 붙어 있어 나눌 틈이 없다. 대개 같은 간격으로 그려주므로
            // 내용 범위를 균등 분할하는 추정이 잘 맞고, 아무것도 못 하는 것보다 낫다.
            if (!forceEqualSplit)
            {
                Debug.LogWarning($"[시트 정규화] 빈 구간이 {gaps.Count}개뿐이라 {expectedFrames}개로 " +
                                 "나눌 수 없다. 내용 범위를 균등 분할한다.");
            }

            float span = (contentEnd - contentStart + 1) / (float)expectedFrames;
            for (int i = 0; i < expectedFrames; i++)
            {
                int from = contentStart + Mathf.RoundToInt(i * span);
                int to = contentStart + Mathf.RoundToInt((i + 1) * span) - 1;
                figures.Add(MeasureFigure(pixels, width, height, from, Mathf.Min(to, contentEnd)));
            }

            return figures;
        }

        gaps.Sort((a, b) => (b.end - b.start) - (a.end - a.start));
        var separators = gaps.GetRange(0, needed);
        separators.Sort((a, b) => a.start - b.start);

        int figureStart = contentStart;
        foreach (var separator in separators)
        {
            figures.Add(MeasureFigure(pixels, width, height, figureStart, separator.start - 1));
            figureStart = separator.end + 1;
        }

        figures.Add(MeasureFigure(pixels, width, height, figureStart, contentEnd));

        return figures;
    }

    /// <summary>
    /// 추가 생성(2026-09-21) — 원본 칸 격자대로 자른다(<see cref="CellGridSheets"/>).
    ///
    /// 경계는 칸의 배수(width × k ÷ 프레임 수) 근처 ±<see cref="CellCutSearch"/>px 안에서 그림이 가장
    /// 적은 세로줄이다. 딱 배수에서 자르지 않는 이유: 소용돌이 팔이 칸 경계를 몇 px 넘어 그려진
    /// 프레임이 있다. 배수에서 곧장 자르면 그 팔 끝이 옆 칸으로 넘어가 다시 "잘린 조각"이 된다.
    ///
    /// 자른 뒤에는 조각 안에서 실제 그림이 있는 가로 범위로 좁힌다. 배율은 가장 큰 프레임을
    /// 기준으로 정하는데, 빈 여백까지 포함한 칸 폭(362px)으로 재면 그림이 필요 이상 작아진다.
    /// </summary>
    private static List<Figure> SplitByCellGrid(
        Color32[] pixels, int width, int height, int frames, int[] columnCounts)
    {
        var figures = new List<Figure>();
        float cellWidth = width / (float)frames;

        var cuts = new int[frames + 1];
        cuts[0] = 0;
        cuts[frames] = width;
        for (int k = 1; k < frames; k++)
        {
            int center = Mathf.RoundToInt(k * cellWidth);
            int best = center;
            int from = Mathf.Max(1, center - CellCutSearch);
            int to = Mathf.Min(width - 2, center + CellCutSearch);
            for (int x = from; x <= to; x++)
            {
                if (columnCounts[x] < columnCounts[best]) best = x;
            }

            cuts[k] = best;
        }

        for (int i = 0; i < frames; i++)
        {
            int from = cuts[i];
            int to = cuts[i + 1] - 1;

            // 조각 안의 실제 그림 범위로 좁힌다. 티끌을 거르는 기준은 다른 경로와 같은 MinColumnPixels다.
            while (from < to && columnCounts[from] < MinColumnPixels) from++;
            while (to > from && columnCounts[to] < MinColumnPixels) to--;

            Figure figure = MeasureFigure(pixels, width, height, from, to);
            figure.CellCenterX = (i + 0.5f) * cellWidth;
            figures.Add(figure);
        }

        return figures;
    }

    /// <summary>프레임 하나의 세로 범위와 다리 중심을 잰다.</summary>
    private static Figure MeasureFigure(
        Color32[] pixels, int width, int height, int minX, int maxX)
    {
        int minY = height;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                if (pixels[row + x].a <= 16) continue;

                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                break;
            }
        }

        // 수정(발이 아니라 머리를 재던 문제) — 여기 y도 아래에서 센 값이다(Compose 참고).
        // 다리는 <b>작은 y</b> 쪽에 있으므로 minY에서 위로 LegBandRatio만큼만 훑는다.
        // 예전에는 maxY에서 아래로 훑어서 머리·후드 폭의 중심을 다리 중심으로 삼았고,
        // 그래서 어깨나 들어올린 팔이 한쪽으로 쏠린 프레임에서 몸이 옆으로 밀렸다.
        int bandTop = minY + Mathf.RoundToInt((maxY - minY) * LegBandRatio);
        int legMinX = maxX;
        int legMaxX = minX;

        for (int y = minY; y <= bandTop; y++)
        {
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                if (pixels[row + x].a <= 16) continue;

                if (x < legMinX) legMinX = x;
                if (x > legMaxX) legMaxX = x;
            }
        }

        int legCenter = legMaxX >= legMinX ? (legMinX + legMaxX) / 2 : (minX + maxX) / 2;

        return new Figure
        {
            MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, LegCenterX = legCenter,
        };
    }

    // ── 3단계: 재배치 ─────────────────────────────────────────────────────

    private static void Compose(Color32[] pixels, int width, int height, List<Figure> figures,
                                string folder, string outputName, Mode mode, int targetHeight,
                                bool cellGrid = false)
    {
        int cell = AshPlayerSpriteSheets.CellSize;
        int groundY = AshPlayerSpriteSheets.GroundLineY - 1; // 발끝이 놓일 행(216)

        // 배율을 프레임마다 따로 구하면 안 된다. 웅크리는 동작은 일부러 키가 낮고, 이펙트도
        // 1번은 작고 3번이 크게 터지는 게 연출이다. 가장 큰 프레임 기준 하나를 전부에 적용한다.
        int tallest = 0;
        int widest = 0;
        foreach (var f in figures)
        {
            tallest = Mathf.Max(tallest, f.Height);
            widest = Mathf.Max(widest, f.Width);
        }

        float scale;
        if (mode == Mode.Character)
        {
            int wanted = targetHeight > 0 ? targetHeight : AshPlayerSpriteSheets.CharacterPixelHeight;
            scale = tallest > 0 ? wanted / (float)tallest : 1f;
        }
        else
        {
            // 이펙트는 가로로 퍼지기도 세로로 솟기도 해서, 긴 쪽을 기준으로 맞춰야 셀을 안 벗어난다.
            int longest = Mathf.Max(tallest, widest);
            int wanted = targetHeight > 0 ? targetHeight : EffectTargetSize;
            scale = longest > 0 ? wanted / (float)longest : 1f;
        }

        int outWidth = cell * figures.Count;
        var output = new Color32[outWidth * cell];

        // 추가 생성(2026-09-21) — 칸 격자 시트는 <b>모든 프레임이 바닥 하나를 같이 쓴다.</b>
        // 가장 낮게 그려진 프레임의 바닥이 지면선에 닿고, 나머지는 원본에서 그만큼 떠 있던 높이를 지킨다.
        // 프레임마다 자기 바닥을 지면선에 붙이면 공이 커질 때마다 위로 들썩인다.
        int unionBottom = int.MaxValue;
        if (cellGrid)
        {
            foreach (var f in figures) unionBottom = Mathf.Min(unionBottom, f.MinY);
        }

        // 배치 좌표를 여기서 계산해 넘긴다. 계산과 그리기가 한 함수에 섞여 있으면
        // 결과가 어긋났을 때 어느 쪽이 틀린 건지 밖에서 볼 수가 없다.
        for (int i = 0; i < figures.Count; i++)
        {
            Figure figure = figures[i];

            int drawWidth = Mathf.Max(1, Mathf.RoundToInt(figure.Width * scale));
            int drawHeight = Mathf.Max(1, Mathf.RoundToInt(figure.Height * scale));

            // 수정(발이 아니라 머리가 맞춰지던 문제) — 아래 좌표는 <b>위아래가 뒤집혀 있다.</b>
            //
            // Texture2D.GetPixels32는 <b>아래쪽 줄부터</b> 담아준다. 이 배열을 그대로
            // y * width + x로 읽고 쓰기 때문에, 이 함수의 모든 y는 "위에서 몇 번째"가 아니라
            // <b>"아래에서 몇 번째"</b>다. 읽기와 쓰기가 같은 규칙이라 그림이 뒤집혀 나오지는
            // 않지만, 세로 기준점을 잡는 계산만은 뜻이 정반대가 된다.
            //
            // 그래서 예전 식(groundY - drawHeight + 1)은 <b>그림의 꼭대기</b>를 지면선에
            // 맞추고 있었다. 실측하면 프레임마다 위쪽 여백이 39px로 똑같았다 — 키 46px짜리
            // 재 무더기까지 같은 자리에 놓였다. 발이 바닥에서 최대 1.2유닛 떠 보이던 원인이다.
            // 보스 시트에서 "위쪽 기준으로 정렬돼 발이 62px 떴다"며 PowerShell 도구를 따로
            // 만들었던 그 문제와 같은 것이고, 그때는 결과를 고쳤지 원인을 고치지 않았다.
            //
            // 뒤집힌 좌표계에서 발은 아래쪽, 즉 <b>작은 y</b>다. 지면선(위에서 217번째)은
            // 아래에서 세면 cell - GroundLineY = 39이고, 그리기는 destTop에서 위로 올라가며
            // 채우므로 발을 그 자리에 두면 된다. 키와 무관하게 상수인 것이 맞다.
            int groundFromBottom = cell - AshPlayerSpriteSheets.GroundLineY;

            // TipRight도 공중에 뜬 것이라 세로는 가운데다.
            int destTop = mode == Mode.FloatCenter || mode == Mode.TipRight
                ? (cell - drawHeight) / 2
                : groundFromBottom;

            int destLeft;
            if (mode == Mode.Character)
            {
                destLeft = (cell / 2) - Mathf.RoundToInt((figure.LegCenterX - figure.MinX) * scale);
            }
            else if (mode == Mode.GroundForward)
            {
                destLeft = ForwardEffectLeftInset;
            }
            else if (mode == Mode.TipRight)
            {
                // 오른쪽 끝이 TipRightInset에 오도록 왼쪽 시작점을 뒤로 민다.
                // 프레임마다 길이가 달라도 촉 위치는 늘 같은 자리에 온다.
                destLeft = TipRightInset - drawWidth;
            }
            else
            {
                destLeft = (cell - drawWidth) / 2;
            }

            // 추가 생성(2026-09-21, 효과가 흘러감) — 칸 격자 시트는 원본 칸 안의 자리를 그대로 옮긴다.
            //
            // 위의 가운데 맞춤은 <b>그림 크기</b>로 가운데를 잡아서, 소용돌이 팔이 한쪽으로 뻗은 프레임은
            // 중심이 반대로 밀린다. 원본은 칸 가운데를 기준으로 제자리에 그려져 있으므로
            // "칸 가운데에서 얼마나 떨어져 있었나"를 배율만 곱해 옮기면 흔들림이 사라진다.
            if (cellGrid)
            {
                destLeft = (cell / 2) + Mathf.RoundToInt((figure.MinX - figure.CellCenterX) * scale);
                destTop = groundFromBottom + Mathf.RoundToInt((figure.MinY - unionBottom) * scale);
            }

            DrawFigure(pixels, width, height, figure, output, outWidth, cell, i,
                       scale, drawWidth, drawHeight, destTop, destLeft);
        }

        var result = new Texture2D(outWidth, cell, TextureFormat.RGBA32, false);
        result.SetPixels32(output);
        result.Apply();

        string outputPath = $"{folder}/{outputName}";
        byte[] png = result.EncodeToPNG();
        Object.DestroyImmediate(result);

        if (!WritePng(outputPath, png)) return;

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

        string anchor = mode switch
        {
            Mode.Character => $"발끝 y={groundY}, 다리 중심 x={cell / 2}",
            Mode.GroundForward => $"바닥 y={groundY}, 왼쪽 끝 x={ForwardEffectLeftInset} 고정",
            Mode.FloatCenter => $"공중 — 셀 정중앙 ({cell / 2}, {cell / 2})",
            Mode.TipRight => $"촉 끝 x={TipRightInset}, 세로 중앙 y={cell / 2}",
            _ => $"바닥 y={groundY}, 가로 중앙 x={cell / 2}",
        };

        // 넣은 값이 아니라 실제로 그려진 픽셀을 다시 잰다. 배치 계산이 틀리면 넣은 값만
        // 보고는 알 수 없고, 결과 PNG를 밖에서 열어 재봐야 알게 된다.
        var report = new System.Text.StringBuilder();
        for (int i = 0; i < figures.Count; i++)
        {
            int x0 = i * cell;
            int minX = cell, maxX = -1, minY = cell, maxY = -1;

            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    if (output[y * outWidth + x0 + x].a <= 16) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0) { report.Append($"\n  {i}: 비어 있음"); continue; }

            report.Append($"\n  {i}: 가로 {minX}~{maxX} 세로 {minY}~{maxY} " +
                          $"(바닥 y={maxY}, 높이 {maxY - minY + 1})");
        }

        Debug.Log($"[시트 정규화] {outputName} 생성 완료 ({outWidth}x{cell}, {figures.Count}프레임)\n" +
                  $"  공통 배율 {scale:F3} (가장 큰 프레임 {widest}x{tallest}px)\n" +
                  $"  기준 {anchor}\n" +
                  $"  실측 결과:{report}");
    }

    /// <summary>
    /// 프레임 하나를 시킨 자리에 축소해 그려 넣는다.
    /// 배치 좌표는 호출하는 쪽이 정한다 — 여기서는 판단하지 않는다.
    /// </summary>
    private static void DrawFigure(
        Color32[] source, int srcWidth, int srcHeight, Figure figure,
        Color32[] output, int outWidth, int cell, int cellIndex, float scale,
        int drawWidth, int drawHeight, int destTop, int destLeft)
    {
        int cellX = cellIndex * cell;

        for (int dy = 0; dy < drawHeight; dy++)
        {
            int outY = destTop + dy;
            if (outY < 0 || outY >= cell) continue;

            for (int dx = 0; dx < drawWidth; dx++)
            {
                int outX = cellX + destLeft + dx;
                if (outX < cellX || outX >= cellX + cell) continue;

                float srcX = figure.MinX + (dx + 0.5f) / scale;
                float srcY = figure.MinY + (dy + 0.5f) / scale;

                output[outY * outWidth + outX] = SampleBilinear(source, srcWidth, srcHeight, srcX, srcY);
            }
        }
    }

    /// <summary>
    /// 이중선형 보간으로 한 점을 뽑는다.
    ///
    /// 알파를 곱한 상태로 섞는 이유: 투명한 픽셀도 RGB 값을 갖고 있어서, 그냥 섞으면
    /// 가장자리에 투명 영역의 색이 배어 나온다. 알파를 곱해두면 그 기여도가 0이 된다.
    /// </summary>
    private static Color32 SampleBilinear(Color32[] source, int width, int height, float x, float y)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x - 0.5f), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y - 0.5f), 0, height - 1);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);

        float fx = Mathf.Clamp01(x - 0.5f - x0);
        float fy = Mathf.Clamp01(y - 0.5f - y0);

        float r = 0f, g = 0f, b = 0f, a = 0f;

        Accumulate(source[y0 * width + x0], (1f - fx) * (1f - fy), ref r, ref g, ref b, ref a);
        Accumulate(source[y0 * width + x1], fx * (1f - fy), ref r, ref g, ref b, ref a);
        Accumulate(source[y1 * width + x0], (1f - fx) * fy, ref r, ref g, ref b, ref a);
        Accumulate(source[y1 * width + x1], fx * fy, ref r, ref g, ref b, ref a);

        if (a <= 0.001f) return new Color32(0, 0, 0, 0);

        return new Color32(
            (byte)Mathf.Clamp(r / a, 0f, 255f),
            (byte)Mathf.Clamp(g / a, 0f, 255f),
            (byte)Mathf.Clamp(b / a, 0f, 255f),
            (byte)Mathf.Clamp(a * 255f, 0f, 255f));
    }

    private static void Accumulate(
        Color32 pixel, float weight, ref float r, ref float g, ref float b, ref float a)
    {
        float alpha = pixel.a / 255f;
        float w = weight * alpha;

        r += pixel.r * w;
        g += pixel.g * w;
        b += pixel.b * w;
        a += w;
    }
}
