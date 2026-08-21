using System;
using UnityEngine;

/// <summary>
/// 추가 생성 — 한 방의 "전투" 역할을 추상화한다.
///
/// 왜 만들었나: <see cref="RoomController"/>가 <see cref="EnemySpawner"/>라는 구체 타입을 직접
/// 들고 있어서 잡몹 방밖에 만들 수 없었다. 보스는 EnemyWraith가 아니라 EnemyBoss라 그 스포너에
/// 아예 꽂히지 않는다. 그런데 방이 굴러가는 순서(전투 → 상자 → 문 → 출구)는 잡몹 방이든
/// 보스 방이든 완전히 같다. 그래서 방마다 달라지는 부분인
/// <b>"전투를 시작한다 / 전투가 끝났다"</b> 두 가지만 여기로 뽑았다.
///
/// 인터페이스가 아니라 MonoBehaviour 추상 클래스로 만든 이유:
/// 유니티 인스펙터는 인터페이스 필드를 직렬화하지 못해서 참조를 드래그로 못 꽂는다.
/// 추상 클래스면 그대로 필드 타입으로 쓸 수 있고, 이미 씬에 꽂혀 있는 EnemySpawner 참조도
/// fileID로 저장돼 있어서 타입만 넓히면 그대로 유지된다.
/// </summary>
public abstract class RoomEncounter : MonoBehaviour
{
    /// <summary>이 방의 적을 모두 처치했을 때 발생한다.</summary>
    public event Action EncounterCleared;

    /// <summary>
    /// 이 방의 전투를 시작한다.
    /// 방을 껐다 켜서 재사용할 때는 Start가 다시 불리지 않으므로 진행 관리자가 직접 부른다.
    /// </summary>
    public abstract void BeginEncounter();

    /// <summary>
    /// 파생 클래스가 전투 종료를 알릴 때 쓴다.
    /// 이벤트는 선언한 클래스 밖에서 Invoke할 수 없어서 이 통로가 필요하다.
    /// </summary>
    protected void RaiseEncounterCleared() => EncounterCleared?.Invoke();
}
