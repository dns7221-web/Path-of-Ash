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

    /// <summary>
    /// 플레이어가 들어오면 통과를 요청한다.
    ///
    /// <b>여기 있던 계측 로그는 2026-09-09에 지웠다.</b> "문이 열렸는데 안 나가진다"의 원인을
    /// 가르려고 넣었던 것이고, 원인이 확정돼서 역할이 끝났다. 원인은 조건도 배선도 아니라
    /// <b>판정에 닿을 수가 없었던 것</b>이었다 — 씬 최상위의 공유 벽이 방과 무관하게 늘 켜져
    /// 있어서, 자기 벽이 더 넓은 보스 방에서 좁은 쪽이 먼저 막았다. 지금은 방이 자기 벽을
    /// 소유하고(<c>AshRoomWallBuilder</c>) 출구 판정이 그 벽 안쪽 면에 붙어 있다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || roomDoor == null || !roomDoor.IsPassable) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        // 같은 물리 프레임에 플레이어의 여러 콜라이더가 들어와도 방 전환은 한 번만 요청한다.
        consumed = true;
        Entered?.Invoke();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 추가 생성(조사용) — <see cref="Entered"/>를 듣고 있는 곳의 수.
    ///
    /// 이 숫자가 0이면 트리거가 정확히 닿아도 화면에는 아무 일도 안 일어난다.
    /// 증상이 "안 나가진다" 하나로 판정 문제와 똑같이 보이는데, 인스펙터로도 좌표로도
    /// 눈으로도 확인할 방법이 없어서 조사용 패널에 숫자로 내보낸다.
    /// </summary>
    public int DebugListenerCount => Entered == null ? 0 : Entered.GetInvocationList().Length;

    /// <summary>
    /// 추가 생성(조사용) — 트리거를 건너뛰고 통과를 직접 일으킨다.
    ///
    /// <b>왜 필요한가.</b> 출구 판정과 <b>그 뒤의 길</b>(방 진행 → 클리어 판정 → 결과 화면)을
    /// 따로 확인할 수 있어야 한다. 둘이 한 덩어리로 묶여 있으면 어딘가 막혔을 때 앞이 문제인지
    /// 뒤가 문제인지 가를 수가 없다. 2026-09-07에 실제로 그랬다 — 판정에 닿지를 못해서
    /// 승리 흐름 전체를 한 번도 못 밟아봤고, 이 버튼으로 뒷길부터 따로 확인했다.
    ///
    /// 판정이 고쳐진 지금도 남겨둔다. 보스까지 17방을 걸어야 하는 게임이라 승리 흐름을
    /// 손보려면 매번 한 판이 통째로 든다. 디버그 패널의 <b>보스 방으로</b> 버튼과 같은 이유다.
    ///
    /// <b>왜 조건을 무시하고 부르는가.</b> 이 함수의 목적은 "조건이 맞는지"가 아니라
    /// "조건이 맞았다고 치면 뒤가 굴러가는지"다. 조건 검사를 그대로 두면 막힌 지점을
    /// 건너뛰지 못해서 아무것도 못 가른다. 대신 지금 상태를 <b>전부 로그로 남겨</b>
    /// 무엇을 건너뛴 것인지 기록에 남게 했다.
    ///
    /// <b>구독자 수를 찍는 이유가 이 함수의 핵심이다.</b> <see cref="Entered"/>를 아무도
    /// 듣고 있지 않으면 트리거가 아무리 정확히 닿아도 화면에는 아무 일도 안 일어난다.
    /// 그리고 그 상태는 인스펙터로도, 콜라이더 좌표로도, 눈으로도 보이지 않는다 —
    /// 증상이 "안 나가진다" 하나로 똑같아서 판정 문제와 구별할 방법이 여태 없었다.
    /// </summary>
    public void ForceEnterForDebug()
    {
        int listeners = Entered == null ? 0 : Entered.GetInvocationList().Length;

        if (exitTrigger == null)
            exitTrigger = GetComponent<Collider2D>();

        string colliderState = exitTrigger == null
            ? "콜라이더 없음"
            : $"enabled={exitTrigger.enabled}, isTrigger={exitTrigger.isTrigger}, " +
              $"layer={gameObject.layer}({LayerMask.LayerToName(gameObject.layer)})";

        string doorState = roomDoor == null ? "roomDoor 참조 없음" : roomDoor.State.ToString();

        // 한 줄로 이어 찍는 이유: 콘솔은 로그 하나를 한 덩어리로 접어서 보여준다.
        // Debug.Log를 네 번 나눠 부르면 다른 로그가 사이에 끼어들어 순서가 섞인다.
        Debug.Log($"[출구/강제] {name} — 통과를 강제로 일으킨다.\n" +
                  $"  구독자 {listeners}명  (0이면 아무도 안 듣는 것이다 — 여기가 원인이다)\n" +
                  $"  판정: {colliderState}\n" +
                  $"  문: {doorState}, consumed={consumed}", this);

        if (listeners == 0)
        {
            // 여기서 멈추지 않고 그냥 알린다. 구독자가 없으면 Invoke가 아무 일도 안 하므로
            // 위험하지 않고, 사람이 로그를 못 보고 지나쳐도 이 경고는 눈에 띈다.
            Debug.LogWarning($"[출구/강제] {name}의 Entered를 듣는 곳이 없다. " +
                             "RoomController.OnEnable이 구독하기 전이거나 exitTrigger 참조가 " +
                             "다른 것을 가리키고 있다.", this);
        }

        consumed = true;
        Entered?.Invoke();
    }
#endif
}
