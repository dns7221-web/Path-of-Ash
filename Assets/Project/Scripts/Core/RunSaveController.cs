using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — 진행 중인 판을 저장하고, 이어하기 때 되돌린다. Game 씬에서만 산다.
///
/// <b>언제 저장하나.</b>
/// <list type="number">
/// <item><b>던전 방(보스 방 포함)에 들어갈 때마다</b> — <see cref="SaveCheckpoint"/>. 튜토리얼은 저장하지 않는다(던전에 들어가기 전이다).</item>
/// <item><b>게임을 끌 때</b> — <see cref="SaveOnQuit"/>. 설정 창의 [게임 나가기]가 부르고, 창 X·Alt+F4로 꺼도
/// 유니티 내장 이벤트 <see cref="Application.quitting"/>이 같은 함수를 부른다. 두 번 불려도 같은 내용이라 괜찮다.</item>
/// </list>
/// 판이 끝나면(사망·클리어) 지운다 — 로그라이크라서 끝난 판은 이어할 수 없어야 한다(<see cref="Discard"/>, RunManager.EndRun).
///
/// <b>씬에 배치하지 않는다.</b> <see cref="RoomSequenceController"/>가 Awake에서 자기 오브젝트에 붙이고 참조를 넘긴다
/// (ClearCutscene과 같은 방식). Game.unity를 고치거나 빌더를 다시 돌리지 않아도 기능이 살아난다.
///
/// 상태의 주인은 그대로다 — 체력은 <see cref="Health"/>, 유물은 <see cref="RelicInventory"/>, 판 기록은 <see cref="RunManager"/>.
/// 이 컴포넌트는 그 값을 읽어 파일 모양으로 옮기고(Capture), 파일에서 읽어 주인에게 돌려줄(ApplyToRun) 뿐이다.
/// </summary>
[DisallowMultipleComponent]
public class RunSaveController : MonoBehaviour
{
    private RunManager runManager;
    private Health health;
    private AshGauge ashGauge;
    private RelicInventory inventory;
    private RelicCatalog catalog;

    // 마지막으로 방에 들어갈 때 남긴 내용. 비어 있으면 아직 던전에 안 들어갔다(튜토리얼 중) — 끌 때 저장할 것이 없다.
    private RunSaveData checkpoint;

    // 판이 끝났다(클리어 연출 시작). 이 뒤로는 저장하지 않는다 — 클리어 연출 중에 끄고 다시 켜면 보스를 또 잡는 일이 없게.
    private bool discarded;

    /// <summary>끌 때 저장할 것이 있는가. 설정 창이 나가기 문구("저장하고 종료" / "튜토리얼은 저장 안 됨")를 고를 때 쓴다.</summary>
    public bool HasCheckpoint => checkpoint != null && !discarded;

    /// <summary>
    /// 필요한 참조를 받는다. <see cref="RoomSequenceController"/>가 Awake에서 부른다.
    /// 플레이어 쪽 컴포넌트는 플레이어에서 꺼낸다 — 유물·체력·재 게이지가 전부 플레이어에 붙어 있다.
    /// </summary>
    public void Init(PlayerController player, RunManager manager)
    {
        runManager = manager;

        if (player != null)
        {
            health = player.GetComponent<Health>();
            ashGauge = player.GetComponent<AshGauge>();
            inventory = player.GetComponent<RelicInventory>();
        }

        catalog = RelicCatalog.Load();
        if (catalog == null)
            Debug.LogError("[저장] 유물 카탈로그가 없다(Assets/Project/Resources/RelicCatalog.asset). " +
                           "Tools → 재의 길 → 프리팹 → 유물 카탈로그 갱신 을 실행해라. 이어하기 때 유물이 비게 된다.", this);
    }

    // 유니티 내장 종료 이벤트. 창 X·Alt+F4·Application.Quit 모두 여기로 온다.
    // 에디터에서는 플레이 모드를 멈출 때도 불린다 — 그래서 에디터에서 던전 도중에 멈추면 다음에 타이틀에서 이어하기가 뜬다(테스트할 때 편하다).
    // 작업 관리자로 강제 종료하면 불리지 않는다. 그 경우는 마지막 방 입구 저장으로 이어진다.
    private void OnEnable() => Application.quitting += SaveOnQuit;
    private void OnDisable() => Application.quitting -= SaveOnQuit;

    /// <summary>방에 들어갈 때 지금 상태를 저장한다. 이 내용이 "끌 때 저장"의 바탕(체크포인트)이 된다.</summary>
    /// <param name="roomIndex">던전 방 배열에서의 위치. 보스 방이면 그 직전 방의 위치.</param>
    /// <param name="inBossRoom">보스 방인가.</param>
    /// <param name="enteredRoomCount">이번 판에서 들어간 방 수(이번 방 포함).</param>
    public void SaveCheckpoint(int roomIndex, bool inBossRoom, int enteredRoomCount)
    {
        if (discarded) return;

        checkpoint = Capture(roomIndex, inBossRoom, enteredRoomCount);
        RunSaveStore.Save(checkpoint);
    }

    /// <summary>
    /// 게임을 끌 때 저장한다. 방은 처음부터 다시 하지만 체력·재 게이지는 나가는 순간의 낮은 값을 남긴다
    /// (규칙과 이유는 <see cref="RunSaveData.WithQuitState"/>). 튜토리얼 중이거나 판이 이미 끝났으면 아무것도 안 한다.
    /// </summary>
    public void SaveOnQuit()
    {
        if (!HasCheckpoint) return;

        // 죽은 뒤 결과 화면으로 넘어가기 전(1.4초)에 끄면 여기로 온다. 죽은 판을 저장하면 안 된다.
        if (runManager != null && runManager.State != RunManager.RunState.Playing) return;

        RunSaveData data = checkpoint.WithQuitState(
            health != null ? health.Current : checkpoint.health,
            ashGauge != null ? ashGauge.Current : checkpoint.ashCharge,
            runManager != null ? runManager.ElapsedSeconds : checkpoint.elapsedSeconds);

        if (RunSaveStore.Save(data))
            Debug.Log($"[저장] 게임 종료 — {data.enteredRoomCount}번째 방 입구로 저장했다(체력 {data.health}).", this);
    }

    /// <summary>판이 끝났다. 저장 파일을 지우고, 이 판에서는 더 저장하지 않는다.</summary>
    public void Discard()
    {
        discarded = true;
        checkpoint = null;
        RunSaveStore.Delete();
    }

    /// <summary>
    /// 이어하기 — 저장한 값을 각 주인에게 돌려준다. 방을 여는 일은 부른 쪽(<see cref="RoomSequenceController"/>)이 한다.
    ///
    /// <b>순서가 중요하다.</b> 유물을 먼저 돌려줘야 최대 체력 보너스가 붙고, 그 뒤에 체력을 넣어야 "최대 5 중 4"가 된다.
    /// 반대로 하면 기본 최대치(3)에 걸려 4가 3으로 잘린다.
    /// Start에서 부르므로 모든 Awake(유물 칸 초기화, 체력 가득 채우기)가 끝난 뒤라는 것이 보장된다.
    /// </summary>
    public void ApplyToRun(RunSaveData save)
    {
        if (save == null) return;

        if (inventory != null)
            inventory.Restore(ToInstances(save.bag), ToInstances(save.equipped), ToInstances(save.bossKeys));

        if (health != null) health.RestoreCurrent(save.health);
        if (ashGauge != null) ashGauge.RestoreCharge(save.ashCharge);
        if (runManager != null) runManager.RestoreProgress(save.elapsedSeconds, save.killCount, save.enteredRoomCount);

        Debug.Log($"[저장] 이어하기 — {save.enteredRoomCount}번째 방, 체력 {save.health}, 열쇠 {save.BossKeyCount}개.", this);
    }

    /// <summary>지금 상태를 파일 모양으로 옮긴다.</summary>
    private RunSaveData Capture(int roomIndex, bool inBossRoom, int enteredRoomCount)
    {
        var data = new RunSaveData
        {
            roomIndex = roomIndex,
            inBossRoom = inBossRoom,
            enteredRoomCount = enteredRoomCount,
            health = health != null ? health.Current : 0,
            ashCharge = ashGauge != null ? ashGauge.Current : 0f,
            elapsedSeconds = runManager != null ? runManager.ElapsedSeconds : 0f,
            killCount = runManager != null ? runManager.KillCount : 0,
        };

        if (inventory != null)
        {
            foreach (RelicInstance relic in inventory.Bag)
                data.bag.Add(SavedRelic.From(relic));

            for (int i = 0; i < RelicInventory.SlotCount; i++)
                data.equipped[i] = SavedRelic.From(inventory.GetEquipped(i));

            for (int i = 0; i < RelicInventory.BossSlotCount; i++)
                data.bossKeys[i] = SavedRelic.From(inventory.GetBossKey(i));
        }

        return data;
    }

    /// <summary>파일 속 유물들을 게임 속 유물로 되돌린다. 빈 칸은 빈 칸 그대로(칸 번호를 지키려고).</summary>
    private List<RelicInstance> ToInstances(IEnumerable<SavedRelic> saved)
    {
        var result = new List<RelicInstance>();
        if (saved == null) return result;

        foreach (SavedRelic item in saved)
            result.Add(ToInstance(item));

        return result;
    }

    /// <summary>
    /// 이름으로 유물 에셋을 찾아 저장한 수치를 붙인다. 다시 굴리지 않는다(RelicInstance.Roll을 부르지 않는 이유).
    /// 못 찾으면(유물 이름이 바뀜) 빈 칸으로 두고 경고만 남긴다 — 유물 하나 때문에 이어하기 전체를 버리지 않는다.
    /// </summary>
    private RelicInstance ToInstance(SavedRelic saved)
    {
        if (saved.IsEmpty) return RelicInstance.None;

        RelicData data = catalog != null ? catalog.Find(saved.id) : null;
        if (data == null)
        {
            Debug.LogWarning($"[저장] 유물 '{saved.id}'을 카탈로그에서 못 찾아 비운다(이름이 바뀌었나?).", this);
            return RelicInstance.None;
        }

        return new RelicInstance(data, saved.amount);
    }
}
