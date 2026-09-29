using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-28, 포트폴리오 1단계) — 유물 재계산 테스트.
///
/// <see cref="RelicInventory"/>의 핵심 규칙은 <b>"장착이 바뀔 때마다 보정치를 0에서 통째로 다시 계산한다"</b>다.
/// 더하고 빼는 방식은 한 번 어긋나면 수치가 조용히 틀어지고, 곱셈(쿨타임)은 나눠서 되돌리면 소수점 오차로 원래 값에 못 돌아온다.
/// 이 테스트들은 그 규칙이 깨지면(누군가 "빠르게" 더하기·빼기로 바꾸면) 바로 빨갛게 뜬다.
///
/// <b>플레이어 프리팹 대신 빈 오브젝트에 필요한 컴포넌트만 붙인다.</b> EditMode에서는 Awake가 돌지 않으므로,
/// 인스펙터에서 참조를 꽂는 것과 같은 길(SerializedObject)로 체력·스킬 참조를 꽂는다.
/// 테스트용 유물도 같은 길로 값을 넣어 만든다 — 게임 코드에 테스트 전용 입구를 만들지 않으려는 것이다.
/// </summary>
public class RelicInventoryTests
{
    private GameObject player;
    private Health health;
    private SkillController skills;
    private RelicInventory inventory;

    // 테스트가 만든 유물 에셋. 끝나면 지운다(안 지우면 편집기 메모리에 쌓인다).
    private readonly List<RelicData> createdRelics = new List<RelicData>();

    /// <summary>테스트마다 새 플레이어를 만든다. 테스트끼리 상태를 나눠 쓰지 않게 하려는 것이다.</summary>
    [SetUp]
    public void SetUp()
    {
        player = new GameObject("테스트 플레이어");
        health = player.AddComponent<Health>();

        // SkillController는 RequireComponent로 PlayerController(와 Rigidbody2D)를 같이 붙인다. 쿨타임 배율을 읽으려고 붙인다.
        skills = player.AddComponent<SkillController>();
        inventory = player.AddComponent<RelicInventory>();

        var serialized = new SerializedObject(inventory);
        serialized.FindProperty("health").objectReferenceValue = health;
        serialized.FindProperty("skills").objectReferenceValue = skills;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>만든 오브젝트와 유물 에셋을 모두 지운다.</summary>
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(player);

        foreach (RelicData relic in createdRelics)
            Object.DestroyImmediate(relic);

        createdRelics.Clear();
    }

    /// <summary>
    /// 체력 +1 유물 둘을 끼웠다 빼기를 10번 되풀이해도 최대 체력은 "기본 + 장착한 만큼"이어야 한다.
    /// 더하기·빼기 방식에서 한 번이라도 짝이 어긋나면 이 숫자가 슬금슬금 늘거나 준다.
    /// </summary>
    [Test]
    public void EquipUnequipCycles_DoNotAccumulateMaxHealth()
    {
        int baseMax = health.Max;

        // 빈 칸이 있으면 주운 즉시 장착된다(0번, 1번 칸).
        inventory.Acquire(MakeRelic(RelicData.EffectKind.MaxHealth, 1f));
        inventory.Acquire(MakeRelic(RelicData.EffectKind.MaxHealth, 1f));
        Assert.AreEqual(baseMax + 2, health.Max, "유물 둘을 끼우면 +2");

        for (int i = 0; i < 10; i++)
        {
            // 빼면 보관함 맨 뒤로 가고, 그걸 다시 0번 칸에 끼운다.
            inventory.Unequip(0);
            Assert.AreEqual(baseMax + 1, health.Max, $"{i + 1}번째 해제 뒤");

            inventory.Equip(inventory.Bag.Count - 1, 0);
            Assert.AreEqual(baseMax + 2, health.Max, $"{i + 1}번째 재장착 뒤");
        }

        inventory.Unequip(0);
        inventory.Unequip(1);
        Assert.AreEqual(baseMax, health.Max, "다 빼면 기본값으로 돌아온다");
    }

    /// <summary>
    /// 쿨타임은 곱해서 쌓는다(12% 둘 = 0.88 × 0.88). 다 빼면 <b>오차 없이 정확히 1</b>이어야 한다.
    /// 곱했다가 나눠서 되돌리는 방식이면 부동소수점 때문에 1에서 조금씩 벗어난다 — 다시 계산하는 방식이라 안 벗어난다.
    /// </summary>
    [Test]
    public void CooldownScale_ReturnsExactlyToOne_WhenAllUnequipped()
    {
        inventory.Acquire(MakeRelic(RelicData.EffectKind.CooldownRate, 12f));
        inventory.Acquire(MakeRelic(RelicData.EffectKind.CooldownRate, 12f));
        Assert.AreEqual(0.88f * 0.88f, skills.CooldownScale, 1e-6f, "12% 둘은 곱해서 쌓인다");

        inventory.Unequip(0);
        inventory.Unequip(1);
        Assert.AreEqual(1f, skills.CooldownScale, "다 빼면 정확히 1(오차 허용 없음)");
    }

    /// <summary>
    /// 보관함에 있는 유물은 효과가 없다. 칸(3개)보다 많이 주우면 넘친 것은 보관함에 들어가고 능력치에 안 들어간다.
    /// 이 게임에서 유물을 모으는 재미는 "무엇을 끼울지 고르는 것"이라, 보관함까지 효과가 들어가면 그 선택이 사라진다.
    /// </summary>
    [Test]
    public void BaggedRelics_HaveNoEffect()
    {
        int baseMax = health.Max;

        for (int i = 0; i < RelicInventory.SlotCount + 1; i++)
            inventory.Acquire(MakeRelic(RelicData.EffectKind.MaxHealth, 1f));

        Assert.AreEqual(1, inventory.Bag.Count, "칸보다 하나 더 주우면 하나는 보관함으로");
        Assert.AreEqual(baseMax + RelicInventory.SlotCount, health.Max, "장착한 칸만큼만 오른다");
    }

    /// <summary>
    /// 보스 열쇠는 전용 칸으로 가고 능력치를 바꾸지 않는다. 칸이 가득 차 보관함으로 넘친 열쇠도 일반 칸에는 못 넣는다.
    /// 열쇠가 일반 칸을 차지하면 열쇠를 모을수록 실제 유물을 못 껴서 약해진 채 보스를 만난다.
    /// </summary>
    [Test]
    public void BossKeys_UseTheirOwnSlots_AndNeverChangeStats()
    {
        int baseMax = health.Max;

        // 열쇠에 체력 값을 일부러 적어 둔다 — 능력치에 섞이면 바로 드러나게.
        for (int i = 0; i < RelicInventory.BossSlotCount + 1; i++)
            inventory.Acquire(MakeRelic(RelicData.EffectKind.MaxHealth, 5f, RelicData.RelicRole.BossKey));

        Assert.AreEqual(RelicInventory.BossSlotCount, inventory.BossKeyCount, "열쇠 칸이 가득 찬다");
        Assert.AreEqual(1, inventory.Bag.Count, "칸보다 많은 열쇠는 보관함으로");
        Assert.AreEqual(0, inventory.FindEmptySlot(), "일반 칸은 비어 있다");

        // 보관함으로 넘친 열쇠를 일반 칸에 억지로 넣어 본다 — 거절돼야 한다(경고만 남는다).
        inventory.Equip(0, 0);
        Assert.IsTrue(inventory.GetEquipped(0).IsEmpty, "열쇠는 일반 칸에 못 들어간다");
        Assert.AreEqual(baseMax, health.Max, "열쇠는 능력치를 안 바꾼다");
    }

    /// <summary>
    /// 테스트용 유물을 만든다. 무작위 범위(amountMax)를 0으로 두어 굴린 값이 늘 amount와 같게 한다(결과가 매번 같아야 테스트다).
    /// </summary>
    private RelicData MakeRelic(RelicData.EffectKind effect, float amount,
                                RelicData.RelicRole role = RelicData.RelicRole.Normal)
    {
        var relic = ScriptableObject.CreateInstance<RelicData>();
        createdRelics.Add(relic);

        var serialized = new SerializedObject(relic);
        serialized.FindProperty("displayName").stringValue = $"테스트 {effect} {amount}";

        // 열거형은 enumValueIndex(이름 목록 순번)가 아니라 intValue(실제 값)로 넣는다 — 값과 순번이 달라도 맞게.
        serialized.FindProperty("effect").intValue = (int)effect;
        serialized.FindProperty("role").intValue = (int)role;
        serialized.FindProperty("amount").floatValue = amount;
        serialized.FindProperty("amountMax").floatValue = 0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return relic;
    }
}
