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

    /// <summary>상자를 열기 전에는 출구 판정 자체를 끈다.</summary>
    public void SetPassageEnabled(bool enabled)
    {
        consumed = false;

        if (exitTrigger == null)
            exitTrigger = GetComponent<Collider2D>();

        exitTrigger.enabled = enabled;
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
