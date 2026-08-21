using UnityEngine;

/// <summary>
/// 유물 하나의 정의. 에셋 파일 하나가 유물 하나다.
///
/// <b>소모품이 아니라 먹으면 즉시 영구 적용되는 유물로 만든 이유:</b>
/// 이 게임은 실시간 액션이라 인벤토리를 열어 아이템을 쓰는 순간 전투 흐름이 끊긴다.
/// 소모품은 "쓸 타이밍을 고르는" 게임에 맞는 장치다. 그리고 판이 끝나면 사라지는 구조라
/// (씬 리로드) 저장할 것도 없다.
///
/// 스킬(<see cref="SkillData"/>)과 달리 상속을 안 쓰고 enum으로 나눈 이유:
/// 스킬은 "무엇을 하는가"가 근접·투사체·장판마다 완전히 다르지만, 유물은 전부
/// <b>"어떤 수치에 얼마를 더한다"</b> 하나다. 상속으로 나누면 파일만 늘고 얻는 게 없다.
/// 나중에 "피격 시 반격" 같은 동작형 유물이 생기면 그때 나누는 게 맞다.
/// </summary>
[CreateAssetMenu(fileName = "Relic", menuName = "재의 길/유물")]
public class RelicData : ScriptableObject
{
    /// <summary>
    /// 유물이 올리는 수치.
    ///
    /// <b>새 항목은 반드시 뒤에 붙여라.</b> 중간에 끼우거나 순서를 바꾸면 이미 만들어둔 유물
    /// 에셋이 통째로 어긋난다 — enum은 파일에 이름이 아니라 <b>순번</b>으로 저장돼서, 예를 들어
    /// MaxStamina 앞에 하나 끼우면 "무게추"가 조용히 스킬 데미지 유물이 된다. 에러도 경고도 안 난다.
    /// </summary>
    public enum EffectKind
    {
        MaxHealth,     // 최대 체력. 올린 만큼 즉시 회복도 된다
        MaxStamina,    // 최대 스태미나
        SkillDamage,   // 모든 스킬 데미지

        // 아래는 유물을 10종으로 늘리면서 추가했다. 위 셋만으로 10개를 만들면
        // 같은 효과를 숫자만 바꿔 세 번씩 넣게 되어, 무엇을 먹었는지 기억에 안 남는다.
        MoveSpeed,     // 이동 속도(유닛/초)
        StaminaRegen,  // 스태미나 초당 회복량
        // 스킬 쿨타임 감소율. <b>퍼센트다</b>(12 = 12% 짧아짐).
        // 0~1 비율로 두면 하한이 1일 때 100% 감소, 즉 쿨타임이 통째로 사라진다.
        // 모든 유물의 하한을 1로 맞추기로 한 이상 이것만 단위가 다르면 사고가 난다.
        CooldownRate,
        AshPerKill,    // 처치 한 번당 차오르는 재 게이지 양

        // 아무 수치도 안 올린다. 보스 열쇠나 클리어 유물처럼 <b>가지고 있다는 사실 자체가
        // 전부</b>인 것들이 쓴다. 효과를 억지로 붙이면 그 유물이 장착 칸을 차지해버려서,
        // 열쇠를 모을수록 오히려 약해진 채 보스를 만나게 된다.
        None,
    }

    /// <summary>
    /// 유물이 판에서 맡는 역할. 효과와 별개다.
    ///
    /// <see cref="EffectKind"/>에 섞지 않은 이유: "무엇을 올리는가"와 "무엇을 위한 물건인가"는
    /// 다른 축이다. 하나로 합치면 나중에 "체력을 올리면서 동시에 열쇠인 유물"을 만들 수 없다.
    /// </summary>
    public enum RelicRole
    {
        Normal,   // 평범한 유물
        BossKey,  // 보스방 열쇠. 다 모으면 문이 부서진 방이 열린다
        RunEnd,   // 보스 클리어 유물. 주우면 보스 방 문이 열리고, 그 문으로 나가면 판이 끝난다
    }

    [Header("표시")]
    [SerializeField] private string displayName = "이름 없는 유물";

    [TextArea(2, 3)]
    [SerializeField] private string description = "";

    [Tooltip("획득 알림과 인벤토리에 보일 그림. 없어도 동작한다.")]
    [SerializeField] private Sprite icon;

    [Header("효과")]
    [SerializeField] private EffectKind effect = EffectKind.MaxHealth;

    [Tooltip("판에서 맡는 역할. 보통은 Normal이다.")]
    [SerializeField] private RelicRole role = RelicRole.Normal;

    [Tooltip("올릴 양. 체력은 정수로 반올림해서 쓴다. 무작위면 이 값이 하한이다.")]
    [SerializeField] private float amount = 1f;

    [Tooltip("무작위 상한. amount보다 크면 매번 amount~이 값 사이에서 뽑는다. " +
             "0이면 고정값이다.")]
    [SerializeField] private float amountMax;

    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public EffectKind Effect => effect;

    /// <summary>판에서 맡는 역할.</summary>
    public RelicRole Role => role;

    /// <summary>수치를 올리지 않는 표식 유물인가. 장착 칸에 넣을 이유가 없다.</summary>
    public bool IsMarker => effect == EffectKind.None;

    /// <summary>고정값이거나, 무작위일 때의 하한.</summary>
    public float Amount => amount;

    /// <summary>먹을 때마다 다른 값이 나오는가.</summary>
    public bool IsRandom => amountMax > amount;

    /// <summary>
    /// 이번에 적용할 양을 정한다. 무작위 유물이면 먹을 때마다 다른 값이 나온다.
    ///
    /// 결과를 필드에 저장하지 않는 게 중요하다. ScriptableObject는 에디터에서 실행 중에 쓴 값이
    /// 파일에 남아서, 한 번 굴린 숫자가 <b>다음 판의 고정값이 되어버린다.</b>
    /// 그래서 매번 새로 굴려 반환만 한다.
    /// </summary>
    public float RollAmount()
    {
        if (!IsRandom) return amount;

        // 양 끝이 정수면 정수로 뽑는다. 실수로 뽑아 반올림하면 하한과 상한이 나올 확률만
        // 절반이 된다(1~7이면 1과 7만 다른 숫자의 절반씩 나온다). 주사위로는 안 맞는다.
        bool whole = Mathf.Approximately(amount, Mathf.Round(amount)) &&
                     Mathf.Approximately(amountMax, Mathf.Round(amountMax));

        return whole
            ? Random.Range(Mathf.RoundToInt(amount), Mathf.RoundToInt(amountMax) + 1)
            : Random.Range(amount, amountMax);
    }
}
