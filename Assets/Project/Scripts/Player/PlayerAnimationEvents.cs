using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-21, 왕관 의식) — 플레이어 애니메이션 클립에 박힌 <b>애니메이션 이벤트</b>를 받아
/// <see cref="PlayerController"/>로 넘긴다.
///
/// <b>왜 따로 있어야 하나.</b> 유니티는 애니메이션 이벤트를 <b>Animator가 붙은 오브젝트</b>의 스크립트에서
/// <b>이름이 같은 함수</b>로 찾아 부른다. 지금은 Animator가 PlayerController와 같은 루트에 있지만,
/// PlayerController는 Animator를 GetComponentInChildren으로 찾으므로 그림을 자식으로 옮겨도 동작하게
/// 짜여 있다. 받는 쪽을 따로 두고 GetComponentInParent로 올라가면 어느 배치에서도 닿는다.
/// 또 PlayerController에는 같은 이름의 C# 이벤트(RelicsTornOut)가 있어서 같은 이름의 함수를 둘 수 없고,
/// 받는 함수를 public으로 열면 아무 코드나 불러 유물을 튀어나오게 할 수 있다.
/// 받을 곳이 없으면 유니티가 "AnimationEvent has no receiver" 오류를 매번 찍는다.
/// (2026-09-21 수정 — 처음 주석은 "Animator가 자식에 있다"고 적었는데, 프리팹을 보니 루트에 있었다.)
///
/// <b>왜 타이머가 아니라 애니메이션 이벤트인가.</b> "유물 뽑힘" 모션의 5번째 장(몸을 젖히는 장면)과
/// 유물 네 개가 튀어나오는 순간은 <b>반드시 같은 프레임</b>이어야 한다(사용자 요청). 연출 쪽에 1.1초를
/// 따로 적어 두면 클립의 프레임 길이를 고치는 순간 둘이 어긋난다. 이벤트는 클립 안에 박혀 있어서
/// 프레임 길이를 바꿔도 그 장면과 함께 움직인다.
///
/// 이벤트 이름(아래 공개 함수 이름)은 <c>AshPlayerDirectionalAnimationBuilder</c>의 동작 표에 적힌
/// 함수 이름과 같아야 한다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAnimationEvents : MonoBehaviour
{
    private PlayerController player;

    private void Awake()
    {
        player = GetComponentInParent<PlayerController>();
    }

    /// <summary>애니메이션 이벤트 — "유물 뽑힘" 5번째 장. 유물이 몸에서 튀어나오는 순간.</summary>
    public void RelicsTornOut()
    {
        if (player != null) player.HandleRelicsTornOut();
    }

    /// <summary>애니메이션 이벤트 — 연출 모션의 마지막(주저앉은 장을 다 보여준 뒤). 조작을 돌려준다.</summary>
    public void ScriptedPoseEnd()
    {
        if (player != null) player.HandleScriptedPoseEnd();
    }
}
