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

#if UNITY_EDITOR
        // 추가 생성(계측) — 출구가 열렸는데 통과가 안 되는 경우를 가르기 위한 것.
        //
        // 왜 필요한가: 문이 열려도 나가지지 않을 때 원인 후보가 셋인데 증상이 똑같다.
        // ① 판정이 안 켜졌다 ② 켜졌는데 플레이어가 거기까지 못 간다 ③ 닿았는데 조건에서 걸린다.
        // 로그가 없으면 셋을 구별할 방법이 없고, 실제로 보스 방에서 그 상황이 났다.
        //
        // 판정 상자의 <b>월드 좌표</b>를 같이 찍는다. 방마다 규격이 달라서(던전 52x29,
        // 보스 방 39x39) 로컬 좌표만으로는 벽 안쪽인지 바깥인지 알 수 없다.
        if (passable)
        {
            Bounds b = exitTrigger.bounds;
            Debug.Log($"[출구/계측] {name} 통과 판정 켜짐 — " +
                      $"x {b.min.x:0.0}~{b.max.x:0.0}, y {b.min.y:0.0}~{b.max.y:0.0}", this);
        }
#endif
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
#if UNITY_EDITOR
        // 추가 생성(계측) — 무엇이 들어왔고 왜 거절됐는지 남긴다.
        //
        // 플레이어가 아닌 것(적, 투사체)까지 찍으면 시끄러우므로 플레이어일 때만 남긴다.
        // 거절 사유를 따로 적는 이유: "안 나가진다"는 증상 하나에 원인이 셋이라
        // 어느 조건에서 돌아섰는지가 곧 답이다.
        if (other.GetComponentInParent<PlayerController>() != null)
        {
            if (consumed)
                Debug.Log($"[출구/계측] {name} — 이미 통과 처리됨(consumed).", this);
            else if (roomDoor == null)
                Debug.LogWarning($"[출구/계측] {name} — roomDoor 참조가 비어 있다.", this);
            else if (!roomDoor.IsPassable)
                Debug.Log($"[출구/계측] {name} — 문이 {roomDoor.State}라 통과 불가.", this);
            else
                Debug.Log($"[출구/계측] {name} — 플레이어 진입, 통과 처리한다.", this);
        }
#endif

        if (consumed || roomDoor == null || !roomDoor.IsPassable) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        // 같은 물리 프레임에 플레이어의 여러 콜라이더가 들어와도 방 전환은 한 번만 요청한다.
        consumed = true;
        Entered?.Invoke();
    }
}
