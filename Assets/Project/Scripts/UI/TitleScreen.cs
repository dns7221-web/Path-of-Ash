using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 타이틀 화면의 진입점. 하는 일은 "시작"과 "종료" 두 개뿐이다.
///
/// 로직이 없는 이유: 타이틀은 상태를 갖지 않는다. 아무것도 기억하지 않고 다음 씬으로 넘길 뿐이다.
/// 버튼의 OnClick에서 GameFlow를 직접 부를 수 없어서(static 클래스는 인스펙터에 못 끌어다 놓는다)
/// 이 컴포넌트가 그 사이를 이어준다.
/// </summary>
public class TitleScreen : MonoBehaviour
{
    // 추가 생성: 시작 버튼 대신 "아무 곳이나 클릭"으로 바꾸면서 들어온 옵션.
    [Header("시작 입력")]
    [Tooltip("켜면 아무 키나 클릭으로 시작한다. 끄면 버튼의 OnClick으로만 시작한다.")]
    [SerializeField] private bool startOnAnyInput = true;

    [Header("키 입력")]
    [Tooltip("게임을 종료하는 키. '아무 키'로 시작하게 해두면 이 키는 예외로 빠져야 한다.")]
    [SerializeField] private Key quitKey = Key.Escape;

    private void Update()
    {
        if (!startOnAnyInput) return;

        // 종료 키를 먼저 본다.
        // 순서가 중요하다 — 아무 키로 시작하게 해두면 Esc도 "아무 키"에 걸려서,
        // 종료하려고 누른 키가 게임을 시작시키는 상황이 된다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[quitKey].wasPressedThisFrame)
        {
            OnQuit();
            return;
        }

        if (IsStartInput()) OnStart();
    }

    /// <summary>
    /// 시작으로 볼 입력이 이번 프레임에 들어왔는지 판단한다.
    /// 마우스 왼쪽 클릭 또는 아무 키나.
    /// </summary>
    private bool IsStartInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            // UI 위를 클릭한 경우는 그 UI가 처리하게 두고 여기서는 무시한다.
            // 이게 없으면 나중에 설정 버튼 같은 걸 놨을 때, 그 버튼을 눌러도
            // "아무 데나 클릭"으로 함께 잡혀서 게임이 시작되어 버린다.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;

            return true;
        }

        // 키보드는 아무 키나. anyKey는 유니티가 제공하는 컨트롤이라 키를 하나씩 훑지 않아도 된다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

        return false;
    }

    /// <summary>새 판 시작. UI 버튼의 OnClick에도 연결할 수 있다.</summary>
    public void OnStart()
    {
        GameFlow.StartNewRun();
    }

    /// <summary>게임 종료. UI 버튼의 OnClick에도 연결할 수 있다.</summary>
    public void OnQuit()
    {
        GameFlow.Quit();
    }
}
