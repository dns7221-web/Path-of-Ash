/// <summary>
/// 추가 생성 — 게임의 조작 목록을 한 곳에 모아둔 표.
///
/// <b>왜 표를 따로 두는가.</b> 지금 조작은 <see cref="PlayerController"/>,
/// <see cref="SkillController"/>, <see cref="RewardChest"/> 등 여러 컴포넌트에 흩어져 있다.
/// 설정 화면에 "무슨 키로 무엇을 하는지"를 보여주려면 그 목록이 어딘가 한 곳에는 있어야 하는데,
/// 화면 쪽에 글자로 박아 넣으면 <b>키를 바꿨을 때 화면만 옛날 값을 말하게 된다.</b>
/// 그 어긋남은 에러도 경고도 없어서, 플레이어가 안내대로 눌러보고 안 되는 것으로만 드러난다.
///
/// <b>키 문구를 손으로 안 적는 이유.</b> 이 표는 <see cref="InputBindings"/>의 액션 이름만
/// 들고 있고, 보여줄 글자는 실제 액션의 바인딩에서 그때그때 읽는다. 경로를 복사해 적어두면
/// 키를 바꿨을 때 표만 옛날 값을 말하게 되고, 그 어긋남은 에러도 경고도 없이
/// <b>플레이어가 안내대로 눌러보고 안 될 때</b>만 드러난다.
///
/// 그래서 이 표에 남은 정보는 "무엇을 뭐라고 부르고 어느 묶음에 넣을 것인가"뿐이다.
/// 실제 키는 InputBindings가 소유하고, 이 표는 그것을 가리키기만 한다.
/// </summary>
public static class InputCatalog
{
    /// <summary>조작 하나. 이름, 키, 바꿀 수 있는지.</summary>
    public readonly struct Entry
    {
        /// <summary>묶음 이름. 같은 값이 이어지면 화면에서 한 덩어리로 묶인다.</summary>
        public readonly string Group;

        /// <summary>플레이어에게 보여줄 조작 이름.</summary>
        public readonly string Name;

        /// <summary>설정에서 키를 바꿀 수 있는 조작인가.</summary>
        public readonly bool Rebindable;

        /// <summary>
        /// 이 조작이 쓰이는 맥락. <b>같은 맥락끼리만 키가 겹친 것으로 본다.</b>
        ///
        /// 결과 화면의 재시작은 스킬 4와 같은 R이지만 두 화면이 동시에 떠 있을 수 없다.
        /// 맥락을 안 나누면 스킬 4를 바꿀 때 재시작 키가 조용히 비워진다.
        /// </summary>
        public readonly string Context;

        /// <summary>
        /// <see cref="InputBindings"/>의 액션 이름. 키 문구를 여기서 읽어온다.
        ///
        /// 경로를 복사해 들고 있지 않은 것이 핵심이다. 실제 액션을 읽으면 플레이어가
        /// 바꾼 키도 그대로 따라오고, <b>표와 실제가 어긋날 자리 자체가 없어진다.</b>
        /// </summary>
        public readonly string ActionId;

        // 손으로 적은 키 문구. 복합 바인딩(이동처럼 키 네 개가 한 조작)은 액션에서 뽑아낼
        // 방법이 마땅치 않아 이쪽을 쓴다. 비어 있으면 액션에서 읽는다.
        private readonly string manualText;

        public Entry(string group, string name, bool rebindable, string actionId,
                     string manualText = null, string context = PlayContext)
        {
            Group = group;
            Name = name;
            Rebindable = rebindable;
            ActionId = actionId;
            Context = context;
            this.manualText = manualText;
        }

        /// <summary>키 문구를 손으로 적었는가. 그러면 실행 중에 갱신해도 소용이 없다.</summary>
        public bool HasManualText => !string.IsNullOrEmpty(manualText);

        /// <summary>화면에 보여줄 키 문구. 예: "Left Shift", "I / Tab"</summary>
        public string KeyText
        {
            get
            {
                if (HasManualText) return manualText;
                if (string.IsNullOrEmpty(ActionId)) return "-";

                return InputBindings.KeyboardTextFor(ActionId);
            }
        }
    }

    /// <summary>
    /// 액션 이름으로 사람이 읽을 조작 이름을 찾는다. 표에 없으면 액션 이름을 그대로 돌려준다.
    ///
    /// 겹친 키를 비웠다고 알릴 때 쓴다. "Skill2의 키를 비웠다"보다
    /// "스킬 2의 키를 비웠다"가 플레이어에게 통한다.
    /// </summary>
    public static string DisplayNameFor(string actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return actionId;

        foreach (Entry entry in Entries)
        {
            if (entry.ActionId == actionId) return entry.Name;
        }

        return actionId;
    }

    /// <summary>맥락 이름. 같은 맥락끼리만 키 겹침을 따진다.</summary>
    public const string PlayContext = "게임";
    public const string ResultContext = "결과";

    /// <summary>액션이 쓰이는 맥락. 표에 없으면 게임 맥락으로 본다.</summary>
    public static string ContextFor(string actionId)
    {
        if (string.IsNullOrEmpty(actionId)) return PlayContext;

        foreach (Entry entry in Entries)
        {
            if (entry.ActionId == actionId) return entry.Context;
        }

        return PlayContext;
    }

    private const string MoveGroup = "이동과 전투";
    private const string WorldGroup = "상호작용";
    private const string ScreenGroup = "화면";
    private const string ResultGroup = "결과 화면";

    /// <summary>
    /// 조작 목록. 화면에 나오는 순서 그대로다.
    ///
    /// 같은 키가 두 줄에 보일 수 있다 — 스킬 4와 결과 화면의 다시 시작이 둘 다 R이다.
    /// 그래서 묶음을 나눠 어느 화면의 조작인지 먼저 읽히게 했다. 겹침 검사도
    /// <see cref="Context"/>가 같을 때만 하므로 한쪽을 바꿔도 다른 쪽은 그대로다.
    /// </summary>
    public static readonly Entry[] Entries =
    {
        // 이동은 키 네 개가 한 조작(복합 바인딩)이라 경로 하나로 못 적는다.
        //
        // WASD가 없는 이유: 스킬을 Q/W/E/R에 두면서 W가 "위로 이동"과 정면으로 겹쳤다.
        // 둘 다 남기면 위로 걸을 때마다 스킬이 나간다. 스킬 배치는 기획이 정한 것이라
        // 이동을 방향키로 옮겼다. (PlayerController.Reset의 주석과 같은 판단이다.)
        new Entry(MoveGroup, "이동", true, InputBindings.Move, manualText: "방향키"),
        new Entry(MoveGroup, "대시", true, InputBindings.Dash),
        new Entry(MoveGroup, "기본 공격", true, InputBindings.BasicAttack),
        new Entry(MoveGroup, "스킬 1", true, InputBindings.Skill1),
        new Entry(MoveGroup, "스킬 2", true, InputBindings.Skill2),
        new Entry(MoveGroup, "스킬 3", true, InputBindings.Skill3),
        new Entry(MoveGroup, "스킬 4", true, InputBindings.Skill4),

        new Entry(WorldGroup, "상자 열기", true, InputBindings.OpenChest),

        new Entry(ScreenGroup, "유물 보관함", true, InputBindings.Inventory),
        new Entry(ScreenGroup, "보스 열쇠", true, InputBindings.BossKeys),

        // ESC는 바꿀 수 없다. 화면을 닫고 빠져나오는 키라, 다른 데로 옮길 수 있게 하면
        // 플레이어가 스스로 창에서 못 나오는 상태를 만들 수 있다.
        new Entry(ScreenGroup, "설정 열기 / 닫기", false, InputBindings.Settings),

        // 결과 화면 전용. 스킬 4와 같은 R이지만 맥락이 달라 서로 겹치지 않는다.
        // 예전에는 "같은 키가 두 줄로 보여 혼란스럽다"는 이유로 목록에서 뺐는데,
        // 빼두면 <b>바꿀 수 없는 키</b>가 되어버린다. 묶음을 나눠 보여주는 편이 낫다.
        new Entry(ResultGroup, "다시 시작", true, InputBindings.Restart,
                  context: ResultContext),
    };
}
