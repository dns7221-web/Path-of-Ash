using System;
using UnityEngine;

/// <summary>
/// 추가 생성 — 열린 문 안쪽에 놓는 출구 트리거다.
/// 문이 실제로 열린 상태에서 플레이어가 들어왔을 때만 다음 방을 요청한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class RoomExitTrigger : MonoBehaviour
{
    [SerializeField] private RoomDoorState roomDoor;

    private Collider2D exitTrigger;
    private bool consumed;

    /// <summary>플레이어가 유효한 출구에 들어왔을 때 발생한다.</summary>
    public event Action Entered;

    private void Awake()
    {
        exitTrigger = GetComponent<Collider2D>();
        exitTrigger.isTrigger = true;
    }

    /// <summary>
    /// 상자를 열기 전에는 출구 판정 자체를 끈다.
    ///
    /// 수정(이름 가림): 파라미터 이름이 <c>enabled</c>였다. 컴파일은 되지만 MonoBehaviour가
    /// 원래 갖고 있는 <c>Behaviour.enabled</c>(이 컴포넌트를 켜고 끄는 값)를 가려버려서,
    /// 나중에 이 함수 안에서 컴포넌트를 끄려고 <c>enabled = false</c>라고 쓰면 파라미터에
    /// 대입되고 아무 일도 일어나지 않는다. 에러도 경고도 안 뜬다.
    /// </summary>
    /// <param name="passable">true면 통과 판정을 켠다.</param>
    public void SetPassageEnabled(bool passable)
    {
        consumed = false;

        // Awake보다 먼저 불릴 수 있다 — 방 진행 관리자가 방이 꺼진 상태에서 초기화하기 때문이다.
        // 비활성 오브젝트에서도 GetComponent는 동작하므로 여기서 한 번 더 확보한다.
        if (exitTrigger == null)
            exitTrigger = GetComponent<Collider2D>();

        exitTrigger.enabled = passable;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || roomDoor == null || !roomDoor.IsPassable) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        // 같은 물리 프레임에 플레이어의 여러 콜라이더가 들어와도 방 전환은 한 번만 요청한다.
        consumed = true;
        Entered?.Invoke();
    }
}
