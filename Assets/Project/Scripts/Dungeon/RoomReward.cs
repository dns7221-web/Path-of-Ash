using System;
using UnityEngine;

/// <summary>
/// 추가 생성 — 한 방의 "보상" 역할을 추상화한다.
///
/// 왜 만들었나: <see cref="RoomController"/>가 <see cref="RewardChest"/>라는 구체 타입을 직접
/// 들고 있어서 보상이 상자뿐이었다. 보스 방은 상자가 아니라 클리어 전용 유물을 떨어뜨리고,
/// 그걸 <b>주웠을 때</b> 문이 열려야 한다.
///
/// 방이 굴러가는 순서는 두 경우가 완전히 같다 — 전투가 끝나면 보상이 나타나고,
/// 그 보상을 챙기면 문이 열린다. 달라지는 건 "보상이 무엇이고 어떻게 챙기는가"뿐이라
/// 그 두 가지만 여기로 뽑았다.
///
/// <see cref="RoomEncounter"/>와 같은 이유로 인터페이스가 아니라 MonoBehaviour 추상 클래스다.
/// 유니티 인스펙터가 인터페이스 필드를 직렬화하지 못한다.
/// </summary>
public abstract class RoomReward : MonoBehaviour
{
    /// <summary>플레이어가 보상을 실제로 챙겼을 때 발생한다. 방은 이걸 받아 문을 연다.</summary>
    public event Action Claimed;

    /// <summary>
    /// 방 진행 상태에 맞춰 보상을 내놓거나 거둔다.
    /// false로 되돌릴 때 처음 상태로 초기화해야 방을 재사용해도 안전하다.
    /// </summary>
    public abstract void SetAvailable(bool available);

    /// <summary>파생 클래스가 보상 획득을 알릴 때 쓴다.</summary>
    protected void RaiseClaimed() => Claimed?.Invoke();
}
