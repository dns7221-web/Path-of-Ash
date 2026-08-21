using System.Collections;
using UnityEngine;

/// <summary>
/// 스킬 하나가 시전될 때 필요한 주변 정보. 스킬은 이걸 받아서 실행된다.
///
/// 스킬이 PlayerController를 직접 참조하지 않게 하려고 만든 묶음이다.
/// 스킬 에셋이 플레이어 컴포넌트를 알면 나중에 적이 같은 스킬을 쓰게 만들 수가 없다.
/// </summary>
public struct SkillContext
{
    /// <summary>코루틴을 돌릴 주체. 스킬은 ScriptableObject라 스스로 코루틴을 못 돌린다.</summary>
    public MonoBehaviour Runner;

    /// <summary>시전자의 위치. 투사체와 장판이 여기서 나간다.</summary>
    public Transform Owner;

    /// <summary>
    /// 바라보는 방향. 근접 판정과 투사체 방향에 쓴다.
    ///
    /// 수정(8방향 전환): bool FacingRight였다. 좌우만 표현할 수 있어서 위를 보고 스킬을 써도
    /// 판정과 이펙트가 옆으로 나갔다. 8방향 애니메이션이 붙은 뒤로는 <b>그림과 판정이
    /// 서로 다른 곳을 가리키는</b> 상태가 되어 반드시 벡터로 바뀌어야 했다.
    /// </summary>
    public Vector2 FacingDirection;

    /// <summary>근접 스킬이 켜고 끌 히트박스. 없으면 근접 스킬은 아무 일도 안 한다.</summary>
    public DamageHitbox MeleeHitbox;

    /// <summary>유물로 얻은 추가 데미지. 스킬 자체 데미지에 더한다.</summary>
    public int BonusDamage;
}

/// <summary>
/// 스킬 하나의 정의. 에셋 파일 하나가 스킬 하나다.
///
/// ScriptableObject로 만든 이유:
/// 1) 밸런스 숫자(쿨다운, 데미지, 모션 길이)가 코드 밖으로 나가서 인스펙터에서 바로 고칠 수
///    있다. 컴파일을 기다리지 않고 Play 중에도 조정된다.
/// 2) QWER 슬롯이 "어떤 에셋을 끼웠는가"로 정해진다. 나중에 스킬 교체 시스템을 넣을 때
///    슬롯에 다른 에셋을 대입하는 것으로 끝난다.
/// 3) 유니티가 제공하는 데이터 자산 방식이라, 직접 만든 저장 포맷보다 에디터 지원이 좋다.
///
/// <b>추상 클래스로 둔 이유</b>: 근접/투사체/장판은 "무엇을 하는가"가 완전히 다르다.
/// 하나의 클래스에 종류 enum을 두고 switch로 갈라도 되지만, 그러면 스킬을 추가할 때마다
/// 이 파일과 SkillController를 같이 고쳐야 한다. 상속으로 두면 <b>새 스킬 = 새 파일 하나</b>이고
/// 기존 코드를 건드릴 이유가 없다.
/// </summary>
public abstract class SkillData : ScriptableObject
{
    [Header("표시")]
    [Tooltip("인벤토리 화면에 보일 이름.")]
    [SerializeField] private string displayName = "이름 없는 스킬";

    [Tooltip("인벤토리 화면에 보일 설명. QWER이 뭔지 알려줄 자리가 여기뿐이다.")]
    [TextArea(2, 4)]
    [SerializeField] private string description = "";

    [Tooltip("스킬 바에 보일 아이콘. 비어 있으면 키 글자만 보인다.")]
    [SerializeField] private Sprite icon;

    [Header("공통 수치")]
    [Tooltip("이 스킬만의 재사용 대기시간(초). 스킬마다 따로 돈다.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 1f;

    [Tooltip("시전 동안 이동이 잠기는 시간(초). 애니메이션 클립 길이와 맞춰야 " +
             "모션이 끝났는데 못 움직이거나 그 반대가 되지 않는다.")]
    [SerializeField, Min(0f)] private float motionSeconds = 0.43f;

    [Tooltip("기본 데미지. 유물 보정치가 여기에 더해진다.")]
    [SerializeField, Min(0)] private int damage = 2;

    // 추가 생성 — 스킬마다 다른 모션을 쓰기 위해.
    //
    // 처음에는 네 스킬이 Attack 트리거 하나를 공유하게 했다. 캐릭터가 검을 든 그림뿐이라
    // 모션이 같아도 된다고 봤는데, 실제로는 기본 공격(베기)·내려찍기·활이 전부 다른 시트로
    // 그려졌다. 트리거 이름을 스킬이 들고 있으면 Animator에 상태를 추가하고 여기 이름만
    // 적으면 되고, SkillController와 PlayerController는 손댈 필요가 없다.
    [Tooltip("시전할 때 켤 Animator 트리거 이름. AshPlayerAnimationBuilder의 상수와 같아야 한다.")]
    [SerializeField] private string animatorTrigger = "Attack";

    // 추가 생성 — 필살기용.
    //
    // "R 슬롯이면 게이지를 쓴다"로 하드코딩하지 않은 이유: 슬롯 번호는 배치일 뿐이고,
    // 나중에 스킬을 다른 칸으로 옮기거나 게이지를 쓰는 스킬이 둘이 될 수 있다.
    // 조건을 스킬 자신이 들고 있으면 SkillController는 슬롯이 몇 번인지 몰라도 된다.
    [Tooltip("켜면 재 게이지가 가득 찼을 때만 쓸 수 있고, 쓰면 게이지를 전부 소모한다.")]
    [SerializeField] private bool requiresFullAshGauge;

    [Header("연출 (없어도 동작한다)")]
    [Tooltip("시전할 때 생성할 이펙트. 베기 궤적 스프라이트나 파티클을 여기 넣는다.")]
    [SerializeField] private GameObject effectPrefab;

    [Tooltip("이펙트를 시전자 앞 얼마에 놓을지(유닛). 바라보는 방향으로 이만큼 떨어진다.")]
    [SerializeField] private float effectForwardOffset = 2f;

    [Tooltip("이펙트가 스스로 사라지지 않을 때 강제로 지우기까지의 시간(초).")]
    [SerializeField, Min(0.1f)] private float effectLifetime = 1f;

    public string DisplayName => displayName;
    public string Description => description;

    /// <summary>스킬 바 아이콘. 없으면 null.</summary>
    public Sprite Icon => icon;
    public float CooldownSeconds => cooldownSeconds;
    public float MotionSeconds => motionSeconds;
    public int Damage => damage;

    /// <summary>시전할 때 켤 Animator 트리거 이름.</summary>
    public string AnimatorTrigger => animatorTrigger;

    /// <summary>재 게이지를 가득 채워야 쓸 수 있는가.</summary>
    public bool RequiresFullAshGauge => requiresFullAshGauge;

    /// <summary>
    /// 이펙트 프리팹. 하위 클래스가 직접 위치를 정해 만들 때 쓴다.
    ///
    /// 장판처럼 "시전자 앞 고정 거리"가 아닌 곳에 이펙트가 놓이는 스킬이 있어서 열어둔다.
    /// 기본 배치로 충분하면 <see cref="SpawnEffect"/>를 쓰면 된다.
    /// </summary>
    protected GameObject EffectPrefab => effectPrefab;

    /// <summary>
    /// 스킬을 실행한다. 시전 모션과 이동 잠금은 <see cref="SkillController"/>가 이미 걸어둔 뒤다.
    /// 여기서는 "이 스킬이 하는 일"만 한다.
    /// </summary>
    public abstract IEnumerator Execute(SkillContext context);

    /// <summary>
    /// 이펙트를 시전자 앞에 만든다. 이펙트가 없으면 아무 일도 안 한다.
    ///
    /// 하위 클래스가 공통으로 쓰라고 여기 둔다. 이펙트를 붙이는 방식은 스킬 종류와 무관하게
    /// 같기 때문이다.
    /// </summary>
    protected void SpawnEffect(SkillContext context)
    {
        if (effectPrefab == null || context.Owner == null) return;

        // 수정(8방향) — 이펙트가 놓일 자리도 바라보는 방향으로 민다.
        Vector2 forward = context.FacingDirection;
        Vector3 position = context.Owner.position + (Vector3)(forward * effectForwardOffset);

        var effect = Object.Instantiate(effectPrefab, position, Quaternion.identity);

        // 수정(8방향) — 좌우 반전 대신 바라보는 방향으로 회전시킨다.
        //
        // 반전으로는 위아래를 표현할 수 없다. 예전에는 위를 보고 베어도 궤적만 옆으로 뻗었다.
        // 궤적 그림이 오른쪽을 향해 그려져 있으므로, 방향의 각도만큼 돌리면 여덟 방향 모두 맞는다.
        Vector2 facing = context.FacingDirection;
        if (facing.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
            effect.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // 파티클이 스스로 끝나든 안 끝나든 일정 시간 뒤에는 반드시 지운다.
        // 안 지우면 전투가 길어질수록 씬에 이펙트 오브젝트가 쌓인다.
        Object.Destroy(effect, effectLifetime);
    }
}
