using System;
using UnityEngine;

/// <summary>
/// 체력을 소유하고 피격·사망을 알리는 컴포넌트. 플레이어와 적이 <b>같은 것을</b> 쓴다.
///
/// 하나로 합친 이유: 체력이 줄고, 무적 시간이 있고, 0이 되면 죽는다는 규칙은 양쪽이 완전히
/// 같다. 따로 만들면 "적은 무적 시간이 있는데 플레이어는 없어서 같은 프레임에 두 번 맞는"
/// 식의 차이가 조용히 생긴다.
///
/// 이 컴포넌트는 <b>죽으면 어떻게 되는지를 모른다.</b> 플레이어는 사망 모션 후 결과 화면으로
/// 가고, 적은 잿더미가 되어 사라진다. 그 차이는 <see cref="Died"/>를 구독하는 쪽이 정한다.
/// 여기서 분기하면 적 종류가 늘 때마다 이 파일을 열어야 한다.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("체력")]
    [Tooltip("최대 체력. 플레이어 5, 잿불 망령 3, 재 무리 1이 기획 수치다.")]
    [SerializeField] private int maxHealth = 3;

    // 유물로 얻은 보정. 장착이 바뀔 때마다 통째로 다시 정해진다(SetBonusMax).
    private int bonusMax;

    [Header("무적")]
    [Tooltip("피격 직후 무적 시간(초). 0이면 한 번의 돌진에 여러 프레임 연속으로 맞는다. " +
             "플레이어 피격 경직(0.2초)보다 살짝 길어야 경직이 풀리는 순간 또 맞지 않는다.")]
    [SerializeField] private float invulnerableSecondsAfterHit = 0.35f;

    // 남은 무적 시간. 0 이하면 맞을 수 있다.
    private float invulnerableTimer;

    /// <summary>현재 체력.</summary>
    public int Current { get; private set; }

    /// <summary>최대 체력. UI가 하트 개수를 그릴 때 읽는다.</summary>
    public int Max => maxHealth + bonusMax;

    /// <summary>죽었는가. 죽은 뒤에는 더 이상 데미지를 받지 않는다.</summary>
    public bool IsDead => Current <= 0;

    /// <summary>
    /// 외부가 거는 무적. 대시 회피가 이 값을 켠다.
    ///
    /// 피격 후 무적 타이머와 따로 둔 이유: 둘은 켜지는 조건이 다르다. 하나로 합치면
    /// 대시가 끝나는 순간 피격 무적까지 같이 풀려서, 대시 직후 한 프레임이 무방비가 된다.
    /// </summary>
    public bool IsInvulnerableExternally { get; set; }

#if UNITY_EDITOR
    /// <summary>
    /// 추가 생성 — 조사용 무적. <see cref="PlayerInvincibleDebugToggle"/>이 F6으로 켜고 끈다.
    ///
    /// <see cref="IsInvulnerableExternally"/>를 같이 쓰지 않고 통로를 따로 뚫은 이유:
    /// 저 값은 <see cref="PlayerController"/>가 <b>매 프레임</b> 대시 상태로 덮어쓴다.
    /// 조사용으로 켜봐야 다음 프레임에 지워져서, 켠 사람 입장에서는 "무적이 안 걸린다"로만 보인다.
    ///
    /// 대시 무적과 성격도 다르다. 저건 게임 규칙이고 이건 규칙 바깥이다. 한 변수에 담으면
    /// 나중에 "지금 무적인 이유가 무엇인지"를 구별할 수 없다.
    /// </summary>
    public bool IsInvulnerableForDebug { get; set; }
#endif

    /// <summary>
    /// 지금 데미지를 받을 수 없는 상태인가.
    ///
    /// 수정(조사용 무적): 식 하나였던 것을 블록으로 풀었다. 조사용 조건은 에디터에만
    /// 존재해야 하는데, 식 가운데에 <c>#if</c>를 끼우면 읽기가 어렵다.
    /// </summary>
    public bool IsInvulnerable
    {
        get
        {
            if (IsDead || IsInvulnerableExternally || invulnerableTimer > 0f) return true;

#if UNITY_EDITOR
            if (IsInvulnerableForDebug) return true;
#endif

            return false;
        }
    }

    /// <summary>
    /// <b>실제로 데미지를 받았을 때만</b> 불린다. 인자는 (남은 체력, 최대 체력).
    ///
    /// 피격 연출(움찔, 넉백, 무적 점멸)을 붙이는 자리다. 회복은 여기로 오지 않는다 —
    /// 처음에는 회복도 이 이벤트를 쐈는데, 상자를 열어 체력을 받을 때마다 플레이어가
    /// 피격 모션을 재생했다. 데미지가 0인데도 맞은 것처럼 보였다.
    /// </summary>
    public event Action<int, int> Damaged;

    /// <summary>
    /// 체력 값이 바뀔 때마다 불린다. 데미지든 회복이든. 인자는 (남은 체력, 최대 체력).
    ///
    /// <b>UI는 이쪽을 봐야 한다.</b> 게이지는 "왜 바뀌었는가"와 무관하게 현재 값만 그리면 된다.
    /// 반대로 연출은 이유를 구분해야 하므로 Damaged를 본다. 두 요구가 달라서 이벤트를 나눴다.
    /// </summary>
    public event Action<int, int> Changed;

    /// <summary>체력이 0이 됐을 때. 한 번만 불린다.</summary>
    public event Action Died;

    /// <summary>
    /// 추가 생성 — 마지막으로 맞은 방향(때린 쪽 → 나). 넉백을 어느 쪽으로 밀지에 쓴다.
    ///
    /// 방향만 기록하고 <b>넉백을 여기서 적용하지 않는 이유</b>: 밀려나는 방식이 대상마다 다르다.
    /// 적은 뒤로 미끄러지지만 플레이어는 경직만 있고 밀리지 않으며, 나중에 나올 거대 보스는
    /// 아예 안 밀려야 한다. Health가 밀어버리면 그 차이를 여기서 분기해야 하고, 그러면
    /// 적 종류가 늘 때마다 이 파일을 열게 된다. 기록만 하고 판단은 맞은 쪽이 한다.
    /// </summary>
    public Vector2 LastHitDirection { get; private set; } = Vector2.right;

    private void Awake()
    {
        Current = Max;
    }

    private void Update()
    {
        if (invulnerableTimer > 0f)
            invulnerableTimer -= Time.deltaTime;
    }

    /// <summary>
    /// 추가 생성 — 체력을 최대치로 되돌리고 모든 무적 상태를 해제한다.
    ///
    /// 적 오브젝트 풀은 같은 인스턴스를 다시 꺼내 쓰므로 Awake가 다시 호출되지 않는다.
    /// 풀에서 재사용할 때 이 함수를 호출하지 않으면 죽은 체력 0 상태로 다시 등장한다.
    /// </summary>
    public void RestoreFull()
    {
        Current = Max;
        invulnerableTimer = 0f;
        IsInvulnerableExternally = false;
    }

    /// <summary>
    /// 데미지를 준다. 무적 중이거나 이미 죽었으면 아무 일도 일어나지 않는다.
    ///
    /// 무적 판정을 호출하는 쪽이 아니라 여기서 하는 이유: 데미지를 주는 곳이 여러 개(적의 돌진,
    /// 플레이어의 검, 나중에 함정)인데 각자 무적을 확인하게 하면 한 곳이 빠뜨린다.
    /// 그 한 곳이 "대시 중인데 가끔 맞는" 버그가 된다.
    /// </summary>
    /// <returns>실제로 데미지가 들어갔으면 true.</returns>
    public bool TakeDamage(int amount) => TakeDamage(amount, null);

    /// <summary>
    /// 추가 생성 — 때린 위치를 같이 받는 판. 넉백 방향을 기록하는 것 외에는 위와 같다.
    /// </summary>
    /// <param name="sourcePosition">때린 쪽의 월드 좌표. null이면 방향을 갱신하지 않는다.</param>
    /// <remarks>수정(2026-09-17) — 본문을 아래 세 인자 판으로 옮겼다. 이 판은 예전처럼 피격 무적을 건다.</remarks>
    public bool TakeDamage(int amount, Vector2? sourcePosition) => TakeDamage(amount, sourcePosition, true);

    /// <summary>
    /// 추가 생성(2026-09-17, Q 2단이 가까운 적에게 막히던 것) — 피격 무적을 걸지 말지까지 정하는 판.
    ///
    /// <b>왜 필요한가.</b> Q(내려찍기)는 한 번의 동작 안에서 1단(0.25초)과 2단(0.42초)이 0.17초 간격으로 들어간다.
    /// 1단이 맞히면 여기서 피격 무적 0.35초가 걸려서, 붙어 있던 적은 2단(데미지 5)을 늘 무시했다.
    /// 기획은 "붙어 있으면 둘 다 맞아 더 아프다"(2 + 5)인데 실제로는 붙을수록 덜 아팠다(2 대 5).
    /// 히트스톱은 원인이 아니다 — 멈추는 동안 무적 타이머와 2단 대기가 같이 멈춰 간격이 그대로다.
    ///
    /// <b>무적을 무시하는 게 아니라 "걸지 않는" 것이다.</b> 이미 걸린 무적(대시 회피·다른 공격의 피격 무적)은
    /// 위 IsInvulnerable 검사에서 그대로 막는다. 회피 규칙은 안 깨진다. 스킬이 자기 연속 타격의 앞 타에만 false를 넘긴다.
    /// </summary>
    /// <param name="grantInvulnerability">false면 이번 타격 뒤에 피격 무적을 걸지 않는다. 바로 이어지는 같은 스킬의 다음 타를 위해서만 쓴다.</param>
    public bool TakeDamage(int amount, Vector2? sourcePosition, bool grantInvulnerability)
    {
        if (amount <= 0) return false;
        if (IsInvulnerable) return false;

        // 수정(2026-09-22) — 아래에 있던 본문을 ApplyDamage로 옮겼다. 피할 수 없는 피해(TakeUnavoidableDamage)가
        // 같은 길로 들어가야 방향 기록·무적·알림 순서가 한 곳에서만 정해진다.
        ApplyDamage(amount, sourcePosition, grantInvulnerability);
        return true;
    }

    /// <summary>
    /// 추가 생성(2026-09-22, 보스 궁극기 "왕관 의식"의 결과) — <b>피할 수 없는 피해.</b>
    /// 대시·피격 무적·연출 무적(<see cref="IsInvulnerableExternally"/>)을 보지 않는다. 디버그 무적만 지킨다.
    ///
    /// <b>왜 필요한가.</b> 왕관 의식을 막는 법은 대시가 아니라 <b>의식 중에 유물을 부수는 것</b>이다(기획 선택
    /// "의식 중 때려서 저지"). 결과가 나오는 순간 마침 대시 중이었다는 이유로 빈사를 피하면, 10초 동안 유물을
    /// 부순 판과 안 부순 판의 차이가 사라진다.
    ///
    /// <b>"예고를 읽고 피한다"는 규칙을 깨지 않는다.</b> 예고(시전 바 10초)와 대응(부수기)이 다 끝난 뒤에
    /// 확정되는 결과라서다. 그래서 공격이 이 함수를 쓰면 안 된다 — 날아오는 공격은 늘 <see cref="TakeDamage(int, Vector2?)"/>로
    /// 들어가 대시로 피할 수 있어야 한다.
    /// </summary>
    /// <returns>실제로 피해가 들어갔으면 true.</returns>
    public bool TakeUnavoidableDamage(int amount, Vector2? sourcePosition)
    {
        if (amount <= 0 || IsDead) return false;
#if UNITY_EDITOR
        // 디버그 무적은 "시험하는 동안 죽지 않게" 켠 것이라 여기서도 지킨다.
        if (IsInvulnerableForDebug) return false;
#endif

        ApplyDamage(amount, sourcePosition, true);
        return true;
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 피해를 실제로 넣고 알린다. 무적 검사는 부르는 쪽이 끝낸 뒤다.
    /// 본문은 원래 <see cref="TakeDamage(int, Vector2?, bool)"/>에 있던 것을 그대로 옮겼다.
    /// </summary>
    private void ApplyDamage(int amount, Vector2? sourcePosition, bool grantInvulnerability)
    {
        // 방향은 데미지가 실제로 들어갈 때만 갱신한다. 무적 중에 스친 공격까지 방향을 바꾸면,
        // 다음에 진짜로 맞았을 때 엉뚱한 쪽으로 밀려난다.
        if (sourcePosition.HasValue)
        {
            Vector2 delta = (Vector2)transform.position - sourcePosition.Value;

            // 정확히 겹쳐 있으면 방향을 못 정한다. 그때는 직전 방향을 유지한다.
            if (delta.sqrMagnitude > 0.0001f) LastHitDirection = delta.normalized;
        }

        Current = Mathf.Max(0, Current - amount);

        // 수정(2026-09-17) — 스킬이 원할 때는 무적을 걸지 않는다. 이유는 TakeDamage(세 인자 판)의 설명에 있다.
        if (grantInvulnerability) invulnerableTimer = invulnerableSecondsAfterHit;

        Damaged?.Invoke(Current, Max);
        Changed?.Invoke(Current, Max);

        if (Current <= 0)
            Died?.Invoke();
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 자폭처럼 확정된 사망을 무적과 관계없이 처리한다.
    /// 피해가 아니므로 피격 연출은 생략하고, 체력 변경과 사망을 알려 방 집계와 풀 복귀를 이어 준다.
    /// 이미 죽었다면 다시 알리지 않아 처치와 보상이 중복되지 않는다.
    ///
    /// <b>스스로 끝내는 경우에만 쓴다.</b> 남을 죽이는 데 쓰면 안 된다 — 무적을 무시하는 공격은
    /// 대시 회피와 피격 후 무적을 무의미하게 만들고, 그건 이 게임이 세운 "예고를 읽고 피한다"는 규칙을 깬다.
    /// 공격은 <see cref="TakeDamage(int, Vector2?)"/>로만 들어가야 한다.
    ///
    /// <see cref="Damaged"/>를 안 쏘는 이유: 그 이벤트는 "맞았다"는 연출(움찔·넉백)의 신호다.
    /// 자폭은 맞은 것이 아니라 스스로 터진 것이라 움찔할 몸이 남아 있지 않다.
    /// </summary>
    public void Kill()
    {
        if (IsDead) return;

        Current = 0;
        Changed?.Invoke(Current, Max);
        Died?.Invoke();
    }

    /// <summary>
    /// 추가 생성 — 유물로 얻은 최대 체력 보정. 장착한 유물이 바뀔 때마다 통째로 다시 정해진다.
    ///
    /// <b>더하고 빼는 대신 "지금 몇인지"를 통째로 넣게 한 이유:</b>
    /// 유물은 이제 장착·해제가 된다. 더하기·빼기로 관리하면 해제할 때 정확히 얼마를 빼야 하는지를
    /// 이쪽이 기억하고 있어야 하고, 한 번이라도 어긋나면 <b>최대 체력이 슬금슬금 늘거나 준다.</b>
    /// 에러 없이 숫자만 틀어지는 종류라 나중에 원인을 찾기 어렵다.
    /// 장착 목록을 아는 쪽이 매번 합계를 내서 넣으면 그런 어긋남 자체가 생기지 않는다.
    ///
    /// 올린 만큼 현재 체력도 같이 올린다. 최대치만 올리고 현재값을 그대로 두면 유물을 꼈는데
    /// 게이지의 <b>빈 칸이 늘어난다.</b> 강해진 게 아니라 약해진 것처럼 보인다.
    /// </summary>
    public void SetBonusMax(int value)
    {
        value = Mathf.Max(0, value);
        if (bonusMax == value) return;

        int before = Max;
        bonusMax = value;
        int delta = Max - before;

        // 올랐으면 그만큼 채워주고, 내렸으면 넘치는 만큼만 깎는다.
        // 내릴 때 delta만큼 빼면 안 된다 — 이미 체력이 닳아 있으면 두 번 깎이는 셈이 된다.
        Current = delta > 0
            ? Mathf.Min(Max, Current + delta)
            : Mathf.Min(Current, Max);

        Changed?.Invoke(Current, Max);
    }

    /// <summary>회복. 최대치를 넘지 않는다. 죽은 뒤에는 살아나지 않는다.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;

        Current = Mathf.Min(Max, Current + amount);

        // 회복은 Changed만 쏜다. Damaged를 쏘면 상자를 열 때 플레이어가 피격 모션을 낸다.
        Changed?.Invoke(Current, Max);
    }
}
