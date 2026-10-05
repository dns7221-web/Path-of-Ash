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

    // 추가 생성(2026-10-05, 이어하기) — "이어하기 / 처음부터" 질문 창. 저장한 판이 있을 때 시작을 누르면 처음 한 번 만든다.
    private QuitConfirmDialog continueDialog;

    private void Update()
    {
        // 추가 생성 — 설정 같은 화면이 열려 있는 동안 타이틀은 입력에서 손을 뗀다.
        //
        // 이게 없으면 타이틀에서 설정을 연 순간 화면이 못 쓰게 된다. 슬라이더를 만지려고
        // 누른 아무 키나 "아무 키로 시작"에 걸려 게임이 시작되고, 설정을 닫으려고 누른
        // Esc는 아래 종료 키에 걸려 게임이 꺼진다. 둘 다 되돌릴 수 없는 동작이다.
        if (PauseGate.IsPaused) return;

        // 추가 생성(2026-10-05, 이어하기) — 이어하기 질문이 떠 있으면 타이틀은 입력에서 손을 뗀다(설정 창과 같은 이유).
        // 이게 없으면 질문을 보며 누른 아무 키가 "아무 키로 시작"에 다시 걸리고, ESC는 게임 종료에 걸린다.
        // ESC는 질문만 닫는다 — 아무것도 고르지 않은 것이라 [처음부터] 같은 동작을 하지 않는다.
        if (continueDialog != null && continueDialog.IsOpen)
        {
            Keyboard dialogKeyboard = Keyboard.current;
            if (dialogKeyboard != null && dialogKeyboard[quitKey].wasPressedThisFrame) continueDialog.Hide();
            return;
        }

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

    /// <summary>
    /// 새 판 시작. UI 버튼의 OnClick에도 연결할 수 있다.
    /// 수정(2026-10-05, 이어하기) — 저장한 판이 있으면 바로 시작하지 않고 "이어하기 / 처음부터"를 묻는다.
    /// </summary>
    public void OnStart()
    {
        if (RunSaveStore.TryLoad(out RunSaveData save))
        {
            AskContinue(save);
            return;
        }

        GameFlow.StartNewRun();
    }

    /// <summary>
    /// 추가 생성(2026-10-05, 이어하기) — 저장한 판이 어디까지 갔는지 보여 주고 이어할지 묻는다.
    ///
    /// 창은 설정 창이 만든다(<see cref="SettingsScreen.CreateDialog"/>) — 버튼 모양의 원본을 설정 창이 갖고 있어서, 같은 모양이 저절로 나온다.
    /// 설정 창을 못 찾아 질문을 못 띄우면 <b>이어하기로</b> 간다. 새 판으로 가면 저장한 판이 말없이 지워진다 —
    /// 되돌릴 수 없는 쪽을 기본값으로 두지 않는다.
    /// </summary>
    private void AskContinue(RunSaveData save)
    {
        if (continueDialog == null)
        {
            SettingsScreen settings = FindFirstObjectByType<SettingsScreen>();
            if (settings != null)
                continueDialog = settings.CreateDialog("", "이어하기", "처음부터", GameFlow.ContinueRun, GameFlow.StartNewRun);
        }

        if (continueDialog == null)
        {
            Debug.LogWarning("[타이틀] 이어하기 질문 창을 만들 수 없어 바로 이어한다(설정 창을 못 찾음).", this);
            GameFlow.ContinueRun();
            return;
        }

        // 어디서 이어지는지를 보여 준다. 숫자가 있어야 "이 판을 살릴 가치가 있나"를 고를 수 있다.
        string where = save.inBossRoom ? "보스 방" : $"{save.enteredRoomCount}번째 방";
        continueDialog.SetMessage($"진행 중인 기록이 있습니다\n{where} · 열쇠 {save.BossKeyCount}/{RelicInventory.BossSlotCount}");
        continueDialog.Show();
    }

    /// <summary>게임 종료. UI 버튼의 OnClick에도 연결할 수 있다.</summary>
    public void OnQuit()
    {
        GameFlow.Quit();
    }
}
