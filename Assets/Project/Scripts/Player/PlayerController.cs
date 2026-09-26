using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어의 이동 입력과 애니메이션 상태를 담당한다.
///
/// 이동을 Rigidbody2D로 처리하는 이유: transform.position을 직접 더하면 벽을 뚫는다.
/// 물리 엔진을 거쳐야 콜라이더가 이동을 막아주고, AshProjectSetup에서 짜둔 충돌 매트릭스
/// (Player x Wall)가 실제로 의미를 갖는다.
///
/// 수정(입력 중앙화): 입력은 더 이상 이 스크립트가 소유하지 않는다.
/// 이동·대시 액션은 <see cref="InputBindings"/>가 만들고 이 컴포넌트는 꺼내 읽기만 한다.
/// 옛 주석이 예고한 "리바인딩 UI를 만들 때 옮긴다"가 실행된 것인데, 옮긴 곳은
/// .inputactions 에셋이 아니라 코드로 만든 액션 맵이다 — 에셋으로 가면 바인딩이 프로젝트
/// 창 안으로 숨어서 <b>어느 키가 무슨 조작인지 코드만 읽어서는 알 수 없게</b> 된다.
/// InputBindings.Build 한 함수에 모아두면 그 장점을 유지하면서 한 곳에서 바꿀 수 있다.
///
/// gravityScale을 코드에서 건드리지 않는 이유: 2D 중력은 AshProjectSetup이 전역에서
/// (0,0)으로 꺼뒀다. gravityScale은 전역 중력에 곱해지는 값이라 전역이 0이면 여기가
/// 1이어도 안 떨어진다. 프리팹마다 하나씩 끄다 빠뜨리는 걸 막으려고 전역으로 정한 결정이라
/// 여기서 다시 손대면 그 결정이 흐려진다.
///
/// 추가 생성 — 애니메이션 작업 시점의 확장 요약:
/// 걷기/달리기/공격/대시/피격/사망이 붙으면서 "지금 무슨 동작 중인가"를 알아야 하게 됐다.
/// 그 상태를 <see cref="ActionState"/>로 코드가 소유하고, Animator에는 결과만 통보한다.
/// <b>Animator의 현재 상태를 코드가 되묻지 않는 것</b>이 이 설계의 핵심이다. Animator는
/// 전이 조건과 블렌딩 때문에 "지금 어느 상태인지"의 답이 한 프레임씩 늦고, 그 값으로 이동을
/// 막으면 공격 첫 프레임에 미끄러진다. 게임 규칙은 코드가, 보여줄 그림은 Animator가 정한다.
///
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    // 추가 생성 — 지금 어떤 동작 중인지. 이동 가능 여부와 입력 수용 여부가 여기서 갈린다.
    private enum ActionState
    {
        Normal,    // 걷기/달리기. 입력을 모두 받는다
        Attacking, // 공격 모션 재생 중. 제자리에 선다
        Dashing,   // 대시 중. 입력과 무관하게 정해진 방향으로 밀려간다
        Hit,       // 피격 경직. 입력을 안 받는다
        Dead,      // 사망. 이후 아무 입력도 받지 않는다

        // 추가 생성(2026-09-21, 왕관 의식) — 연출이 플레이어를 붙잡고 있다(보스 궁극기의 "유물 뽑힘" 등).
        // 입력·이동·스킬을 안 받고, 맞아도 경직으로 끊기지 않으며, 피해도 받지 않는다.
        // 끝내는 쪽은 연출 모션의 마지막 애니메이션 이벤트(또는 연출 쪽의 안전장치)다.
        Scripted,
    }

    [Header("이동")]
    // 수정(애니메이션 작업 시점): moveSpeed 하나였던 것을 걷기/달리기 둘로 나눴다.
    // 스태미나로 달리기를 제한하려면 "달릴 때만 빠른" 속도가 따로 있어야 의미가 생긴다.
    // 수정(Game 씬 규격 확인 시점): 3.5 / 6.5 → 8.7 / 16.
    //
    // 속도는 캐릭터 크기가 아니라 <b>방 크기</b>가 정하는 값이다. 캐릭터를 두 배로 키워도
    // 방을 가로지르는 데 걸려야 할 시간은 그대로다. 처음 값은 화면 가로 20유닛을 전제로
    // 잡았는데, 실제 Game 씬은 카메라 14 = 화면 가로 49.8유닛이었다. 배율 14/5.625 = 2.49를
    // 그대로 곱했다. 그래서 아래 세 속도가 전부 같은 비율로 커졌다.
    // 재수정(스케일 2 상태에서 체감 보정): 8.7 / 16 → 11 / 20.
    //
    // 방 크기만 놓고 계산하면 8.7 / 16이 맞다(횡단 5.7초 / 3.1초). 그런데 캐릭터를 스케일 2로
    // 키운 상태라 화면에서 차지하는 덩치가 두 배가 됐고, 그러면 같은 속도라도 "제 몸길이를
    // 초당 몇 번 지나가는가"가 절반이 되어 굼떠 보인다. 눈에 보이는 속도감은 방이 아니라
    // 캐릭터 크기 대비로 읽히기 때문에 1.25배 정도 얹었다.
    //
    // 나중에 PPU 재임포트가 정상적으로 먹어서 스케일을 1로 되돌리면 캐릭터가 다시 작아지므로
    // 이 값도 8.7 / 16으로 되돌리는 게 맞다.
    // 수정(달리기 삭제): runSpeed를 없애고 이동 속도를 하나로 합쳤다.
    //
    // 달리기와 대시가 둘 다 스태미나를 쓰니 자원 하나가 두 가지를 결정해서 과했다.
    // 달리기를 빼면 스태미나는 <b>회피 전용 자원</b>이 되어 역할이 선명해진다 —
    // "지금 대시를 쓸 것인가"만 묻게 된다.
    //
    // 속도는 걷기(11)와 달리기(20) 사이인 14로 올렸다. 달리기가 없어진 만큼 기본 이동이
    // 답답하면 안 되고, 그렇다고 20이면 회피가 필요 없어진다.
    [Tooltip("이동 속도(월드 유닛/초). 화면 가로가 49.8유닛이라 14면 약 3.6초에 횡단한다.")]
    [SerializeField] private float moveSpeed = 14f;

    // 추가 생성 — 대시
    [Header("대시")]
    // 수정(Game 씬 규격 확인 시점): 14 → 35. 걷기/달리기와 같은 2.49배였다.
    // 재수정(스케일 2 체감 보정): 35 → 44. 0.25초에 11유닛을 이동한다.
    // 대시는 달리기보다 확실히 빨라야 회피기로 읽히므로 달리기(20)의 2.2배를 유지했다.
    [Tooltip("대시 중 이동 속도(월드 유닛/초). 0.25초 동안 약 11유닛을 이동한다.")]
    [SerializeField] private float dashSpeed = 44f;

    [Tooltip("대시 지속 시간(초). 대시 클립이 4프레임 / 16fps = 0.25초라 그 값에 맞췄다. " +
             "이 값과 클립 길이가 어긋나면 모션이 끝났는데도 미끄러지거나 그 반대가 된다.")]
    [SerializeField] private float dashDuration = 0.25f;

    // 추가 생성(대시 VFX) — 대시를 시작한 자리에 남기는 잿불 자국.
    //
    // 대시에 이펙트가 필요한 이유: 0.25초에 11유닛을 가는데 그림은 캐릭터 한 장뿐이라,
    // 빠르게 지나가면 "순간이동"으로 읽히고 어느 쪽으로 피했는지 궤적이 안 남는다.
    // 출발점에서 진행 방향으로 뻗는 자국이 있으면 방향과 거리가 동시에 읽힌다.
    //
    // 스킬처럼 SkillData로 빼지 않은 이유: 대시는 스킬 슬롯이 아니라 이동의 일부라 이 컴포넌트가
    // 시작과 방향을 소유한다. 이펙트를 놓을 자리(출발점)와 방향(dashDirection)을 아는 곳도 여기뿐이다.
    [Tooltip("대시를 시작한 자리에 남길 이펙트. 오른쪽으로 뻗게 그린 그림을 대시 방향으로 돌려 놓는다. 비우면 안 남긴다.")]
    [SerializeField] private GameObject dashEffectPrefab;

    // 추가 생성(대시 VFX) — 이펙트를 발밑에서 화면 위로 띄울 높이.
    //
    // 0(발밑)이 아닌 이유: 자국이 앞서 가는 캐릭터의 <b>몸 뒤</b>로 이어져야 몸에서 끌려 나온
    // 꼬리로 보인다. 발밑에 두면 바닥에 그은 줄이 되어 캐릭터와 따로 논다.
    // 1은 캐릭터 키(5.94유닛)의 약 1/6로, 목업에서 줄기 끝이 캐릭터의 다리·엉덩이 뒤에 닿는 높이였다.
    // SkillData.effectHeight와 같은 성격 — 바닥 위치가 아니라 화면상의 높이다.
    [Tooltip("대시 이펙트를 발밑에서 화면 위로 띄울 높이(유닛). 줄기가 캐릭터 몸 뒤로 이어지게 맞춘다.")]
    [SerializeField, Min(0f)] private float dashEffectHeight = 1f;

    // 추가 생성(2026-09-17, 대시 발자국 잔불) — 대시하는 동안 지나간 바닥에 불씨를 깐다.
    //
    // 출발점 자국(dashEffectPrefab)만으로는 "어디서 출발했는지"만 남고 "어디까지 갔는지"는 비어 있었다.
    // 지나간 길에 불씨가 깔리면 11유닛을 피한 거리가 그대로 바닥에 보인다.
    //
    // 이펙트를 대시마다 새로 만들지 않고 플레이어 자식 하나를 켜고 끄는 이유: 불씨는 몸이 움직인 거리만큼
    // 뿌려야 하는데(Rate over Distance — 유니티 내장), 그 거리를 아는 것은 몸에 붙어 따라다니는 방출기다.
    // 불씨 자체는 월드 공간이라 몸이 지나가도 바닥에 남는다.
    [Tooltip("대시하는 동안만 방출을 켤 파티클(플레이어 자식, 평소에는 방출이 꺼져 있다). 비우면 안 남긴다.")]
    [SerializeField] private ParticleSystem dashTrail;

    /// <summary>
    /// 추가 생성 — 대시 이펙트가 스스로 안 사라질 때 강제로 지우기까지의 시간(초).
    /// 지금 프리팹은 6프레임 / 16fps = 0.375초 뒤 스스로 지운다. 루프로 잘못 설정된 프리팹을
    /// 끼웠을 때 대시할 때마다 방에 쌓이지 않게 하는 안전장치다(SkillData.SpawnEffect와 같은 이유).
    /// </summary>
    private const float DashEffectMaxLifetime = 2f;

    // 추가 생성 — 액션 지속 시간
    [Header("액션 지속 시간")]
    // 수정(스킬 시스템 도입): attackDuration과 attackCooldown을 여기서 뺐다.
    // 공격 모션 길이와 재사용 대기시간은 이제 스킬마다 다르므로 SkillData 에셋이 들고 있다.
    // 전투 리듬(공격 주기 약 1.08초 vs 적 회복 0.9초)에 대한 판단은 그대로 유효하고,
    // 그 숫자는 Q 스킬 에셋의 Cooldown Seconds에 들어간다.
    // 수정(8방향 전환 후 길이 어긋남): 0.2 -> 0.43.
    // 옛 시트에서는 피격이 dash_hit 6칸 중 2칸이라 2/10 = 0.2초였다. 8방향에는 player_hit
    // 시트가 따로 있고 6칸을 전부 쓰므로 6/14 = 0.429초다. 0.2초로 두면 클립의 절반 이상이
    // 잘려서 <b>맞았다는 게 화면에서 읽히지 않는다.</b> 대시와 달리 피격은 느껴져야 하는
    // 연출이라 클립을 줄이지 않고 경직을 늘리는 쪽으로 맞췄다.
    [Tooltip("피격 경직 시간(초). hit 클립 6프레임 / 14fps = 0.429초.")]
    [SerializeField] private float hitDuration = 0.43f;

    // 수정(입력 중앙화): moveAction / dashAction 필드를 걷어내고 InputBindings에서 꺼내 쓴다.
    //
    // 왜 옮겼나: 액션이 이 컴포넌트 안에 있으면 설정 화면이 키를 바꿀 방법이 없다.
    // 씬에 있는 이 컴포넌트를 찾아내야 하는데, 컴포넌트는 씬과 함께 생겼다 사라진다.
    //
    // 잃은 것: 인스펙터에서 키를 바꾸는 기능. 그 자리는 설정 화면이 가져간다. 두 곳에서
    // 바꿀 수 있으면 "지금 어느 키가 맞는지"를 프리팹과 저장값 중 무엇으로 볼지 정할 수 없다.
    //
    // 프리팹에 남아 있는 옛 바인딩 데이터는 읽는 곳이 없어져서 다음 저장 때 사라진다.

    [Header("참조 (비어 있어도 동작한다)")]
    [Tooltip("사망 후 입력을 끊기 위해 상태를 읽는다. 비우면 항상 조작 가능한 상태로 본다.")]
    [SerializeField] private RunManager runManager;

    [Tooltip("idle/run 전환용. 비우면 애니메이션 없이 이동만 한다.")]
    [SerializeField] private Animator animator;

    [Tooltip("좌우 반전용. 비우면 반전하지 않는다.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    // 추가 생성 — 스태미나
    [Tooltip("달리기/대시 비용을 낸다. 비우면 스태미나 제한 없이 무한히 달린다.")]
    [SerializeField] private PlayerStamina stamina;

    // 추가 생성 — 공용 체력과 공격 판정
    [Tooltip("플레이어 체력. 비우면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private Health health;

    // 수정(스킬 시스템 도입): 히트박스를 켜고 끄는 건 SkillController가 한다.
    // 여기 참조가 남아 있는 이유는 두 가지뿐이다 — 좌우 반전 시 히트박스도 같이 뒤집어야
    // 하고, 피격·사망으로 시전이 끊길 때 판정을 확실히 꺼야 한다.
    [Tooltip("검 히트박스. 좌우 반전과 강제 해제에만 쓴다. 실제 켜고 끄기는 SkillController가 한다.")]
    [SerializeField] private DamageHitbox attackHitbox;

    private Rigidbody2D rb;

    // 이번 프레임의 이동 입력. Update에서 읽고 FixedUpdate에서 쓴다.
    private Vector2 moveInput;

    // 추가 생성 — 현재 동작 상태와 그에 딸린 값들
    private ActionState actionState = ActionState.Normal;

    // 이번 프레임에 실제로 달리는 중인가. 입력만으로 정하지 않고 스태미나까지 본 결과다.

    // 대시가 시작될 때 고정된 방향. 대시 중에는 입력을 무시하므로 시작 시점의 방향을 들고 있어야 한다.
    private Vector2 dashDirection = Vector2.right;

    // 수정(입력 없는 대시 방향 버그, 2026-09-14) — 여기 있던 bool facingRight를 걷어냈다.
    // 옛 주석: "마지막으로 바라본 방향이 오른쪽인가. 입력이 없을 때 대시 방향을 정하는 데 쓴다."
    //
    // 걷어낸 이유: 그 "입력이 없을 때 대시 방향"이 버그였다. 대시 그림은 8방향 facingDirection으로
    // 고르는데 이동은 좌우 bool로 정해서, 위를 보고 서서 Shift를 누르면 몸은 옆으로 미끄러지고
    // 그림은 위로 돌진했다. 게임을 시작하자마자(아래를 보고 선 채) 대시해도 오른쪽으로 나갔다.
    // 이 값을 읽는 곳이 대시 하나뿐이었으므로 남겨두면 같은 실수를 다시 부르는 죽은 값이 된다.

    // 추가 생성 — 8방향 애니메이션이 쓰는 실제 방향. 아래(정면)에서 시작한다.
    private Vector2 facingDirection = Vector2.down;

    // 추가 생성(2026-09-26, 클리어 연출) — 연출(Scripted) 중에 연출이 시킨 걷기 속도. 평소에는 0이다.
    // 입력(moveInput)과 따로 두는 이유: 연출 중에는 입력을 막아야 하는데, 같은 변수를 쓰면 Update가 매 프레임
    // 입력으로 덮어써서(연출 중이면 0) 연출이 시킨 걸음이 한 프레임도 못 간다.
    private Vector2 scriptedVelocity;


    // 애니메이터 파라미터 이름을 매 프레임 문자열로 넘기면 내부에서 해시를 다시 계산한다.
    // 미리 해시로 만들어두면 그 비용과 문자열 할당이 사라진다.
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // 추가 생성(8방향) — 블렌드 트리가 방향을 고르는 데 쓰는 두 값.
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    // 추가 생성 — 나머지 파라미터도 같은 이유로 해시를 미리 떠둔다.
    // 이름 문자열은 AshPlayerAnimationBuilder의 상수와 같아야 한다. 한쪽만 바꾸면 컴파일은
    // 되지만 애니메이션이 조용히 안 바뀌므로, 이름을 고칠 일이 생기면 양쪽을 같이 고쳐야 한다.
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DashHash = Animator.StringToHash("Dash");
    private static readonly int HitHash = Animator.StringToHash("Hit");
    private static readonly int DieHash = Animator.StringToHash("Die");

    /// <summary>
    /// 추가 생성 — 지금 무적인가. 대시 중에는 공격을 통과한다.
    ///
    /// 수정(주석 최신화): "아직 읽는 쪽이 없다"고 적혀 있었으나 지금은 <see cref="Update"/>가
    /// 매 프레임 이 값을 <see cref="Health.IsInvulnerableExternally"/>에 밀어 넣는다.
    /// 대시 0.25초 <b>전체</b>가 무적이라, 보스의 재 폭발처럼 방향으로 못 피하는 패턴도
    /// 판정 순간에 대시가 걸려 있으면 통과한다.
    ///
    /// 무적 구간을 대시보다 짧게(앞부분만) 두지 않은 이유: 스태미나 25(회복 18/초)가 이미
    /// 남발을 막고 있다. 여기서 또 조이면 대시가 이동에도 회피에도 쓰기 애매해진다.
    /// </summary>
    // 수정(2026-09-21, 왕관 의식) — 연출 중(Scripted)도 무적이다. 유물이 뽑히는 동안 조작을 못 하는데
    // 그 사이 이미 날아오던 창에 맞으면 피할 방법이 없는 피해가 된다.
    public bool IsInvincible => actionState == ActionState.Dashing || actionState == ActionState.Scripted;

    /// <summary>추가 생성 — 죽었는가. 적 AI가 추격을 멈출 때 읽을 값이다.</summary>
    public bool IsDead => actionState == ActionState.Dead;

    /// <summary>추가 생성(2026-09-21) — 연출이 플레이어를 붙잡고 있는가(<see cref="BeginScripted"/>).</summary>
    public bool IsScripted => actionState == ActionState.Scripted;

    /// <summary>
    /// 추가 생성(2026-09-21, 왕관 의식) — "유물 뽑힘" 모션의 5번째 장(몸을 젖히는 순간)에 울린다.
    /// 클립에 박힌 애니메이션 이벤트가 <see cref="PlayerAnimationEvents"/>를 거쳐 여기로 온다.
    /// 유물을 날려 보내는 쪽(왕관 의식)이 이 신호를 듣는다 — 그래야 그림과 유물이 같은 프레임에 나온다.
    /// </summary>
    public event System.Action RelicsTornOut;

    /// <summary>추가 생성(2026-09-21) — 연출 모션이 끝나 조작이 돌아온 순간에 울린다.</summary>
    public event System.Action ScriptedPoseEnded;

    // 수정(입력 없는 대시 방향 버그, 2026-09-14) — FacingRight 속성을 걷어냈다.
    // 옛 주석: "오른쪽을 보고 있는가. 대시 기본 방향에만 쓴다(스킬은 FacingDirection)."
    // 읽는 곳이 대시뿐이었고, 대시도 이제 FacingDirection을 쓴다. 이유는 위 facingRight 자리의 주석에 있다.

    /// <summary>추가 생성(8방향) — 바라보는 방향 벡터. 스킬을 8방향으로 옮길 때 쓴다.</summary>
    public Vector2 FacingDirection => facingDirection;

    /// <summary>
    /// 추가 생성 — 유물로 얻은 이동 속도 보정. moveSpeed에 더해진다.
    ///
    /// moveSpeed를 직접 올리지 않고 따로 둔 이유: moveSpeed는 인스펙터에서 손으로 맞춘
    /// 기본값이라, 유물이 그걸 덮어쓰면 <b>원래 값이 뭐였는지 알 수 없게 된다.</b>
    /// 나중에 "속도가 왜 이렇지"를 볼 때 기본값과 보정치가 나뉘어 있어야 셈이 보인다.
    /// </summary>
    public float BonusMoveSpeed { get; set; }

    /// <summary>
    /// 추가 생성 — 스킬 시전 모션을 시작한다. 시전할 수 있는 상태였으면 true.
    ///
    /// <b>이 함수가 PlayerController와 SkillController의 경계다.</b>
    /// 여기서는 "지금 움직일 수 있는 상태인가"만 판단하고 이동을 잠근 뒤 모션을 재생한다.
    /// 무슨 스킬인지, 데미지가 얼마인지, 쿨다운이 얼마인지는 알지 못한다.
    /// 그래서 스킬이 넷으로 늘어나도 이 파일은 바뀌지 않는다.
    ///
    /// 수정: 트리거 이름을 인자로 받는다. 처음에는 Attack 하나를 네 스킬이 공유했는데,
    /// 기본 공격·내려찍기·활이 전부 다른 시트로 그려지면서 모션이 달라졌다. 어느 트리거를
    /// 켤지는 스킬 에셋이 알고 있으므로 여기서는 받아서 켜기만 한다.
    /// </summary>
    public bool TryBeginSkillMotion(float motionSeconds, string animatorTrigger)
    {
        if (actionState != ActionState.Normal) return false;

        StartCoroutine(SkillMotionRoutine(motionSeconds, animatorTrigger));
        return true;
    }

    /// <summary>
    /// 컴포넌트를 처음 붙였을 때 참조를 자동으로 채운다(에디터 전용 콜백).
    ///
    /// 수정(입력 중앙화): 여기서 키 바인딩도 만들었는데 그 부분을 걷어냈다.
    /// 바인딩은 이제 <see cref="InputBindings"/>가 만든다.
    /// </summary>
    private void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        runManager = FindFirstObjectByType<RunManager>();

        // 추가 생성
        stamina = GetComponent<PlayerStamina>();

        // 수정(입력 중앙화): 여기 있던 이동·대시 바인딩 생성은 InputBindings.Build로 옮겼다.
        // 그쪽 주석에 왜 이동이 방향키인지(W가 스킬 2와 겹친다), 왜 대시가 Shift인지가 남아 있다.
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        // 인스펙터에서 비워둔 채로 넣었을 경우를 대비한다. 없으면 없는 대로 동작한다.
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // 추가 생성 — 스태미나도 같은 규칙. 없으면 무제한으로 달리는 상태가 된다.
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (health == null) health = GetComponent<Health>();

        // 추가 생성 — RunManager는 씬에 있는 오브젝트라 프리팹에 미리 연결해둘 수가 없다
        // (프리팹은 씬 오브젝트를 참조하지 못한다). Reset에서 채운 값도 프리팹으로 저장되는
        // 순간 비워지므로, 프리팹으로 배치했을 때를 대비해 여기서 한 번 더 찾는다.
        // 매 프레임 찾는 게 아니라 시작할 때 한 번이라 비용은 무시할 수 있다.
        if (runManager == null) runManager = FindFirstObjectByType<RunManager>();
    }

    // 수정(입력 중앙화): 액션을 켜고 끄던 코드를 걷어냈다.
    // 맵은 InputBindings가 부팅 때 켜고 끄지 않는다 — 켜져 있어도 읽는 쪽이 없으면
    // 아무 일도 안 일어나고, 이 컴포넌트가 꺼지면 Update가 안 돌아서 읽는 쪽도 없다.
    private void OnEnable()
    {
        // 추가 생성 — Health가 게임 규칙을 소유하고 컨트롤러는 연출에만 반응한다.
        if (health != null)
        {
            health.Damaged += OnHealthDamaged;
            health.Died += Die;
        }
    }

    private void OnDisable()
    {
        attackHitbox?.Deactivate();

        // 추가 생성(2026-09-17) — 대시 도중 오브젝트가 꺼지면 대시 코루틴이 끄기 전에 멈춘다.
        // 방출이 켜진 채로 남으면 다시 켜졌을 때 걷기만 해도 불씨가 깔린다.
        SetDashTrail(false);

        if (health != null)
        {
            health.Damaged -= OnHealthDamaged;
            health.Died -= Die;
        }
    }

    // 수정(입력 중앙화): OnDestroy가 하던 액션 해제를 걷어냈다.
    // 액션의 주인이 InputBindings로 바뀌었으므로, 여기서 해제하면 이 오브젝트가 죽을 때
    // 다른 곳이 쓰는 액션까지 같이 죽는다.

    private void Update()
    {
        // 추가 생성 — RunManager가 먼저 판을 끝낸 경우(디버그 사망 키 등)를 따라잡는다.
        // 사망 진입점을 Die() 하나로 두되, 외부에서 EndRun이 먼저 불린 경로도 여기서 흡수한다.
        //
        // 수정(2026-09-26, 클리어 때 부서지던 것) — 클리어로 끝난 판은 따라잡지 않는다(!IsCleared).
        // 이 검사는 "판이 끝났다 = 죽었다"라고 가정했는데, 클리어도 판을 끝낸다(EndRun(true)). 그래서 클리어 연출이 끝나고
        // 결과 화면을 기다리는 1.4초 동안 사망 모션(몸이 재로 부서짐)이 나왔다. 이긴 판에서 죽을 이유는 없다.
        if (actionState != ActionState.Dead &&
            runManager != null && runManager.State != RunManager.RunState.Playing &&
            !runManager.IsCleared)
        {
            Die();
        }

        // 죽은 뒤에는 입력을 받지 않는다. RunManager를 안 연결했으면 항상 조작 가능으로 본다
        // (ResultScreen과 같은 규칙 — 참조가 비어도 흐름은 확인할 수 있어야 한다).
        //
        // 수정(애니메이션 작업 시점): 공격/대시/피격 중에도 이동 입력을 끊어야 해서
        // 조건에 actionState를 더했다.
        bool canMove = actionState == ActionState.Normal &&
                       (runManager == null || runManager.State == RunManager.RunState.Playing);

        // normalized가 아니라 ClampMagnitude를 쓰는 이유:
        // normalized는 길이를 무조건 1로 만들어서 게임패드를 살짝 기울여도 전력질주가 된다.
        // ClampMagnitude는 1을 넘을 때만 깎으므로, 키보드 대각선(길이 1.41)은 1로 줄이면서
        // 스틱의 아날로그 세기는 그대로 살린다.
        moveInput = canMove
            ? Vector2.ClampMagnitude(InputBindings.MoveAction.ReadValue<Vector2>(), 1f)
            : Vector2.zero;

        // 추가 생성 — 바라보는 방향을 먼저 갱신한다. 대시 방향이 이 값을 쓰므로
        // 아래 액션 입력 처리보다 앞에 있어야 한다.
        //
        // 수정(입력 없는 대시 방향 버그, 2026-09-14) — 여기서 좌우 bool(facingRight)도 같이 갱신하던
        // 두 줄을 걷어냈다. 위 문장의 "이 값"은 이제 아래 facingDirection 하나다.

        // 수정(8방향 전환) — 좌우 bool과 별개로 실제 방향 벡터를 들고 있는다.
        //
        // bool을 남겨둔 이유: 대시 방향을 정할 때 "입력이 없으면 마지막 좌우"라는 기존 규칙이
        // 아직 그 값을 쓴다. 스킬 쪽은 전부 방향 벡터로 옮겼다.
        // → 수정(2026-09-14): 그 규칙이 버그라 대시도 이 벡터로 옮기고 bool은 걷어냈다.
        //
        // 입력이 0일 때 갱신하지 않는 이유: 멈춘 순간 마지막으로 보던 방향이 유지돼야
        // 그 방향 idle이 나온다. 안 그러면 손을 떼는 순간 정면으로 홱 돌아간다.
        if (moveInput.sqrMagnitude > 0.0001f)
            facingDirection = moveInput.normalized;


        // 추가 생성
        HandleActionInput();

        UpdateVisuals();

        // 추가 생성 — 대시 무적의 최종 판정은 Health 한 곳에서 한다.
        if (health != null) health.IsInvulnerableExternally = IsInvincible;
    }

    /// <summary>
    /// 실제 이동. 물리는 FixedUpdate에서 다뤄야 한다 — Update에서 속도를 넣으면
    /// 프레임률에 따라 물리 스텝당 적용 횟수가 달라져서 이동 거리가 기기마다 달라진다.
    /// </summary>
    private void FixedUpdate()
    {
        // AddForce가 아니라 속도를 직접 대입하는 이유: 로그라이크 슬래셔는 조작이 즉각적으로
        // 붙어야 한다. 힘으로 밀면 가속과 관성이 생겨서 입력을 놓아도 미끄러진다.
        //
        // 수정(애니메이션 작업 시점): 상태에 따라 대입할 속도가 달라져서 분기를 넣었다.
        switch (actionState)
        {
            case ActionState.Normal:
                rb.linearVelocity = moveInput * (moveSpeed + BonusMoveSpeed);
                break;

            case ActionState.Dashing:
                // 대시 중에는 입력을 무시하고 시작할 때 정한 방향으로만 간다.
                // 대시 도중 방향을 꺾을 수 있으면 회피기가 아니라 그냥 빠른 이동이 된다.
                rb.linearVelocity = dashDirection * dashSpeed;
                break;

            // 추가 생성(2026-09-26, 클리어 연출) — 연출 중에는 연출이 시킨 속도로 걷는다(SetScriptedMove).
            // 시키지 않았으면 0이라 예전처럼 제자리에 선다(왕관 의식은 그대로다).
            case ActionState.Scripted:
                rb.linearVelocity = scriptedVelocity;
                break;

            default:
                // 공격/피격/사망 중에는 제자리에 선다. 속도를 안 지우면 직전 이동 속도가
                // 그대로 남아서 공격하는 내내 미끄러진다.
                rb.linearVelocity = Vector2.zero;
                break;
        }
    }

    // 수정(달리기 삭제): UpdateRunState를 지웠다. 이동 속도가 하나뿐이라 "지금 달리는가"를
    // 판정할 이유가 없어졌고, 스태미나는 대시에서만 소모된다.

    /// <summary>추가 생성 — 공격/대시 입력을 받아 해당 코루틴을 시작한다.</summary>
    private void HandleActionInput()
    {
        // Normal이 아닐 때는 새 액션을 받지 않는다. 공격 중 공격, 대시 중 대시를 막는다.
        if (actionState != ActionState.Normal) return;

        // 대시를 공격보다 먼저 보는 이유: 같은 프레임에 둘 다 눌렸다면 회피가 우선이어야
        // 플레이어가 손해를 안 본다.
        if (InputBindings.DashAction.WasPressedThisFrame() && (stamina == null || stamina.TryConsumeDash()))
        {
            StartCoroutine(DashRoutine());
            return;
        }

        // 수정(스킬 시스템 도입): 공격 입력은 SkillController가 받는다.
        // 여기 남은 건 대시뿐이라 위 분기에서 이미 처리가 끝났다.
    }

    /// <summary>
    /// 추가 생성 — 공격. 정해진 시간 동안 제자리에 서고 끝나면 Normal로 돌아온다.
    ///
    /// 코루틴을 쓴 이유: "트리거 쏘고 → 잠시 기다렸다가 → 상태 되돌리기"는 유니티가 코루틴으로
    /// 쓰라고 만들어둔 형태다. 직접 타이머 변수를 두고 Update에서 빼면 상태마다 변수가 하나씩
    /// 늘어나고, 그 변수를 초기화하는 걸 빠뜨리는 실수가 생긴다.
    /// </summary>
    /// <summary>
    /// 수정(스킬 시스템 도입): AttackRoutine을 대체한다.
    ///
    /// 예전에는 이 코루틴이 히트박스를 켜고 끄는 것까지 했는데, 그건 "무엇을 하는 스킬인가"에
    /// 딸린 일이라 SkillData로 옮겼다. 여기 남은 건 이동 잠금과 모션 재생뿐이다.
    /// 히트박스 타이밍과 스킬 실행은 SkillController가 병렬로 돌린다.
    /// </summary>
    private IEnumerator SkillMotionRoutine(float motionSeconds, string animatorTrigger)
    {
        actionState = ActionState.Attacking;

        // 이름이 비어 있으면 기본 공격 트리거로 물러선다. 에셋을 새로 만들고 트리거 이름을
        // 안 적었을 때 아무 모션도 안 나오는 것보다, 뭐라도 나오는 편이 원인을 찾기 쉽다.
        animator?.SetTrigger(string.IsNullOrEmpty(animatorTrigger) ? "Attack" : animatorTrigger);

        yield return new WaitForSeconds(motionSeconds);

        // 추가 생성(2026-09-24) — 자세를 붙잡고 있는 동안(R 무릎 꿇기)은 움직이지 못하게 계속 기다린다. HoldPoseWhile 참고.
        while (holdingPose) yield return null;

        // 대기하는 동안 죽었을 수 있다. 그 경우 Normal로 되돌리면 시체가 다시 움직인다.
        if (actionState == ActionState.Attacking)
            actionState = ActionState.Normal;
    }

    /// <summary>추가 생성 — 대시. 시작 시점의 방향으로 정해진 시간 동안 밀려간다.</summary>
    private IEnumerator DashRoutine()
    {
        // 입력이 있으면 그 방향, 없으면 바라보던 방향으로 나간다.
        // 제자리 대시가 아무 데도 안 가면 회피기로 못 쓴다.
        //
        // 수정(입력 없는 대시 방향 버그, 2026-09-14) — 입력이 없을 때 좌우 bool 대신 facingDirection을 쓴다.
        // 예전 식: moveInput이 있으면 moveInput.normalized, 없으면 (facingRight ? 오른쪽 : 왼쪽).
        //
        // facingDirection 하나로 두 경우가 다 된다. 이 코루틴은 Update → HandleActionInput에서 시작되는데,
        // Update가 그보다 앞에서 facingDirection을 입력이 있으면 이번 프레임 입력 방향으로, 없으면
        // 마지막으로 움직인 방향 그대로 갱신해 두기 때문이다(StartCoroutine은 첫 yield까지 바로 실행된다).
        // 그리고 대시 그림(블렌드 트리)도 같은 값으로 고르므로 <b>그림·이동·이펙트가 한 방향</b>을 가리킨다.
        dashDirection = facingDirection;

        actionState = ActionState.Dashing;
        animator?.SetTrigger(DashHash);

        // 추가 생성(대시 VFX) — 방향이 정해진 직후, 몸이 움직이기 전의 자리에 남긴다.
        SpawnDashEffect();

        // 추가 생성(2026-09-17) — 몸이 미끄러지는 동안만 바닥 불씨를 뿌린다.
        SetDashTrail(true);

        yield return new WaitForSeconds(dashDuration);

        // 추가 생성(2026-09-17) — 대시가 끝나면 끈다. 걷는 동안에도 뿌리면 발자국이 아니라 불길이 된다.
        SetDashTrail(false);

        if (actionState == ActionState.Dashing)
            actionState = ActionState.Normal;
    }

    /// <summary>
    /// 추가 생성(2026-09-17, 대시 발자국 잔불) — 바닥 불씨의 방출만 켜고 끈다.
    ///
    /// 파티클 시스템 자체는 계속 재생 중이고 방출(Emission)만 여닫는다. 시스템을 Stop/Play로 다루면
    /// 대시가 끝날 때 아직 바닥에 남은 불씨까지 같이 지워지거나(Clear), 다시 켤 때 처음부터 새로 도는
    /// 준비 시간이 끼어든다. 방출만 끄면 이미 깔린 불씨는 제 수명대로 꺼진다.
    /// </summary>
    private void SetDashTrail(bool on)
    {
        if (dashTrail == null) return;

        ParticleSystem.EmissionModule emission = dashTrail.emission;
        emission.enabled = on;
    }

    /// <summary>
    /// 추가 생성(대시 VFX) — 출발점에 대시 자국을 남긴다. 이펙트가 없으면 아무 일도 안 한다.
    ///
    /// 캐릭터의 자식으로 붙이지 않고 월드에 놓는 이유: 이 그림은 "여기서 출발했다"는 표시다.
    /// 자식으로 달면 캐릭터를 따라 11유닛을 같이 미끄러져서 자국이 아니라 몸에 붙은 장식이 된다.
    ///
    /// 회전만으로 여덟 방향을 맞추는 이유: 그림이 오른쪽으로 뻗고 위아래 대칭으로 그려져 있다
    /// (생성 프롬프트의 조건). 그래서 왼쪽 대시에 180도가 걸려 뒤집혀도 같은 그림으로 읽힌다.
    /// Q(내려찍기)의 이펙트는 파편이 늘 위로 솟아야 해서 반전과 회전을 섞었지만, 이건 그럴 필요가 없다.
    ///
    /// 세로 원근 압축(SkillData.Forward)을 안 거는 이유: 대시는 dashSpeed로 방향과 무관하게 같은 거리를
    /// 실제로 이동한다. 자국의 길이도 실제 이동을 따라가야 위로 대시할 때 줄기가 캐릭터에서 끊기지 않는다.
    /// </summary>
    private void SpawnDashEffect()
    {
        if (dashEffectPrefab == null) return;

        float angle = Mathf.Atan2(dashDirection.y, dashDirection.x) * Mathf.Rad2Deg;
        Vector3 position = transform.position + Vector3.up * dashEffectHeight;

        var effect = Instantiate(dashEffectPrefab, position, Quaternion.Euler(0f, 0f, angle));
        Destroy(effect, DashEffectMaxLifetime);
    }

    /// <summary>
    /// 추가 생성 — 피격 경직. 데미지 시스템이 생기면 여기를 호출한다.
    ///
    /// 데미지 계산(체력 감소)을 여기 넣지 않은 이유: 체력은 플레이어만 갖는 값이 아니라
    /// 적도 갖는다. 나중에 Health 컴포넌트를 따로 만들어 양쪽이 공유하고, 이 함수는 그
    /// 컴포넌트가 "맞았다"고 알려줄 때 반응하는 연출 쪽만 맡는다.
    /// </summary>
    public void TakeHitReaction()
    {
        // 죽었거나 대시 무적 중이면 경직에 걸리지 않는다.
        // 수정(2026-09-21) — IsInvincible에 연출 중(Scripted)도 들어가서, 연출 모션도 경직으로 끊기지 않는다.
        if (actionState == ActionState.Dead || IsInvincible) return;

        // 이전 액션 코루틴이 돌고 있을 수 있으므로 전부 끊는다. 안 끊으면 공격 코루틴이
        // 나중에 깨어나서 경직 중인 상태를 Normal로 되돌려버린다.
        StopAllCoroutines();
        // 추가 생성 — 공격 도중 피격되면 코루틴과 함께 꺼지지 못한 판정도 즉시 정리한다.
        attackHitbox?.Deactivate();
        StartCoroutine(HitRoutine());
    }

    /// <summary>추가 생성 — 실제 데미지가 들어간 경우에만 피격 모션을 시작한다.</summary>
    // ── 추가 생성(2026-09-24, R 무릎 꿇기 유지) ─────────────────────────
    // 궁극기 모션(0.6초)은 무릎을 꿇는 순간 폭발이 터지는데, 폭발(0.75초)이 끝나기 전에 모션이 끝나 곧장 일어났다.
    // 폭발이 살아 있는 동안 마지막 프레임(무릎 꿇은 그림)에 멈춰 두고, 그동안은 움직이지 못하게 한다.
    private bool holdingPose;

    /// <summary>
    /// 추가 생성 — delay초 뒤 애니메이터를 멈춰(Animator.speed = 0) 지금 자세를 붙잡고, effect가 사라지면 풀어 준다.
    ///
    /// 클립을 늘리거나 전이를 바꾸지 않고 애니메이터 속도로 멈추는 이유: 클립과 컨트롤러는 빌더가 다시 굽는 에셋이라
    /// 거기에 넣으면 빌더를 돌릴 때마다 사라진다. 이 방식은 "이펙트가 끝날 때까지"라는 길이를 실행 중에 정할 수 있다.
    /// delay는 클립의 마지막 프레임이 보이는 동안 멈추도록 부르는 쪽(AreaSkillData)이 계산한다 —
    /// 클립이 끝까지 가면 Exit Time 전이로 Idle(서 있는 그림)로 넘어가 버린다.
    /// 맞거나 죽으면 바로 푼다(ReleasePose) — 멈춘 애니메이터는 피격·사망 그림도 멈춰 버린다. 안전 상한 3초.
    /// 수정(2026-09-26) — 연출에 붙잡힐 때(BeginScripted)도 푼다. <b>StopAllCoroutines로 이 코루틴을 끊는 곳은
    /// 전부 ReleasePose를 같이 불러야 한다</b> — 끊긴 코루틴은 끝의 ReleasePose와 안전 상한 3초까지 같이 잃는다.
    /// </summary>
    public void HoldPoseWhile(GameObject effect, float delay)
    {
        if (effect == null || actionState == ActionState.Dead) return;
        StartCoroutine(HoldPoseRoutine(effect, delay));
    }

    private IEnumerator HoldPoseRoutine(GameObject effect, float delay)
    {
        holdingPose = true;
        yield return new WaitForSeconds(delay);

        if (holdingPose && animator != null) animator.speed = 0f;

        float giveUpAt = Time.time + 3f;
        while (holdingPose && effect != null && Time.time < giveUpAt) yield return null;

        ReleasePose();
    }

    /// <summary>추가 생성 — 붙잡은 자세를 푼다. 피격·사망에서도 부른다.</summary>
    private void ReleasePose()
    {
        holdingPose = false;
        if (animator != null) animator.speed = 1f;
    }

    private void OnHealthDamaged(int current, int max)
    {
        // 추가 생성(2026-09-24) — 맞으면 무릎 꿇기를 풀어야 피격 그림이 움직인다.
        ReleasePose();

        if (current > 0) TakeHitReaction();
    }

    private IEnumerator HitRoutine()
    {
        actionState = ActionState.Hit;
        animator?.SetTrigger(HitHash);

        yield return new WaitForSeconds(hitDuration);

        if (actionState == ActionState.Hit)
            actionState = ActionState.Normal;
    }

    /// <summary>
    /// 추가 생성 — 사망. 입력을 끊고 사망 모션을 재생한 뒤 그 상태로 멈춘다.
    ///
    /// 여러 번 불려도 안전하다. RunManager.EndRun도 중복 호출을 막고 있어서, 데미지 여러 개가
    /// 같은 프레임에 들어와도 결과가 같다.
    /// </summary>
    public void Die()
    {
        if (actionState == ActionState.Dead) return;

        // 진행 중이던 공격/대시 코루틴을 끊는다. 안 끊으면 사망 모션 중에 코루틴이 깨어나
        // 상태를 Normal로 되돌리고, 죽은 캐릭터가 다시 걷는다.
        StopAllCoroutines();
        attackHitbox?.Deactivate();

        // 추가 생성(2026-09-17) — 대시 도중에 죽으면 대시 코루틴이 끄기 전에 끊긴다. 여기서 꺼야
        // 쓰러진 몸이 밀려나는 동안(넉백 등) 바닥 불씨가 계속 깔리지 않는다.
        SetDashTrail(false);

        // 추가 생성(2026-09-24) — 무릎 꿇기를 붙잡은 채 죽으면 사망 그림이 멈춰 있지 않게 푼다.
        ReleasePose();

        actionState = ActionState.Dead;
        moveInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;

        animator?.SetTrigger(DieHash);

        // 판을 끝낸다. 이미 끝나 있으면 RunManager가 알아서 무시한다.
        runManager?.EndRun(false);
    }

    /// <summary>
    /// 추가 생성(2026-09-21, 왕관 의식) — 연출이 플레이어를 붙잡는다. 입력·이동·스킬을 막고,
    /// 정한 방향을 보게 한 뒤 연출 모션을 튼다. 죽었으면 붙잡지 못하고 false를 돌려준다.
    ///
    /// 진행 중이던 공격·대시·경직 코루틴을 끊는 이유는 <see cref="Die"/>와 같다 — 안 끊으면
    /// 나중에 깨어난 코루틴이 상태를 Normal로 되돌려 연출 도중에 걸어 다닌다.
    ///
    /// 방향을 트리거보다 먼저 애니메이터에 넣는 이유: 블렌드 트리가 들어가는 첫 프레임에 방향을
    /// 고른다. 순서가 바뀌면 한 프레임 동안 옛 방향 그림이 나온다.
    /// </summary>
    /// <param name="face">바라볼 방향. "유물 뽑힘"은 화면 쪽(아래)이다.</param>
    /// <param name="animatorTrigger">틀 연출 모션의 트리거 이름.</param>
    public bool BeginScripted(Vector2 face, string animatorTrigger)
    {
        if (actionState == ActionState.Dead) return false;

        StopAllCoroutines();
        attackHitbox?.Deactivate();
        SetDashTrail(false);

        // 추가 생성(2026-09-26, 유물 뽑힘 모션이 첫 장에 굳던 것) — R 무릎 꿇기를 붙잡은 채 붙잡히면 푼다. Die와 같은 이유다.
        // 위 StopAllCoroutines가 HoldPoseRoutine을 끝까지 못 가게 끊으면 ReleasePose가 안 불려 애니메이터가 속도 0으로 남는다.
        // 그러면 "유물 뽑힘" 모션이 첫 장에서 멈추고, 그 모션의 이벤트(RelicsTornOut·ScriptedPoseEnd)도 안 와서 의식이
        // 시간 초과로 넘어간다. 풀려난 뒤에도 그 그림 그대로 미끄러져 다닌다(맞거나 R을 다시 써야 풀렸다).
        // R 폭발(8 피해)은 보스 체력 35%를 넘기는 한 방이 되기 쉬워서, 의식은 무릎 꿇기 도중에 시작되는 일이 흔하다.
        ReleasePose();

        actionState = ActionState.Scripted;
        moveInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
        scriptedVelocity = Vector2.zero; // 추가 생성(2026-09-26) — 붙잡히면 일단 멈춘다. 걷기는 연출이 SetScriptedMove로 따로 시킨다.

        if (face.sqrMagnitude > 0.0001f) facingDirection = face.normalized;
        UpdateVisuals();

        if (animator != null && !string.IsNullOrEmpty(animatorTrigger)) animator.SetTrigger(animatorTrigger);
        return true;
    }

    /// <summary>
    /// 추가 생성(2026-09-21) — 연출이 플레이어를 놓아준다. 연출 중이 아니면 아무것도 안 한다
    /// (그사이 죽었다면 Dead를 Normal로 되돌리면 안 된다).
    /// </summary>
    public void EndScripted()
    {
        if (actionState != ActionState.Scripted) return;

        scriptedVelocity = Vector2.zero; // 추가 생성(2026-09-26) — 연출이 시킨 걸음을 풀려난 뒤로 끌고 가지 않는다.
        actionState = ActionState.Normal;
        ScriptedPoseEnded?.Invoke();
    }

    /// <summary>
    /// 추가 생성(2026-09-26, 클리어 연출) — 연출 중인 플레이어를 걷게 한다(0을 주면 멈춘다). 연출 중이 아니면 무시한다.
    ///
    /// 위치를 직접 옮기지 않고 속도를 주는 이유: 평소 이동과 같은 길(FixedUpdate → Rigidbody2D)을 타야 벽·소품 충돌이
    /// 그대로 먹고, 애니메이터의 Speed도 실제 속도에서 나와서 걷기 모션이 저절로 나온다(UpdateVisuals).
    /// 속도가 있으면 그 방향을 바라보게 한다 — 8방향 걷기 그림이 걷는 방향과 맞아야 한다.
    /// </summary>
    /// <param name="velocity">걷는 속도(유닛/초). 방향과 빠르기를 같이 담는다.</param>
    public void SetScriptedMove(Vector2 velocity)
    {
        if (actionState != ActionState.Scripted) return;

        scriptedVelocity = velocity;
        if (velocity.sqrMagnitude > 0.0001f) facingDirection = velocity.normalized;
    }

    /// <summary>추가 생성(2026-09-21) — 애니메이션 이벤트 "RelicsTornOut"을 받는다. <see cref="PlayerAnimationEvents"/>가 부른다.</summary>
    internal void HandleRelicsTornOut()
    {
        // 연출 중일 때만 넘긴다. 다른 경로로 같은 클립이 재생돼도(예: 에디터 미리보기) 유물이 튀지 않게.
        if (actionState == ActionState.Scripted) RelicsTornOut?.Invoke();
    }

    /// <summary>추가 생성(2026-09-21) — 애니메이션 이벤트 "ScriptedPoseEnd"를 받는다. 모션이 끝나면 조작을 돌려준다.</summary>
    internal void HandleScriptedPoseEnd()
    {
        EndScripted();
    }

    /// <summary>애니메이션 파라미터와 좌우 반전을 갱신한다.</summary>
    private void UpdateVisuals()
    {
        // 입력이 아니라 실제 속도를 넘기는 이유: 나중에 넉백이나 대시로 몸이 밀릴 때도
        // 달리는 애니메이션이 나와야 한다. 입력을 기준으로 하면 밀려나는 동안 가만히 서 있다.
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, rb.linearVelocity.magnitude);

            // 추가 생성(8방향) — 블렌드 트리가 이 두 값으로 방향을 고른다.
            // 정규화한 값을 넣는 이유: 블렌드 트리의 방향 좌표가 단위원 위에 놓여 있어서
            // 크기가 작으면 어느 방향에도 온전히 도달하지 못하고 섞인 그림이 나온다.
            animator.SetFloat(MoveXHash, facingDirection.x);
            animator.SetFloat(MoveYHash, facingDirection.y);
        }

        // 수정(8방향 전환) — 스프라이트 반전을 없앴다.
        //
        // 왜 없애야 하는가: 이제 왼쪽 방향 그림이 시트에 따로 들어 있다. 거기에 반전까지 걸면
        // 왼쪽을 볼 때 두 번 뒤집혀 오른쪽 그림이 나온다. 게다가 반전은 검을 반대 손으로
        // 옮겨버려서, 방향을 바꿀 때마다 무기가 손을 갈아타는 것처럼 보인다.
        if (spriteRenderer != null)
            spriteRenderer.flipX = false;

        // 수정(8방향 전환) — 히트박스를 좌우 반전이 아니라 방향으로 배치한다.
        //
        // 예전에는 X 스케일을 뒤집어 오프셋을 반대편으로 보냈다. 그 방법은 위아래를 표현할 수
        // 없어서, 위를 보고 공격해도 판정은 옆에 생겼다. 이제 바라보는 방향으로 회전시켜
        // 여덟 방향 모두 그림과 판정이 같은 곳에 놓인다.
        if (attackHitbox != null)
        {
            Vector3 scale = attackHitbox.transform.localScale;
            scale.x = Mathf.Abs(scale.x);
            attackHitbox.transform.localScale = scale;

            float angle = Mathf.Atan2(facingDirection.y, facingDirection.x) * Mathf.Rad2Deg;
            attackHitbox.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
