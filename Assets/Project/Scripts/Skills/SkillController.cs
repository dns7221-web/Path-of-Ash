using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// QWER 스킬 슬롯의 입력과 재사용 대기시간을 관리한다.
///
/// PlayerController에서 공격을 떼어낸 이유: 스킬이 넷으로 늘어나면 입력·쿨다운·데미지·판정이
/// 전부 그 파일로 들어가서, 이동과 전투가 한 덩어리가 된다. 여기로 나누면
/// <b>PlayerController는 "지금 움직일 수 있는가"만, 이쪽은 "무엇을 시전하는가"만</b> 안다.
///
/// 스킬 자체의 동작은 <see cref="SkillData"/> 에셋이 들고 있고 이 컴포넌트는 모른다.
/// 그래서 W(투사체)와 E(장판)를 추가할 때 이 파일은 한 줄도 안 바뀐다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public class SkillController : MonoBehaviour
{
    /// <summary>
    /// 슬롯 개수. 기본 공격 하나 + QWER 넷 = 다섯이다.
    ///
    /// 기본 공격을 스킬 슬롯 밖에 두지 않은 이유: 쿨다운, 시전 모션, 이동 잠금, 데미지 보정이
    /// 스킬과 완전히 같은 규칙이다. 밖에 두면 그 규칙을 두 곳에서 관리하게 된다.
    /// "항상 쓸 수 있는 스킬"로 보는 쪽이 코드가 하나로 유지된다.
    /// </summary>
    public const int SlotCount = 5;

    [Header("슬롯 (0=기본공격/Ctrl, 1=Q, 2=W, 3=E, 4=R)")]
    [Tooltip("각 키에 끼울 스킬 에셋. 비어 있는 슬롯은 눌러도 아무 일도 없다.")]
    [SerializeField] private SkillData[] slots = new SkillData[SlotCount];

    [Header("입력")]
    // 수정(공격 입력 복구): 액션 하나에 바인딩 넷을 묶던 것을 슬롯마다 액션 하나로 바꿨다.
    //
    // 묶어두면 바인딩 순서가 곧 슬롯 번호라서 슬롯 하나에 키를 여러 개 달 수가 없다.
    // 기본 공격은 마우스 좌클릭·J·Q 셋 다 먹어야 하는데 그게 불가능했다.
    // 액션을 넷으로 나누면 각 액션이 바인딩을 몇 개든 가질 수 있다.
    [Tooltip("슬롯별 입력. 순서가 곧 슬롯 순서(Q/W/E/R)다.")]
    [SerializeField] private InputAction[] slotActions = new InputAction[SlotCount];

    [Header("참조")]
    [Tooltip("근접 스킬이 켜고 끌 히트박스. 비우면 근접 스킬이 동작하지 않는다.")]
    [SerializeField] private DamageHitbox meleeHitbox;

    [Tooltip("필살기가 쓰는 재 게이지. 비우면 게이지가 필요한 스킬이 나가지 않는다.")]
    [SerializeField] private AshGauge ashGauge;

    [Header("공용 대기시간")]
    [Tooltip("스킬 하나를 쓴 뒤 다른 스킬까지 잠기는 시간(초).\n" +
             "이게 없으면 Q→W→E를 0.1초 안에 쏟아붓고 도망가는 게 최적해가 되어, " +
             "스킬마다 다른 쿨다운을 준 의미가 사라진다.")]
    [SerializeField, Min(0f)] private float globalCooldownSeconds = 0.4f;

    private PlayerController playerController;

    // 슬롯별 남은 재사용 대기시간.
    private readonly float[] cooldownTimers = new float[SlotCount];

    // 모든 슬롯에 공통으로 걸리는 대기시간.
    private float globalCooldownTimer;

    /// <summary>
    /// 유물로 얻은 추가 데미지. 모든 스킬에 더해진다.
    ///
    /// 여기 두는 이유: 데미지를 실제로 계산하는 곳이 여기라, 보정치도 같은 자리에 있어야
    /// "왜 이 숫자가 나왔는지"를 한 군데서 볼 수 있다. 유물 시스템이 이 값을 올린다.
    /// </summary>
    public int BonusDamage { get; set; }

    /// <summary>
    /// 추가 생성 — 쿨타임 배율. 1이 기본이고, 유물이 이 값을 곱해서 줄인다.
    ///
    /// 뺄셈이 아니라 곱셈인 이유: 초를 직접 빼면 쿨타임이 짧은 기본 공격이 먼저 0이 되어
    /// <b>무한 연타가 된다.</b> 배율은 아무리 쌓아도 0에 가까워질 뿐 넘지 않고,
    /// 긴 스킬일수록 더 많이 줄어서 체감도 자연스럽다.
    /// </summary>
    public float CooldownScale { get; set; } = 1f;

    /// <summary>슬롯에 끼워진 스킬. 인벤토리 화면이 읽는다.</summary>
    public SkillData GetSlot(int index)
        => index >= 0 && index < SlotCount ? slots[index] : null;

    /// <summary>남은 대기시간(초). 인벤토리 화면과 HUD가 읽는다.</summary>
    public float GetCooldownRemaining(int index)
        => index >= 0 && index < SlotCount ? Mathf.Max(0f, cooldownTimers[index]) : 0f;

    /// <summary>
    /// 추가 생성 — 그 슬롯 쿨타임의 전체 길이. UI가 남은 비율을 낼 때 나눌 값이다.
    ///
    /// UI가 SkillData.CooldownSeconds를 직접 나누면 안 되는 이유: 유물이 <see cref="CooldownScale"/>로
    /// 실제 길이를 줄여놨는데 UI만 원래 길이로 나누면, 쿨타임 표시가 <b>가득 찬 상태에서 시작하지 않고</b>
    /// 중간부터 줄어든다. 실제로 얼마나 남았는지를 아는 건 이쪽뿐이라 여기서 알려준다.
    /// </summary>
    public float GetCooldownTotal(int index)
    {
        var skill = GetSlot(index);
        return skill == null ? 0f : skill.CooldownSeconds * CooldownScale;
    }

    private void Reset()
    {
        // 컴포넌트를 처음 붙일 때 기본 키를 채운다. PlayerController와 같은 방식이다.
        slotActions = new InputAction[SlotCount];

        // 슬롯 0 — 기본 공격. 항상 쓰는 평타라 손가락이 늘 닿아 있는 Ctrl에 둔다.
        // 이동이 방향키로 옮겨갔으므로 왼손이 방향키, 오른손이 Ctrl+QWER에 놓인다.
        slotActions[0] = new InputAction("BasicAttack", InputActionType.Button);
        slotActions[0].AddBinding("<Keyboard>/leftCtrl");
        slotActions[0].AddBinding("<Keyboard>/rightCtrl");
        slotActions[0].AddBinding("<Gamepad>/buttonWest");

        slotActions[1] = new InputAction("Skill_Q", InputActionType.Button);
        slotActions[1].AddBinding("<Keyboard>/q");
        slotActions[1].AddBinding("<Gamepad>/buttonNorth");

        slotActions[2] = new InputAction("Skill_W", InputActionType.Button);
        slotActions[2].AddBinding("<Keyboard>/w");
        slotActions[2].AddBinding("<Gamepad>/buttonEast");

        slotActions[3] = new InputAction("Skill_E", InputActionType.Button);
        slotActions[3].AddBinding("<Keyboard>/e");
        slotActions[3].AddBinding("<Gamepad>/leftShoulder");

        slotActions[4] = new InputAction("Skill_R", InputActionType.Button);
        slotActions[4].AddBinding("<Keyboard>/r");
        slotActions[4].AddBinding("<Gamepad>/rightShoulder");
    }

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        if (ashGauge == null) ashGauge = GetComponent<AshGauge>();

        // 인스펙터에서 배열 길이가 어긋났을 때를 대비한다. 길이가 다르면 인덱스 접근이 예외를 낸다.
        slots = FitLength(slots);
        slotActions = FitLength(slotActions);
    }

    /// <summary>배열을 슬롯 개수에 맞춘다. 모자라면 채우고 넘치면 자른다.</summary>
    private static T[] FitLength<T>(T[] source)
    {
        if (source != null && source.Length == SlotCount) return source;

        var result = new T[SlotCount];
        if (source != null)
        {
            for (int i = 0; i < Mathf.Min(source.Length, SlotCount); i++) result[i] = source[i];
        }

        return result;
    }

    private void OnEnable()
    {
        foreach (var action in slotActions) action?.Enable();
    }

    private void OnDisable()
    {
        foreach (var action in slotActions) action?.Disable();
    }

    private void OnDestroy()
    {
        foreach (var action in slotActions) action?.Dispose();
    }

    private void Update()
    {
        TickCooldowns();

        for (int i = 0; i < SlotCount; i++)
        {
            if (slotActions[i] != null && slotActions[i].WasPressedThisFrame()) TryUse(i);
        }
    }

    private void TickCooldowns()
    {
        if (globalCooldownTimer > 0f) globalCooldownTimer -= Time.deltaTime;

        for (int i = 0; i < SlotCount; i++)
        {
            if (cooldownTimers[i] > 0f) cooldownTimers[i] -= Time.deltaTime;
        }
    }

    /// <summary>슬롯의 스킬을 쓴다. 조건이 안 맞으면 아무 일도 하지 않는다.</summary>
    private void TryUse(int slot)
    {
        SkillData skill = slots[slot];
        if (skill == null) return;

        if (cooldownTimers[slot] > 0f || globalCooldownTimer > 0f) return;

        // 재 게이지가 필요한 스킬은 가득 찼는지 먼저 본다.
        // 아직 소모하지는 않는다 — 아래에서 시전이 거절될 수 있어서, 여기서 비우면
        // 모아둔 게이지가 아무 일도 없이 사라진다.
        if (skill.RequiresFullAshGauge && (ashGauge == null || !ashGauge.IsFull)) return;

        // 이동이나 시전이 가능한 상태인지는 PlayerController가 판단한다.
        // 대시 중이거나 죽었을 때 스킬이 나가면 안 된다.
        if (!playerController.TryBeginSkillMotion(skill.MotionSeconds, skill.AnimatorTrigger)) return;

        // 시전이 확정된 뒤에 소모한다.
        if (skill.RequiresFullAshGauge) ashGauge.TryConsumeAll();

        cooldownTimers[slot] = skill.CooldownSeconds * CooldownScale;
        globalCooldownTimer = globalCooldownSeconds;

        StartCoroutine(Run(skill));
    }

    private IEnumerator Run(SkillData skill)
    {
        var context = new SkillContext
        {
            Runner = this,
            Owner = transform,
            FacingRight = playerController.FacingRight,
            MeleeHitbox = meleeHitbox,
            BonusDamage = BonusDamage,
        };

        yield return skill.Execute(context);

        // 시전이 중간에 끊겼을(피격·사망) 수 있으므로 판정을 확실히 끈다.
        // 안 끄면 맞고 쓰러진 뒤에도 검 판정이 남아 적이 계속 데미지를 받는다.
        meleeHitbox?.Deactivate();
    }
}
