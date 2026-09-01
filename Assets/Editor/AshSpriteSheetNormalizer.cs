using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GPT가 뽑아준 원본 그림을 프로젝트 규격의 스프라이트 시트로 바꾸는 도구.
///
/// 메뉴: Tools → 재의 길 → 원본 시트 정규화
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
        };

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
        ("Assets/Art/Generated", "skill-icons-ember-set.png",
                                 "skill_icons_5frames_1280x256.png", 5, Mode.FloatCenter, 0),

        // ── 잿불 사수(원거리 일반 적) ──
        //
        // 원본이 1536x1024로 왔다. 가로는 맞지만(256x6) 세로가 네 배고, 인물은 위아래
        // 가운데 띠에만 있다. 이 도구를 만든 이유가 정확히 이것이다 — 이미지 생성 모델은
        // 정해진 몇 가지 비율만 낼 수 있어서 6:1 캔버스를 애초에 못 맞춘다.
        // 규격은 코드가 맞추고, 그림에는 초록 배경·같은 키·같은 바닥선만 요구한다.
        //
        // 목표 키는 기본값(160)을 쓴다. 플레이어와 같은 크기다 — 활을 든 인간형이라
        // 덩치로 위협하는 적이 아니고, 크기 차이는 보스가 맡는다(200).
        // 원본이 1536x1024, 1881x836처럼 제각각으로 온다. 가로세로 6:1(또는 4:1) 캔버스를
        // 이미지 생성 모델이 못 맞추기 때문인데, 이 도구가 있는 이유가 정확히 그것이다.
        // 그림에는 초록 배경·같은 키·같은 바닥선만 요구하고 나머지는 코드가 맞춘다.
        //
        // 목표 키는 기본값(160)을 쓴다. 플레이어와 같은 크기다 — 활을 든 인간형이라
        // 덩치로 위협하는 적이 아니고, 크기 차이는 보스가 맡는다(200).
        (EnemyFolder, "ash_marksman_walk_6frames_raw.png",
                      "ash_marksman_walk_6frames_1536x256.png", 6, Mode.Character, 0),
        (EnemyFolder, "ash_marksman_aim_4frames_raw.png",
                      "ash_marksman_aim_4frames_1024x256.png", 4, Mode.Character, 0),

        // 발사 3번째 프레임에 화살이 그려져 있다. 그 화살이 프레임 사이 빈 구간에 걸쳐 있어서
        // 경계 판정이 한 칸 밀릴 수 있다. 프레임 수가 안 맞으면 도구가 에러로 알려주므로
        // 그때 ForceEqualSplit에 넣으면 된다.
        (EnemyFolder, "ash_marksman_shoot_4frames_raw.png",
                      "ash_marksman_shoot_4frames_1024x256.png", 4, Mode.Character, 0),

        // 한 장에 피격 2프레임 + 사망 4프레임. 마지막 칸은 재 무더기라 거의 비어 있는데,
        // 프레임 사이 간격이 넓어서 빈 구간 순위로도 갈린다. 망령 시트와 같은 구성이다.
        (EnemyFolder, "ash_marksman_hit_death_6frames_raw.png",
                      "ash_marksman_hit_death_6frames_1536x256.png", 6, Mode.Character, 0),

        // ── 잿불 자폭병 ──
        //
        // 목표 키를 사수(160)가 아니라 <b>141</b>로 잡는다. 세 시트의 배율을 하나로 묶기 위해서다.
        //
        // 점화 시트는 프레임마다 몸이 부풀어 마지막이 첫 프레임의 1.48배(383 → 566px)다.
        // 배율은 시트 안에서 하나이므로 가장 큰 프레임이 셀에 들어가는지가 상한을 정하는데,
        // 발끝이 y=216이라 위로 쓸 수 있는 것이 216px뿐이고 안전값은 214다. 그 214를 맞추면
        // 점화 첫 프레임은 145px이 된다. 걷기를 160으로 두면 점화가 시작될 때 몸이 9% 작아져
        // <b>쪼그라들었다가 부푸는</b> 그림이 된다 — 예비동작이 가장 크게 읽혀야 하는 적에게
        // 정반대의 신호다.
        //
        // 그래서 점화 첫 프레임(383px)을 145px로 만드는 배율 0.379를 세 시트에 공통으로 적용한다.
        // 걷기의 가장 큰 프레임 373 x 0.379 = 141, 피격·사망은 393 x 0.379 = 149다.
        // 결과적으로 망령(141)과 같은 키가 되는데, 항아리를 끌어안은 뭉툭한 잡몹이라 사수보다
        // 작은 편이 오히려 맞다. 덩치로 위협하는 적이 아니고, 위협은 부푸는 순간에만 나온다.
        (EnemyFolder, "ash_bomber_walk_6frames_raw.png",
                      "ash_bomber_walk_6frames_1536x256.png", 6, Mode.Character, 141),

        // 점화. 214는 셀 상한이다 — 더 키우면 마지막 프레임의 불꽃이 셀 위에서 잘린다.
        (EnemyFolder, "ash_bomber_fuse_4frames_raw.png",
                      "ash_bomber_fuse_4frames_1024x256.png", 4, Mode.Character, 214),

        // 한 장에 피격 2프레임 + 사망 4프레임. 뒤로 갈수록 재 무더기로 낮아진다.
        (EnemyFolder, "ash_bomber_hit_death_6frames_raw.png",
                      "ash_bomber_hit_death_6frames_1536x256.png", 6, Mode.Character, 149),

        // 자폭병의 폭발. 발밑에서 사방으로 퍼진다 — 왕의 잿불과 같은 성격이라 같은 방식이다.
        // 실제 크기는 EnemyBomber의 Explosion Effect Scale이 정한다. 여기서는 셀에 꽉 차지
        // 않게만 두고, 판정 반경(6유닛)에 맞추는 일은 프리팹 쪽 배율 한 곳에서만 한다.
        (VfxFolder, "vfx_bomber_blast_6frames_raw.png",
                    "vfx_bomber_blast_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // R 필살기. 검을 머리 위로 치켜드는 프레임이 있어 목표를 키운다.
        (PlayerFolder, "player_ultimate_kings_ember_execution_6poses_v3_raw.png",
                       "player_ultimate_6frames_1536x256.png", 6, Mode.Character, 210),

        // 왕의 잿불 폭발. 발밑에서 사방으로 퍼진다.
        (VfxFolder, "vfx_kings_ember_full_room_6frames_raw.png",
                    "vfx_kings_ember_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 지팡이 주문은 바닥에서 솟는 잿불 기둥이다. 그 자리에서 위로 퍼진다.
        (VfxFolder, "vfx_ash_staff_ground_spell_6frames_raw.png",
                    "vfx_ash_staff_ground_spell_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 검이 박힌 지점의 충격파. 그 점을 중심으로 사방으로 퍼진다.
        (VfxFolder, "vfx_sword_slam_impact_6frames_raw.png",
                    "vfx_sword_slam_impact_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 검이 박힌 지점에서 앞으로 터져 나간다. 시작점 고정.
        (VfxFolder, "vfx_sword_slam_forward_burst_6frames_raw.png",
                    "vfx_sword_slam_forward_burst_6frames_1536x256.png", 6, Mode.GroundForward, 0),

        // 화살은 공중을 나는 투사체다. 바닥에 닿지 않는다.
        (VfxFolder, "vfx_ember_arrow_flight_6frames_raw.png",
                    "vfx_ember_arrow_flight_6frames_1536x256.png", 6, Mode.FloatCenter, 0),

        (VfxFolder, "vfx_ember_arrow_impact_6frames_raw.png",
                    "vfx_ember_arrow_impact_6frames_1536x256.png", 6, Mode.FloatCenter, 0),

        // 사수가 쏘는 화살. 한 장짜리라 프레임이 1개다.
        // 촉 끝을 기준으로 놓아야 맞는 지점과 눈에 보이는 촉이 일치한다.
        (VfxFolder, "ash_marksman_ember_arrow_raw.png",
                    "ash_marksman_ember_arrow_1frame_256x256.png", 1, Mode.TipRight, 0),

        // vfx_ember_slash_A/B는 검 스킬이 내려찍기로 바뀌면서 쓰지 않는다. 목록에서 뺐다.
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

    [MenuItem("Tools/재의 길/원본 시트 정규화")]
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
    [MenuItem("Tools/재의 길/원본 시트 정규화 (고른 것만)")]
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

        RemoveBackground(pixels);

        var figures = FindFigures(pixels, width, height, expectedFrames,
                                  ForceEqualSplit.Contains(outputName));
        if (figures.Count != expectedFrames)
        {
            Debug.LogError($"[시트 정규화] {sourceName}에서 프레임을 {figures.Count}개 만들었는데 " +
                           $"{expectedFrames}개를 기대했다. 원본에 내용이 없거나 배경 판정이 잘못됐다.");
            return;
        }

        Compose(pixels, width, height, figures, folder, outputName, mode, targetHeight);
    }

    // ── 1단계: 배경 제거 ──────────────────────────────────────────────────

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

            float bleed = (1f - alpha) * p.g;
            byte g = (byte)Mathf.Clamp((p.g - bleed) / alpha, 0f, 255f);

            pixels[i] = new Color32(p.r, g, p.b, (byte)Mathf.RoundToInt(alpha * 255f));
        }
    }

    // ── 2단계: 프레임 분리 ────────────────────────────────────────────────

    private struct Figure
    {
        public int MinX, MaxX, MinY, MaxY;
        public int LegCenterX;

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
    }

    /// <summary>
    /// 세로줄마다 불투명 픽셀 수를 세어 프레임을 나눈다.
    /// 원하는 프레임 수가 N이면 경계는 N-1개다. 빈 구간을 넓은 순서로 정렬해 위쪽 N-1개를 쓴다.
    /// </summary>
    private static List<Figure> FindFigures(
        Color32[] pixels, int width, int height, int expectedFrames, bool forceEqualSplit)
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
                                string folder, string outputName, Mode mode, int targetHeight)
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
