using NUnit.Framework;

/// <summary>
/// 추가 생성(2026-09-28, 포트폴리오 1단계) — 방 순환 테스트.
///
/// <see cref="RoomRoute"/>는 <see cref="RoomSequenceController"/>가 따르는 규칙이다 — 다음 방 번호, 빈 칸 건너뛰기,
/// 문을 나가면 어디로 가는지. 방 오브젝트 없이 규칙만 시험하므로 씬을 띄우지 않고 몇 ms 안에 끝난다.
/// 그중 "빈 칸 건너뛰기"는 예전에 방 참조 하나가 비어 있으면 <b>진행이 영구히 멈추던</b> 버그를 다시 막는다.
/// </summary>
public class RoomRouteTests
{
    /// <summary>던전 방은 무한히 순환한다 — 마지막 방 다음은 첫 방이다.</summary>
    [Test]
    public void NextIndex_WrapsFromLastRoomToFirst()
    {
        Assert.AreEqual(1, RoomRoute.NextIndex(0, 3));
        Assert.AreEqual(2, RoomRoute.NextIndex(1, 3));
        Assert.AreEqual(0, RoomRoute.NextIndex(2, 3), "마지막 다음은 첫 방");
    }

    /// <summary>아직 들어간 방이 없으면(-1) 첫 방부터, 방이 하나도 없으면 -1이다.</summary>
    [Test]
    public void NextIndex_StartsAtFirstRoom_AndHandlesNoRooms()
    {
        Assert.AreEqual(0, RoomRoute.NextIndex(-1, 3), "처음에는 첫 방");
        Assert.AreEqual(-1, RoomRoute.NextIndex(0, 0), "방이 없으면 -1");
    }

    /// <summary>비어 있는 칸은 건너뛴다(예전 진행 정지 버그). 1번에서 시작하면 1·2번이 비어 있으니 3번이다.</summary>
    [Test]
    public void FindNextValid_SkipsEmptySlots()
    {
        bool[] hasRoom = { true, false, false, true };
        Assert.AreEqual(3, RoomRoute.FindNextValid(1, hasRoom.Length, i => hasRoom[i]));
    }

    /// <summary>건너뛰다 끝에 닿으면 처음으로 돌아가서 찾는다.</summary>
    [Test]
    public void FindNextValid_WrapsAroundWhileSkipping()
    {
        bool[] hasRoom = { true, false, false };
        Assert.AreEqual(0, RoomRoute.FindNextValid(1, hasRoom.Length, i => hasRoom[i]));
    }

    /// <summary>칸이 전부 비어 있으면 -1을 돌려주고, 칸 수만큼만 확인한다(무한 루프 없음).</summary>
    [Test]
    public void FindNextValid_AllEmpty_ReturnsMinusOneWithoutLooping()
    {
        int checks = 0;
        int found = RoomRoute.FindNextValid(0, 5, i => { checks++; return false; });

        Assert.AreEqual(-1, found);
        Assert.AreEqual(5, checks, "한 바퀴(5칸)만 보고 멈춘다");
    }

    /// <summary>튜토리얼 문을 나가면 던전 첫 방부터 시작한다(튜토리얼은 순환에 끼지 않는다).</summary>
    [Test]
    public void Exit_FromTutorial_GoesToFirstDungeonRoom()
    {
        Assert.AreEqual(RoomRoute.ExitDestination.FirstDungeonRoom,
            RoomRoute.ForExit(isTutorial: true, isBossRoom: false, bossClearEndsRun: true, hasBossRoom: true, bossGateOpen: false));
    }

    /// <summary>부서진 문(열쇠를 다 모아 연 문)으로 나가면 보스 방으로 간다 — 보스로 가는 유일한 길이다.</summary>
    [Test]
    public void Exit_ThroughBrokenGate_GoesToBossRoom()
    {
        Assert.AreEqual(RoomRoute.ExitDestination.BossRoom,
            RoomRoute.ForExit(isTutorial: false, isBossRoom: false, bossClearEndsRun: true, hasBossRoom: true, bossGateOpen: true));
    }

    /// <summary>보스 방 문을 나가면 판이 끝난다 — 이 게임의 유일한 승리 조건이다.</summary>
    [Test]
    public void Exit_FromBossRoom_EndsRun()
    {
        Assert.AreEqual(RoomRoute.ExitDestination.EndRun,
            RoomRoute.ForExit(isTutorial: false, isBossRoom: true, bossClearEndsRun: true, hasBossRoom: true, bossGateOpen: false));
    }

    /// <summary>보스 방이 연결돼 있지 않으면 부서진 문이 열려도 갈 곳이 없으니 다음 방으로 간다(멈추지 않는다).</summary>
    [Test]
    public void Exit_BrokenGateWithoutBossRoom_GoesToNextRoom()
    {
        Assert.AreEqual(RoomRoute.ExitDestination.NextDungeonRoom,
            RoomRoute.ForExit(isTutorial: false, isBossRoom: false, bossClearEndsRun: true, hasBossRoom: false, bossGateOpen: true));
    }

    /// <summary>평범한 문은 다음 던전 방으로 간다.</summary>
    [Test]
    public void Exit_NormalDoor_GoesToNextRoom()
    {
        Assert.AreEqual(RoomRoute.ExitDestination.NextDungeonRoom,
            RoomRoute.ForExit(isTutorial: false, isBossRoom: false, bossClearEndsRun: true, hasBossRoom: true, bossGateOpen: false));
    }
}
