using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 재의 왕(보스) AI.
///
/// <b>망령(<see cref="EnemyWraith"/>)과 따로 만든 이유:</b> 망령은 "발견 → 예비동작 → 돌진"
/// 한 줄기라 상태가 순서대로 흐른다. 보스는 매번 <b>거리를 보고 무엇을 할지 고른다.</b>
/// 망령에 패턴 선택과 페이즈 전환을 끼워 넣으면 그 클래스가 두 종류의 AI를 겸하게 되고,
/// 일반 몹을 손볼 때마다 보스가 깨지는지 확인해야 한다.
///
/// <b>패턴을 거리로 가르는 것이 이 보스의 전부다.</b>
/// 붙으면 내려찍기, 떨어지면 잿불 파도. 한 자리에 서 있으면 안 되게 만드는 장치다.
/// 둘 다 예비동작이 애니메이션에 들어 있어서, 플레이어는 모션을 보고 빠질 수 있다.
///
/// 페이즈 전환은 <b>컨트롤러를 갈아 끼운다.</b> 오브젝트도 Health도 그대로라
/// 진행 중인 체력과 위치가 안 끊긴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class EnemyBoss : MonoBehaviour
{
    private enum State { Idle, Chase, Attack, Transition, Hit, Dead }

    [Header("페이즈")]
    [Tooltip("2페이즈에서 쓸 컨트롤러. 체력이 절반이 되면 갈아 끼운다.")]
    [SerializeField] private RuntimeAnimatorController phase2Controller;

    // 수정(밸런스) — 0.5 → 0.7. 체력도 40 → 90으로 올렸다.
    //
    // <b>두 페이즈의 몫을 일부러 어긋나게 나눈다.</b> 0.5면 두 페이즈가 정확히 반반인데,
    // 그러면 2페이즈가 "체력이 한 번 더 있는 1페이즈"가 된다. 실제로 어려운 쪽은 2페이즈다
    // — 재 폭발이 그때만 나오고, 이동과 쿨다운도 빨라진다.
    //
    // 0.7이면 1페이즈가 전체의 30%(27), 2페이즈가 70%(63)를 맡는다. 예전 수치(반반, 20/20)와
    // 견주면 1페이즈는 27로 조금 길어질 뿐이고 <b>2페이즈만 3배가 된다.</b>
    // 노리는 그림이 그것이다 — 앞은 배우는 구간, 뒤는 버티는 구간.
    //
    // 체력바는 페이즈마다 새로 그리므로 이 비대칭이 화면에 드러나지 않는다.
    // 두 페이즈 다 가득 찬 바에서 0까지 내려간다.
    [Tooltip("이 비율 이하로 떨어지면 2페이즈로 넘어간다. 0.7이면 1페이즈가 30%, 2페이즈가 70%를 맡는다.")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float phase2HealthRatio = 0.7f;

    // 수정(Timeline 도입) — transitionSeconds 필드를 지웠다.
    //
    // 예전 기록: 0.75 → 0.875로 고친 적이 있다. ashking_transition 클립이 8fps 7프레임이라
    // 0.875초인데 코드가 0.75초만 기다려서, 갑옷이 무너지는 <b>마지막 한 프레임이 재생되기
    // 전에</b> 컨트롤러가 갈아 끼워졌다. 보스전의 유일한 절정을 0.125초 차이로 아무도 못
    // 보고 있었다.
    //
    // 그 사고의 뿌리는 <b>같은 시간이 두 곳(클립과 인스펙터)에 적혀 있던 것</b>이다. 이제
    // 연출 길이의 주인은 타임라인 에셋 하나뿐이고, 보스는 BossTransitionSequence.TotalSeconds를
    // 물어본다. 두 숫자가 갈라질 자리가 없어졌다.

    // 추가 생성 — 2페이즈에서 공격 모션 시간에 곱할 값.
    //
    // 왜 필요한가: 아래 slamMotionSeconds·waveMotionSeconds는 1페이즈 클립에 맞춘 숫자다.
    // 그런데 2페이즈 클립은 15fps로 뽑아서 1페이즈(12fps)보다 짧다. 프레임 수는 7로 같으므로
    // 길이 비가 12/15 = 0.8로 딱 떨어진다.
    //
    // 이 값이 없을 때 실제로 어떻게 보였나: 2페이즈 내려찍기 클립은 0.467초인데 코드가 0.6초를
    // 기다려서 보스가 <b>마지막 프레임에서 0.13초 굳어 있었다.</b> 파도는 0.23초였다.
    // "2페이즈는 빨라진다"고 만들어놓고 정작 2페이즈가 더 오래 멈춰 있는 상태였다.
    //
    // 이동 속도(phase2SpeedScale)나 쿨다운(phase2CooldownScale)과 합치지 않은 이유:
    // 저 둘은 <b>연출 의도</b>이고(더 사납게), 이건 <b>클립 길이라는 사실</b>이다.
    // 사납기를 조절하려고 배율을 만졌다가 애니메이션이 어긋나면 원인을 찾을 수 없다.
    [Tooltip("2페이즈에서 공격 모션 시간에 곱할 값. 2페이즈 클립이 15fps라 1페이즈(12fps)의 0.8배다.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float phase2MotionScale = 0.8f;

    [Header("이동")]
    // 수정(보스가 플레이어에게 못 붙음): 4.5 → 7.
    //
    // 2페이즈에서 거리를 1초마다 찍어보니 5.3~12.5에서만 놀았다. 근접 패턴 사거리가
    // 내려찍기 5, 재 폭발 5인데 <b>거리가 한 번도 5 아래로 안 내려갔다.</b>
    // 그래서 실제로 나오는 패턴이 사거리 20짜리 파도 하나뿐이었다.
    //
    // 원인은 단순하다. 보스 4.5 × 2페이즈 1.4 = 6.3인데 플레이어는 14다. 두 배 넘게 빠르니
    // 쫓아가는 것 자체가 성립하지 않는다. 사거리를 늘려서 맞추는 방법도 있지만, 그러면
    // "붙어서 싸우는 보스"가 "멀리서도 때리는 보스"로 바뀌어 그림과 어긋난다.
    //
    // 7이면 2페이즈에서 9.8이다. 여전히 플레이어보다 느려서 <b>도망은 갈 수 있다.</b>
    // 대신 플레이어가 공격하려고 멈추는 순간에는 붙는다. 그 창을 만드는 것이 목적이다.
    [Tooltip("이동 속도. 플레이어(14)보다 느려야 도망갈 수 있지만, 너무 느리면 근접 패턴이 " +
             "영영 사거리에 못 들어온다.")]
    [SerializeField] private float moveSpeed = 7f;

    [Tooltip("2페이즈에서 이동 속도에 곱할 값.")]
    [SerializeField] private float phase2SpeedScale = 1.4f;

    // 추가 생성 — 2페이즈에서 몸통 콜라이더 가로에 곱할 값.
    //
    // 숫자의 근거는 그림이다. 시트의 불투명 픽셀을 재보면 1페이즈 idle이 폭 150px,
    // 2페이즈 idle이 131px로 <b>131 ÷ 150 ≒ 0.87</b>이다.
    //
    // 세로를 안 줄이는 이유도 같은 측정에서 나왔다. 두 페이즈 모두 몸 높이가 200px로 같다.
    // "거체가 무너지고 작아진다"는 기획 문장을 콜라이더 전체 축소로 옮기면 그림과 어긋나서,
    // 2페이즈에서 <b>머리 쪽을 때렸는데 안 맞는</b> 반대 문제가 생긴다. 실제로 줄어든 것은
    // 폭이므로 폭만 줄인다.
    [Tooltip("2페이즈에서 몸통 콜라이더 가로에 곱할 값. 시트에서 잰 폭 비(131÷150)다. " +
             "세로는 두 페이즈의 몸 높이가 같아서 건드리지 않는다.")]
    [Range(0.3f, 1f)]
    [SerializeField] private float phase2ColliderWidthScale = 0.87f;

    [Tooltip("이 거리보다 가까우면 공격 사이에 뒤로 물러난다. 내려찍기 사거리보다 작아야 한다.")]
    [SerializeField] private float retreatDistance = 3.5f;

    [Tooltip("물러날 때의 속도 배율. 1이면 플레이어가 영영 못 따라잡는다.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float retreatSpeedScale = 0.55f;

    [Header("내려찍기")]
    [Tooltip("이 거리 안이면 내려찍기를 고른다.")]
    [SerializeField] private float slamRange = 5f;

    [Tooltip("모션 시작부터 판정이 나가기까지(초). 예비동작 길이다. " +
             "ashking_slam의 4번째 프레임(0.333)에 맞춰 검이 바닥에 닿는 순간이다.")]
    [SerializeField] private float slamHitDelay = 0.33f;

    // 수정(타이밍 정합): 0.6 → 0.583. 클립 길이 그대로다.
    // 어차피 컨트롤러의 전이가 ExitTime 1이라 클립은 끝까지 재생된다. 코드가 더 오래 잠그면
    // 그 차이만큼 마지막 프레임에서 굳어 있고, 짧게 잠그면 공격 그림 위로 보스가 걷는다.
    [Tooltip("모션 전체 길이(초). ashking_slam 클립 길이(7프레임 ÷ 12fps = 0.583)와 맞춘다.")]
    [SerializeField] private float slamMotionSeconds = 0.583f;

    [SerializeField] private Vector2 slamHitSize = new Vector2(7f, 5f);
    [SerializeField] private int slamDamage = 2;

    [Header("잿불 파도")]
    [Tooltip("멀리 있을 때 쓴다. 비어 있으면 이 패턴을 건너뛴다.")]
    [SerializeField] private Projectile wavePrefab;

    // 추가 생성 — 잿불 파도를 쓸 수 있는 최대 거리.
    //
    // 왜 필요한가: 예전에는 "내려찍기 사거리 밖"이 곧 파도 조건이었다. 그런데 쿨다운 중에
    // 보스가 계속 다가오기 때문에, 쿨다운이 끝나는 순간에는 거의 항상 사거리 안이었다.
    // 그래서 파도는 사실상 한 번도 나오지 않았다. 이제 두 패턴의 사거리를 겹쳐두고
    // 겹치는 구간에서는 번갈아 쓴다.
    [Tooltip("잿불 파도를 쓸 수 있는 최대 거리. 내려찍기 사거리보다 커야 두 패턴이 섞인다.")]
    [SerializeField] private float waveRange = 20f;

    // 추가 생성 — 파도 전용 쿨다운.
    //
    // 왜 공용 쿨다운으로는 부족한가: 공용 쿨다운(attackCooldown)은 "공격 후 쉬는 시간"이라
    // 1.1초로 짧다. 두 패턴을 번갈아 쓰게 하면 파도가 2.2초마다 나오는데, 화면을 가로지르는
    // 광역 패턴이 그 빈도로 나오면 <b>평타처럼 보인다.</b> 보스 패턴은 가끔 나와서 예비동작을
    // 읽고 대비하는 맛이 있어야 한다. 그래서 파도만 따로 훨씬 긴 쿨다운을 둔다.
    [Tooltip("잿불 파도를 다시 쓰기까지의 시간(초). 이 값이 파도 빈도를 정한다.")]
    [SerializeField, Min(0f)] private float waveCooldown = 6f;

    [Tooltip("모션 시작부터 발사까지(초). ashking_wave의 5번째 프레임(0.417) 근처다.")]
    [SerializeField] private float waveFireDelay = 0.4f;

    // 수정(타이밍 정합): 0.7 → 0.583. 내려찍기와 같은 이유다.
    [Tooltip("모션 전체 길이(초). ashking_wave 클립 길이(7프레임 ÷ 12fps = 0.583)와 맞춘다.")]
    [SerializeField] private float waveMotionSeconds = 0.583f;

    [Tooltip("한 번에 나가는 발수. 2페이즈에서는 여기에 2가 더해진다.")]
    [SerializeField] private int waveCount = 3;

    [Tooltip("발 사이 각도(도).")]
    [SerializeField] private float waveSpreadDegrees = 18f;

    [SerializeField] private int waveDamage = 1;
    [SerializeField] private float waveSpawnHeight = 1.6f;

    // 추가 생성 — 2페이즈 전용 궁극기. 기획 4패턴 중 "재 폭발"이다.
    //
    // <b>세 번째 회피 축을 만드는 것이 이 패턴의 목적이다.</b>
    // 내려찍기는 조준한 방향으로 상자가 나가므로 <b>옆으로</b> 빠져서 피한다.
    // 잿불 파도는 날아오는 것이라 <b>사이로</b> 피한다.
    // 재 폭발은 보스를 중심으로 사방에 나가서 방향으로는 못 피한다 — <b>멀어져야</b> 피한다.
    // 셋이 같은 회피법을 공유하면 패턴을 늘려도 플레이어가 하는 일은 안 늘어난다.
    //
    // 판정을 조준하지 않는 것이 핵심이라, 이 패턴에는 AimFrom도 slamAim도 쓰지 않는다.
    [Header("재 폭발 (2페이즈 궁극기)")]
    // 수정(화면 전체 판정으로 변경): 9 → 12. 판정 반경과 같은 값이다.
    //
    // 이 값이 걸어온 길을 남겨둔다. 처음 4는 물러나는 거리 3.5와의 0.5유닛 창을 노리는 셈이라
    // 한 번도 안 걸렸다. 9로 늘리고 <b>달려들어 거리를 좁히는</b> 방식으로 맞췄다.
    // 지금은 판정이 화면 전체라 좁힐 거리가 없다 — 어디서 걸리든 닿으므로 반경과 같게 둔다.
    [Tooltip("2페이즈에서 이 거리 안에 플레이어가 있을 때 고른다. " +
             "판정이 화면 전체라 반경과 같은 값이면 된다.")]
    [SerializeField] private float ultimateRange = 12f;

    // 추가 생성 — 예비동작 동안 플레이어 쪽으로 달려드는 속도.
    //
    // 수정(화면 전체 판정으로 변경): 14 → 0. <b>달려들 이유가 사라졌다.</b>
    //
    // 반경 5이던 시절에는 이 값이 회피 규칙 자체였다. 플레이어 이동 속도와 같은 14로 두면
    // "예비동작 보고 반대로 뛰면 거리가 유지되어 산다"가 성립했다. 판정이 화면을 덮는 지금은
    // 뛰어봐야 범위 안이라 달려드는 것이 연출도 규칙도 아니게 됐다. 남겨두면 <b>이유 없이
    // 플레이어를 덮치는 움직임</b>만 남는다.
    //
    // 0이 아닌 값을 다시 넣으려면 반경을 근접 크기로 되돌리는 것이 먼저다. 둘은 한 쌍이다.
    [Tooltip("예비동작 동안 플레이어 쪽으로 달려드는 속도(유닛/초). 0이면 제자리에서 터뜨린다. " +
             "판정이 화면 전체면 좁힐 거리가 없으므로 0이 맞다.")]
    [SerializeField, Min(0f)] private float ultimateLungeSpeed = 0f;

    // 수정(방 대부분을 덮는 판정): 5 → 12.
    //
    // 근거는 <b>화면이 아니라 방 바닥</b>이다. 보스 방 바닥은 21.5 × 23.1유닛이라
    // 중앙에서 좌우 끝이 10.75, 상하 끝이 11.55, <b>모서리가 15.8</b>이다.
    // 12는 그 사이 값이라 상하좌우 끝은 덮으면서 <b>네 모서리는 안전지대로 남는다.</b>
    //
    // 모서리를 일부러 남기는 이유: 다 덮으면(반경 16) 회피 수단이 대시 무적 0.25초 하나뿐이다.
    // 대시는 이동에도 쓰는 자원이라 스태미나가 비어 있는 순간이 자주 오고, 그때는 회피 불가
    // 3피해가 된다(최대 체력 5). 모서리가 살아 있으면 <b>보스에게서 멀어진다</b>는 답이 하나 더 생기고,
    // 보스가 구석에 설수록 반대편이 넓게 안전해져서 위치 싸움도 같이 생긴다.
    //
    // 카메라로 재지 않는 이유: 씬의 orthographic size가 14라 화면은 49.8 × 28유닛이다.
    // 방보다 훨씬 넓어서 방 전체가 한 화면에 들어온다. 즉 이 게임에서 "화면 전체"는
    // 판정 기준이 될 수 없다 — 기준은 <b>플레이어가 실제로 서 있을 수 있는 바닥</b>이다.
    // (AshProjectSetup의 상수는 5.625인데 씬은 14다. 그 도구를 다시 돌리면 카메라가
    // 확 당겨지므로, 보스 방 구도를 확인하고 나서 돌려야 한다.)
    //
    // <b>시트 그림보다 훨씬 크다는 점을 알고 쓴다.</b> 폭발 프레임의 그림 폭은 9.6유닛(반경 4.8)이라
    // 판정의 절반도 안 된다. 그래서 <see cref="ultimateEffectPrefab"/>이 필수가 됐다 —
    // 이제 이펙트가 "어디까지 맞는지"를 알려주는 유일한 수단이다.
    [Tooltip("보스를 중심으로 한 판정 반경(유닛). 12면 방 바닥(21.5 × 23.1)의 상하좌우 끝까지 " +
             "닿고 네 모서리만 안전하게 남는다. 시트 그림보다 크므로 이펙트로 범위를 보여줘야 한다.")]
    [SerializeField] private float ultimateRadius = 12f;

    // 수정(화면 전체 판정): 0.3 → 0.5. 클립을 10fps에서 6fps로 늦춘 것과 한 쌍이다.
    //
    // 판정이 화면을 덮으면서 <b>회피 수단이 대시 무적 0.25초 하나</b>로 줄었다. 0.3초
    // 예비동작으로는 보고 나서 누를 시간이 없다. 0.5면 무적 시간의 두 배라 여유가 생긴다.
    [Tooltip("모션 시작부터 판정까지(초). ashking2_ultimate의 4번째 프레임(3 ÷ 6fps = 0.5)에서 " +
             "발밑이 터진다. 이 시간 안에 대시를 눌러야 산다.")]
    [SerializeField] private float ultimateHitDelay = 0.5f;

    [Tooltip("모션 전체 길이(초). ashking2_ultimate 클립 길이(6프레임 ÷ 6fps = 1.0)와 맞춘다.")]
    [SerializeField] private float ultimateMotionSeconds = 1f;

    [Tooltip("맞았을 때 피해량. 내려찍기(2)보다 크다 — 예비동작이 길고 피할 수 있는 대신 아프다.")]
    [SerializeField] private int ultimateDamage = 3;

    [Tooltip("다시 쓰기까지의 시간(초). 파도(6초)보다 훨씬 길어야 '가끔 나오는 큰 것'이 된다.")]
    [SerializeField, Min(0f)] private float ultimateCooldown = 12f;

    // 추가 생성 — 판정 순간에 터뜨릴 이펙트 프리팹.
    //
    // 왜 시트 그림만으로는 부족한가: 궁극기 클립은 10fps라 <b>폭발 프레임이 0.1초</b>다.
    // 그 사이에 화면에서 무슨 일이 있었는지 읽을 수가 없다. fps를 낮춰도 한 프레임은
    // 0.15초 남짓이라 한계가 같다.
    //
    // 더 큰 문제는 크기다. 시트의 폭발은 256px 셀 안에 그려져 있어서 아무리 커도 셀을 못 넘는데,
    // 실제 판정은 반경 5(지름 10유닛)다. <b>맞은 범위와 보이는 범위가 다르면</b> 플레이어는
    // 이 패턴을 배울 수 없다. 문서에 적어둔 "큰 궤적은 별도 VFX로 분리한다"가 이 경우다.
    //
    // 프리팹 쪽에 수명·프레임이 다 들어 있어서(SpriteFrameAnimator + destroyWhenFinished)
    // 여기서는 만들어 놓기만 하면 된다. Q 스킬 이펙트와 같은 구조다.
    [Tooltip("판정 순간에 터뜨릴 이펙트. 비우면 시트의 폭발 프레임만 0.1초 보인다.")]
    [SerializeField] private GameObject ultimateEffectPrefab;

    // 배율을 눈대중이 아니라 계산으로 잡을 수 있게 근거를 남긴다.
    //
    // KingsEmber 기준: 시트 셀은 256px(PPU 32 = 8유닛)인데 <b>그림은 셀을 다 안 채운다.</b>
    // 가장 큰 프레임이 158px = 4.94유닛이다. 프리팹 자체 스케일이 4.5이므로
    // 배율 1에서 실제로 보이는 지름은 4.94 × 4.5 ≈ <b>22.2유닛</b>이다.
    //
    //     필요한 배율 = (판정 반경 × 2) ÷ 22.2
    //
    // 반경 12면 24 ÷ 22.2 ≈ 1.08, 반경 16이면 32 ÷ 22.2 ≈ 1.44다.
    // 셀 크기(8유닛)로 계산하면 배율이 1.6배 작게 나온다 — 안 보이는 여백까지 그림으로 세는 셈이라
    // 이펙트가 판정보다 한참 작아진다. 처음에 그렇게 계산해서 한 번 틀렸다.
    [Tooltip("이펙트 크기 배율. (판정 반경 × 2) ÷ 22.2 이 계산값이고, 씬 뷰의 진한 주황 원과 " +
             "눈으로 확인한다. 이펙트가 판정보다 작으면 '안 맞을 줄 알았는데 맞는' 패턴이 된다.")]
    [SerializeField, Min(0.05f)] private float ultimateEffectScale = 1.08f;

    // 추가 생성 — 2페이즈에 들어선 뒤 첫 재 폭발까지의 유예.
    //
    // 왜 필요한가: 전환 연출 동안 보스는 무적이라 플레이어는 대개 <b>바로 옆에 붙어 있다.</b>
    // 유예 없이 쿨다운을 0으로 두면 변신 직후에 회피 불가능한 3피해가 꽂힌다.
    //
    // 수정(궁극기가 안 나옴): 4 → 1.5.
    //
    // 4초로 뒀더니 <b>한 번도 못 보고 보스가 죽었다.</b> 2페이즈 체력은 20인데(보스 40의 절반)
    // 평타만 3.1DPS(2딜 ÷ 0.65초)라 6.5초, R(8)과 E(4)를 먼저 꽂으면 3초도 안 걸린다.
    // 유예가 2페이즈 길이와 비슷하면 "가끔 나오는 큰 것"이 아니라 "안 나오는 것"이 된다.
    //
    // 1.5초의 근거: 전환 연출 0.875초가 끝난 시점부터 세는 값이고, 예비동작이 0.3초 더 있다.
    // 붙어 있던 플레이어도 1.8초면 반경 밖으로 나갈 거리(이동속도 14면 4.2유닛 이상)를
    // 충분히 움직인 뒤다. 회피 불가를 막는다는 원래 목적은 그대로 지켜진다.
    [Tooltip("2페이즈 시작 후 첫 재 폭발까지의 유예(초). 변신 직후 회피 불가 피해를 막되, " +
             "2페이즈가 짧아서 한 번도 안 나오는 일이 없을 만큼만 짧게 둔다.")]
    [SerializeField, Min(0f)] private float ultimateFirstDelay = 1.5f;

    [Header("공통")]
    [Tooltip("공격이 끝난 뒤 다음 공격까지 쉬는 시간(초). 없으면 쉴 틈 없이 맞는다.")]
    [SerializeField] private float attackCooldown = 1.1f;

    [Tooltip("2페이즈에서 쉬는 시간에 곱할 값. 작을수록 사납다.")]
    [SerializeField] private float phase2CooldownScale = 0.6f;

    // 수정(타이밍 정합): 0.12 → 0.125. 코드가 아니라 <b>클립을 코드에 맞췄다.</b>
    //
    // 어긋난 상황: 피격 클립이 10fps 3프레임(0.3초)인데 경직은 0.12초였다. 컨트롤러의 전이가
    // ExitTime 1이라 클립은 0.3초를 다 재생하므로, 나머지 0.18초 동안 <b>움찔하는 그림 위로
    // 보스가 걸어다녔다.</b>
    //
    // 클립을 늘리지 않고 줄인 이유: 0.3초 경직은 Health의 피격 무적 0.35초와 거의 같아서,
    // 플레이어가 계속 때리면 보스가 <b>서 있는 시간의 86%를 경직으로 보낸다.</b> 그건 위 툴팁이
    // 경계하는 바로 그 상태다. 그래서 클립 쪽을 24fps로 다시 잡아 0.125초로 만들었다
    // (플레이어 Dash 클립을 16 → 24fps로 줄인 것과 같은 처리다).
    [Tooltip("피격 경직 시간(초). 보스는 짧아야 한다 — 길면 연타로 아무것도 못 하게 된다. " +
             "ashking_hit 클립 길이(3프레임 ÷ 24fps = 0.125)와 맞춘다.")]
    [SerializeField] private float hitStunSeconds = 0.125f;

    [SerializeField] private LayerMask playerLayer;

    private Rigidbody2D body;
    private Health health;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Transform player;

    // 추가 생성 — 몸통(피격받는) 콜라이더. 2페이즈에서 폭을 줄이려고 들고 있는다.
    private CapsuleCollider2D bodyCollider;

    // 추가 생성 — 2페이즈 전환 연출. 시간표는 저쪽(타임라인 에셋)이 들고 있고,
    // 이 클래스는 "시작해라"와 "얼마나 걸리냐"만 주고받는다.
    private BossTransitionSequence transitionSequence;

    // 추가 생성 — 조준할 때 겨눌 플레이어 콜라이더. 발밑(Transform)이 아니라 이쪽을 노린다.
    //
    // 왜 필요한가: 이 게임은 발바닥을 원점으로 쓴다. 그래서 Transform 위치는 <b>맞아야 할 몸이
    // 아니라 서 있는 바닥</b>이다. 잿불 파도처럼 높이를 두고 나가는 것은 그 차이만큼 빗나간다.
    private Collider2D playerCollider;

    private State state = State.Idle;
    private bool isPhase2;

    // 추가 생성(전환이 두 번 돌았다) — 전환 연출을 한 번이라도 시작했는가.
    //
    // isPhase2와 나눠둔 이유는 OnDamaged의 검사 자리에 적어뒀다. 한 줄로 줄이면:
    // <b>연출이 실패해도 두 번 하지는 않는다.</b>
    private bool transitionStarted;

    // 추가 생성(시그널 유실 안전망) — 밖에 2페이즈를 알린 적이 있는가.
    //
    // isPhase2와 또 나눈 이유: 알리는 시각(계획표 2.375)과 몸이 실제로 바뀌는 시각(2.875)이
    // 다르다. 하나로 묶으면 아래 안전망이 둘 중 하나를 <b>반드시 두 번</b> 부르게 된다.
    private bool announcedPhase2;

    private float cooldownTimer;

    /// <summary>
    /// 추가 생성 — 2페이즈로 넘어가는 체력 비율. 체력바가 눈금을 어디에 그릴지 정할 때 읽는다.
    ///
    /// 값을 밖으로 내주기만 하고 바꾸지는 못하게 둔다. 이 수치의 주인은 보스다. UI가 이걸
    /// 고칠 수 있으면 "화면에 보이는 눈금"과 "실제로 전환되는 지점"이 갈라질 수 있는데,
    /// 그때 무엇이 맞는지 정할 방법이 없다.
    /// </summary>
    public float Phase2HealthRatio => phase2HealthRatio;

    /// <summary>
    /// 추가 생성 — 2페이즈 연출이 끝나고 실제로 넘어간 순간에 울린다.
    ///
    /// 보스가 화면을 직접 건드리지 않게 하려고 이벤트로 뺐다. 사망을 <see cref="Health"/>가
    /// 알리고 방(<see cref="BossEncounter"/>)이 받아 처리하는 것과 같은 구조다.
    /// 이 클래스는 <b>누가 듣는지 모른다.</b>
    ///
    /// 전환이 <b>시작될 때</b>가 아니라 <b>껍질이 깨질 때</b> 울린다. 시작할 때 울리면
    /// 갑옷이 무너지는 것을 보기도 전에 바 색이 먼저 바뀌어 결과를 미리 말해버린다.
    ///
    /// <b>수정(Timeline 도입) — 울리는 시각이 연출 끝(3.125)에서 2.375로 당겨졌다.</b>
    ///
    /// 이벤트를 새로 만들지 않고 <b>발화 시점만</b> 옮겼다. 이 이벤트가 실제로 뜻하는 것은
    /// "이제 2페이즈라고 화면에 말해도 되는 순간"이고, 그건 계획표에서 플래시가 터지고
    /// 이름이 바뀌는 2.375다. 3.125는 무적을 푸는 <b>보스 내부 사정</b>이라 밖에 알릴 것이 없다.
    /// 두 순간에 각각 이벤트를 두면 그중 하나는 듣는 데가 없는 채로 남는다.
    ///
    /// 자세한 이유는 <see cref="OnTransitionRevealed"/>에 적어뒀다.
    /// </summary>
    public event Action EnteredPhase2;

    // 추가 생성 — 파도를 다시 쓸 수 있을 때까지 남은 시간.
    private float waveCooldownTimer;

    // 추가 생성 — 재 폭발을 다시 쓸 수 있을 때까지 남은 시간.
    private float ultimateCooldownTimer;

    // 추가 생성 — 이번 내려찍기가 노리는 방향. 예비동작이 시작될 때 고정하고 판정 때 그대로 쓴다.
    //
    // 지역 변수가 아니라 필드로 둔 이유: 기즈모가 같은 값을 그려야 인스펙터에서 눈으로 보는
    // 범위와 실제 판정이 일치한다. 판정 범위를 맞추는 도구가 실제와 다르면 없느니만 못하다.
    private Vector2 slamAim = Vector2.right;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int SlamHash = Animator.StringToHash("Slam");
    private static readonly int WaveHash = Animator.StringToHash("Wave");
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private static readonly int DieHash = Animator.StringToHash("Die");
    private static readonly int TransitionHash = Animator.StringToHash("Transition");

    // 추가 생성 — 재 폭발 트리거. 이 상태는 2페이즈 컨트롤러에만 있다.
    private static readonly int UltimateHash = Animator.StringToHash("Ultimate");

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // 추가 생성 — 몸통 콜라이더. 없어도 동작하지만 2페이즈에서 몸이 안 줄어든다.
        bodyCollider = GetComponent<CapsuleCollider2D>();

        // 추가 생성 — 전환 연출. 없으면 연출 없이 페이즈만 바뀐다(EnterPhase2 참고).
        transitionSequence = GetComponent<BossTransitionSequence>();
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;

        // 추가 생성 — 연출의 세 순간을 받는다.
        //
        // 연출이 보스의 몸을 직접 만지지 않게 하려고 이렇게 나눴다. 스프라이트와 애니메이터는
        // 보스의 것이라 <b>연출이 중간에 끊겼을 때 되돌릴 책임도 보스에게</b> 있어야 한다.
        // 연출이 남의 몸을 껐다가 자기가 멈춰버리면 꺼진 채로 남는 길이 생긴다.
        if (transitionSequence == null) return;

        transitionSequence.ArmorBroken += OnTransitionArmorBroken;
        transitionSequence.Revealed += OnTransitionRevealed;
        transitionSequence.BossReturns += OnTransitionBossReturns;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;

        if (transitionSequence == null) return;

        transitionSequence.ArmorBroken -= OnTransitionArmorBroken;
        transitionSequence.Revealed -= OnTransitionRevealed;
        transitionSequence.BossReturns -= OnTransitionBossReturns;
    }

    private void Start()
    {
        // 플레이어는 프리팹 인스턴스라 인스펙터로 미리 연결할 수 없다.
        // Include가 필요하다 — 연출 중에 잠깐 꺼져 있으면 못 찾고 영영 가만히 서 있게 된다.
        var controller = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (controller != null)
        {
            player = controller.transform;

            // 추가 생성 — 플레이어 오브젝트에는 콜라이더가 여럿 붙어 있다(몸통, 공격 히트박스).
            // 이름이나 순서로 고르지 않고 <b>playerLayer 마스크에 걸리는 것</b>을 고른다.
            // 내려찍기 판정이 쓰는 마스크와 같은 값이라, 둘이 서로 다른 것을 겨눌 수가 없다.
            foreach (Collider2D candidate in controller.GetComponentsInChildren<Collider2D>(true))
            {
                if ((playerLayer.value & (1 << candidate.gameObject.layer)) == 0) continue;

                playerCollider = candidate;
                break;
            }
        }

        if (player == null)
            Debug.LogWarning("[보스] 플레이어를 못 찾았다. 그 자리에 서 있게 된다.", this);
    }

    private void Update()
    {
        // 수정(주석과 동작 불일치) — 파도 쿨다운을 상태 검사보다 <b>위로</b> 올렸다.
        //
        // 예전에는 아래 return 뒤에 있으면서 주석만 "공격 중에도 계속 흐른다"고 적혀 있었다.
        // 실제로는 공격·경직·전환 중에 멈춰 있었고, 그래서 인스펙터의 6초는 <b>쉬는 시간 6초</b>를
        // 뜻했다. 패턴 하나가 0.58초씩 걸리니 체감 주기는 7~8초까지 늘어난다.
        // waveCooldown을 시전 <b>시작</b> 시점에 거는 이유(주기를 인스펙터 숫자와 맞추기 위해)와
        // 정면으로 어긋나던 자리다.
        if (waveCooldownTimer > 0f) waveCooldownTimer -= Time.deltaTime;

        // 추가 생성 — 재 폭발 쿨다운도 같은 자리에서 흐른다.
        // 파도와 같은 이유다. 상태와 무관하게 흘러야 인스펙터에 적은 초가 실제 주기와 맞는다.
        if (ultimateCooldownTimer > 0f) ultimateCooldownTimer -= Time.deltaTime;

        if (state == State.Dead || state == State.Attack ||
            state == State.Transition || state == State.Hit) return;

        if (player == null) { Stop(); return; }

        // 공격 쿨다운은 여기 그대로 둔다. 이 값은 EndAttack이 걸고 아래 "쉬는 동안 자리를 다시
        // 잡는다" 분기에서만 쓰이므로, 쉬는 상태에서만 흘러도 뜻이 어긋나지 않는다.
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        Vector2 toPlayer = player.position - transform.position;
        float distance = toPlayer.magnitude;

        FaceTowards(toPlayer.x);

        // 쉬는 동안은 자리를 다시 잡는다. 이 틈이 없으면 플레이어가 반격할 자리가 사라진다.
        if (cooldownTimer > 0f) { Reposition(toPlayer, distance); return; }

        ChoosePattern(toPlayer, distance);
    }

    /// <summary>
    /// 추가 생성 — 이번에 쓸 패턴을 고른다.
    ///
    /// 수정(패턴 편중): 예전에는 거리 하나로만 갈랐다.
    /// <c>if (거리 &lt;= 내려찍기 사거리) 내려찍기; else 파도;</c>
    /// 그런데 쉬는 동안 <see cref="Reposition"/>이 계속 다가오기 때문에 쿨다운이 끝나는
    /// 시점에는 거의 항상 사거리 안이었다. 결과적으로 <b>내려찍기만 무한 반복</b>했고
    /// 파도 애니메이션은 한 번도 재생된 적이 없다.
    ///
    /// 두 사거리를 겹쳐두고, 겹치는 구간에서는 <b>직전과 다른 패턴</b>을 쓴다.
    /// 무작위 대신 번갈아 쓰는 이유: 무작위는 운 나쁘면 같은 패턴이 서너 번 이어져
    /// 똑같은 문제가 다시 보인다. 번갈아 쓰면 플레이어가 다음을 읽을 수 있어
    /// "패턴을 외워서 공략한다"는 보스전의 재미도 같이 생긴다.
    /// </summary>
    private void ChoosePattern(Vector2 toPlayer, float distance)
    {
        // 추가 생성 — 재 폭발이 가장 먼저다.
        //
        // 쿨다운이 12초라 자주 나오지 않는데, 순서를 뒤로 두면 그 긴 쿨다운이 돌아온 순간에
        // 마침 파도 쿨다운도 돌아와 있거나 사거리 안이라는 이유로 계속 밀린다. 그러면 실제
        // 주기는 12초가 아니라 "12초 + 다른 패턴이 안 걸릴 때까지"가 되어 인스펙터 숫자가
        // 뜻을 잃는다. 드물게 나오는 패턴일수록 나올 차례가 됐을 때는 반드시 나와야 한다.
        bool canUltimate = isPhase2 && ultimateCooldownTimer <= 0f && distance <= ultimateRange;
        if (canUltimate) { StartUltimate(toPlayer); return; }

        // 파도는 자기 쿨다운이 돌아왔을 때만 쓴다. 빈도를 이 하나로 통제하므로
        // "직전에 무엇을 썼는지" 같은 기억이 따로 필요 없다.
        bool canWave = wavePrefab != null && distance <= waveRange && waveCooldownTimer <= 0f;
        if (canWave) { StartWave(toPlayer); return; }

        if (distance <= slamRange) { StartCoroutine(Slam(toPlayer)); return; }

        Chase(toPlayer, distance);
    }

    /// <summary>
    /// 추가 생성 — 잿불 파도를 시작하고 전용 쿨다운을 건다.
    ///
    /// 쿨다운을 시전이 끝난 뒤가 아니라 <b>시작할 때</b> 거는 이유:
    /// 끝난 뒤에 걸면 모션 길이(0.7초)만큼 간격이 더 늘어나, 인스펙터에 적은 숫자와
    /// 실제 체감 주기가 어긋난다. 시작 시점 기준이라야 "6초마다 한 번"이 그대로 지켜진다.
    /// </summary>
    private void StartWave(Vector2 toPlayer)
    {
        waveCooldownTimer = waveCooldown * (isPhase2 ? phase2CooldownScale : 1f);
        StartCoroutine(Wave(toPlayer));
    }

    /// <summary>
    /// 추가 생성 — 재 폭발을 시작하고 전용 쿨다운을 건다.
    ///
    /// 파도와 같이 <b>시작할 때</b> 건다. 끝난 뒤에 걸면 모션 길이(0.6초)만큼 간격이 늘어
    /// 인스펙터의 12초와 체감 주기가 어긋난다.
    ///
    /// 파도와 달리 phase2CooldownScale을 곱하지 않는다. 이 패턴은 2페이즈에만 있어서
    /// "2페이즈라서 더 빨라진다"는 비교 대상이 없다. 곱하면 인스펙터의 12초가 실제로는
    /// 7.2초라는 뜻이 되어, 숫자를 읽고 조정할 수 없게 된다.
    /// </summary>
    private void StartUltimate(Vector2 toPlayer)
    {
        ultimateCooldownTimer = ultimateCooldown;

        // 추가 생성 — 이 패턴은 12초에 한 번이라 "안 나온다"와 "못 봤다"를 눈으로 구별할 수 없다.
        // 로그가 있으면 콘솔만 보고 판단이 끝난다.
        Debug.Log($"[보스] 재 폭발 시전 — 거리 {toPlayer.magnitude:0.0}, " +
                  $"{ultimateHitDelay}초 뒤 반경 {ultimateRadius} 판정.", this);

        StartCoroutine(Ultimate(toPlayer));
    }

    /// <summary>
    /// 공격 사이에 자리를 다시 잡는다. <b>절대 멈춰 서지 않는다.</b>
    ///
    /// 처음엔 사거리 안이면 Stop()을 불렀는데, 그러면 쉬는 동안 보스가 가만히 서 있어서
    /// "걷는 모션이 거의 안 나오고 공격만 반복하는" 모습이 됐다. 게다가 사거리 경계에서
    /// 프레임마다 멈췄다 갔다를 반복해 떨렸다.
    ///
    /// 대신 너무 붙었으면 <b>뒤로 물러난다.</b> 망령의 넉백과 같은 목적이다 —
    /// 한 번 치고 물러나면 플레이어가 파고들 자리가 생기고, 거리가 계속 변해서
    /// 다음 패턴이 무엇일지 읽는 재미가 생긴다.
    /// </summary>
    private void Reposition(Vector2 toPlayer, float distance)
    {
        state = State.Chase;

        float speed = moveSpeed * (isPhase2 ? phase2SpeedScale : 1f);

        // 물러날 때는 느리게. 같은 속도로 빼면 플레이어가 영영 못 따라잡는다.
        bool retreat = distance < retreatDistance;
        Vector2 direction = toPlayer.normalized * (retreat ? -1f : 1f);
        if (retreat) speed *= retreatSpeedScale;

        body.linearVelocity = direction * speed;
        if (animator != null) animator.SetFloat(SpeedHash, speed);
    }

    /// <summary>플레이어에게 다가간다. 잿불 파도가 없을 때 먼 거리에서 쓴다.</summary>
    private void Chase(Vector2 toPlayer, float distance)
    {
        Reposition(toPlayer, distance);
    }

    private void Stop()
    {
        state = State.Idle;
        body.linearVelocity = Vector2.zero;
        if (animator != null) animator.SetFloat(SpeedHash, 0f);
    }

    /// <summary>
    /// 내려찍기. 예비동작을 두고 판정을 뒤늦게 낸다.
    ///
    /// 수정(8방향 판정): 판정 상자를 <b>플레이어가 있는 쪽으로 돌린다.</b>
    ///
    /// 예전에는 중심을 <c>FacingSign() * slamHitSize.x * 0.35f</c>만큼 x축으로만 밀었다.
    /// <see cref="FacingSign"/>이 읽는 것은 <c>spriteRenderer.flipX</c> 하나뿐이라
    /// 보스가 아는 방향은 좌우 둘밖에 없었는데, 이 게임은 탑다운이고 플레이어는 8방향으로 움직인다.
    /// 그래서 <c>slamRange</c>(5) 안이면서 상자 세로(중심에서 ±2.5) 밖인
    /// <b>보스의 정북·정남 구간이 통째로 헛쳤다.</b> 옆에서는 맞고 위아래에서는 안 맞는데,
    /// 플레이어 쪽에서는 그 이유를 알 방법이 없다.
    ///
    /// 그림은 여전히 좌우 두 방향뿐이다 — 시트가 그것뿐이라 어쩔 수 없다. 하지만
    /// <b>판정은 그림이 아니라 실제 방향을 따라야 한다.</b> 둘을 같은 값으로 묶어둔 것이 원래 문제였다.
    /// </summary>
    /// <param name="toPlayer">패턴을 고른 시점의 보스 → 플레이어 벡터.</param>
    private IEnumerator Slam(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack; // Stop이 Idle로 되돌리므로 다시 잠근다

        // 추가 생성 — 조준 방향을 예비동작이 시작되는 지금 고정한다.
        //
        // 잿불 파도(<see cref="Wave"/>)는 반대로 발사 직전에 방향을 다시 잡는다. 두 패턴이 다른 이유:
        // 파도는 화면을 가로지르는 원거리 견제라, 제자리에서 옆으로 한 발짝 걷는 것만으로 전부
        // 피해지면 패턴이 성립하지 않는다. 반면 내려찍기는 <b>예비동작을 보고 그 자리를 벗어나는 것이
        // 회피 그 자체다.</b> 판정 순간에 다시 조준하면 어디로 도망쳐도 맞게 되고, 그러면 아래
        // slamHitDelay로 예비동작을 둔 이유가 통째로 사라진다.
        slamAim = AimFrom(toPlayer);

        // 추가 생성 — 페이즈에 맞춘 시간을 여기서 한 번에 구한다.
        //
        // 기다릴 때마다 MotionTime()을 다시 부르지 않는 이유: 대기 도중에 페이즈가 넘어가면
        // 앞의 대기는 1페이즈 값으로, 뒤의 대기는 2페이즈 값으로 계산된다. 그러면 마지막 줄의
        // (모션 − 판정) 뺄셈이 음수가 되어 <b>모션이 판정 직후에 그냥 끝나버린다.</b>
        // 시작 시점에 한 쌍으로 묶어두면 그 어긋남이 생길 수 없다.
        float hitDelay = MotionTime(slamHitDelay);
        float motionSeconds = MotionTime(slamMotionSeconds);

        if (animator != null) animator.SetTrigger(SlamHash);

        yield return new WaitForSeconds(hitDelay);

        // 판정을 모션 시작이 아니라 여기서 내는 이유: 검이 아직 머리 위에 있는데 맞으면
        // 플레이어는 "안 맞았는데 데미지가 들어왔다"고 느낀다. 예비동작을 보고 피할 수 있어야
        // 패턴을 읽는 재미가 생긴다.
        //
        // 수정(8방향 판정) — 중심을 조준 방향으로 밀고, 상자도 같은 각도로 돌린다.
        // 이렇게 해야 slamHitSize.x가 "조준 방향으로의 길이", y가 "그 축을 가로지르는 폭"이라는
        // 뜻이 여덟 방향 어디서나 똑같이 유지된다. 예전 코드에서 그 뜻은 좌우일 때만 맞았다.
        Vector2 center = (Vector2)transform.position + slamAim * (slamHitSize.x * 0.35f);
        float angle = Mathf.Atan2(slamAim.y, slamAim.x) * Mathf.Rad2Deg;

        // 상자 네 귀퉁이를 직접 구해 판정하지 않고 OverlapBox의 각도 인자를 쓴다.
        // 회전한 사각형과의 겹침 판정은 유니티가 이미 해주는 일이다.
        var hit = Physics2D.OverlapBox(center, slamHitSize, angle, playerLayer);
        if (hit != null)
        {
            var target = hit.GetComponentInParent<Health>();
            if (target != null) target.TakeDamage(slamDamage, transform.position);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, motionSeconds - hitDelay));

        EndAttack();
    }

    /// <summary>
    /// 추가 생성 — 판정에 쓸 조준 방향을 길이 1로 만든다.
    ///
    /// 보스와 플레이어가 정확히 겹쳐 방향을 정할 수 없을 때는 그림이 보는 쪽으로 떨어뜨린다.
    /// 0 벡터를 그대로 쓰면 중심이 안 밀리고 각도도 0이 되어 상자가 보스 발밑에 놓인다.
    /// <b>코앞의 플레이어를 오히려 못 때리는</b> 반대 상황이 된다.
    /// </summary>
    private Vector2 AimFrom(Vector2 toPlayer)
    {
        return toPlayer.sqrMagnitude > 0.0001f
            ? toPlayer.normalized
            : new Vector2(FacingSign(), 0f);
    }

    private IEnumerator Wave(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack;

        // 추가 생성 — 내려찍기와 같은 이유로 페이즈 시간을 한 쌍으로 먼저 구한다.
        float fireDelay = MotionTime(waveFireDelay);
        float motionSeconds = MotionTime(waveMotionSeconds);

        if (animator != null) animator.SetTrigger(WaveHash);

        yield return new WaitForSeconds(fireDelay);

        // 수정(조준 원점 어긋남) — <b>쏘는 자리에서 맞힐 자리로</b> 겨눈다.
        //
        // 예전에는 방향을 "보스 발밑 → 플레이어 발밑"으로 잡아놓고, 정작 투사체는 발밑이 아니라
        // waveSpawnHeight(1.6)만큼 위에서 내보냈다. 그러면 파도는 목표에 도달했을 때
        // <b>플레이어 발밑보다 1.6유닛 위</b>에 있다. 플레이어 캡슐이 0~1.25이고 파도 히트박스가
        // ±0.7이라, 옆에서 쏠 때 겹치는 구간이 0.9~1.25의 <b>0.35유닛</b>뿐이었다. 그것도
        // 캡슐의 둥근 꼭대기라서, 값 하나만 건드려도 조용히 안 맞게 되는 상태였다.
        //
        // 내려찍기(<see cref="Slam"/>)와 같은 계열의 실수다. 거기서는 판정 방향이 그림을 따라갔고
        // 여기서는 조준 원점이 발사 원점을 안 따라갔다.
        Vector2 spawn = (Vector2)transform.position + Vector2.up * waveSpawnHeight;
        Vector2 target = playerCollider != null
            ? (Vector2)playerCollider.bounds.center
            : (player != null ? (Vector2)player.position : (Vector2)transform.position + toPlayer);

        // 시전 시작이 아니라 여기서 방향을 다시 잡는다. 예비동작 동안 플레이어가 움직였으면
        // 그쪽으로 나가야 한다 — 안 그러면 제자리에서 옆으로 걸어 나가기만 해도 전부 피해진다.
        // (내려찍기는 반대로 시작 시점에 고정한다. 이유는 Slam 쪽 주석에 적어뒀다.)
        Vector2 toTarget = target - spawn;
        Vector2 aim = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : AimFrom(toPlayer);

        int count = waveCount + (isPhase2 ? 2 : 0);

        // 가운데를 기준으로 좌우 대칭이 되게 각도를 나눈다.
        float start = -waveSpreadDegrees * (count - 1) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, start + waveSpreadDegrees * i) * aim;

            var shot = Instantiate(wavePrefab, spawn, Quaternion.identity);
            shot.Launch(direction, waveDamage);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, motionSeconds - fireDelay));

        EndAttack();
    }

    /// <summary>
    /// 추가 생성 — 재 폭발. 보스를 중심으로 사방에 판정이 나간다.
    ///
    /// <b>조준하지 않는 유일한 패턴이다.</b> 내려찍기는 조준 방향으로 상자를 돌리고 파도는
    /// 발사 직전에 방향을 다시 잡지만, 여기서는 방향이라는 개념 자체가 없다. 그래서
    /// <see cref="AimFrom"/>도 <see cref="slamAim"/>도 쓰지 않는다. 회피는 옆으로 빠지는 것이
    /// 아니라 <b>반경 밖으로 나가는 것</b>이다.
    ///
    /// <see cref="MotionTime"/>을 안 거치는 것이 중요하다. 그 함수는 "1페이즈 클립에 맞춰
    /// 적은 값을 2페이즈 클립 길이로 환산"하는 도구인데, 이 패턴은 2페이즈에만 있어서
    /// <b>환산할 1페이즈 값이 없다.</b> 아래 두 숫자는 처음부터 2페이즈 클립(10fps)의 실제
    /// 길이다. 여기에 0.8을 곱하면 판정이 폭발 그림보다 0.06초 먼저 나가서, 아무것도 안
    /// 터졌는데 맞는 프레임이 생긴다.
    /// </summary>
    private IEnumerator Ultimate(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack; // Stop이 Idle로 되돌리므로 다시 잠근다

        // 추가 생성 — 달려들 방향을 시작 시점에 고정한다.
        //
        // 내려찍기와 같은 이유다. 예비동작 도중에 방향을 다시 잡으면 어디로 도망쳐도 따라와서,
        // "예비동작을 보고 반대로 뛴다"는 회피가 성립하지 않는다. 판정이 사방으로 나가는
        // 패턴이라 <b>거리를 벌리는 것 말고는 피할 방법이 없어서</b> 더 중요하다.
        Vector2 aim = AimFrom(toPlayer);

        if (animator != null) animator.SetTrigger(UltimateHash);

        // 예비동작 동안 달려든다. Stop()이 속도를 0으로 만든 뒤라 여기서 다시 넣는다.
        body.linearVelocity = aim * ultimateLungeSpeed;

        yield return new WaitForSeconds(ultimateHitDelay);

        // 터지는 순간 멈춘다. 안 멈추면 폭발 그림이 뜬 채로 미끄러져서 판정 위치와 그림이 어긋난다.
        body.linearVelocity = Vector2.zero;

        // 추가 생성 — 이펙트를 판정과 <b>같은 자리, 같은 순간에</b> 만든다.
        //
        // 순서가 중요하다. 아래 OverlapCircle보다 먼저 두는 이유는, 맞은 쪽이 죽으면서
        // 무슨 연출을 하든 이펙트는 이미 나와 있어야 하기 때문이다. 판정 결과에 따라
        // 이펙트가 달라지면 플레이어는 "맞았을 때만 터지는" 것으로 배운다.
        SpawnUltimateEffect();

        // 원 하나로 판정한다. 상자를 돌려 쓰는 내려찍기와 달리 회전이 필요 없어서
        // OverlapCircle이 그대로 맞는 도구다.
        var hit = Physics2D.OverlapCircle(transform.position, ultimateRadius, playerLayer);
        if (hit != null)
        {
            var target = hit.GetComponentInParent<Health>();
            if (target != null) target.TakeDamage(ultimateDamage, transform.position);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, ultimateMotionSeconds - ultimateHitDelay));

        EndAttack();
    }

    /// <summary>
    /// 추가 생성 — 재 폭발 이펙트를 보스 발밑에 만든다.
    ///
    /// 보스의 자식으로 붙이지 않는 이유: 붙이면 보스가 죽거나 다음 패턴으로 움직일 때
    /// 이펙트가 같이 따라다닌다. 폭발은 <b>그 자리에서 일어난 사건</b>이라 보스와 함께
    /// 움직이면 안 된다. 수명은 프리팹의 SpriteFrameAnimator가 알아서 끝낸다.
    /// </summary>
    private void SpawnUltimateEffect()
    {
        // 수정(이펙트가 안 나옴): 조용히 넘어가지 않고 이유를 남긴다.
        //
        // 비어 있어도 패턴 자체는 멀쩡히 돌아간다 — 모션도 나오고 피해도 들어간다.
        // 그래서 화면만 보면 "이펙트를 넣었는데 안 보인다"와 "안 넣었다"가 똑같아 보인다.
        // 실제로 그 둘을 구별하지 못해 한 번 헤맸다.
        if (ultimateEffectPrefab == null)
        {
            Debug.LogWarning("[보스] 재 폭발 이펙트 프리팹이 비어 있다. 보스 프리팹 인스펙터의 " +
                             "Ultimate Effect Prefab에 KingsEmber를 꽂아라. " +
                             "지금은 시트의 폭발 프레임(0.1초)만 보인다.", this);
            return;
        }

        var effect = Instantiate(ultimateEffectPrefab, transform.position, Quaternion.identity);

        // 프리팹에 이미 들어 있는 크기에 배율을 곱한다. 절대값으로 덮으면 프리팹마다
        // 다른 기준 크기를 여기서 다시 외워야 한다.
        if (!Mathf.Approximately(ultimateEffectScale, 1f))
            effect.transform.localScale *= ultimateEffectScale;
    }

    /// <summary>
    /// 추가 생성 — 페이즈에 맞춘 동작 시간. 1페이즈는 그대로, 2페이즈는 클립 길이 비만큼 줄인다.
    ///
    /// 곱하는 자리를 한 함수로 모은 이유: 곱해야 할 곳이 네 군데(내려찍기 판정·모션,
    /// 파도 발사·모션)라, 각자 곱하게 두면 언젠가 한 곳을 빠뜨린다. 그리고 그 한 곳은
    /// <b>2페이즈에서 한 패턴만 어긋나는</b> 증상으로 나타나서, 보고도 재현 조건을 잡기 어렵다.
    ///
    /// 판정 시점까지 같이 줄이는 것이 핵심이다. 클립 전체가 0.8배면 검이 바닥에 닿는 프레임도
    /// 0.8배 지점이다. 모션만 줄이고 판정을 그대로 두면 2페이즈에서 판정이 모션의 훨씬 뒷부분에
    /// 걸려서, 예비동작을 읽고 피하라고 만든 시간이 사라진다.
    /// </summary>
    private float MotionTime(float seconds)
        => seconds * (isPhase2 ? phase2MotionScale : 1f);

    private void EndAttack()
    {
        cooldownTimer = attackCooldown * (isPhase2 ? phase2CooldownScale : 1f);
        state = State.Idle;
    }

    private void OnDamaged(int current, int max)
    {
        if (state == State.Dead || state == State.Transition) return;

        // 수정(죽는 한 대가 페이즈 전환을 켜고 갔다) — 이 피해로 이미 죽었으면 여기서 끝낸다.
        //
        // Health는 Damaged → Changed → Died 순으로 알린다. 그래서 <b>죽인 한 대도 먼저
        // 이리로 온다.</b> 그때 state는 아직 Dead가 아니라서 위 검사에 안 걸리고, 체력이
        // 0이니 아래 절반 검사는 반드시 참이 된다. 결과적으로 죽는 순간 EnterPhase2가
        // 시작되어 <b>전환 모션 트리거와 무적을 켜고</b>, 곧이어 도착한 OnDied의
        // StopAllCoroutines에 중간에서 잘린다. 무적은 켜진 채 남고 사망 모션 대신
        // 전환 모션이 먼저 재생된다.
        //
        // 지금 수치(보스 체력 40, 전환 50%)로는 한 대에 20 이상을 깎아야 해서 안 나온다.
        // 하지만 <b>보스 체력을 낮춰 테스트할 때는 바로 나온다</b> — 확인하려고 줄인 값이
        // 확인하려던 것을 망가뜨리는 셈이다.
        //
        // 죽음은 OnDied가 처리한다. 여기서 할 일이 없다.
        if (current <= 0) return;

        // 절반이 되면 페이즈 전환. 공격 도중이어도 끼어든다 — 반쯤 진행된 패턴보다
        // 페이즈가 바뀌었다는 신호가 훨씬 중요하다.
        //
        // 수정(전환이 두 번 돌았다) — 검사 대상을 isPhase2에서 transitionStarted로 바꿨다.
        //
        // isPhase2는 연출 <b>중간</b>(2.875, 2페이즈 보스 등장)에 켜진다. 그래서 그 신호가
        // 유실되면 isPhase2가 영원히 false로 남고, 보스를 한 대 더 때리는 순간 <b>전환이
        // 처음부터 다시 시작한다.</b> 실제로 그렇게 됐다 — 콘솔에 "2페이즈로 넘어갔다"가
        // 두 번씩 찍혔다.
        //
        // 두 값의 뜻이 다르다. isPhase2는 "지금 2페이즈인가"이고 transitionStarted는
        // "전환을 한 번이라도 시작했는가"다. <b>다시 하지 않을 이유는 뒤쪽</b>이다.
        if (!transitionStarted && phase2Controller != null && current <= max * phase2HealthRatio)
        {
            transitionStarted = true;
            StopAllCoroutines();
            StartCoroutine(EnterPhase2());
            return;
        }

        // 공격 중에는 경직을 안 건다. 걸면 예비동작이 끊겨서, 플레이어가 계속 때리는 것만으로
        // 보스가 아무 패턴도 못 쓰는 허수아비가 된다.
        if (state == State.Attack) return;

        StartCoroutine(HitStun());
    }

    private IEnumerator HitStun()
    {
        state = State.Hit;
        body.linearVelocity = Vector2.zero;
        if (animator != null) animator.SetTrigger(HitHash);

        yield return new WaitForSeconds(hitStunSeconds);

        if (state == State.Hit) state = State.Idle;
    }

    private IEnumerator EnterPhase2()
    {
        state = State.Transition;
        body.linearVelocity = Vector2.zero;

        // 연출 중에는 무적이다. 안 그러면 못 움직이는 동안 두들겨 맞아서
        // 2페이즈를 보기도 전에 죽는 보스가 된다.
        health.IsInvulnerableExternally = true;

        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.SetTrigger(TransitionHash);
        }

        // 수정(Timeline 도입) — 여기서 시간을 세지 않는다.
        //
        // 무엇을 언제 켜고 끄는지는 전부 타임라인 에셋에 있고, 이 코루틴이 하는 일은
        // <b>시작 신호와 무적</b>뿐이다. 페이즈가 실제로 바뀌는 것은 아래 세 개의
        // On... 함수가 시그널을 받아서 한다.
        //
        // 길이를 먼저 받아두고 <b>그 값이 성립하는지</b> 본다. 0이면 연출이 붙어 있어도
        // 기다리지 않고 지나가버리는데, 그건 시그널이 하나도 안 울린다는 뜻이다.
        float seconds = transitionSequence != null ? transitionSequence.TotalSeconds : 0f;

        if (seconds > 0f)
        {
            transitionSequence.Play();
            yield return new WaitForSeconds(seconds);
        }
        else
        {
            // 연출이 없어도 <b>전투는 이어져야 한다.</b> 여기서 그냥 돌아가면 보스가 무적인 채
            // 1페이즈로 영원히 서 있게 되는데, 그건 연출이 빠진 것보다 훨씬 나쁘다.
            // 시그널이 할 일을 순서대로 직접 부른다.
            Debug.LogError("[보스] 전환 연출을 못 쓴다(컴포넌트가 없거나 타임라인이 안 붙었다). " +
                           "연출 없이 2페이즈로 넘어간다. " +
                           "Tools → 재의 길 → 보스 전환 이펙트 생성 을 실행해라.", this);

            OnTransitionRevealed();
            OnTransitionBossReturns();
        }

        // 추가 생성(시그널 유실 안전망) — 연출이 끝났는데 페이즈가 안 바뀌었으면 코드로 넘긴다.
        //
        // 여기까지 왔는데 isPhase2가 false라면 타임라인의 시그널이 도착하지 않았다는 뜻이다.
        // 그대로 두면 아래에서 <b>무적만 풀린다.</b> 결과는 1페이즈 컨트롤러를 문 채 스프라이트가
        // 꺼져 있고, 맞기는 하는 보스다 — 화면에 아무것도 없는데 체력이 깎인다.
        // transitionStarted가 이미 true라 다시 시도하지도 않으므로 그 상태로 끝까지 간다.
        //
        // 실제로 그 경로로 두 번 물렸다(이름이 "???"로 남고 체력바가 안 찼다). 지금 배선
        // (SignalReceiver + INotificationReceiver 이중 수신)으로는 안 나야 정상이지만,
        // <b>연출이 빠지는 것과 전투가 망가지는 것은 무게가 다르다.</b> 싸게 막아둔다.
        //
        // 아래 두 함수는 각자 "한 번만 돈다" 검사를 갖고 있다. 그래서 시그널이 절반만 도착한
        // 경우(2.375는 왔고 2.875는 유실)에도 <b>안 온 쪽만</b> 채워진다.
        if (!isPhase2)
        {
            Debug.LogError("[보스] 전환 연출의 시그널이 도착하지 않았다. 페이즈를 코드로 넘긴다. " +
                           "BossTransition.playable의 시그널 트랙과 프리팹의 시그널 참조를 확인해라.", this);

            OnTransitionRevealed();
            OnTransitionBossReturns();
        }

        // 추가 생성 — 첫 재 폭발까지 유예를 준다.
        // 전환 연출 내내 무적이라 플레이어는 대개 코앞에 서 있다. 여기서 0이면
        // 변신하자마자 회피 불가능한 한 방이 나간다.
        ultimateCooldownTimer = ultimateFirstDelay;

        health.IsInvulnerableExternally = false;
        cooldownTimer = 0.4f;
        state = State.Idle;

        // 수정(궁극기 확인): 언제부터 재 폭발이 나올 수 있는지 같이 남긴다.
        // 이 줄과 "재 폭발 시전" 로그의 시간 차가 곧 유예 + 다음 공격까지의 대기다.
        Debug.Log($"[보스] 2페이즈로 넘어갔다. 재 폭발은 {ultimateFirstDelay}초 뒤부터, " +
                  $"거리 {ultimateRange} 안에서 나온다.", this);
    }

    /// <summary>
    /// 추가 생성 — 계획표 <c>0.875</c>. 갑옷이 다 무너졌다. 보스 스프라이트를 숨긴다.
    ///
    /// 여기부터 <c>2.875</c>까지 화면에 있는 것은 시트 이펙트(gather → egg → shatter)와
    /// 재 파티클뿐이다. 보스가 안 보이는 동안 컨트롤러를 갈아 끼우므로 교체가 눈에 안 띈다.
    /// </summary>
    private void OnTransitionArmorBroken()
    {
        if (spriteRenderer != null) spriteRenderer.enabled = false;
    }

    /// <summary>
    /// 추가 생성 — 계획표 <c>2.375</c>. 껍질이 깨지는 순간. 밖(체력바)에 전환을 알린다.
    ///
    /// <b>수정 — 알리는 시점을 연출 끝(3.125)에서 여기로 당겼다.</b>
    ///
    /// 예전에는 "연출이 끝나고 실제로 넘어간 순간"에 울렸다. 그때는 연출이 0.875초짜리
    /// 한 덩어리라 그 끝이 곧 절정이었다. 지금은 절정이 <b>껍질이 깨지는 2.375초</b>고,
    /// 3.125초는 무적을 푸는 보스 내부 사정일 뿐이다. 이름 교체와 체력바 재충전이
    /// 껍질이 깨지고 0.75초 뒤에 오면 <b>연출과 UI가 따로 논다.</b>
    /// </summary>
    private void OnTransitionRevealed()
    {
        // 수정(시그널 유실 안전망) — 두 번 알리지 않는다.
        //
        // 안전망이 이 함수를 다시 부를 수 있다. EnteredPhase2를 두 번 울리면 듣는 쪽
        // (체력바)이 이름 교체와 재충전 연출을 두 번 재생한다.
        if (announcedPhase2) return;
        announcedPhase2 = true;

        EnteredPhase2?.Invoke();
    }

    /// <summary>
    /// 추가 생성 — 계획표 <c>2.875</c>. 2페이즈 보스가 나타난다.
    ///
    /// 컨트롤러 교체와 콜라이더 축소를 <b>보이기 직전</b>에 모아두는 이유: 셋 중 하나라도
    /// 다른 시각에 하면 그 사이 동안 <b>절반만 2페이즈인 보스</b>가 존재한다. 예를 들어
    /// 콜라이더만 먼저 줄면 아직 1페이즈 그림인데 맞는 자리가 좁아진다.
    /// </summary>
    private void OnTransitionBossReturns()
    {
        // 수정(시그널 유실 안전망) — 두 번 갈아 끼우지 않는다.
        //
        // isPhase2는 이 함수의 결과이면서 동시에 안전망의 조건이다. 여기서 한 번 더 막아두면
        // 시그널과 안전망이 같은 프레임에 겹쳐도 <c>Rebind</c>가 두 번 돌지 않는다.
        // Rebind는 애니메이터 상태를 처음부터 다시 물리므로 두 번 돌면 첫 프레임이 튄다.
        if (isPhase2) return;

        isPhase2 = true;

        // 추가 생성 — 몸이 줄었으니 맞는 자리도 줄인다.
        ShrinkColliderForPhase2();

        if (animator != null)
        {
            animator.runtimeAnimatorController = phase2Controller;

            // 컨트롤러를 바꾸면 파라미터가 새로 잡히므로 상태를 처음부터 다시 물린다.
            // 안 하면 예전 컨트롤러의 재생 위치가 남아 첫 프레임이 엉뚱하게 나온다.
            animator.Rebind();
        }

        if (spriteRenderer != null) spriteRenderer.enabled = true;
    }

    /// <summary>
    /// 추가 생성 — 2페이즈 몸통 콜라이더를 그림에 맞춰 좁힌다.
    ///
    /// 폭만 줄이고 세로와 오프셋은 그대로 두는 이유는 필드 주석에 적어뒀다 —
    /// 시트를 재보면 두 페이즈의 몸 높이가 200px로 같고 폭만 150 → 131로 줄었다.
    ///
    /// 프리팹 값을 코드에서 덮는 대신 <b>곱하는</b> 이유: 콜라이더의 기준 크기는
    /// AshBossPrefabBuilder가 보스 키에서 계산해 넣는다. 여기에 절대값을 적으면 그 계산을
    /// 두 곳에서 하게 되고, 나중에 보스 키를 바꿨을 때 2페이즈만 옛 크기로 남는다.
    /// 배율이면 기준이 무엇으로 바뀌든 따라간다.
    ///
    /// 되돌리는 코드를 두지 않은 이유: 2페이즈에서 1페이즈로 돌아가는 길이 없다. 판을 다시
    /// 하면 씬을 새로 로드하므로 프리팹 값 그대로 시작한다.
    /// </summary>
    private void ShrinkColliderForPhase2()
    {
        if (bodyCollider == null) return;

        Vector2 size = bodyCollider.size;
        bodyCollider.size = new Vector2(size.x * phase2ColliderWidthScale, size.y);
    }

    private void OnDied()
    {
        StopAllCoroutines();

        // 추가 생성 — 전환 중이었다면 연출을 멈추고 몸을 되돌린다.
        //
        // 전환 중에는 무적이라 여기 올 일이 없어야 하지만, StopAllCoroutines가 EnterPhase2를
        // 중간에서 자를 수 있는 이상 <b>스프라이트가 꺼진 채 남는 길</b>이 존재한다.
        // 그러면 사망 모션이 재생되는데 화면에는 아무것도 없다. 죽었는지 사라졌는지 모른다.
        if (transitionSequence != null) transitionSequence.StopAndReset();
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        state = State.Dead;
        body.linearVelocity = Vector2.zero;

        if (animator != null) animator.SetTrigger(DieHash);

        // 시체를 밟고 지나가지 않게 충돌만 끈다. 오브젝트는 남겨서 사망 모션이 끝까지 보인다.
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 추가 생성 — 인스펙터 값이 서로 모순되면 그 자리에서 알려준다.
    ///
    /// 왜 필요한가: 재 폭발이 안 나오길래 한참을 찾았는데, 원인이 시전 사거리를 1.5로 적어둔
    /// 것이었다. 보스는 <see cref="retreatDistance"/>(3.5)보다 가까워지면 <b>뒤로 물러나므로</b>
    /// 그보다 작은 사거리는 영영 충족되지 않는다. 그런데 인스펙터에서 두 값은 서로 다른
    /// 항목에 떨어져 있어서, 나란히 놓고 보지 않는 한 모순이 보이지 않는다.
    ///
    /// 잘못된 값을 코드가 조용히 고치지 않고 <b>경고만 하는</b> 이유: 밸런스 수치는 사람이
    /// 정하는 것이다. 자동으로 바로잡으면 인스펙터에 적은 숫자와 실제로 도는 숫자가 달라져서
    /// 더 헷갈린다. 무엇이 왜 모순인지만 알려주고 판단은 남긴다.
    /// </summary>
    private void OnValidate()
    {
        if (ultimateRange < retreatDistance)
        {
            Debug.LogWarning(
                $"[보스] 재 폭발 시전 사거리({ultimateRange})가 물러나는 거리({retreatDistance})보다 " +
                "작다. 보스는 그 거리 안으로 안 붙으므로 이 패턴은 나오지 않는다. " +
                "사거리를 물러나는 거리보다 크게 잡아라.", this);
        }

        // 수정(달려들기 도입): "사거리 ≤ 반경"이던 규칙을 <b>달려들어 좁히는 거리까지 포함</b>해
        // 다시 잡았다. 예전 규칙이면 제자리에서 터뜨리는 경우만 맞았다.
        //
        // 닿을 수 있는 최대 거리 = 판정 반경 + (달려드는 속도 × 예비동작 시간).
        // 이걸 넘는 사거리는 "시전은 하는데 도착을 못 해서 헛치는" 구간이 된다.
        float reach = ultimateRadius + ultimateLungeSpeed * ultimateHitDelay;
        if (ultimateRange > reach)
        {
            Debug.LogWarning(
                $"[보스] 재 폭발 시전 사거리({ultimateRange})가 닿을 수 있는 거리({reach:0.0})보다 크다. " +
                $"반경({ultimateRadius}) + 달려들기({ultimateLungeSpeed} × {ultimateHitDelay}초)로는 " +
                "그 거리에서 시전해도 못 닿는다. 사거리를 줄이거나 달려드는 속도를 올려라.", this);
        }

        if (slamRange > waveRange)
        {
            Debug.LogWarning(
                $"[보스] 내려찍기 사거리({slamRange})가 파도 사거리({waveRange})보다 크다. " +
                "두 사거리가 겹치지 않으면 패턴이 한쪽으로 편중된다.", this);
        }
    }
#endif

    /// <summary>바라보는 방향(오른쪽 +1). 스프라이트가 원래 오른쪽을 본다고 가정한다.</summary>
    private float FacingSign() => spriteRenderer != null && spriteRenderer.flipX ? -1f : 1f;

    private void FaceTowards(float deltaX)
    {
        if (spriteRenderer == null || Mathf.Abs(deltaX) < 0.1f) return;

        spriteRenderer.flipX = deltaX < 0f;
    }

    /// <summary>
    /// 인스펙터에서 사거리와 판정 범위를 눈으로 확인한다.
    ///
    /// 수정(8방향 판정): 상자를 조준 방향으로 돌려서 그린다. 예전에는 항상 오른쪽으로 그렸는데,
    /// 코드가 좌우만 보던 시절에는 그게 사실이었지만 지금은 <b>기즈모가 거짓말을 하게 된다.</b>
    /// 판정 범위를 맞추는 도구가 실제 판정과 다르면 없느니만 못하다.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, slamRange);

        // 실행 중이면 지금 플레이어 쪽을, 편집 중이면(플레이어가 없다) 마지막 조준 방향을 그린다.
        // 편집 중 기본값은 오른쪽이라 예전 기즈모와 같은 그림이 나온다 — 눈으로 맞추던 기준이 안 바뀐다.
        Vector2 aim = Application.isPlaying && player != null
            ? AimFrom((Vector2)player.position - (Vector2)transform.position)
            : slamAim;

        float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
        Vector3 center = transform.position + (Vector3)(aim * (slamHitSize.x * 0.35f));

        // Gizmos.matrix로 좌표계를 통째로 돌린다. 귀퉁이 네 점을 직접 구해 선을 긋는 것보다 짧고,
        // 무엇보다 Physics2D.OverlapBox에 넘기는 각도와 같은 값을 쓰므로 둘이 어긋날 여지가 없다.
        Matrix4x4 saved = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, slamHitSize);

        Gizmos.matrix = saved;

        // 추가 생성 — 재 폭발의 판정 반경(진한 주황)과 시전 사거리(연한 주황).
        //
        // 두 개를 같이 그리는 이유: 바깥 원에서 시전을 시작해 달려들어 안쪽 원으로 좁힌다는
        // 관계가 숫자 두 개로는 안 보인다. 바깥 원이 <b>너무 크면</b> 달려들어도 못 닿아
        // 헛치는데, 그 경계는 OnValidate가 경고로 알려준다.
        Gizmos.color = new Color(1f, 0.5f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, ultimateRadius);

        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, ultimateRange);
    }
}
