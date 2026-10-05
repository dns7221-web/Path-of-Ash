using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — 진행 중인 한 판을 파일로 남기는 데이터. <see cref="RunSaveStore"/>가 JSON으로 읽고 쓴다.
///
/// <b>무엇을 담는가 — "방 입구에 다시 세울 수 있는 만큼만".</b> 이어하기는 나간 방의 <b>처음부터</b> 다시 시작한다(사용자 결정).
/// 그래서 적의 위치·체력, 투사체, 스킬 쿨타임, 플레이어 위치는 담지 않는다. 방에 들어가면 어차피 새로 정해지는 값이다.
/// 담는 것은 방을 다시 열어도 저절로 돌아오지 않는 것뿐이다 — 몇 번째 방인지, 체력, 재 게이지, 유물, 판 기록.
///
/// <b>왜 JsonUtility인가.</b> 유니티 내장 직렬화라 패키지가 필요 없고, [Serializable] 클래스의 public 필드를 그대로 옮긴다.
/// Dictionary를 못 담는 제약이 있어서 유물은 목록과 고정 길이 배열로 담는다(장착 3칸, 열쇠 4칸).
/// PlayerPrefs를 쓰지 않는 이유: 유물 목록 같은 배열을 키 수십 개로 쪼개야 하고, 레지스트리는 구조 있는 데이터를 담는 곳이 아니다.
///
/// 필드를 public으로 둔 이유: JsonUtility가 읽고 쓰는 것이 public 필드(또는 [SerializeField])다. 게임 코드는 이 클래스를
/// 저장·복원 때만 만지고, 판 도중의 진짜 상태는 여전히 RunManager·RelicInventory 같은 주인이 갖는다.
/// </summary>
[Serializable]
public class RunSaveData
{
    /// <summary>
    /// 저장 형식 번호. 필드의 뜻이나 구성을 바꾸면 올린다.
    /// 번호가 다른 파일은 <see cref="RunSaveStore.TryLoad(out RunSaveData)"/>가 버린다 — 옛 형식을 억지로 읽으면 빈 값이
    /// 섞인 채로 이어져서 "유물이 사라졌다" 같은 원인 모를 문제가 된다. 버리고 처음부터가 낫다.
    /// </summary>
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;

    // ───── 방 진행 ─────

    /// <summary>던전 방 배열(<c>RoomSequenceController.rooms</c>)에서의 위치. 보스 방이면 보스 방에 들어가기 직전 방의 위치.</summary>
    public int roomIndex = -1;

    /// <summary>보스 방에 있었는가. 보스 방은 배열 밖이라 위치 번호만으로는 가리킬 수 없다.</summary>
    public bool inBossRoom;

    /// <summary>이번 판에서 들어간 방 수(튜토리얼 제외). 결과 화면의 "몇 번째 방까지"이고 이어하기 질문에도 쓴다.</summary>
    public int enteredRoomCount;

    // ───── 플레이어 ─────

    public int health;
    public float ashCharge;

    // ───── 판 기록(결과 화면) ─────

    public float elapsedSeconds;
    public int killCount;

    // ───── 유물 ─────

    public List<SavedRelic> bag = new List<SavedRelic>();
    public SavedRelic[] equipped = new SavedRelic[RelicInventory.SlotCount];
    public SavedRelic[] bossKeys = new SavedRelic[RelicInventory.BossSlotCount];

    /// <summary>모은 보스 열쇠 수. 이어하기 질문("열쇠 2/4")에 쓴다.</summary>
    public int BossKeyCount
    {
        get
        {
            int count = 0;
            if (bossKeys == null) return 0;
            foreach (SavedRelic key in bossKeys)
                if (!key.IsEmpty) count++;
            return count;
        }
    }

    /// <summary>
    /// 게임을 끌 때 저장할 값. 방 입구에서 남긴 체크포인트(this)를 바탕으로, 나가는 순간의 값 중 <b>플레이어에게 불리한 쪽</b>을 고른다.
    ///
    /// <b>왜 낮은 쪽인가(사용자 결정 — 나갈 때 체력 유지).</b> 이어하면 방을 처음부터 다시 하니까, 입구 때 체력을 그대로 돌려주면
    /// "죽기 직전에 끄고 다시 켜서 체력 되돌리기"가 된다. 낮은 쪽을 고르면 끄고 켜서 얻는 것이 없다.
    /// 재 게이지도 같은 이유다 — 궁극기를 쓰고 끈 뒤 다시 켜면 게이지가 차 있는 일이 없어야 한다.
    ///
    /// 처치 수는 입구 때 값을 쓴다 — 방을 다시 하면서 같은 적을 또 잡으므로 지금 값을 쓰면 두 번 센다.
    /// 시간은 흐른 만큼 센다(큰 쪽) — 실제로 플레이한 시간이다.
    ///
    /// 체크포인트 자체는 바꾸지 않고 새 사본을 돌려준다. 같은 판에서 나가기를 눌렀다가 취소해도 체크포인트가 깎이지 않게.
    /// </summary>
    public RunSaveData WithQuitState(int currentHealth, float currentAshCharge, float currentElapsedSeconds)
    {
        // JSON을 한 번 거쳐 깊은 복사를 한다. MemberwiseClone은 유물 목록을 같이 가리켜서, 사본을 고치면 원본도 바뀐다.
        RunSaveData copy = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(this));
        copy.health = Mathf.Min(health, currentHealth);
        copy.ashCharge = Mathf.Min(ashCharge, currentAshCharge);
        copy.elapsedSeconds = Mathf.Max(elapsedSeconds, currentElapsedSeconds);
        return copy;
    }
}

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — 저장 파일 속 유물 하나. <see cref="RelicInstance"/>의 파일용 모양이다.
///
/// 유물 에셋(<see cref="RelicData"/>) 자체는 파일에 넣을 수 없어서 <b>에셋 이름</b>을 담고, 불러올 때
/// <see cref="RelicCatalog"/>에서 이름으로 다시 찾는다. 수치(amount)는 주울 때 굴린 값을 그대로 담는다 —
/// 다시 굴리면 "끄고 켜서 왕의 주사위 다시 굴리기"가 된다(RelicInstance 주석의 주사위 이야기와 같은 이유).
/// </summary>
[Serializable]
public struct SavedRelic
{
    /// <summary>유물 에셋 이름(예: Relic_AshKey). 비어 있으면 빈 칸.</summary>
    public string id;

    /// <summary>주울 때 정해진 수치.</summary>
    public float amount;

    public bool IsEmpty => string.IsNullOrEmpty(id);

    /// <summary>게임 속 유물 하나를 파일용으로 바꾼다. 빈 칸은 빈 칸(id 없음)이 된다.</summary>
    public static SavedRelic From(RelicInstance relic)
    {
        if (relic.IsEmpty) return default;
        return new SavedRelic { id = relic.Data.name, amount = relic.Amount };
    }
}
