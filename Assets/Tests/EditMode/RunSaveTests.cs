using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — 판 저장 테스트.
///
/// 지키는 규칙:
/// <list type="bullet">
/// <item>저장 → 파일 → 불러오기를 거쳐도 값이 그대로다(유물 수치 포함 — 다시 굴리면 안 된다).</item>
/// <item>깨진 파일·옛 형식은 조용히 거절한다(게임을 멈추지 않는다).</item>
/// <item>끌 때 저장은 체력·재 게이지를 낮은 쪽으로 고른다(끄고 켜서 체력 되돌리기 막기).</item>
/// <item>모든 유물이 카탈로그에 있다(빠지면 이어할 때 유물이 사라진다).</item>
/// <item>이어하기로 되돌린 유물이 보정치를 다시 계산한다(최대 체력이 오른다).</item>
/// </list>
///
/// 실제 저장 파일(persistentDataPath)을 건드리지 않게, 경로를 받는 함수로 임시 폴더에서 시험한다.
/// </summary>
public class RunSaveTests
{
    private string tempPath;
    private readonly List<Object> created = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        tempPath = Path.Combine(Path.GetTempPath(), "PathOfAsh_RunSaveTest_" + System.Guid.NewGuid().ToString("N") + ".json");
    }

    [TearDown]
    public void TearDown()
    {
        RunSaveStore.Delete(tempPath);

        foreach (Object obj in created)
            if (obj != null) Object.DestroyImmediate(obj);
        created.Clear();
    }

    /// <summary>저장한 값이 파일을 거쳐 그대로 돌아온다. 빈 칸은 빈 칸으로, 칸 번호도 그대로.</summary>
    [Test]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var data = new RunSaveData
        {
            roomIndex = 3,
            inBossRoom = true,
            enteredRoomCount = 9,
            health = 4,
            ashCharge = 62.5f,
            elapsedSeconds = 321.5f,
            killCount = 27,
        };
        data.bag.Add(new SavedRelic { id = "Relic_Weight", amount = 1f });
        data.equipped[1] = new SavedRelic { id = "Relic_AshKingDie", amount = 6f };
        data.bossKeys[0] = new SavedRelic { id = "Relic_AshKey", amount = 0f };

        Assert.IsTrue(RunSaveStore.Save(data, tempPath));
        Assert.IsTrue(RunSaveStore.TryLoad(tempPath, out RunSaveData loaded));

        Assert.AreEqual(3, loaded.roomIndex);
        Assert.IsTrue(loaded.inBossRoom);
        Assert.AreEqual(9, loaded.enteredRoomCount);
        Assert.AreEqual(4, loaded.health);
        Assert.AreEqual(62.5f, loaded.ashCharge);
        Assert.AreEqual(321.5f, loaded.elapsedSeconds);
        Assert.AreEqual(27, loaded.killCount);

        Assert.AreEqual(1, loaded.bag.Count);
        Assert.AreEqual("Relic_Weight", loaded.bag[0].id);
        Assert.IsTrue(loaded.equipped[0].IsEmpty, "빈 칸은 빈 칸으로 돌아온다");
        Assert.AreEqual("Relic_AshKingDie", loaded.equipped[1].id);
        Assert.AreEqual(6f, loaded.equipped[1].amount, "주울 때 굴린 수치를 지킨다(다시 굴리지 않는다)");
        Assert.AreEqual(1, loaded.BossKeyCount);
    }

    /// <summary>파일이 없으면 false. 예외가 나면 안 된다.</summary>
    [Test]
    public void TryLoad_ReturnsFalse_WhenNoFile()
    {
        Assert.IsFalse(RunSaveStore.TryLoad(tempPath, out RunSaveData loaded));
        Assert.IsNull(loaded);
    }

    /// <summary>깨진 파일은 거절한다. 게임을 멈추지 않는다(경고만 남긴다).</summary>
    [Test]
    public void TryLoad_RejectsCorruptFile()
    {
        File.WriteAllText(tempPath, "{ 이건 JSON이 아니다");
        Assert.IsFalse(RunSaveStore.TryLoad(tempPath, out _));
    }

    /// <summary>형식 번호가 다른 옛 파일은 거절한다. 억지로 읽으면 빈 값이 섞인 채 이어진다.</summary>
    [Test]
    public void TryLoad_RejectsOtherVersion()
    {
        // Save는 형식 번호를 지금 것으로 맞추므로, Save를 거치지 않고 옛 번호 그대로 파일에 쓴다.
        // (JSON 글자를 바꿔치기하지 않는 이유: 들여쓰기·띄어쓰기 형식에 테스트가 묶이면 형식만 바뀌어도 깨진다.)
        var old = new RunSaveData { version = RunSaveData.CurrentVersion - 1, health = 3 };
        File.WriteAllText(tempPath, JsonUtility.ToJson(old));

        Assert.IsFalse(RunSaveStore.TryLoad(tempPath, out _));
    }

    /// <summary>지우면 없다. 없는 파일을 지워도 괜찮다.</summary>
    [Test]
    public void Delete_RemovesFile_AndIsSafeWhenMissing()
    {
        RunSaveStore.Save(new RunSaveData(), tempPath);
        RunSaveStore.Delete(tempPath);
        Assert.IsFalse(File.Exists(tempPath));
        Assert.DoesNotThrow(() => RunSaveStore.Delete(tempPath));
    }

    /// <summary>
    /// 끌 때 저장 — 체력·재 게이지는 입구 때와 지금 중 낮은 쪽, 시간은 큰 쪽.
    /// 그리고 체크포인트(원본)는 그대로다(나가기를 눌렀다 취소해도 깎이지 않게).
    /// </summary>
    [Test]
    public void WithQuitState_KeepsLowerHealthAndAsh_AndLeavesCheckpointUntouched()
    {
        var checkpoint = new RunSaveData { health = 5, ashCharge = 80f, elapsedSeconds = 100f, killCount = 10 };
        checkpoint.bag.Add(new SavedRelic { id = "Relic_Weight", amount = 1f });

        RunSaveData hurt = checkpoint.WithQuitState(currentHealth: 2, currentAshCharge: 0f, currentElapsedSeconds: 130f);
        Assert.AreEqual(2, hurt.health, "다쳤으면 다친 채로");
        Assert.AreEqual(0f, hurt.ashCharge, "궁극기를 썼으면 빈 채로");
        Assert.AreEqual(130f, hurt.elapsedSeconds, "흐른 시간은 센다");
        Assert.AreEqual(10, hurt.killCount, "처치 수는 입구 때 값 — 방을 다시 하며 또 잡으므로");

        RunSaveData healed = checkpoint.WithQuitState(currentHealth: 7, currentAshCharge: 100f, currentElapsedSeconds: 130f);
        Assert.AreEqual(5, healed.health, "입구 때보다 많아도 입구 값 — 끄고 켜서 얻는 것이 없다");
        Assert.AreEqual(80f, healed.ashCharge);

        Assert.AreEqual(5, checkpoint.health, "원본 체크포인트는 그대로");
        hurt.bag.Clear();
        Assert.AreEqual(1, checkpoint.bag.Count, "사본의 유물 목록을 고쳐도 원본은 그대로(깊은 복사)");
    }

    /// <summary>
    /// 프로젝트의 모든 유물이 카탈로그에 있고, 이름이 겹치지 않는다.
    /// 유물을 새로 만들고 "유물 카탈로그 갱신"을 잊으면 여기서 빨갛게 뜬다.
    /// </summary>
    [Test]
    public void Catalog_ContainsEveryRelic_WithUniqueNames()
    {
        RelicCatalog catalog = RelicCatalog.Load();
        Assert.IsNotNull(catalog, "Assets/Project/Resources/RelicCatalog.asset이 없다");

        string[] inProject = AssetDatabase.FindAssets("t:RelicData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<RelicData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(relic => relic != null)
            .Select(relic => relic.name)
            .ToArray();

        foreach (string name in inProject)
            Assert.IsNotNull(catalog.Find(name), $"{name}이 카탈로그에 없다 — Tools → 재의 길 → 프리팹 → 유물 카탈로그 갱신");

        Assert.AreEqual(inProject.Length, inProject.Distinct().Count(), "유물 이름이 겹친다 — 저장 파일은 이름으로 유물을 찾는다");
    }

    /// <summary>
    /// 이어하기로 되돌린 유물도 보정치를 다시 계산한다 — 장착 칸의 최대 체력 +2가 실제 최대 체력에 붙는다.
    /// Restore가 Recalculate를 빠뜨리면 "유물은 끼고 있는데 효과가 없다"가 된다.
    /// </summary>
    [Test]
    public void InventoryRestore_ReappliesEquippedEffects_AndKeepsSlotOrder()
    {
        var player = new GameObject("테스트 플레이어");
        created.Add(player);
        Health health = player.AddComponent<Health>();
        RelicInventory inventory = player.AddComponent<RelicInventory>();

        var serialized = new SerializedObject(inventory);
        serialized.FindProperty("health").objectReferenceValue = health;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        int baseMax = health.Max;
        RelicData plusTwo = MakeRelic(RelicData.EffectKind.MaxHealth, 2f);

        inventory.Restore(
            new List<RelicInstance>(),
            new List<RelicInstance> { RelicInstance.None, new RelicInstance(plusTwo, 2f), RelicInstance.None },
            new List<RelicInstance>());

        Assert.AreEqual(baseMax + 2, health.Max, "장착 칸의 효과가 다시 붙는다");
        Assert.IsTrue(inventory.GetEquipped(0).IsEmpty);
        Assert.AreEqual(plusTwo, inventory.GetEquipped(1).Data, "2번 칸에 있던 유물은 2번 칸으로");
    }

    /// <summary>테스트용 유물을 만든다(RelicInventoryTests와 같은 방식 — 인스펙터와 같은 길로 값을 넣는다).</summary>
    private RelicData MakeRelic(RelicData.EffectKind effect, float amount)
    {
        var relic = ScriptableObject.CreateInstance<RelicData>();
        created.Add(relic);

        var serialized = new SerializedObject(relic);
        serialized.FindProperty("effect").intValue = (int)effect;
        serialized.FindProperty("amount").floatValue = amount;
        serialized.FindProperty("amountMax").floatValue = 0f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return relic;
    }
}
