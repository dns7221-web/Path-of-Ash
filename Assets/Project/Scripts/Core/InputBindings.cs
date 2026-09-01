using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — 게임의 모든 조작 액션을 한 곳에서 만들고 소유한다.
///
/// <b>왜 모았나.</b> 예전에는 액션이 컴포넌트마다 흩어져 있었다. 이동·대시는
/// <see cref="PlayerController"/>가, 스킬 다섯은 <see cref="SkillController"/>가, 화면 키는
/// 각 화면이 각자 만들었다. 그 구조에서는 설정 화면이 키를 바꿀 방법이 없다. 바꾸려면
/// 씬에 있는 그 컴포넌트를 찾아내야 하는데, 컴포넌트는 씬과 함께 생겼다 사라지고
/// 상자처럼 <b>여러 개인 것도 있어서</b> 어느 것을 바꿔야 하는지 답이 없다.
/// 조작 하나에는 액션 하나가 맞고, 그 하나가 여기 있다.
///
/// <b>전에는 인스펙터에서 키를 바꿀 수 있었다.</b> 액션이 [SerializeField]라 프리팹에
/// 저장됐기 때문이다. 그 자리는 이제 설정 화면이 가져간다. 두 곳에서 바꿀 수 있으면
/// "지금 어느 키가 맞는지"를 프리팹과 저장값 중 무엇으로 판단할지 정할 수 없다.
///
/// <b>맵을 켜두고 안 끈다.</b> 액션이 켜져 있어도 읽는 쪽이 없으면 아무 일도 안 일어난다.
/// 반대로 컴포넌트마다 켜고 끄면, 상자처럼 여럿인 것에서 하나가 꺼질 때 나머지까지
/// 먹통이 되는 실수가 나온다. 켜고 끄는 판단은 읽는 쪽(Update가 도는지)이 이미 하고 있다.
/// </summary>
public static class InputBindings
{
    // ── 액션 이름 ─────────────────────────────────────────────────────────
    //
    // 문자열을 상수로 두는 이유: 오타를 내면 FindAction이 null을 돌려주는데, null은
    // 그 자리에서 안 터지고 "그 키만 안 먹는" 형태로 나중에 드러난다.

    public const string Move = "Move";
    public const string Dash = "Dash";
    public const string BasicAttack = "BasicAttack";
    public const string Skill1 = "Skill1";
    public const string Skill2 = "Skill2";
    public const string Skill3 = "Skill3";
    public const string Skill4 = "Skill4";
    public const string OpenChest = "OpenChest";
    public const string Inventory = "Inventory";
    public const string BossKeys = "BossKeys";
    public const string Settings = "Settings";
    public const string Restart = "Restart";

    /// <summary>
    /// 스킬 슬롯 순서. <see cref="SkillController"/>의 슬롯 번호와 같은 순서여야 한다 —
    /// 0번이 기본 공격, 1~4번이 Q/W/E/R다.
    /// </summary>
    public static readonly string[] SkillSlotIds =
    {
        BasicAttack, Skill1, Skill2, Skill3, Skill4
    };

    /// <summary>
    /// 키 변경을 저장하는 자리.
    ///
    /// <see cref="Build"/>에서 바인딩 순서를 바꾸면 저장된 값이 엉뚱한 자리에 붙는다.
    /// 그때는 이 이름 뒤의 숫자를 올려서 옛 저장값을 버려야 한다.
    /// </summary>
    private const string OverridesKey = "Ash.Input.Overrides.v2";

    /// <summary>저장 문자열의 칸 구분자. 바인딩 경로에는 안 나오는 글자여야 한다.</summary>
    private const char Separator = '|';

    /// <summary>저장 문자열의 줄 구분자(줄바꿈).</summary>
    private const char LineBreak = (char)10;

    private static InputActionMap map;

    // ── 꺼내 쓰기 ─────────────────────────────────────────────────────────

    /// <summary>이름으로 액션을 꺼낸다. 아직 안 만들어졌으면 여기서 만든다.</summary>
    public static InputAction Get(string id)
    {
        EnsureBuilt();

        InputAction action = map.FindAction(id);
        if (action == null)
            Debug.LogError($"[입력] '{id}' 액션이 없다. InputBindings.Build에 빠졌거나 이름이 틀렸다.");

        return action;
    }

    public static InputAction MoveAction => Get(Move);
    public static InputAction DashAction => Get(Dash);
    public static InputAction OpenChestAction => Get(OpenChest);
    public static InputAction InventoryAction => Get(Inventory);
    public static InputAction BossKeysAction => Get(BossKeys);
    public static InputAction SettingsAction => Get(Settings);
    public static InputAction RestartAction => Get(Restart);

    /// <summary>
    /// 바인딩이 바뀌었을 때 알린다. 키를 글자로 보여주는 UI가 이걸 듣고 다시 그린다.
    ///
    /// 이게 없으면 스킬바나 튜토리얼 안내가 <b>화면을 다시 켤 때까지</b> 옛 키를 말한다.
    /// 설정 창에서 키를 바꾸고 닫으면 그 자리에서 맞아야 한다.
    /// </summary>
    public static event System.Action BindingsChanged;

    /// <summary>스킬 슬롯의 액션. 0이 기본 공격, 1~4가 Q/W/E/R.</summary>
    public static InputAction SkillAction(int slot)
    {
        if (slot < 0 || slot >= SkillSlotIds.Length) return null;
        return Get(SkillSlotIds[slot]);
    }

    // ── 만들기 ────────────────────────────────────────────────────────────

    private static void EnsureBuilt()
    {
        if (map != null) return;

        Build();
        LoadOverrides();

        // 만든 즉시 켠다. 끄는 시점은 없다 — 위 주석의 이유.
        map.Enable();
    }

    /// <summary>
    /// 액션과 기본 바인딩을 만든다.
    ///
    /// 표(배열)로 돌리지 않고 한 줄씩 적은 이유: 이동은 키 넷을 묶는 컴포지트고, 스킬은
    /// 키보드와 패드를 하나씩 갖고, 상자는 패드가 없다. 모양이 다 달라서 표로 만들면
    /// 표의 칸이 예외투성이가 되고, 그러면 표를 읽는 것이 코드를 읽는 것보다 어려워진다.
    /// </summary>
    private static void Build()
    {
        map = new InputActionMap("Ash");

        // 이동 — 2DVector 컴포지트는 키 네 개를 Vector2 하나로 묶어주는 유니티 내장 바인딩이다.
        // 키를 하나씩 읽어서 직접 벡터를 조립하지 않는 이유가 이거다.
        //
        // WASD가 없는 이유: 스킬을 Q/W/E/R에 두면서 W가 "위로 이동"과 정면으로 겹쳤다.
        // 둘 다 남기면 위로 걸을 때마다 스킬이 나간다. 스킬 배치가 기획이 정한 것이라
        // 이동을 방향키로 옮겼다.
        // AddAction의 매개변수 이름은 InputAction 생성자와 다르다 —
        // 생성자는 expectedControlType, 이쪽은 expectedControlLayout이다.
        InputAction move = map.AddAction(Move, InputActionType.Value, expectedControlLayout: "Vector2");
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        // 게임패드는 스틱 하나가 이미 Vector2라 컴포지트가 필요 없다.
        move.AddBinding("<Gamepad>/leftStick");

        // 대시 — 눌린 순간에만 반응하면 되므로 Button이다.
        InputAction dash = map.AddAction(Dash, InputActionType.Button);
        dash.AddBinding("<Keyboard>/leftShift");
        dash.AddBinding("<Keyboard>/rightShift");
        dash.AddBinding("<Gamepad>/buttonSouth");

        // 기본 공격 — 항상 쓰는 평타라 손가락이 늘 닿아 있는 Ctrl에 둔다.
        InputAction attack = map.AddAction(BasicAttack, InputActionType.Button);
        attack.AddBinding("<Keyboard>/leftCtrl");
        attack.AddBinding("<Keyboard>/rightCtrl");
        attack.AddBinding("<Gamepad>/buttonWest");

        AddButton(Skill1, "<Keyboard>/q", "<Gamepad>/buttonNorth");
        AddButton(Skill2, "<Keyboard>/w", "<Gamepad>/buttonEast");
        AddButton(Skill3, "<Keyboard>/e", "<Gamepad>/leftShoulder");
        AddButton(Skill4, "<Keyboard>/r", "<Gamepad>/rightShoulder");

        // 상자 열기 — 패드 바인딩을 일부러 안 넣는다. 남은 버튼이 없다.
        // 공격·대시·스킬 넷이 여섯 버튼을 다 써서, 아무 버튼이나 붙이면 상자 앞에서
        // 스킬을 쓸 때마다 상자가 열린다. E는 스킬 3이라 F를 쓴다.
        AddButton(OpenChest, "<Keyboard>/f", null);

        // 화면 키. 패드로는 아직 화면을 못 여는데, 그건 화면 조작 전체가 마우스 전제라
        // 열어봐야 고를 수가 없기 때문이다.
        InputAction inventory = map.AddAction(Inventory, InputActionType.Button);
        inventory.AddBinding("<Keyboard>/i");
        inventory.AddBinding("<Keyboard>/tab");

        AddButton(BossKeys, "<Keyboard>/t", null);

        // 설정 — 바꿀 수 없는 키다. 화면을 닫고 빠져나오는 키라 다른 데로 옮길 수 있게 하면
        // 플레이어가 스스로 창에서 못 나오는 상태를 만들 수 있다.
        AddButton(Settings, "<Keyboard>/escape", "<Gamepad>/start");

        // 결과 화면 재시작. 스킬 4와 같은 R이지만 <b>쓰이는 화면이 달라</b> 겹치지 않는다.
        // 겹침 검사는 같은 맥락(InputCatalog의 Context)끼리만 하므로 서로를 지우지 않는다.
        AddButton(Restart, "<Keyboard>/r", "<Gamepad>/start");
    }

    /// <summary>버튼 액션 하나를 만든다. 패드 경로가 없으면 키보드만 붙인다.</summary>
    private static void AddButton(string id, string keyboardPath, string gamepadPath)
    {
        InputAction action = map.AddAction(id, InputActionType.Button);
        action.AddBinding(keyboardPath);

        if (!string.IsNullOrEmpty(gamepadPath)) action.AddBinding(gamepadPath);
    }

    // ── 보여줄 글자 ───────────────────────────────────────────────────────

    /// <summary>
    /// 이 조작의 키보드 키를 사람이 읽을 문구로 만든다. 예: "I / Tab"
    ///
    /// <b>바인딩을 실제 액션에서 읽는 것이 핵심이다.</b> 설정 화면이 문구를 따로 들고 있으면
    /// 키를 바꿨을 때 화면만 옛날 값을 말하게 되고, 그 어긋남은 에러도 경고도 없다.
    /// effectivePath를 쓰므로 플레이어가 바꾼 키도 그대로 반영된다.
    ///
    /// 패드 버튼은 뺀다. 키보드와 섞으면 한 줄이 너무 길어지고, 지금 바꿀 수 있는 건
    /// 키보드뿐이라 목록에 패드를 띄우면 그것도 바꿀 수 있다는 뜻으로 읽힌다.
    /// </summary>
    public static string KeyboardTextFor(string id)
    {
        InputAction action = Get(id);
        if (action == null) return "-";

        string text = string.Empty;

        foreach (InputBinding binding in action.bindings)
        {
            // 컴포지트는 건너뛴다. 묶음 자체(isComposite)는 경로가 없고, 조각들
            // (isPartOfComposite)은 "Up", "Down"처럼 따로 보여줄 것이 아니다.
            if (binding.isComposite || binding.isPartOfComposite) continue;

            string path = binding.effectivePath;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>")) continue;

            string one = InputControlPath.ToHumanReadableString(
                path, InputControlPath.HumanReadableStringOptions.OmitDevice);

            text = string.IsNullOrEmpty(text) ? one : text + " / " + one;
        }

        return string.IsNullOrEmpty(text) ? "-" : text;
    }

    // ── 키 바꾸기 ─────────────────────────────────────────────────────────

    /// <summary>지금 키를 받는 중인가. 받는 동안에는 다른 버튼을 잠가야 한다.</summary>
    public static bool IsRebinding { get; private set; }

    /// <summary>
    /// 이 조작의 키를 새로 받는다. 다음에 누르는 키가 그 조작의 키가 된다.
    ///
    /// 액션을 잠깐 끄는 이유: Input System은 <b>켜져 있는 액션의 바인딩을 바꾸는 것을 막는다.</b>
    /// 안 끄고 부르면 예외가 난다. 끝나면 다시 켠다.
    ///
    /// 마우스와 패드를 제외하는 이유: 지금 바꿀 수 있는 건 키보드 바인딩 한 자리뿐이다.
    /// 패드 버튼을 그 자리에 넣으면 키보드로는 그 조작을 아예 못 하게 된다.
    /// </summary>
    /// <param name="onFinished">
    /// 끝났을 때 부른다. 첫 번째 값은 실제로 바뀌었는지, 두 번째 값은
    /// 겹쳐서 비운 조작이 있으면 그 안내 문구(없으면 null).
    /// </param>
    public static void StartRebind(string id, System.Action<bool, string> onFinished)
    {
        EnsureBuilt();

        if (IsRebinding)
        {
            onFinished?.Invoke(false, null);
            return;
        }

        InputAction action = Get(id);
        int bindingIndex = action == null ? -1 : FirstKeyboardBindingIndex(action);

        if (bindingIndex < 0)
        {
            Debug.LogWarning($"[입력] '{id}'에는 바꿀 키보드 바인딩이 없다.");
            onFinished?.Invoke(false, null);
            return;
        }

        IsRebinding = true;
        action.Disable();

        action.PerformInteractiveRebinding(bindingIndex)
              .WithControlsExcluding("<Mouse>")
              .WithControlsExcluding("<Gamepad>")

              // ESC로 취소한다. ESC는 바꿀 수 없는 키라 여기 써도 뺏기지 않는다 —
              // 취소 키가 곧 바인딩될 수 있으면 "그만두기"를 누를 방법이 없어진다.
              .WithCancelingThrough("<Keyboard>/escape")

              .OnComplete(operation =>
              {
                  operation.Dispose();
                  action.Enable();
                  IsRebinding = false;

                  string cleared = ClearConflicts(action, bindingIndex);
                  SaveOverrides();

                  BindingsChanged?.Invoke();
                  onFinished?.Invoke(true, cleared);
              })
              .OnCancel(operation =>
              {
                  operation.Dispose();
                  action.Enable();
                  IsRebinding = false;

                  onFinished?.Invoke(false, null);
              })
              .Start();
    }

    /// <summary>이 액션에서 처음 나오는 키보드 바인딩의 번호. 없으면 -1.</summary>
    private static int FirstKeyboardBindingIndex(InputAction action)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isComposite || binding.isPartOfComposite) continue;

            string path = binding.effectivePath;
            if (!string.IsNullOrEmpty(path) && path.StartsWith("<Keyboard>")) return i;
        }

        return -1;
    }

    /// <summary>
    /// 방금 정한 키를 이미 쓰고 있던 다른 바인딩을 비운다.
    ///
    /// <b>막지 않고 비우는 쪽을 고른 이유:</b> 이미 쓰는 키라고 거절하면 플레이어는 왜 안
    /// 되는지 모른 채 다른 키를 계속 눌러보게 된다. 비우고 무엇을 비웠는지 알려주면
    /// "저기도 다시 정해야겠구나"가 바로 보인다.
    ///
    /// 먼저 모아두고 나중에 적용하는 이유: 바인딩을 고치는 도중에 목록을 계속 훑으면
    /// 방금 바꾼 것이 다시 걸려들 수 있다.
    /// </summary>
    private static string ClearConflicts(InputAction changed, int changedIndex)
    {
        string newPath = changed.bindings[changedIndex].effectivePath;
        if (string.IsNullOrEmpty(newPath)) return null;

        // 같은 맥락에서만 겹침을 따진다.
        // 스킬 4(R)와 결과 화면 재시작(R)은 쓰이는 화면이 달라 동시에 눌릴 일이 없다.
        // 맥락을 안 보면 스킬 4를 바꿀 때 재시작 키가 조용히 비워진다.
        string changedContext = InputCatalog.ContextFor(changed.name);

        var targets = new System.Collections.Generic.List<(InputAction action, int index)>();

        foreach (InputAction other in map.actions)
        {
            if (InputCatalog.ContextFor(other.name) != changedContext) continue;

            for (int i = 0; i < other.bindings.Count; i++)
            {
                if (other == changed && i == changedIndex) continue;

                InputBinding binding = other.bindings[i];

                // 묶음 자체는 경로가 없다. 조각(이동의 Up/Down 등)은 검사 대상이다 —
                // 스킬을 위쪽 방향키로 옮기면 실제로 이동과 겹친다.
                if (binding.isComposite) continue;
                if (binding.effectivePath != newPath) continue;

                targets.Add((other, i));
            }
        }

        if (targets.Count == 0) return null;

        string cleared = null;
        foreach (var target in targets)
        {
            // 빈 경로를 덮어씌우면 그 바인딩은 아무 키도 안 받는 상태가 된다.
            target.action.ApplyBindingOverride(target.index, string.Empty);

            string name = InputCatalog.DisplayNameFor(target.action.name);
            cleared = cleared == null ? name : cleared + ", " + name;
        }

        return cleared;
    }

    /// <summary>
    /// 이 조작의 <b>대표 키</b> 하나만 짧게. 스킬바처럼 칸이 좁은 곳에서 쓴다.
    ///
    /// "Left "/"Right " 접두사를 떼는 이유: 기본 공격은 좌우 Ctrl 둘 다 먹지만 칸에는
    /// "Ctrl"이라고만 적혀야 읽힌다. 좌우가 갈리는 키(Ctrl/Shift/Alt)는 어느 쪽을 눌러도
    /// 같은 조작이라 접두사가 정보를 더하지 않는다.
    /// </summary>
    public static string ShortKeyTextFor(string id)
    {
        InputAction action = Get(id);
        if (action == null) return "-";

        int index = FirstKeyboardBindingIndex(action);
        if (index < 0) return "-";

        string text = InputControlPath.ToHumanReadableString(
            action.bindings[index].effectivePath,
            InputControlPath.HumanReadableStringOptions.OmitDevice);

        if (text.StartsWith("Left ")) return text.Substring(5);
        if (text.StartsWith("Right ")) return text.Substring(6);

        return text;
    }

    // ── 저장 ──────────────────────────────────────────────────────────────

    /// <summary>플레이어가 바꾼 키를 저장한다.</summary>
    public static void SaveOverrides()
    {
        EnsureBuilt();

        var builder = new System.Text.StringBuilder();

        foreach (InputAction action in map.actions)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                // 빈 문자열도 저장해야 한다. 겹쳐서 <b>비운</b> 바인딩이 그 값이라,
                // 안 남기면 다음에 켤 때 비웠던 키가 되살아난다.
                string overridePath = action.bindings[i].overridePath;
                if (overridePath == null) continue;

                builder.Append(action.name).Append(Separator)
                       .Append(i).Append(Separator)
                       .Append(overridePath).Append(LineBreak);
            }
        }

        PlayerPrefs.SetString(OverridesKey, builder.ToString());
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 저장해둔 키 변경을 되살린다.
    ///
    /// <b>유니티가 주는 JSON을 안 쓰는 이유.</b> <c>SaveBindingOverridesAsJson</c>은 바인딩을
    /// <b>GUID로</b> 가리킨다. 그 GUID는 액션 맵을 만들 때 새로 생기는데, 이 맵은 에셋이 아니라
    /// 코드로 매번 새로 만들어서 실행할 때마다 값이 달라진다. 그래서 저장한 값이 하나도 안 맞고
    /// <c>"Could not override binding as no existing binding was found with the id..."</c>
    /// 경고만 잔뜩 남았다. <b>키 변경이 저장은 되는데 복원이 안 되는</b> 상태였다.
    ///
    /// 액션 이름과 바인딩 번호는 <see cref="Build"/>가 정하는 값이라 실행할 때마다 같다.
    /// 그래서 그 둘로 가리킨다. 대신 Build에서 바인딩 순서를 바꾸면 저장된 값이 엉뚱한 자리에
    /// 붙으므로, 순서를 바꿀 때는 저장 키(<see cref="OverridesKey"/>)도 같이 올려야 한다.
    /// </summary>
    private static void LoadOverrides()
    {
        string saved = PlayerPrefs.GetString(OverridesKey, string.Empty);
        if (string.IsNullOrEmpty(saved)) return;

        foreach (string line in saved.Split(LineBreak))
        {
            if (string.IsNullOrEmpty(line)) continue;

            // 경로 자체에 구분자가 없으므로 셋으로 나누면 충분하다.
            string[] parts = line.Split(Separator);
            if (parts.Length != 3) continue;

            InputAction action = map.FindAction(parts[0]);
            if (action == null) continue;

            if (!int.TryParse(parts[1], out int index)) continue;
            if (index < 0 || index >= action.bindings.Count) continue;

            action.ApplyBindingOverride(index, parts[2]);
        }
    }

    /// <summary>모든 키를 기본값으로 되돌린다.</summary>
    public static void ResetAllToDefaults()
    {
        EnsureBuilt();

        map.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(OverridesKey);
        PlayerPrefs.Save();

        BindingsChanged?.Invoke();
    }

    /// <summary>
    /// 부팅 때 미리 만들어둔다.
    ///
    /// 첫 번째로 읽는 쪽이 만들게 두면 그 프레임에 액션 열한 개를 한꺼번에 만들면서
    /// 눈에 띄는 끊김이 생긴다. 어차피 만들 것이면 씬이 뜨기 전에 만드는 편이 낫다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        EnsureBuilt();
    }

    /// <summary>
    /// 플레이 모드를 다시 시작할 때 정적 상태를 비운다.
    ///
    /// 도메인 리로드를 꺼두면 static 필드가 이전 실행의 맵을 들고 시작한다. 그 맵은
    /// 이미 해제된 장치를 가리키고 있어서 키가 아예 안 먹는다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        map?.Disable();
        map?.Dispose();
        map = null;
        BindingsChanged = null;
    }
}
