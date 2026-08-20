using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 주운 유물을 보관하고, 장착한 것만 효과를 적용한다. 플레이어에 붙는다.
///
/// <b>보관함과 장착 칸을 나눈 구조다.</b> 유물을 주우면 보관함에 들어갈 뿐 아무 효과가 없고,
/// 장착 칸(<see cref="SlotCount"/>개)에 넣어야 효과가 붙는다. 칸이 모자라면 무엇을 버릴지
/// 골라야 하고, 그 선택이 이 게임에서 유물을 모으는 재미다.
///
/// 효과 적용을 유물 에셋이 아니라 여기서 하는 이유: 유물이 <see cref="Health"/>,
/// <see cref="PlayerStamina"/>, <see cref="SkillController"/>를 직접 알면 에셋이 씬 오브젝트를
/// 참조해야 해서 성립하지 않는다. 유물은 "무엇을 얼마나"만 들고 있고, 그걸 누구에게 적용할지는
/// 플레이어를 아는 이쪽이 안다.
///
/// <b>효과를 더하고 빼지 않고 매번 통째로 다시 계산하는 것이 이 클래스의 핵심 규칙이다.</b>
/// 장착이 바뀔 때마다 모든 보정치를 0으로 되돌리고 장착 목록을 처음부터 다시 더한다.
/// 해제할 때 정확히 얼마를 빼야 하는지를 기억하는 방식은, 한 번이라도 어긋나면 수치가
/// 슬금슬금 틀어지는데 에러가 안 나서 원인을 찾기 어렵다. 다시 계산하면 그 틈이 아예 없다.
///
/// 판이 끝나면 초기화할 필요가 없다 — 씬을 다시 로드하면 이 컴포넌트가 통째로 새로 생긴다.
/// AshGauge와 같은 이유로 저장 코드가 한 줄도 없다.
/// </summary>
[DisallowMultipleComponent]
public class RelicInventory : MonoBehaviour
{
    /// <summary>장착 칸 개수. 인벤토리 패널 그림의 팔각형 칸 수와 같아야 한다.</summary>
    public const int SlotCount = 3;

    [Header("참조 (비어 있으면 같은 오브젝트에서 찾는다)")]
    [SerializeField] private Health health;
    [SerializeField] private PlayerStamina stamina;
    [SerializeField] private SkillController skills;
    [SerializeField] private PlayerController movement;
    [SerializeField] private AshGauge ashGauge;

    // 주운 순서대로 쌓인다. 여기 있는 동안에는 아무 효과가 없다.
    private readonly List<RelicInstance> bag = new List<RelicInstance>();

    // 장착 칸. 빈 칸은 RelicInstance.None이다.
    private readonly RelicInstance[] equipped = new RelicInstance[SlotCount];

    [Header("보스")]
    [Tooltip("보스방이 열리는 데 필요한 열쇠 개수.")]
    [SerializeField, Min(1)] private int bossKeysRequired = 4;

    /// <summary>보관함. 인벤토리 화면이 읽는다.</summary>
    public IReadOnlyList<RelicInstance> Bag => bag;

    /// <summary>
    /// 지금까지 모은 보스 열쇠 개수. 장착 여부는 상관없다.
    ///
    /// <b>보관함에만 있어도 인정하는 것이 중요하다.</b> 장착해야 인정하면 열쇠 3개가
    /// 장착 칸 3개를 그대로 차지해서, 열쇠를 다 모은 순간 <b>유물 효과가 하나도 없는 상태로</b>
    /// 보스를 만나게 된다. 모으는 행위가 플레이어를 약하게 만들면 안 된다.
    /// </summary>
    public int BossKeyCount { get; private set; }

    /// <summary>보스방을 열 수 있는가.</summary>
    public bool HasAllBossKeys => BossKeyCount >= bossKeysRequired;

    /// <summary>필요한 열쇠 총 개수. UI가 "2/3" 같은 표시를 낼 때 읽는다.</summary>
    public int BossKeysRequired => bossKeysRequired;

    /// <summary>이 유물을 이미 가지고 있는가. 상자가 열쇠를 중복으로 주지 않으려고 묻는다.</summary>
    public bool Has(RelicData relic)
    {
        if (relic == null) return false;

        foreach (RelicInstance item in bag)
            if (item.Data == relic) return true;

        foreach (RelicInstance item in equipped)
            if (item.Data == relic) return true;

        return false;
    }

    /// <summary>장착 칸을 읽는다. 범위를 벗어나면 빈 칸을 돌려준다.</summary>
    public RelicInstance GetEquipped(int slot)
        => slot >= 0 && slot < SlotCount ? equipped[slot] : RelicInstance.None;

    /// <summary>유물을 주웠을 때. 획득 알림 UI가 듣는다.</summary>
    public event Action<RelicData> Gained;

    /// <summary>보관함이나 장착 칸이 바뀌었을 때. 인벤토리 화면이 다시 그린다.</summary>
    public event Action Changed;

    private void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (skills == null) skills = GetComponent<SkillController>();
        if (movement == null) movement = GetComponent<PlayerController>();
        if (ashGauge == null) ashGauge = GetComponent<AshGauge>();

        for (int i = 0; i < SlotCount; i++) equipped[i] = RelicInstance.None;
    }

    /// <summary>
    /// 유물을 줍는다. 보관함에 들어갈 뿐 효과는 아직 없다.
    ///
    /// 빈 장착 칸이 있으면 바로 끼워준다. 처음 세 개까지는 "주웠는데 아무 일도 안 일어나네"를
    /// 겪지 않게 하려는 것이다. 칸이 다 차고 나서부터가 진짜 선택이다.
    /// </summary>
    public void Acquire(RelicData relic)
    {
        if (relic == null) return;

        // 무작위 유물은 바로 여기서 딱 한 번 굴린다. 장착할 때 굴리면 뺐다 꼈다로 다시 굴릴 수 있다.
        var instance = RelicInstance.Roll(relic);
        bag.Add(instance);

        if (relic.Role == RelicData.RelicRole.BossKey) BossKeyCount++;

        // 표식 유물(효과 없음)은 자동 장착에서 뺀다. 끼워봐야 아무 일도 안 일어나는데
        // 칸만 차지해서, 열쇠를 모을수록 오히려 약해진 채 보스를 만나게 된다.
        int emptySlot = relic.IsMarker ? -1 : FindEmptySlot();
        if (emptySlot >= 0) Equip(bag.Count - 1, emptySlot);
        else Changed?.Invoke();

        // 인벤토리 화면이 있어도 콘솔 로그는 남긴다. 무작위 유물이 얼마로 굴렀는지는
        // 화면에 잠깐 뜨고 마는데, 밸런스를 볼 때는 지난 기록이 필요하다.
        Debug.Log($"[유물 획득] {relic.DisplayName} ({relic.Effect} +{instance.Amount:0.##}" +
                  $"{(relic.IsRandom ? " 무작위" : "")}) — 보관함 {bag.Count}개" +
                  $"{(emptySlot >= 0 ? $", {emptySlot + 1}번 칸에 장착" : ", 장착 칸이 가득 참")}", this);

        Gained?.Invoke(relic);

        // 클리어 유물은 줍는 순간 판이 끝난다. 알림이 나간 뒤에 끝내야
        // "무엇을 주웠는지"가 화면에 한 번은 뜬다.
        if (relic.Role == RelicData.RelicRole.RunEnd) EndRun();
    }

    /// <summary>클리어 유물을 주웠다. 판을 끝내고 결과 화면으로 넘긴다.</summary>
    private void EndRun()
    {
        var run = FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);
        if (run == null)
        {
            Debug.LogWarning("[유물] RunManager를 못 찾아 판을 끝내지 못했다.", this);
            return;
        }

        Debug.Log("[유물] 클리어 유물을 획득했다. 판을 종료한다.", this);
        run.EndRun(true);
    }

    /// <summary>
    /// 보관함의 유물을 장착 칸에 넣는다. 그 칸에 이미 뭔가 있으면 보관함으로 돌아간다.
    ///
    /// 꺼낸 것을 보관함에서 지우고 넣던 것을 되돌리는 순서가 중요하다. 반대로 하면
    /// 인덱스가 밀려서 엉뚱한 유물이 장착된다.
    /// </summary>
    public void Equip(int bagIndex, int slot)
    {
        if (bagIndex < 0 || bagIndex >= bag.Count) return;
        if (slot < 0 || slot >= SlotCount) return;

        RelicInstance incoming = bag[bagIndex];
        RelicInstance outgoing = equipped[slot];

        bag.RemoveAt(bagIndex);
        equipped[slot] = incoming;

        if (!outgoing.IsEmpty) bag.Add(outgoing);

        Recalculate();
        Changed?.Invoke();
    }

    /// <summary>장착 칸을 비우고 보관함으로 돌려보낸다.</summary>
    public void Unequip(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return;
        if (equipped[slot].IsEmpty) return;

        bag.Add(equipped[slot]);
        equipped[slot] = RelicInstance.None;

        Recalculate();
        Changed?.Invoke();
    }

    /// <summary>비어 있는 장착 칸 번호. 없으면 -1.</summary>
    public int FindEmptySlot()
    {
        for (int i = 0; i < SlotCount; i++)
            if (equipped[i].IsEmpty) return i;

        return -1;
    }

    /// <summary>
    /// 장착 목록을 보고 모든 보정치를 처음부터 다시 계산한다.
    ///
    /// 0으로 되돌리고 다시 더하는 이유는 클래스 주석에 적은 대로다. 특히 쿨타임은 곱셈이라
    /// 뺄 수가 없다 — 0.88을 두 번 곱한 뒤 하나를 해제하려면 나눠야 하는데, 부동소수점에서
    /// 곱했다 나눈 값은 원래 값으로 정확히 안 돌아온다. 다시 계산하면 그 문제가 없다.
    /// </summary>
    private void Recalculate()
    {
        int bonusHealth = 0;
        float bonusStamina = 0f;
        int bonusDamage = 0;
        float bonusSpeed = 0f;
        float bonusRegen = 0f;
        float cooldownScale = 1f;
        float bonusAsh = 0f;

        foreach (RelicInstance item in equipped)
        {
            if (item.IsEmpty) continue;

            switch (item.Data.Effect)
            {
                case RelicData.EffectKind.MaxHealth:
                    // 체력은 정수라 반올림한다. 0.5 같은 값을 넣으면 아무 일도 안 일어나는데
                    // 그건 "꼈는데 아무 변화가 없다"로 보여서 최악이다.
                    bonusHealth += Mathf.RoundToInt(item.Amount);
                    break;

                case RelicData.EffectKind.MaxStamina:
                    bonusStamina += item.Amount;
                    break;

                case RelicData.EffectKind.SkillDamage:
                    bonusDamage += Mathf.RoundToInt(item.Amount);
                    break;

                case RelicData.EffectKind.MoveSpeed:
                    bonusSpeed += item.Amount;
                    break;

                case RelicData.EffectKind.StaminaRegen:
                    bonusRegen += item.Amount;
                    break;

                case RelicData.EffectKind.CooldownRate:
                    // 수치가 퍼센트라 100으로 나눈다.
                    // 곱해서 쌓는다. 빼서 쌓으면 여러 개 꼈을 때 0 이하로 내려가 쿨타임이
                    // 사라진다. 곱셈은 아무리 껴도 0에 가까워질 뿐 넘지 않는다.
                    cooldownScale *= 1f - Mathf.Clamp01(item.Amount / 100f);
                    break;

                case RelicData.EffectKind.AshPerKill:
                    bonusAsh += item.Amount;
                    break;
            }
        }

        if (health != null) health.SetBonusMax(bonusHealth);
        if (stamina != null)
        {
            stamina.SetBonusMax(bonusStamina);
            stamina.BonusRegenPerSecond = bonusRegen;
        }
        if (skills != null)
        {
            skills.BonusDamage = bonusDamage;
            skills.CooldownScale = cooldownScale;
        }
        if (movement != null) movement.BonusMoveSpeed = bonusSpeed;
        if (ashGauge != null) ashGauge.BonusChargePerKill = bonusAsh;
    }
}
