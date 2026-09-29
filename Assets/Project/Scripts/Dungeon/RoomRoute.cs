using System;

/// <summary>
/// 추가 생성(2026-09-28, 포트폴리오 1단계 테스트) — 방 진행에서 "어디로 가나"를 정하는 규칙만 모은 순수 C# 클래스.
///
/// <b>왜 <see cref="RoomSequenceController"/>에서 빼냈나.</b> 컨트롤러는 방 오브젝트를 켜고 끄고, 플레이어를 옮기고,
/// 판을 끝내는 <b>실행</b>을 맡는다. 그 안에 섞여 있던 <b>판단</b>(다음 방 번호, 문을 나가면 어디로)을 여기로 옮기면
/// 씬·방 오브젝트 없이도 규칙을 테스트로 지킬 수 있다(PathOfAsh.Tests.EditMode의 RoomRouteTests).
/// 컨트롤러의 동작은 옮기기 전과 같다 — 판단 순서와 조건을 그대로 옮겼다.
///
/// MonoBehaviour가 아닌 정적 클래스인 이유: 상태가 없다. 필요한 사실(어느 방인가, 문이 열렸나)은 부르는 쪽이 넘긴다.
/// </summary>
public static class RoomRoute
{
    /// <summary>방 문을 나갔을 때 갈 곳.</summary>
    public enum ExitDestination
    {
        /// <summary>튜토리얼을 나왔다 — 던전 첫 방부터 시작한다.</summary>
        FirstDungeonRoom,

        /// <summary>보스 방 문을 나왔다 — 판이 끝난다(클리어).</summary>
        EndRun,

        /// <summary>부서진 문을 지났다 — 보스 방으로 간다.</summary>
        BossRoom,

        /// <summary>평범한 문 — 다음 던전 방으로 간다.</summary>
        NextDungeonRoom,
    }

    /// <summary>
    /// 문을 나간 방이 어떤 방이었는지로 다음 갈 곳을 정한다.
    ///
    /// <b>검사 순서가 곧 우선순위다</b> — 튜토리얼 → 보스 방(클리어) → 부서진 문 → 평범한 문.
    /// 보스 방 문을 나가는 것이 판을 끝내는 유일한 길이고, 보스로 가는 유일한 길은 부서진 문이다
    /// (예전의 "N번째 방마다 보스"는 걷어냈다 — 이유는 RoomSequenceController.AdvanceToNextRoom 주석).
    /// </summary>
    /// <param name="isTutorial">나간 방이 튜토리얼 방인가.</param>
    /// <param name="isBossRoom">나간 방이 보스 방인가.</param>
    /// <param name="bossClearEndsRun">보스 방을 나가면 판을 끝내는 설정인가(끄면 보스 방도 평범한 방처럼 다음으로 간다).</param>
    /// <param name="hasBossRoom">씬에 보스 방이 연결돼 있는가. 없으면 부서진 문이 열려 있어도 갈 곳이 없다.</param>
    /// <param name="bossGateOpen">나간 방의 문이 보스로 가는 부서진 문이었는가(방이 판단한 값).</param>
    public static ExitDestination ForExit(bool isTutorial, bool isBossRoom, bool bossClearEndsRun,
                                          bool hasBossRoom, bool bossGateOpen)
    {
        if (isTutorial) return ExitDestination.FirstDungeonRoom;
        if (isBossRoom && bossClearEndsRun) return ExitDestination.EndRun;
        if (hasBossRoom && bossGateOpen) return ExitDestination.BossRoom;

        return ExitDestination.NextDungeonRoom;
    }

    /// <summary>
    /// 지금 방 다음 번호. 마지막 방 다음은 첫 방이다(던전 방은 무한히 순환한다).
    /// 아직 들어간 방이 없으면(-1) 첫 방(0)이 나온다. 방이 하나도 없으면 -1.
    /// </summary>
    public static int NextIndex(int current, int count)
        => count <= 0 ? -1 : (current + 1) % count;

    /// <summary>
    /// start부터 순환하며 쓸 수 있는 첫 방 번호를 찾는다. 없으면 -1.
    ///
    /// <b>배열 길이만큼만 도는 이유:</b> 전부 비어 있으면 무한 루프에 빠진다. 한 바퀴를 다 돌고도 못 찾으면
    /// 쓸 수 있는 방이 없다는 뜻이다. 빈 칸을 건너뛰는 이유는 RoomSequenceController.ActivateRoom 주석(진행 정지 버그).
    /// </summary>
    /// <param name="start">찾기 시작할 번호. 범위를 벗어나도 순환해서 맞춘다.</param>
    /// <param name="count">방 칸 개수.</param>
    /// <param name="isValid">그 번호의 방을 쓸 수 있는가. 부르는 쪽이 빈 칸 경고 같은 일을 여기서 해도 된다.</param>
    public static int FindNextValid(int start, int count, Func<int, bool> isValid)
    {
        if (count <= 0 || isValid == null) return -1;

        for (int step = 0; step < count; step++)
        {
            // 음수 start도 받을 수 있게 한 번 더 더해서 나눈다(C#의 %는 음수를 그대로 음수로 돌려준다).
            int candidate = ((start + step) % count + count) % count;
            if (isValid(candidate)) return candidate;
        }

        return -1;
    }
}
