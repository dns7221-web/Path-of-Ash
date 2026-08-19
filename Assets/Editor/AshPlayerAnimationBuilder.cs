using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 잘라둔 스프라이트로 애니메이션 클립과 AnimatorController를 만들어내는 에디터 도구.
///
/// 메뉴: Tools → 재의 길 → 플레이어 애니메이션 생성
///
/// 애니메이션 창에서 손으로 만들지 않고 스크립트로 둔 이유:
/// 1) 클립 7개에 프레임 38장을 손으로 끌어다 놓으면 순서가 한 번은 어긋난다. 그리고 어긋난 걸
///    눈으로 찾으려면 재생해봐야 한다.
/// 2) 상태 7개와 전이 12개를 창에서 이으면 조건을 하나 빠뜨리기 쉽고, 빠뜨린 전이는
///    "가끔 공격 모션에서 안 빠져나오는" 식으로 나중에 나타난다.
/// 3) 프레임 레이트를 조정할 일이 반드시 생기는데, 표의 숫자 하나 고치고 메뉴를 다시 누르면 된다.
///
/// 이 도구는 <see cref="AshPlayerSpriteSlicer"/>가 먼저 돌아 있어야 동작한다.
/// </summary>
public static class AshPlayerAnimationBuilder
{
    // ── 경로 ──────────────────────────────────────────────────────────────

    // 캐릭터별 폴더와 컨트롤러 이름은 AshPlayerSpriteSheets의 CharacterSet이 들고 있다.
    // 여기서는 그 위의 공통 뿌리만 안다.
    private const string AnimationRoot = "Assets/Project/Animations";

    // ── 애니메이터 파라미터 이름 ────────────────────────────────────────────
    // 런타임 코드(PlayerController)도 같은 이름을 쓴다. 문자열을 양쪽에 따로 적으면
    // 한쪽 오타를 컴파일러가 못 잡아주므로, 여기 상수를 진실의 원천으로 두고 런타임은
    // Animator.StringToHash로 해시를 떠서 쓴다.

    public const string ParamSpeed = "Speed";
    public const string ParamIsRunning = "IsRunning";
    public const string ParamAttack = "Attack";

    // 추가 생성 — 스킬마다 다른 모션을 쓰게 되면서 트리거가 늘었다.
    // SkillData 에셋의 Animator Trigger 칸에 이 문자열을 적으면 그 모션이 나온다.
    public const string ParamSwordSlam = "SwordSlam";
    public const string ParamBow = "Bow";
    public const string ParamStaff = "Staff";
    public const string ParamUltimate = "Ultimate";
    public const string ParamDash = "Dash";
    public const string ParamHit = "Hit";
    public const string ParamDie = "Die";

    /// <summary>
    /// "움직이는 중"으로 볼 최소 속도(유닛/초).
    ///
    /// 0이 아니라 0.1인 이유: 물리로 이동하면 키를 뗀 뒤에도 속도가 정확히 0이 되기까지
    /// 한두 프레임이 걸리고, 벽에 밀착했을 때도 아주 작은 값이 남는다. 0으로 비교하면
    /// 그 순간마다 Idle과 Walk가 한 프레임씩 번갈아 재생되며 떨린다.
    /// </summary>
    public const float MoveSpeedThreshold = 0.1f;

    // ── 메뉴 ──────────────────────────────────────────────────────────────

    // 수정(적 추가 시점): 캐릭터 세트 전체를 돈다. 슬라이서와 같은 이유로 메뉴는 하나만 둔다.
    [MenuItem("Tools/재의 길/캐릭터 애니메이션 생성")]
    public static void BuildAll()
    {
        EnsureFolder(AnimationRoot);

        foreach (var set in AshPlayerSpriteSheets.AllSets)
        {
            if (!BuildSet(set)) return; // 실패 원인은 각 단계가 이미 로그로 남겼다
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>캐릭터 한 종류의 클립과 컨트롤러를 만든다. 실패하면 false.</summary>
    private static bool BuildSet(AshPlayerSpriteSheets.CharacterSet set)
    {
        EnsureFolder(set.AnimationFolder);

        // 세그먼트 이름 → 만들어진 클립. 아래 상태 머신 구성에서 다시 꺼내 쓴다.
        var clips = new Dictionary<string, AnimationClip>();

        foreach (var sheet in set.Sheets)
        {
            var sprites = LoadSprites(set, sheet);
            if (sprites == null) return false;

            foreach (var segment in sheet.Segments)
            {
                var clip = BuildClip(set, segment, sprites);
                if (clip == null) return false;
                clips[segment.Name] = clip;
            }
        }

        var controller = LoadOrCreateController(set.ControllerPath);

        // 상태 머신 구조는 캐릭터마다 다르다. 플레이어는 이동 3단계 + 액션 4개, 적은
        // 배회 → 예비동작 → 돌진의 한 줄기다. 한 함수로 억지로 합치면 조건문 범벅이 된다.
        if (clips.ContainsKey("idle")) BuildPlayerController(controller, clips);
        else BuildWraithController(controller, clips);

        EditorUtility.SetDirty(controller);

        Debug.Log($"[{set.DisplayName} 애니메이션] 클립 {clips.Count}개와 컨트롤러 생성 완료 " +
                  $"→ {set.ControllerPath}");
        return true;
    }

    // ── 클립 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 시트 한 장에서 잘려 나온 스프라이트를 이름으로 찾을 수 있게 사전으로 만든다.
    /// 실패하면 null을 돌려주고 원인을 로그로 남긴다.
    /// </summary>
    private static Dictionary<string, Sprite> LoadSprites(
        AshPlayerSpriteSheets.CharacterSet set, AshPlayerSpriteSheets.Sheet sheet)
    {
        string path = $"{set.FolderPath}/{sheet.FileName}.png";

        // 시트는 스프라이트 여러 개를 서브 에셋으로 들고 있으므로 LoadAllAssetsAtPath로 꺼낸다.
        // LoadAssetAtPath는 대표 에셋(Texture2D) 하나만 준다.
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        var map = new Dictionary<string, Sprite>();

        foreach (var asset in all)
        {
            if (asset is Sprite sprite && !map.ContainsKey(sprite.name))
                map.Add(sprite.name, sprite);
        }

        if (map.Count == 0)
        {
            Debug.LogError(
                $"[플레이어 애니메이션] {sheet.FileName}에 잘린 스프라이트가 없다. " +
                $"Tools → 재의 길 → 플레이어 스프라이트 슬라이스 를 먼저 실행해라.");
            return null;
        }

        return map;
    }

    /// <summary>
    /// 세그먼트 하나를 AnimationClip으로 만든다. 이미 같은 경로에 클립이 있으면
    /// 새로 만들지 않고 내용만 덮어쓴다.
    ///
    /// 덮어쓰는 이유: 클립을 지우고 다시 만들면 GUID가 바뀌어서, 이미 그 클립을 참조하고 있는
    /// 컨트롤러나 프리팹의 연결이 끊긴다. 슬라이서가 스프라이트 ID를 물려주는 것과 같은 이유다.
    /// </summary>
    private static AnimationClip BuildClip(
        AshPlayerSpriteSheets.CharacterSet set,
        AshPlayerSpriteSheets.Segment segment, Dictionary<string, Sprite> sprites)
    {
        string clipPath = $"{set.AnimationFolder}/{set.ClipName(segment.Name)}.anim";

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        bool isNew = clip == null;
        if (isNew) clip = new AnimationClip();

        clip.frameRate = segment.Fps;

        // 어떤 컴포넌트의 어떤 값을 애니메이션할지 지정한다.
        // path가 빈 문자열인 이유: SpriteRenderer를 Animator와 같은 게임오브젝트(플레이어 루트)에
        // 둘 것이기 때문이다. 자식 오브젝트에 두면 여기에 그 자식 이름을 적어야 한다.
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite",
        };

        // 프레임 수보다 키를 하나 더 만든다.
        //
        // 유니티는 클립 길이를 "마지막 키의 시간"으로 정한다. 키를 프레임 수만큼만 놓으면
        // 6프레임 클립의 길이가 5/fps가 되어, 마지막 프레임이 화면에 0초 동안만 보인다.
        // 루프 애니메이션에서는 이게 "한 프레임이 건너뛰어지는" 것처럼 보인다.
        // 마지막에 같은 스프라이트로 키를 하나 더 찍어 길이를 프레임수/fps로 맞춘다.
        var keys = new ObjectReferenceKeyframe[segment.FrameCount + 1];

        for (int i = 0; i < segment.FrameCount; i++)
        {
            string spriteName = set.SpriteName(segment.Name, i);

            if (!sprites.TryGetValue(spriteName, out var sprite))
            {
                Debug.LogError($"[플레이어 애니메이션] 스프라이트를 못 찾았다: {spriteName}");
                return null;
            }

            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / (float)segment.Fps,
                value = sprite,
            };
        }

        keys[segment.FrameCount] = new ObjectReferenceKeyframe
        {
            time = segment.FrameCount / (float)segment.Fps,
            value = keys[segment.FrameCount - 1].value,
        };

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        // 루프 여부는 커브가 아니라 클립 설정에 들어 있어서 따로 건드려야 한다.
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = segment.Loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (isNew) AssetDatabase.CreateAsset(clip, clipPath);
        else EditorUtility.SetDirty(clip);

        return clip;
    }

    // ── 컨트롤러 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 상태 머신을 구성한다.
    ///
    /// 구조:
    ///   Idle ↔ Walk ↔ Run          (이동 계열. Speed와 IsRunning으로 오간다)
    ///   Any State → Attack/Dash/Hit/Death   (액션 계열. 트리거로 끼어든다)
    ///   Attack/Dash/Hit → Idle     (재생이 끝나면 자동 복귀)
    ///   Death → 없음               (죽으면 그 상태로 멈춘다)
    ///
    /// 이동을 Blend Tree가 아니라 상태 3개로 나눈 이유: Walk와 Run이 서로 다른 프레임 수(8/6)와
    /// 다른 재생 속도(10/12fps)를 가진 별개의 그림이고, 둘 사이를 스프라이트로 섞는 건 불가능하다.
    /// Blend Tree는 값을 섞을 수 있는 애니메이션(3D 본, 파라미터)에서 의미가 있다.
    ///
    /// 액션에 Any State를 쓴 이유: 걷다가도 서 있다가도 공격이 나가야 하는데, 상태마다 전이를
    /// 하나씩 그으면 이동 상태 3개 x 액션 4개 = 12개를 관리해야 한다. Any State는 그걸 4개로 줄인다.
    /// </summary>
    private static AnimatorController LoadOrCreateController(string path)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

        if (controller == null) return AnimatorController.CreateAnimatorControllerAtPath(path);

        // 기존 컨트롤러의 내용만 비운다. 에셋 자체를 지우면 GUID가 바뀌어서 프리팹의
        // Animator에 연결해둔 컨트롤러가 빠져버린다.
        ClearController(controller);
        return controller;
    }

    /// <summary>
    /// 잿불 망령의 상태 머신.
    ///
    /// 배회 → 예비동작 → 돌진 → 배회의 한 줄기다. 플레이어처럼 이동 단계가 갈리지 않는다.
    ///
    /// <b>여기서 유니티 내장 기능을 최대한 쓴 지점이 Windup → Charge 전이다.</b>
    /// 예비동작이 끝나면 돌진이 나가야 하는데, 이걸 코드에서 "0.4초 기다렸다가 돌진 트리거"로
    /// 짜면 클립 길이(4프레임/10fps=0.4초)와 코드의 0.4초가 따로 관리되어 반드시 어긋난다.
    /// Exit Time 전이를 쓰면 <b>애니메이션이 끝나는 순간이 곧 돌진 시작</b>이라 두 값이
    /// 하나가 된다. fps를 바꿔도 저절로 맞는다.
    /// </summary>
    private static void BuildWraithController(
        AnimatorController controller, Dictionary<string, AnimationClip> clips)
    {
        // 적은 Speed도 IsRunning도 필요 없다. 배회는 기본 상태이고 나머지는 사건이라 전부 트리거다.
        controller.AddParameter(ParamAttack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamHit, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamDie, AnimatorControllerParameterType.Trigger);

        var machine = controller.layers[0].stateMachine;

        var walk = AddState(machine, "Walk", clips["walk"], new Vector3(300f, 0f, 0f));
        var windup = AddState(machine, "Windup", clips["windup"], new Vector3(560f, 0f, 0f));
        var charge = AddState(machine, "Charge", clips["charge"], new Vector3(820f, 0f, 0f));
        var hit = AddState(machine, "Hit", clips["hit"], new Vector3(560f, 120f, 0f));
        var death = AddState(machine, "Death", clips["death"], new Vector3(560f, 200f, 0f));

        machine.defaultState = walk;

        // 플레이어를 발견하면 코드가 Attack 트리거를 쏜다. 그 뒤로는 애니메이션이 알아서 흘러간다.
        var toWindup = walk.AddTransition(windup);
        ApplyInstantTransition(toWindup);
        toWindup.AddCondition(AnimatorConditionMode.If, 0f, ParamAttack);

        // 예비동작이 끝나면 자동으로 돌진, 돌진이 끝나면 자동으로 배회로 복귀.
        // 코드가 개입하지 않는다.
        AddExitTimeTransition(windup, charge);
        AddExitTimeTransition(charge, walk);

        // 피격과 사망은 어느 상태에서든 끼어들어야 한다.
        AddTriggerFromAnyState(machine, hit, ParamHit);
        AddTriggerFromAnyState(machine, death, ParamDie);

        // 경직이 끝나면 배회로 돌아간다. 죽으면 안 돌아온다.
        AddExitTimeTransition(hit, walk);
    }

    private static void BuildPlayerController(
        AnimatorController controller, Dictionary<string, AnimationClip> clips)
    {
        controller.AddParameter(ParamSpeed, AnimatorControllerParameterType.Float);
        controller.AddParameter(ParamIsRunning, AnimatorControllerParameterType.Bool);
        controller.AddParameter(ParamAttack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamSwordSlam, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamBow, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamStaff, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamUltimate, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamDash, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamHit, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(ParamDie, AnimatorControllerParameterType.Trigger);

        var machine = controller.layers[0].stateMachine;

        // 상태를 화면에 격자로 배치한다. 안 하면 전부 한 점에 겹쳐 생성돼서
        // Animator 창을 열었을 때 사람이 읽을 수가 없다.
        var idle = AddState(machine, "Idle", clips["idle"], new Vector3(300f, 0f, 0f));
        var walk = AddState(machine, "Walk", clips["walk"], new Vector3(300f, 80f, 0f));
        var run = AddState(machine, "Run", clips["run"], new Vector3(300f, 160f, 0f));
        var attack = AddState(machine, "Attack", clips["attack"], new Vector3(600f, 0f, 0f));
        var dash = AddState(machine, "Dash", clips["dash"], new Vector3(600f, 80f, 0f));
        var hit = AddState(machine, "Hit", clips["hit"], new Vector3(600f, 160f, 0f));
        var death = AddState(machine, "Death", clips["death"], new Vector3(600f, 240f, 0f));

        machine.defaultState = idle;

        // ── 이동 계열 전이 ──
        // Idle → Walk: 움직이기 시작하면
        AddFloatTransition(idle, walk, AnimatorConditionMode.Greater, MoveSpeedThreshold);

        // Walk → Idle: 멈추면
        AddFloatTransition(walk, idle, AnimatorConditionMode.Less, MoveSpeedThreshold);

        // Walk → Run: 달리기가 켜지면
        AddBoolTransition(walk, run, true);

        // Run → Walk: 달리기가 꺼지면 (스태미나가 바닥나는 경우가 여기로 온다)
        AddBoolTransition(run, walk, false);

        // Run → Idle: 달리는 도중에 그대로 멈춘 경우. 이 전이가 없으면 Run에서 Walk를 거쳐야만
        // Idle로 갈 수 있어서, 달리다 키를 놓으면 걷는 모션이 한 번 스쳐 지나간다.
        AddFloatTransition(run, idle, AnimatorConditionMode.Less, MoveSpeedThreshold);

        // ── 액션 계열 전이 (Any State에서 트리거로 진입) ──
        // 추가 생성 — 스킬마다 다른 모션. 기본 공격과 같은 방식으로 Any State에서 들어온다.
        var swordSlam = AddState(machine, "SwordSlam", clips["sword_slam"], new Vector3(860f, 0f, 0f));
        var bow = AddState(machine, "Bow", clips["bow"], new Vector3(860f, 80f, 0f));

        AddTriggerFromAnyState(machine, swordSlam, ParamSwordSlam);
        var staff = AddState(machine, "Staff", clips["staff"], new Vector3(860f, 160f, 0f));

        AddTriggerFromAnyState(machine, bow, ParamBow);
        var ultimate = AddState(machine, "Ultimate", clips["ultimate"], new Vector3(860f, 240f, 0f));

        AddTriggerFromAnyState(machine, staff, ParamStaff);
        AddTriggerFromAnyState(machine, ultimate, ParamUltimate);
        AddExitTimeTransition(ultimate, idle);
        AddExitTimeTransition(swordSlam, idle);
        AddExitTimeTransition(bow, idle);
        AddExitTimeTransition(staff, idle);

        AddTriggerFromAnyState(machine, attack, ParamAttack);
        AddTriggerFromAnyState(machine, dash, ParamDash);
        AddTriggerFromAnyState(machine, hit, ParamHit);
        AddTriggerFromAnyState(machine, death, ParamDie);

        // ── 액션이 끝나면 Idle로 복귀 ──
        // Idle로 보내는 이유: 복귀 시점의 이동 상태를 여기서 판단하지 않아도, Idle에 도착한
        // 다음 프레임에 위의 Idle → Walk 전이가 Speed를 보고 알아서 이어받는다.
        // 전이 시간이 0이라 걷는 중에 공격해도 멈춘 그림이 보이지 않는다.
        AddExitTimeTransition(attack, idle);
        AddExitTimeTransition(dash, idle);
        AddExitTimeTransition(hit, idle);

        // Death는 나가는 전이를 만들지 않는다. 마지막 프레임(잿더미에 검이 꽂힌 그림)에서
        // 멈춰 있어야 결과 화면으로 넘어갈 때까지 그림이 유지된다.
    }

    /// <summary>컨트롤러의 파라미터와 상태를 모두 지운다. 에셋 자체는 남긴다.</summary>
    private static void ClearController(AnimatorController controller)
    {
        // 뒤에서부터 지운다. 앞에서부터 지우면 인덱스가 당겨지면서 하나씩 건너뛴다.
        for (int i = controller.parameters.Length - 1; i >= 0; i--)
            controller.RemoveParameter(i);

        var machine = controller.layers[0].stateMachine;

        // states는 배열 사본을 돌려주지만, RemoveState가 원본 목록을 건드리므로
        // 사본을 미리 확보한 뒤 순회한다.
        var states = machine.states;
        foreach (var child in states)
            machine.RemoveState(child.state); // 그 상태에 걸린 전이도 같이 사라진다

        var anyTransitions = machine.anyStateTransitions;
        foreach (var transition in anyTransitions)
            machine.RemoveAnyStateTransition(transition);
    }

    /// <summary>상태 하나를 지정한 위치에 추가한다.</summary>
    private static AnimatorState AddState(
        AnimatorStateMachine machine, string name, AnimationClip clip, Vector3 position)
    {
        var state = machine.AddState(name, position);
        state.motion = clip;
        return state;
    }

    /// <summary>
    /// 스프라이트 애니메이션용 전이 기본값을 적용한다.
    ///
    /// 전이 시간을 0으로 두는 이유: 크로스페이드는 두 애니메이션의 값을 섞는 기능인데,
    /// 스프라이트는 "이 그림 아니면 저 그림"이라 섞을 수가 없다. 시간을 남겨두면 그동안
    /// 이전 상태의 그림이 그대로 보여서 입력이 늦게 먹는 것처럼 느껴진다.
    /// </summary>
    private static void ApplyInstantTransition(AnimatorStateTransition transition)
    {
        transition.duration = 0f;
        transition.hasFixedDuration = true;
        transition.exitTime = 0f;
        transition.hasExitTime = false;
    }

    /// <summary>Speed 값을 조건으로 하는 즉시 전이.</summary>
    private static void AddFloatTransition(
        AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
    {
        var transition = from.AddTransition(to);
        ApplyInstantTransition(transition);
        transition.AddCondition(mode, threshold, ParamSpeed);
    }

    /// <summary>IsRunning 값을 조건으로 하는 즉시 전이.</summary>
    private static void AddBoolTransition(AnimatorState from, AnimatorState to, bool value)
    {
        var transition = from.AddTransition(to);
        ApplyInstantTransition(transition);
        transition.AddCondition(
            value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, ParamIsRunning);
    }

    /// <summary>
    /// Any State에서 트리거로 진입하는 전이.
    ///
    /// canTransitionToSelf를 끄는 이유: 켜두면 공격 중에 공격 트리거가 또 들어왔을 때
    /// 애니메이션이 0프레임부터 다시 시작한다. 연타하면 첫 프레임만 반복되면서 검이 나가지 않는다.
    /// 연속 공격(콤보)은 나중에 따로 상태를 만들어 붙일 문제다.
    /// </summary>
    private static void AddTriggerFromAnyState(
        AnimatorStateMachine machine, AnimatorState to, string triggerName)
    {
        var transition = machine.AddAnyStateTransition(to);
        ApplyInstantTransition(transition);
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
    }

    /// <summary>
    /// 재생이 끝나면 자동으로 넘어가는 전이. 조건 없이 시간만 본다.
    ///
    /// exitTime 1은 "이 상태의 클립을 100% 재생한 시점"이라는 뜻이다. 0.9로 두면 마지막
    /// 10%를 건너뛰는데, 공격의 마지막 프레임은 검을 거두는 그림이라 잘리면 동작이 툭 끊긴다.
    /// </summary>
    private static void AddExitTimeTransition(AnimatorState from, AnimatorState to)
    {
        var transition = from.AddTransition(to);
        transition.duration = 0f;
        transition.hasFixedDuration = true;
        transition.hasExitTime = true;
        transition.exitTime = 1f;
    }

    /// <summary>폴더가 없으면 만든다. AssetDatabase는 없는 폴더에 에셋을 못 만든다.</summary>
    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        int lastSlash = folderPath.LastIndexOf('/');
        string parent = folderPath.Substring(0, lastSlash);
        string name = folderPath.Substring(lastSlash + 1);

        AssetDatabase.CreateFolder(parent, name);
    }
}
