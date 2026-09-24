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
/// 붙으면 내려찍기, 떨어지면 재의 창. 한 자리에 서 있으면 안 되게 만드는 장치다.
/// 둘 다 예비동작이 있어서(내려찍기는 애니메이션, 창은 조준선과 시전 바) 보고 빠질 수 있다.
///
/// 수정(2026-09-20, 기획 선택) — 원거리를 잿불 파도에서 <b>재의 창</b>으로 갈아탔다.
/// 둘 다 부채꼴로 날아오는 원거리라 회피법이 똑같았고, 그림까지 같아서 화면에서도 구별되지
/// 않았다. 패턴이 둘로 보이지 않으면 늘린 만큼의 재미가 없다.
///
/// 페이즈 전환은 <b>컨트롤러를 갈아 끼운다.</b> 오브젝트도 Health도 그대로라
/// 진행 중인 체력과 위치가 안 끊긴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Health))]
public class EnemyBoss : MonoBehaviour
{
    // 수정(2026-09-21, 왕관 의식) — Ritual을 더했다. 의식 동안 보스는 멈춰 서서 무적이고 아무 패턴도 안 쓴다.
    // 수정(2026-09-22) — Groggy를 더했다. 유물 넷을 다 부숴 의식을 막으면, 정해 둔 시간 동안 멈춰 서서 맞기만 한다(무적 아님).
    private enum State { Idle, Chase, Attack, Transition, Hit, Dead, Ritual, Groggy }

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

    // 추가 생성(2026-09-21, 전환 흐름 B) — 갑옷이 무너질 때 흐려져 사라지는 시간.
    //
    // 예전에는 갑옷 붕괴 신호에 그림을 한 프레임에 껐다. 그때는 전환 모션이 여자까지 다 보여준
    // 뒤라 끊겨도 티가 덜 났는데, 흐름 B는 금이 간 기사 모습에서 곧장 재로 넘어간다. 한 프레임에
    // 사라지면 "무너졌다"가 아니라 "없어졌다"로 보여서, 모여드는 재와 겹치게 천천히 흐린다.
    [Tooltip("갑옷 붕괴 신호에 1페이즈 그림이 흐려져 사라지는 시간(초). 0이면 바로 꺼진다.")]
    [SerializeField, Min(0f)] private float transitionFadeOutSeconds = 0.3f;

    // 추가 생성(2026-09-21, 전환 흐름 B) — 깨진 알 속에서 2페이즈 모습이 떠오르는 시간.
    // 알이 깨지는 번쩍임 뒤에 한 프레임에 켜지면 알 그림과 겹쳐 튀어 보인다.
    [Tooltip("2페이즈 모습이 나타날 때 흐려졌다 선명해지는 시간(초). 0이면 바로 켜진다.")]
    [SerializeField, Min(0f)] private float transitionFadeInSeconds = 0.3f;

    // 추가 생성 — 2페이즈에서 공격 모션 시간에 곱할 값.
    //
    // 왜 필요한가: 아래 slamMotionSeconds·spearMotionSeconds는 1페이즈 클립에 맞춘 숫자다.
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

    // 추가 생성(2026-09-21, 기획 선택 "2페이즈 체력 35%에서 한 번") — 왕관 의식(진짜 궁극기)을 여는 체력 비율.
    //
    // 코드 안의 "Ultimate"(재 폭발)와 이름이 겹치지 않게 왕관 의식이라고 부른다. 재 폭발은 기획에서
    // 일반 패턴으로 남기기로 했고, 왕관 의식은 판마다 한 번뿐인 절정이다.
    [Tooltip("2페이즈에서 체력이 이 비율 이하로 떨어지면 왕관 의식을 한 번 시작한다(체력 전체 기준).")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float crownRitualHealthRatio = 0.35f;

    // 추가 생성(2026-09-22, 기획 "넷을 다 부수면 보스 그로기") — 의식을 막았을 때의 보상.
    [Tooltip("유물을 부숴(기본 3개) 왕관 의식을 막으면 이 시간(초) 동안 멈춰 서서 맞기만 한다. 무적은 아니다. 0이면 그로기 없이 바로 싸운다.")]
    [SerializeField, Min(0f)] private float groggySeconds = 5f;

    [Tooltip("그로기 동안 시전 바에 띄울 이름. 바가 차는 동안이 마음껏 때릴 수 있는 시간이다.")]
    [SerializeField] private string groggyCastName = "그로기";

    // 추가 생성(2026-09-22, 보스 파티클 기획 1부 "왕관 의식") — 의식 동안의 자세와 파티클.
    //
    // <b>자세를 애니메이터 상태로 만들지 않은 이유.</b> 넷 다 "한 장을 붙들고 있는" 자세라 움직이는 클립이 필요 없다.
    // 상태로 만들면 1·2페이즈 컨트롤러 두 벌과 보스 애니메이션 빌더를 같이 고쳐야 한다. 그래서 자세를 보여 주는 동안만
    // 애니메이터를 끄고 그 장의 스프라이트를 직접 넣는다(SetPose). 끝나면 다시 켜서 대기 모션으로 돌아간다.
    // 그림과 파티클은 Tools → 재의 길 → 파티클 → 보스 파티클 만들기 가 채운다. 비어 있으면 그 연출만 빠진다.
    [Header("왕관 의식 연출 (보스 파티클 만들기가 채운다)")]
    [Tooltip("동작-1 손 뻗기 — 유물을 끌어낼 때(궁극기 시트 4번째 장).")]
    [SerializeField] private Sprite ritualReachSprite;
    [Tooltip("동작-2 의식 자세 — 칼을 머리 위로 든 채 버틴다(내려찍기 시트 3번째 장).")]
    [SerializeField] private Sprite ritualHoldSprite;
    [Tooltip("동작-3 움찔 — 유물이 깨질 때 차례로 보여 줄 장(피격 두 장).")]
    [SerializeField] private Sprite[] ritualFlinchSprites = Array.Empty<Sprite>();
    [Tooltip("동작-4 무너짐 — 그로기 동안의 자세(칼을 짚고 무릎 꿇은 장).")]
    [SerializeField] private Sprite groggySprite;
    [Tooltip("손 뻗기 장에서 손의 자리(발밑 기준, 오른쪽을 볼 때). 줄기가 여기로 끌려 들어간다.")]
    [SerializeField] private Vector2 ritualHandOffset = new Vector2(2.45f, 5.95f);
    [Tooltip("유물 실이 닿는 가슴 높이(발밑 기준).")]
    [SerializeField] private float ritualChestHeight = 4.2f;
    [Tooltip("의식-1 손 뻗기 줄기. 코드가 플레이어 가슴에서 보스 손 쪽으로 직접 뿌린다(월드 공간).")]
    [SerializeField] private ParticleSystem ritualTether;
    [Tooltip("의식-1 줄기가 초당 뿌리는 수(기획 70 × 화려하게 1.5).")]
    [SerializeField, Min(0f)] private float ritualTetherRate = 105f;
    [Tooltip("의식-2 왕관 점화. 의식 자세 동안 켜고 점점 세진다.")]
    [SerializeField] private ParticleSystem crownFire;
    [Tooltip("의식-2 왕관 불이 가장 세지기까지의 시간(초). 유물이 부서질 때마다 처음부터 다시 커진다.")]
    [SerializeField, Min(0.1f)] private float crownIgniteSeconds = 10f;
    [Tooltip("의식-3 재 소용돌이. 의식 자세 동안 켠다.")]
    [SerializeField] private ParticleSystem ashVortex;
    [Tooltip("의식-5 유물이 깨질 때 가슴에서 튀는 불꽃(한 번씩 터진다).")]
    [SerializeField] private ParticleSystem ritualChestSparks;
    [Tooltip("의식-6 무너질 때 왕관에서 쏟아지는 재(한 번 터진다).")]
    [SerializeField] private ParticleSystem collapseAsh;
    [Tooltip("의식-6 그로기 동안 머리 위를 맴도는 꺼져 가는 불씨.")]
    [SerializeField] private ParticleSystem dizzyEmbers;
    [Tooltip("의식-7 못 막았을 때 방으로 퍼지는 충격파(한 번 터진다).")]
    [SerializeField] private ParticleSystem ritualBlast;

    // 추가 생성(2026-09-23, 보스 파티클 기획 2부 "보스 평소") — 평소 동작의 파티클. 보스 파티클 만들기가 채운다. 비어 있으면 그 연출만 빠진다.
    [Header("보스 평소 파티클 (보스 파티클 만들기가 채운다)")]
    [Tooltip("보스-1 잿불 기운 — 2페이즈에 들어서는 순간 켜서 죽을 때까지 몸에서 불티가 피어오른다.")]
    [SerializeField] private ParticleSystem emberAura;
    [Tooltip("보스-5 걸음 재 먼지 — 걷는 동안 걸음 간격마다 한 번 터진다.")]
    [SerializeField] private ParticleSystem footDust;
    [Tooltip("걸음 간격(초). 걷기 클립 6장·10fps = 0.6초에 두 걸음.")]
    [SerializeField, Min(0.05f)] private float footstepInterval = 0.3f;
    [Tooltip("보스-2 내려찍기 파편 — 칼이 바닥에 닿는 판정 순간에 터진다(돌 파편·재 먼지·불티).")]
    [SerializeField] private ParticleSystem slamImpact;
    [Tooltip("내려찍기 파편이 터지는 자리(발밑 기준, 오른쪽을 볼 때). 칼끝이 닿는 곳이다.")]
    [SerializeField] private Vector2 slamImpactOffset = new Vector2(2.3f, 0.1f);
    [Tooltip("보스-3 재의 창 조준 불씨 — 겨누는 동안 창이 나갈 자리로 불씨가 빨려 든다.")]
    [SerializeField] private ParticleSystem spearGather;
    [Tooltip("보스-4 재 폭발 모으기 — 시전 동안 판정 반경 밖에서 재가 소용돌이치며 모인다.")]
    [SerializeField] private ParticleSystem burstGather;
    [Tooltip("보스-4 재 폭발 고리 — 판정 순간 판정 반경까지 퍼진다. 반경은 ultimateRadius를 읽어 속도를 맞춘다.")]
    [SerializeField] private ParticleSystem burstRing;
    [Tooltip("보스-6 피격 조각 — 맞을 때 때린 반대쪽으로 갑옷 조각과 재가 튄다.")]
    [SerializeField] private ParticleSystem hitShards;
    [Tooltip("보스-7 사망 재 — 무너지는 동안 몸에서 재와 불티가 피어오른다.")]
    [SerializeField] private ParticleSystem deathAsh;

    // 추가 생성(2026-09-23) — 다음 걸음 먼지까지 남은 시간.
    private float footstepTimer;

    // 추가 생성(2026-09-22) — 손 뻗기 줄기를 뿌리는 중인가, 의식 자세까지 갔는가(못 막았을 때만 충격파를 터뜨리려고),
    // 줄기 방출의 소수점 누적, 왕관 불을 켠 시각.
    private bool ritualTetherOn;
    private bool ritualHoldReached;
    private float ritualTetherCarry;
    private float crownFireStartTime;
    private float crownFireBaseRate = -1f;

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

    // 추가 생성(2026-09-20, 기획 선택 "C 재의 창") — 예고하고 나가는 원거리 공격.
    //
    // <b>잿불 파도와 무엇이 다른가.</b> 파도는 발사 직전에 방향을 잡아 곧바로 나간다.
    // 재의 창은 <see cref="BossAimFan"/>의 부채꼴 조준선으로 <b>먼저 보여준 뒤</b> 나간다.
    // 그래서 회피법이 갈린다 — 파도는 "날아오는 것 사이로" 피하고, 창은 "겨누는 동안 선 밖으로"
    // 빠진다. 회피 축이 겹치지 않아야 패턴을 늘린 만큼 플레이어가 할 일도 늘어난다
    // (재 폭발 주석에 적어둔 것과 같은 기준이다).
    //
    // 시전 바로 예고하는 유일한 패턴이다. 기획에서 원거리 공격은 바로 알리되 <b>저지는 안 되는</b>
    // 것으로 정했다. 그래서 이 패턴은 맞아도 끊기지 않는다 — 공격 중 경직을 안 거는 기존 규칙이
    // 그대로 적용된다(<see cref="OnDamaged"/> 참고).
    [Header("재의 창 (원거리)")]
    [Tooltip("창 프리팹. 비어 있으면 이 패턴을 건너뛴다. " +
             "Tools → 재의 길 → 프리팹 → 재의 창 투사체 생성 이 만들어 꽂는다.")]
    [SerializeField] private Projectile spearPrefab;

    [Tooltip("재의 창을 쓸 수 있는 최대 거리. 조준선 길이도 이 값이다.")]
    [SerializeField] private float spearRange = 18f;

    [Tooltip("다시 쓰기까지의 시간(초). 2페이즈에서는 페이즈 배율이 곱해져 더 자주 나온다.")]
    [SerializeField, Min(0f)] private float spearCooldown = 9f;

    [Tooltip("겨누는 시간(초). 이 동안 조준선이 따라 돌고 시전 바가 찬다. " +
             "짧으면 예고가 예고 구실을 못 하고, 길면 보스가 멈춰 서 있는 시간이 된다.")]
    [SerializeField, Min(0.1f)] private float spearAimSeconds = 1.1f;

    [Tooltip("1페이즈에 나가는 창의 수. 기획에서 1페이즈에도 짧게 넣기로 했다.")]
    [SerializeField, Min(1)] private int spearCount = 3;

    [Tooltip("2페이즈에 더해지는 창의 수.")]
    [SerializeField, Min(0)] private int spearPhase2Bonus = 2;

    [Tooltip("창 사이 각도(도).")]
    [SerializeField] private float spearSpreadDegrees = 14f;

    [Tooltip("창이 하나씩 나가는 간격(초). 0이면 한꺼번에 나간다. 조금 두면 가운데부터 " +
             "좌우로 퍼지는 순서가 눈에 보인다.")]
    [SerializeField, Min(0f)] private float spearFireInterval = 0.07f;

    // 추가 생성 — 조준이 따라 도는 속도.
    //
    // <b>이 값 하나가 이 패턴의 회피 난이도다.</b> 무한대면 겨누는 동안 무엇을 해도 조준이
    // 붙어 있어서 시전 시간이 그냥 대기 시간이 된다. 제한을 두면 보스 주위를 크게 돌아
    // 조준을 흘릴 수 있고, 그때 비로소 "겨누는 동안 움직인다"가 회피가 된다.
    [Tooltip("조준선이 플레이어를 따라 도는 속도(도/초). 클수록 끈질기게 따라붙는다.")]
    [SerializeField, Min(0f)] private float spearTurnDegreesPerSecond = 200f;

    [SerializeField] private int spearDamage = 1;

    [Tooltip("모션 시작부터 창이 나가기까지(초). ashking_wave 클립의 5번째 프레임(0.417) 근처다. " +
             "파도를 빼면서 이 두 숫자가 창으로 넘어왔다 — 던지는 그림은 같은 클립을 쓴다.")]
    [SerializeField] private float spearFireDelay = 0.4f;

    [Tooltip("던지는 모션 전체 길이(초). 클립 길이(7프레임 ÷ 12fps = 0.583)와 맞춘다.")]
    [SerializeField] private float spearMotionSeconds = 0.583f;

    [Tooltip("창이 나가는 높이(유닛). 조준선도 같은 높이에서 시작한다 — 파도가 조준 원점을 " +
             "발사 원점과 어긋나게 잡아 헛치던 실수를 되풀이하지 않기 위해서다.")]
    [SerializeField] private float spearSpawnHeight = 1.6f;

    [Tooltip("시전 바에 적을 이름.")]
    [SerializeField] private string spearCastName = "재의 창";

    [Tooltip("조준선을 그리는 컴포넌트. 비어 있으면 실행할 때 스스로 붙인다 — 그림 자산이 " +
             "필요 없는 컴포넌트라 프리팹에 미리 달아두지 않아도 된다.")]
    [SerializeField] private BossAimFan aimFan;

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

    /// <summary>
    /// 추가 생성(2026-09-21) — 왕관 의식이 시작된 순간에 울린다. 방(<see cref="BossEncounter"/>)이 받아
    /// 의식 연출(<see cref="CrownRitual"/>)을 돌리고, 끝나면 <see cref="EndCrownRitual"/>을 부른다.
    ///
    /// <see cref="EnteredPhase2"/>와 같은 이유로 이벤트다. 보스는 방의 네 귀퉁이도, 플레이어의 유물도
    /// 모른다 — 의식에 필요한 것은 전부 방 쪽에 있다.
    /// </summary>
    public event Action CrownRitualStarted;

    // 추가 생성(2026-09-21) — 이번 판에 의식을 이미 시작했는가. 한 판에 한 번뿐이다.
    private bool crownRitualStarted;

    /// <summary>
    /// 추가 생성(2026-09-20, 시전 바) — 예고가 필요한 패턴을 시작할 때 울린다.
    /// 넘기는 값은 (패턴 이름, 시전 길이(초))다.
    ///
    /// <see cref="EnteredPhase2"/>와 같은 이유로 이벤트다. 보스는 화면에 무엇이 있는지 모르고,
    /// 방(<see cref="BossEncounter"/>)이 받아 시전 바에 넘긴다. 길이를 같이 보내는 이유는
    /// 눈금의 주인이 보스이기 때문이다 — 바가 1.1초를 따로 적어두면 보스 쪽 시전 시간을
    /// 바꿨을 때 <b>바만 옛 속도로 찬다.</b>
    /// </summary>
    public event Action<string, float> CastStarted;

    /// <summary>
    /// 추가 생성(2026-09-20) — 시전이 끝났거나 끊겼을 때 울린다.
    ///
    /// 끊기는 경우도 같은 이벤트로 알린다(죽음·페이즈 전환). 바 입장에서 할 일은 둘 다
    /// "그만 채운다"로 같고, 두 이벤트로 나누면 한쪽을 안 듣는 곳이 반드시 생긴다.
    /// </summary>
    public event Action CastEnded;

    // 추가 생성(2026-09-20) — 재의 창을 다시 쓸 수 있을 때까지 남은 시간.
    private float spearCooldownTimer;

    // 추가 생성 — 재 폭발을 다시 쓸 수 있을 때까지 남은 시간.
    private float ultimateCooldownTimer;

    // 추가 생성 — 이번 내려찍기가 노리는 방향. 예비동작이 시작될 때 고정하고 판정 때 그대로 쓴다.
    //
    // 지역 변수가 아니라 필드로 둔 이유: 기즈모가 같은 값을 그려야 인스펙터에서 눈으로 보는
    // 범위와 실제 판정이 일치한다. 판정 범위를 맞추는 도구가 실제와 다르면 없느니만 못하다.
    private Vector2 slamAim = Vector2.right;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int SlamHash = Animator.StringToHash("Slam");
    // 수정(2026-09-20, 파도 제거) — WaveHash → ThrowHash. 애니메이터 상태 이름은 "Wave" 그대로다
    // (컨트롤러를 고치면 1·2페이즈 두 벌을 다시 이어야 한다). 쓰는 쪽은 이제 재의 창뿐이다.
    private static readonly int ThrowHash = Animator.StringToHash("Wave");
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
        // 패턴 전용 쿨다운은 상태 검사보다 <b>위</b>에서 흐른다.
        //
        // 아래 return 뒤에 두면 공격·경직·전환 중에 멈춘다. 그러면 인스펙터에 적은 9초가
        // <b>쉬는 시간 9초</b>라는 뜻이 되고, 패턴 하나가 1.5초씩 걸리니 체감 주기는 11초까지
        // 늘어난다. 쿨다운을 시전 <b>시작</b> 시점에 거는 이유(숫자와 실제 주기를 맞추려고)와
        // 정면으로 어긋난다. 예전에 파도가 정확히 그 상태였다.
        if (ultimateCooldownTimer > 0f) ultimateCooldownTimer -= Time.deltaTime;
        if (spearCooldownTimer > 0f) spearCooldownTimer -= Time.deltaTime;

        // 추가 생성(2026-09-22) — 의식 연출은 아래 상태 검사(의식 중이면 return)보다 위에서 돈다.
        UpdateRitualEffects();

        // 수정(2026-09-21) — 의식 중(Ritual)에도 아무것도 안 한다.
        // 수정(2026-09-22) — 그로기(Groggy) 중에도 아무것도 안 한다.
        if (state == State.Dead || state == State.Attack ||
            state == State.Transition || state == State.Hit || state == State.Ritual ||
            state == State.Groggy) return;

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

        // 추가 생성(2026-09-20) — 재의 창은 파도보다 먼저 본다.
        //
        // 이유는 재 폭발을 맨 앞에 둔 것과 같다. 시전에 1초 넘게 걸리는 데다 쿨다운도 길어서,
        // 순서를 뒤로 두면 차례가 됐을 때 파도에 계속 밀린다. 그러면 인스펙터의 9초가
        // "9초 + 파도가 안 걸릴 때까지"가 되어 숫자가 뜻을 잃는다.
        bool canSpear = SpearPrefab != null && distance <= spearRange && spearCooldownTimer <= 0f;
        if (canSpear) { StartAshSpears(toPlayer); return; }

        if (distance <= slamRange) { StartCoroutine(Slam(toPlayer)); return; }

        Chase(toPlayer, distance);
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

        // 추가 생성(2026-09-23, 보스-5) — 걷는 동안 걸음 간격마다 발밑에 재 먼지를 터뜨린다.
        // 애니메이션 이벤트 대신 시간으로 세는 이유: 걷기 클립은 보스 애니메이션 빌더가 매번 새로 굽는다.
        // 이벤트를 박으면 빌더까지 고쳐야 하는데, 먼지는 발과 한두 프레임 어긋나도 눈에 안 띈다.
        footstepTimer -= Time.deltaTime;
        if (footDust != null && footstepTimer <= 0f)
        {
            footDust.Play();
            footstepTimer = footstepInterval;
        }
    }

    /// <summary>플레이어에게 다가간다. 원거리 패턴이 쿨다운일 때 먼 거리에서 쓴다.</summary>
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
        // 재의 창(<see cref="AshSpears"/>)은 반대로 겨누는 동안 계속 방향을 고쳐 잡는다. 두 패턴이 다른 이유:
        // 창은 화면을 가로지르는 원거리라, 제자리에서 옆으로 한 발짝 걷는 것만으로 전부
        // 피해지면 패턴이 성립하지 않는다(그래서 조준선으로 미리 보여준다). 반면 내려찍기는 <b>예비동작을 보고 그 자리를 벗어나는 것이
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

        // 추가 생성(2026-09-23, 보스-2) — 칼끝이 닿는 자리에서 파편·먼지·불티가 터진다(판정과 같은 순간).
        if (slamImpact != null)
        {
            slamImpact.transform.localPosition = new Vector3(slamImpactOffset.x * FacingSign(), slamImpactOffset.y, 0f);
            slamImpact.Play();
        }

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

    /// <summary>
    /// 추가 생성(2026-09-20) — 창 프리팹. 비어 있으면 이 패턴을 건너뛴다.
    ///
    /// 수정(2026-09-20, 파도 제거) — 비었을 때 파도 프리팹으로 대신 쏘던 임시 처리를 걷어냈다.
    /// 파도 패턴 자체가 사라져서 빌려올 것이 없다. 대신 <see cref="OnValidate"/>가 비어 있으면
    /// 알려준다 — 조용히 사라지는 패턴을 다시 찾아다니지 않게.
    /// </summary>
    private Projectile SpearPrefab => spearPrefab;

    /// <summary>
    /// 추가 생성(2026-09-20) — 재의 창을 시작하고 전용 쿨다운을 건다.
    /// 재 폭발과 같이 <b>시작할 때</b> 건다(이유는 <see cref="StartUltimate"/> 주석 참고).
    /// </summary>
    private void StartAshSpears(Vector2 toPlayer)
    {
        spearCooldownTimer = spearCooldown * (isPhase2 ? phase2CooldownScale : 1f);
        StartCoroutine(AshSpears(toPlayer));
    }

    /// <summary>
    /// 추가 생성(2026-09-20) — 재의 창. <b>겨누는 것을 보여주고</b> 나가는 원거리 공격이다.
    ///
    /// 흐름: 부채꼴 조준선을 켠다 → 시전 바를 띄운다 → 겨누는 동안 조준선이 플레이어를 따라
    /// 돈다 → 방향을 그 자리에서 굳힌다 → 던지는 모션에 맞춰 가운데부터 좌우로 하나씩 나간다.
    ///
    /// <b>방향을 발사 직전이 아니라 시전이 끝나는 순간에 굳히는 것이 핵심이다.</b> 파도처럼
    /// 발사 시점에 다시 잡으면, 모션이 도는 0.4초 동안 조준선은 멈춰 있는데 창은 다른 데로
    /// 나간다. 조준선이 거짓말을 하는 셈이라, 예고 자체를 못 믿게 된다.
    /// </summary>
    private IEnumerator AshSpears(Vector2 toPlayer)
    {
        state = State.Attack;
        Stop();
        state = State.Attack; // Stop이 Idle로 되돌리므로 다시 잠근다

        int count = spearCount + (isPhase2 ? spearPhase2Bonus : 0);
        Vector2 aim = AimFrom(toPlayer);

        BossAimFan fan = EnsureAimFan();
        fan.Show(count, spearSpreadDegrees, spearRange, spearSpawnHeight);

        // 추가 생성(2026-09-23, 보스-3) — 겨누는 동안 창이 나갈 자리로 불씨를 모은다. 조준선이 사라질 때 같이 멈춘다.
        if (spearGather != null)
        {
            spearGather.transform.localPosition = new Vector3(0f, spearSpawnHeight, 0f);
            spearGather.Play();
        }
        fan.Aim(aim);

        CastStarted?.Invoke(spearCastName, spearAimSeconds);

        // 겨누는 동안. 프레임마다 조준을 조금씩 돌리고 몸도 그쪽을 본다.
        for (float t = 0f; t < spearAimSeconds; t += Time.deltaTime)
        {
            aim = TrackPlayer(aim);
            fan.Aim(aim);
            FaceTowards(aim.x);
            yield return null;
        }

        // 보이는 선 그대로를 발사 방향으로 굳힌다. 각도를 여기서 다시 계산하지 않고
        // 조준선에게 물어보는 이유는 BossAimFan.DirectionAt 주석에 적어뒀다.
        var directions = new Vector2[count];
        for (int i = 0; i < count; i++) directions[i] = fan.DirectionAt(i);

        CastEnded?.Invoke();

        // 던지는 그림은 보스의 "Wave" 상태를 그대로 쓴다. 클립 이름이 파도에서 왔을 뿐,
        // 대검을 휘둘러 무언가를 날리는 동작이라 창에도 맞는다.
        float fireDelay = MotionTime(spearFireDelay);
        float motionSeconds = MotionTime(spearMotionSeconds);

        if (animator != null) animator.SetTrigger(ThrowHash);

        yield return new WaitForSeconds(fireDelay);

        // 첫 창이 나가는 순간 조준선을 끈다. 남겨두면 이미 날아간 뒤에도 선이 떠 있어서
        // "아직 안 쐈다"로 읽힌다.
        fan.Hide();
        if (spearGather != null) spearGather.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        Vector2 spawn = (Vector2)transform.position + Vector2.up * spearSpawnHeight;
        Projectile prefab = SpearPrefab;
        float fired = 0f;

        foreach (int index in FireOrder(count))
        {
            var spear = Instantiate(prefab, spawn, Quaternion.identity);
            spear.Launch(directions[index], spearDamage);

            if (spearFireInterval <= 0f) continue;

            yield return new WaitForSeconds(spearFireInterval);
            fired += spearFireInterval;
        }

        yield return new WaitForSeconds(Mathf.Max(0f, motionSeconds - fireDelay - fired));

        EndAttack();
    }

    /// <summary>
    /// 추가 생성(2026-09-20) — 창이 나가는 순서. <b>가운데부터 좌우로 번갈아</b> 나간다.
    ///
    /// 한쪽 끝에서부터 차례로 쏘면 부채꼴이 아니라 <b>쓸어내리는 선</b>으로 보인다.
    /// 가운데부터 퍼지면 짧은 간격에도 "가운데를 노리고 좌우로 벌어진다"가 읽힌다.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<int> FireOrder(int count)
    {
        int center = count / 2;
        yield return center;

        for (int step = 1; step <= count; step++)
        {
            int right = center + step;
            if (right < count) yield return right;

            int left = center - step;
            if (left >= 0) yield return left;
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-20) — 지금 조준을 플레이어 쪽으로 <b>조금만</b> 돌린다.
    ///
    /// 한 프레임에 돌릴 수 있는 각도를 <see cref="spearTurnDegreesPerSecond"/>로 막는다.
    /// 목표를 발밑이 아니라 몸통 가운데로 잡는 이유는 파도와 같다 — 쏘는 높이와 겨누는
    /// 높이가 다르면 옆에서 쏠 때만 스치듯 맞는다.
    /// </summary>
    private Vector2 TrackPlayer(Vector2 current)
    {
        if (player == null) return current;

        Vector2 spawn = (Vector2)transform.position + Vector2.up * spearSpawnHeight;
        Vector2 target = playerCollider != null
            ? (Vector2)playerCollider.bounds.center
            : (Vector2)player.position;

        Vector2 desired = target - spawn;
        if (desired.sqrMagnitude < 0.0001f) return current;

        float delta = Vector2.SignedAngle(current, desired.normalized);
        float step = Mathf.Clamp(delta, -spearTurnDegreesPerSecond * Time.deltaTime,
                                 spearTurnDegreesPerSecond * Time.deltaTime);

        Vector3 turned = Quaternion.Euler(0f, 0f, step) * (Vector3)current;
        return ((Vector2)turned).normalized;
    }

    /// <summary>
    /// 추가 생성(2026-09-20) — 조준선 컴포넌트를 얻는다. 없으면 그 자리에서 붙인다.
    ///
    /// 프리팹에 미리 달아두지 않아도 되는 이유: <see cref="BossAimFan"/>은 스프라이트도
    /// 머티리얼도 인스펙터에서 물릴 것이 없다. 프리팹을 고쳐야만 도는 구조로 만들면
    /// 보스 프리팹을 다시 만드는 빌더까지 같이 고쳐야 한다.
    /// </summary>
    private BossAimFan EnsureAimFan()
    {
        if (aimFan == null) aimFan = GetComponent<BossAimFan>();
        if (aimFan == null) aimFan = gameObject.AddComponent<BossAimFan>();

        return aimFan;
    }

    /// <summary>
    /// 추가 생성(2026-09-20) — 시전을 도중에 끊는다. 죽음·페이즈 전환처럼 코루틴이
    /// 통째로 멈추는 자리에서 부른다.
    ///
    /// 코루틴이 잘리면 조준선을 끄는 코드도 같이 잘린다. 그러면 <b>보스가 죽은 자리에
    /// 조준선만 남고</b> 시전 바도 가득 찬 채로 굳는다.
    /// </summary>
    private void CancelCast()
    {
        if (aimFan != null) aimFan.Hide();

        // 추가 생성(2026-09-23) — 조준 불씨도 같이 끊는다. 코루틴이 잘리면 멈추는 줄까지 잘린다.
        if (spearGather != null) spearGather.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        CastEnded?.Invoke();
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

        // 추가 생성(2026-09-23, 보스-4) — 시전 동안 판정 반경 밖에서 재가 소용돌이치며 모인다. 반경은 코드 값을 읽는다.
        if (burstGather != null)
        {
            var gatherShape = burstGather.shape;
            gatherShape.radius = ultimateRadius + 0.5f;
            burstGather.Play();
        }

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

        // 추가 생성(2026-09-23, 보스-4) — 판정 순간 고리가 판정 반경까지 퍼진다. 수명(0.3~0.36초)에 맞춰 속도를 반경에서 구한다 —
        // 숫자를 따로 적어 두면 반경을 고쳤을 때 고리가 판정과 다른 데서 멈춘다.
        if (burstGather != null) burstGather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (burstRing != null)
        {
            var ringMain = burstRing.main;
            ringMain.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.36f);
            ringMain.startSpeed = new ParticleSystem.MinMaxCurve(ultimateRadius / 0.36f, ultimateRadius / 0.3f);
            burstRing.Play();
        }

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

        // 추가 생성(2026-09-23, 보스-6) — 맞은 순간 때린 반대쪽으로 갑옷 조각이 튄다. 죽는 한 대에도 튄다(아래 return보다 먼저).
        // 조각은 +X로 튀게 만들어 두고 맞은 방향으로 돌린다(명중 불똥과 같은 방식).
        if (hitShards != null)
        {
            Vector2 away = health.LastHitDirection;
            hitShards.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg);
            hitShards.Play();
        }

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

            // 추가 생성(2026-09-20) — 시전 중이었다면 먼저 끊는다. StopAllCoroutines가
            // 조준선을 끄는 줄까지 같이 자르기 때문이다.
            CancelCast();

            StopAllCoroutines();
            StartCoroutine(EnterPhase2());
            return;
        }

        // 추가 생성(2026-09-21, 왕관 의식) — 2페이즈에서 체력이 의식 비율 아래로 내려가면 한 번 시작한다.
        //
        // 페이즈 전환처럼 공격 도중이어도 끼어든다. 의식은 판의 절정이라 반쯤 진행된 패턴보다 중요하다.
        // 시전 중이던 창은 끊고(조준선·시전 바가 남지 않게) 코루틴을 전부 멈춘다.
        if (isPhase2 && !crownRitualStarted && current <= max * crownRitualHealthRatio)
        {
            crownRitualStarted = true;
            CancelCast();
            StopAllCoroutines();
            BeginCrownRitual();
            return;
        }

        // 공격 중에는 경직을 안 건다. 걸면 예비동작이 끊겨서, 플레이어가 계속 때리는 것만으로
        // 보스가 아무 패턴도 못 쓰는 허수아비가 된다.
        if (state == State.Attack) return;

        // 추가 생성(2026-09-22) — 그로기 중에는 경직(HitStun)을 새로 걸지 않는다. HitStun은 끝날 때 상태를 Idle로
        // 돌려놓아서, 걸면 그로기가 첫 대에 풀려 버린다. 맞은 모션만 보여 준다.
        if (state == State.Groggy)
        {
            // 수정(2026-09-22) — 그로기 중에는 무릎 꿇은 자세를 붙들고 있어(애니메이터 꺼짐) 맞은 모션 대신 가슴 불꽃만 튄다.
            if (animator != null && animator.enabled) animator.SetTrigger(HitHash);
            else if (ritualChestSparks != null) ritualChestSparks.Play();
            return;
        }

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
                           "Tools → 재의 길 → 프리팹 → 보스 전환 이펙트 생성 을 실행해라.", this);

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
        // 수정(2026-09-21, 흐름 B) — 한 프레임에 끄던 것을 흐리며 끄게 바꿨다. 이유는
        // transitionFadeOutSeconds 주석 참고. 다 흐려진 뒤에는 예전처럼 렌더러를 끈다.
        if (spriteRenderer != null) StartCoroutine(FadeSprite(1f, 0f, transitionFadeOutSeconds, true));
    }

    /// <summary>
    /// 추가 생성(2026-09-21) — 보스 그림의 투명도만 <paramref name="from"/>에서 <paramref name="to"/>로 옮긴다.
    ///
    /// 색(RGB)은 건드리지 않는다. 피격 번쩍임 같은 다른 연출이 색을 쓰기 때문이다.
    /// 게임 시간으로 흐른다 — 전환 타임라인과 같은 시계라야 깨짐 순간의 멈춤(히트스톱)에 같이 멈춘다.
    /// </summary>
    /// <param name="hideAtEnd">끝나면 렌더러를 끈다(갑옷 붕괴). 켜 둔 채 투명하게 두면 그림자·정렬 계산이 계속 돈다.</param>
    private IEnumerator FadeSprite(float from, float to, float seconds, bool hideAtEnd)
    {
        if (spriteRenderer == null) yield break;

        spriteRenderer.enabled = true;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            SetSpriteAlpha(Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }

        SetSpriteAlpha(to);
        if (hideAtEnd)
        {
            spriteRenderer.enabled = false;

            // 꺼 둔 뒤에는 투명도를 되돌려 둔다. 다음에 켜는 쪽(2페이즈 등장·사망)이 투명한 채로 켜지지 않게.
            SetSpriteAlpha(1f);
        }
    }

    /// <summary>추가 생성(2026-09-21) — 그림의 투명도만 바꾼다.</summary>
    private void SetSpriteAlpha(float alpha)
    {
        if (spriteRenderer == null) return;

        Color color = spriteRenderer.color;
        color.a = alpha;
        spriteRenderer.color = color;
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

        // 추가 생성(2026-09-23, 보스-1) — 2페이즈 모습이 드러나는 순간부터 몸에서 불티가 피어오른다.
        if (emberAura != null) emberAura.Play();

        // 추가 생성 — 몸이 줄었으니 맞는 자리도 줄인다.
        ShrinkColliderForPhase2();

        if (animator != null)
        {
            animator.runtimeAnimatorController = phase2Controller;

            // 컨트롤러를 바꾸면 파라미터가 새로 잡히므로 상태를 처음부터 다시 물린다.
            // 안 하면 예전 컨트롤러의 재생 위치가 남아 첫 프레임이 엉뚱하게 나온다.
            animator.Rebind();
        }

        // 수정(2026-09-21, 흐름 B) — 한 프레임에 켜던 것을 깨진 알 속에서 떠오르게 바꿨다.
        // 이유는 transitionFadeInSeconds 주석 참고. 투명에서 시작해야 켜지는 순간 번쩍 튀지 않는다.
        if (spriteRenderer != null)
        {
            SetSpriteAlpha(0f);
            spriteRenderer.enabled = true;
            StartCoroutine(FadeSprite(0f, 1f, transitionFadeInSeconds, false));
        }
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

    /// <summary>
    /// 추가 생성(2026-09-21) — 왕관 의식에 들어간다. 멈춰 서서 무적이 되고, 방에 알린다.
    ///
    /// 듣는 쪽이 없으면(의식 연출을 안 꾸린 씬) 곧바로 끝낸다. 안 그러면 보스가 무적인 채로
    /// 영영 서 있어서 판이 끝나지 않는다 — 에러 없이 "보스가 안 죽는다"로만 보이는 막힘이다.
    /// </summary>
    private void BeginCrownRitual()
    {
        state = State.Ritual;
        body.linearVelocity = Vector2.zero;
        health.IsInvulnerableExternally = true;
        if (animator != null) animator.SetFloat(SpeedHash, 0f);

        Debug.Log($"[보스] 왕관 의식 시작 — 체력 {health.Current}/{health.Max}.", this);

        // 추가 생성(2026-09-22) — 동작-1 손 뻗기와 의식-1 줄기. 플레이어 쪽으로 돌아서 손을 뻗고, 유물이 뽑힐 때까지
        // (RitualRelicsTorn) 플레이어 가슴에서 손으로 불티를 끌어온다. 의식을 받는 쪽이 없으면 아래에서 곧바로 끝난다.
        if (player != null) FaceTowards(player.position.x - transform.position.x);
        SetPose(ritualReachSprite);
        ritualTetherOn = ritualTether != null && player != null;
        ritualTetherCarry = 0f;
        ritualHoldReached = false;

        if (CrownRitualStarted == null)
        {
            Debug.LogWarning("[보스] 왕관 의식을 받아 줄 곳이 없다(방에 CrownRitual이 없다). 의식 없이 이어간다. " +
                             "Tools → 재의 길 → 씬·세팅 → 왕관 의식 구성 을 실행해라.", this);
            EndCrownRitual();
            return;
        }

        CrownRitualStarted.Invoke();
    }

    /// <summary>
    /// 추가 생성(2026-09-21) — 왕관 의식을 끝내고 싸움으로 돌아온다. 의식 연출이 끝나면 방이 부른다.
    /// 의식 중이 아니면 아무것도 안 한다(두 번 불려도 안전).
    /// </summary>
    /// <param name="groggy">수정(2026-09-22) — 유물을 다 부숴 의식을 막았는가. 참이면 곧바로 싸우지 않고 그로기에 빠진다.</param>
    public void EndCrownRitual(bool groggy = false)
    {
        if (state != State.Ritual) return;

        health.IsInvulnerableExternally = false;

        // 추가 생성(2026-09-22) — 의식 연출(줄기·왕관 불·소용돌이)을 거둔다.
        ritualTetherOn = false;
        if (crownFire != null) crownFire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (ashVortex != null) ashVortex.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // 추가 생성(2026-09-22) — 의식을 막혔으면 그로기로 넘어간다.
        if (groggy && groggySeconds > 0f)
        {
            StartCoroutine(Groggy());
            return;
        }

        // 추가 생성(2026-09-22) — 의식-7 못 막았을 때: 들고 있던 칼을 내리치며 방으로 충격파가 퍼진다.
        // 결과(체력 깎기)는 의식이 바로 앞에서 넣었다. 칼을 내리치는 그림은 내려찍기 모션을 그대로 튼다(판정 없이 모습만).
        // 의식을 받는 쪽이 없어 곧바로 끝난 경우(의식 자세까지 못 감)에는 터뜨리지 않는다 — 아무 일도 없었는데 충격파만 난다.
        ClearPose();
        if (ritualHoldReached)
        {
            if (ritualBlast != null) ritualBlast.Play();
            if (animator != null) animator.SetTrigger(SlamHash);
        }

        // 의식에서 풀리자마자 공격이 날아오면 플레이어가 자리를 잡을 틈이 없다. 전환 뒤와 같은 짧은 쉼.
        cooldownTimer = 0.6f;
        state = State.Idle;
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 유물이 뽑혀 나갔다(방이 의식의 신호를 전한다). 줄기를 끊어 양 끝에서 터뜨리고,
    /// 0.4초 더 손을 뻗고 있다가 의식 자세(칼을 머리 위로)로 바꾸며 왕관 불과 재 소용돌이를 켠다.
    /// </summary>
    public void RitualRelicsTorn()
    {
        if (state != State.Ritual) return;

        if (ritualTetherOn)
        {
            ritualTetherOn = false;
            BurstTether(RitualHand(), 30);
            if (player != null) BurstTether(player.position + Vector3.up * 2f, 30);
        }

        StartCoroutine(RitualHoldAfterReach());
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 유물 하나가 깨졌다. 가슴에서 불꽃이 튀고 움찔한 뒤 의식 자세로 돌아간다(동작-3, 의식-5).
    /// 무적이라 피해는 없고 모습만이다.
    /// </summary>
    public void RitualFlinch()
    {
        if (state != State.Ritual) return;

        if (ritualChestSparks != null) ritualChestSparks.Play();
        if (ritualFlinchSprites.Length > 0) StartCoroutine(RitualFlinchRoutine());

        // 추가 생성(2026-09-23) — 유물이 부서지면 시전 바가 처음으로 돌아간다. 왕관 불도 약해졌다가 다시 커진다.
        if (ritualHoldReached) crownFireStartTime = Time.time;
    }

    /// <summary>추가 생성(2026-09-22) — 유물 실이 닿을 보스 가슴(월드 좌표).</summary>
    public Vector3 RitualChest => transform.position + Vector3.up * ritualChestHeight;

    private IEnumerator RitualHoldAfterReach()
    {
        yield return new WaitForSeconds(0.4f);
        if (state != State.Ritual) yield break;

        SetPose(ritualHoldSprite);
        ritualHoldReached = true;
        crownFireStartTime = Time.time;
        FaceChild(crownFire);
        FaceChild(collapseAsh);
        if (crownFire != null) crownFire.Play();
        if (ashVortex != null) ashVortex.Play();
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 자식 파티클의 좌우 자리를 보스가 보는 쪽에 맞춘다. 그림은 flipX로 뒤집히지만
    /// 자식 오브젝트는 안 뒤집혀서, 왼쪽을 볼 때 왕관 불이 머리 반대편에 뜬다.
    /// </summary>
    private void FaceChild(ParticleSystem particles)
    {
        if (particles == null) return;

        Vector3 local = particles.transform.localPosition;
        local.x = Mathf.Abs(local.x) * FacingSign();
        particles.transform.localPosition = local;
    }

    private IEnumerator RitualFlinchRoutine()
    {
        foreach (Sprite sprite in ritualFlinchSprites)
        {
            SetPose(sprite);
            yield return new WaitForSeconds(0.15f);
            if (state != State.Ritual) yield break;
        }

        SetPose(ritualHoldSprite);
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 의식 연출의 매 프레임 일. 줄기를 뿌리고, 왕관 불을 의식 진행에 맞춰 키운다.
    /// </summary>
    private void UpdateRitualEffects()
    {
        if (ritualTetherOn && player != null)
        {
            ritualTetherCarry += ritualTetherRate * Time.deltaTime;
            int count = (int)ritualTetherCarry;
            ritualTetherCarry -= count;

            Vector3 chest = player.position + Vector3.up * 2f;
            Vector3 hand = RitualHand();
            const float life = 0.42f;
            for (int i = 0; i < count; i++)
            {
                var emit = new ParticleSystem.EmitParams
                {
                    position = chest + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.3f),
                    velocity = (hand - chest) / life + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.6f),
                    startLifetime = life,
                    applyShapeToPosition = false,
                };
                ritualTether.Emit(emit, 1);
            }
        }

        // 왕관 불은 켠 순간 약하게 시작해 의식 시간 동안 네 배 반까지 세진다(기획: 초당 10 → 45).
        if (crownFire != null && crownFire.isEmitting)
        {
            var emission = crownFire.emission;
            if (crownFireBaseRate < 0f) crownFireBaseRate = emission.rateOverTimeMultiplier;
            float progress = Mathf.Clamp01((Time.time - crownFireStartTime) / crownIgniteSeconds);
            emission.rateOverTimeMultiplier = crownFireBaseRate * Mathf.Lerp(1f, 4.5f, progress);
        }
    }

    /// <summary>추가 생성(2026-09-22) — 손 뻗기 장에서 손의 월드 좌표. 왼쪽을 보면 좌우가 뒤집힌다.</summary>
    private Vector3 RitualHand()
    {
        return transform.position + new Vector3(ritualHandOffset.x * FacingSign(), ritualHandOffset.y, 0f);
    }

    /// <summary>추가 생성(2026-09-22) — 줄기 끝에서 불티를 사방으로 터뜨린다(줄기가 끊기는 순간).</summary>
    private void BurstTether(Vector3 at, int count)
    {
        if (ritualTether == null) return;

        for (int i = 0; i < count; i++)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = (Vector3)(UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(2f, 7f)),
                startLifetime = UnityEngine.Random.Range(0.25f, 0.45f),
                applyShapeToPosition = false,
            };
            ritualTether.Emit(emit, 1);
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 한 장짜리 자세를 붙든다. 애니메이터를 끄고 그 스프라이트를 직접 넣는다(이유는 필드 주석).
    /// 그림이 비어 있으면 아무것도 안 한다 — 애니메이터가 하던 모습(대기)이 그대로 남는다.
    /// </summary>
    private void SetPose(Sprite sprite)
    {
        if (sprite == null || spriteRenderer == null) return;

        if (animator != null) animator.enabled = false;
        spriteRenderer.sprite = sprite;
    }

    /// <summary>추가 생성(2026-09-22) — 자세를 놓고 애니메이터를 다시 켠다. 다시 켜진 애니메이터는 대기 상태부터 돈다.</summary>
    private void ClearPose()
    {
        if (animator != null) animator.enabled = true;
    }

    /// <summary>추가 생성(2026-09-22) — 의식·그로기 연출을 전부 거둔다(죽을 때).</summary>
    private void StopRitualEffects()
    {
        ritualTetherOn = false;
        if (crownFire != null) crownFire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (ashVortex != null) ashVortex.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (dizzyEmbers != null) dizzyEmbers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        ClearPose();
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 그로기. 멈춰 서서 맞기만 한다. 무적이 아니고, 남은 시간을 시전 바로 보여 준다.
    ///
    /// 시전 바를 쓰는 이유: 이때 플레이어에게 필요한 정보는 "얼마 동안 때릴 수 있나" 하나다. 바는 이미 보스 체력바
    /// 아래에 있고, 보스가 신호(<see cref="CastStarted"/>)만 내면 방이 받아 채운다 — 새 UI 없이 같은 자리에서 읽힌다.
    /// 그로기 도중에 죽으면 <see cref="OnDied"/>가 코루틴을 멈추고 <see cref="CancelCast"/>로 바를 거둔다.
    ///
    /// 스케일 시간으로 센다. 다른 보스 코루틴과 같이 히트스톱·일시정지에 멈춰야, 인벤토리를 여는 동안
    /// 그로기가 저절로 흘러가 버리지 않는다.
    /// </summary>
    private IEnumerator Groggy()
    {
        state = State.Groggy;
        body.linearVelocity = Vector2.zero;
        if (animator != null) animator.SetFloat(SpeedHash, 0f);

        Debug.Log($"[보스] 왕관 의식을 막혔다 — {groggySeconds:0.#}초 그로기.", this);
        CastStarted?.Invoke(groggyCastName, groggySeconds);

        // 추가 생성(2026-09-22) — 동작-4 무너짐, 의식-6 무너짐 재. 움찔 둘째 장을 잠깐 보인 뒤 칼을 짚고 무릎 꿇는다.
        // 왕관 불이 꺼지며 재가 쏟아지고, 그로기 동안 꺼져 가는 불씨가 머리 위를 맴돈다.
        if (collapseAsh != null) collapseAsh.Play();
        if (ritualFlinchSprites.Length > 1)
        {
            SetPose(ritualFlinchSprites[1]);
            yield return new WaitForSeconds(0.15f);
        }

        SetPose(groggySprite);
        if (dizzyEmbers != null) dizzyEmbers.Play();

        yield return new WaitForSeconds(groggySeconds);

        CastEnded?.Invoke();

        // 추가 생성(2026-09-22) — 일어나며 연출을 거둔다.
        if (dizzyEmbers != null) dizzyEmbers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        ClearPose();

        // 그로기에서 풀리자마자 공격이 날아오면 억울하다. 의식 뒤와 같은 짧은 쉼을 둔다.
        cooldownTimer = 0.6f;
        state = State.Idle;
    }

    private void OnDied()
    {
        // 추가 생성(2026-09-20) — 시전 중에 죽으면 조준선과 시전 바가 그대로 남는다.
        // 코루틴을 멈추기 전에 끊어야 한다.
        CancelCast();

        // 추가 생성(2026-09-22) — 그로기 도중에 죽으면 무릎 꿇은 자세(애니메이터 꺼짐)가 남아 사망 모션이 안 나온다. 먼저 놓는다.
        StopRitualEffects();

        // 추가 생성(2026-09-23, 보스-7·보스-1) — 무너지는 몸에서 재가 피어오르고, 잿불 기운은 꺼진다.
        if (emberAura != null) emberAura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (burstGather != null) burstGather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (deathAsh != null) deathAsh.Play();

        StopAllCoroutines();

        // 추가 생성 — 전환 중이었다면 연출을 멈추고 몸을 되돌린다.
        //
        // 전환 중에는 무적이라 여기 올 일이 없어야 하지만, StopAllCoroutines가 EnterPhase2를
        // 중간에서 자를 수 있는 이상 <b>스프라이트가 꺼진 채 남는 길</b>이 존재한다.
        // 그러면 사망 모션이 재생되는데 화면에는 아무것도 없다. 죽었는지 사라졌는지 모른다.
        if (transitionSequence != null) transitionSequence.StopAndReset();
        if (spriteRenderer != null) spriteRenderer.enabled = true;

        // 추가 생성(2026-09-21) — 흐리던 도중에 코루틴이 멈췄다면 반투명으로 남는다. 사망 모션은 선명하게.
        SetSpriteAlpha(1f);

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

        if (slamRange > spearRange)
        {
            Debug.LogWarning(
                $"[보스] 내려찍기 사거리({slamRange})가 재의 창 사거리({spearRange})보다 크다. " +
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
