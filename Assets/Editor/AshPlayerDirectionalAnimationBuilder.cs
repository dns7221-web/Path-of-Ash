using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 8방향 플레이어 스프라이트를 잘라 클립 80개와 블렌드 트리를 만든다.
///
/// 메뉴: Tools → 재의 길 → 8방향 플레이어 애니메이션 생성
///
/// 왜 도구인가:
/// 액션 10개 x 방향 8개 = <b>클립 80개</b>다. 손으로 만들 수 있는 양이 아니고, 하나라도
/// 프레임 순서가 어긋나면 그 방향만 이상하게 재생되는데 어느 클립인지 찾기도 어렵다.
///
/// 왜 상태를 80개 만들지 않는가:
/// 방향마다 상태를 두면 전이가 방향 수의 제곱으로 늘어나 관리가 불가능해진다. 대신 액션당
/// 상태 하나에 <b>블렌드 트리(2D Simple Directional)</b>를 넣고 MoveX/MoveY로 방향을 고른다.
/// 상태는 10개, 전이는 지금과 같은 수로 유지된다.
///
/// 시트 격자 규칙 (그림을 직접 보고 확인한 값):
///   가로 = 프레임, 세로 = 방향. 위에서부터 S, SW, W, NW, N, NE, E, SE 순서다.
///   즉 정면에서 시작해 반시계 방향으로 돈다.
///
/// 유니티 스프라이트 좌표는 <b>아래에서 위로</b> 올라가므로, 이미지 맨 윗줄(S)이
/// rect.y가 가장 큰 줄이 된다. 이 뒤집힘을 놓치면 위로 걷는데 정면을 보게 된다.
/// </summary>
public static class AshPlayerDirectionalAnimationBuilder
{
    private const string SheetFolder = "Assets/Project/Art/Sprites/Player/Topdown35/Production8Dir";
    private const string ClipFolder = "Assets/Project/Animations/Player/Directional";
    private const string ControllerPath = "Assets/Project/Animations/Player/PlayerDirectional.controller";
    private const string PlayerPrefabPath = "Assets/Project/Prefabs/Player/Player.prefab";

    private const int Cell = 256;
    // 플레이어 PPU는 임포트 규칙 한 곳에서 정한다. 여기 숫자를 따로 적으면 둘이 어긋난다.
    private static float PixelsPerUnit => AshSpriteImportRules.PlayerPixelsPerUnit;

    /// <summary>피벗 y. 프레임 아래에서 39px — 정규화한 발 라인(216행)과 같은 줄이다.</summary>
    private const float PivotY = 39f / 256f;

    /// <summary>이미지 위에서부터의 방향 이름.</summary>
    private static readonly string[] DirectionNames = { "S", "SW", "W", "NW", "N", "NE", "E", "SE" };

    /// <summary>블렌드 트리에서 각 방향이 놓일 좌표. 유니티 기준 X=오른쪽, Y=위쪽.</summary>
    private static readonly Vector2[] DirectionVectors =
    {
        new Vector2(0f, -1f),                    // S
        new Vector2(-0.7071f, -0.7071f),         // SW
        new Vector2(-1f, 0f),                    // W
        new Vector2(-0.7071f, 0.7071f),          // NW
        new Vector2(0f, 1f),                     // N
        new Vector2(0.7071f, 0.7071f),           // NE
        new Vector2(1f, 0f),                     // E
        new Vector2(0.7071f, -0.7071f),          // SE
    };

    private class ActionDef
    {
        public string Sheet;    // 시트 파일 이름(확장자 제외)
        public string State;    // 애니메이터 상태 이름
        public string Trigger;  // 이 액션을 부르는 트리거. Idle/Walk는 없다.
        public float Fps;
        public bool Loop;
    }

    private static readonly ActionDef[] Actions =
    {
        new ActionDef { Sheet = "player_idle",       State = "Idle",      Trigger = null,         Fps = 6f,  Loop = true  },
        new ActionDef { Sheet = "player_walk",       State = "Walk",      Trigger = null,         Fps = 12f, Loop = true  },
        new ActionDef { Sheet = "player_attack",     State = "Attack",    Trigger = "Attack",     Fps = 14f, Loop = false },
        new ActionDef { Sheet = "player_bow",        State = "Bow",       Trigger = "Bow",        Fps = 12f, Loop = false },
        new ActionDef { Sheet = "player_staff",      State = "Staff",     Trigger = "Staff",      Fps = 12f, Loop = false },
        new ActionDef { Sheet = "player_sword_slam", State = "SwordSlam", Trigger = "SwordSlam",  Fps = 12f, Loop = false },
        new ActionDef { Sheet = "player_ultimate",   State = "Ultimate",  Trigger = "Ultimate",   Fps = 10f, Loop = false },
        new ActionDef { Sheet = "player_dash_hit",   State = "Dash",      Trigger = "Dash",       Fps = 16f, Loop = false },
        new ActionDef { Sheet = "player_hit",        State = "Hit",       Trigger = "Hit",        Fps = 14f, Loop = false },
        new ActionDef { Sheet = "player_death",      State = "Die",       Trigger = "Die",        Fps = 8f,  Loop = false },
    };

    [MenuItem("Tools/재의 길/8방향 플레이어 애니메이션 생성")]
    public static void Build()
    {
        string spritePath = FindSpriteRendererPath();
        if (spritePath == null) return;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (ActionDef action in Actions) SliceSheet(action);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        EnsureFolder(ClipFolder);

        // 액션별로 방향 8개의 클립을 만들어 모아둔다.
        var clips = new Dictionary<string, AnimationClip[]>();
        foreach (ActionDef action in Actions)
        {
            AnimationClip[] made = BuildClips(action, spritePath);
            if (made != null) clips[action.State] = made;
        }

        BuildController(clips);
        AssetDatabase.SaveAssets();
        Debug.Log($"[8방향] 클립 {clips.Count * DirectionNames.Length}개와 컨트롤러를 만들었다.\n" +
                  $"컨트롤러: {ControllerPath}\n" +
                  "Player 프리팹의 Animator에 연결했다. PlayerController가 MoveX/MoveY를 넣어야 방향이 바뀐다.");
    }

    /// <summary>
    /// 시트를 256px 격자로 자르고 임포트 설정을 맞춘다.
    ///
    /// 스프라이트 이름을 <c>{시트}_{방향}_{프레임}</c>으로 붙이는 이유:
    /// 클립을 만들 때 이름으로 골라내야 순서가 보장된다. 유니티가 매기는 기본 이름(_0, _1 ...)은
    /// 격자 순서가 바뀌면 같이 바뀌어서 어느 방향인지 알 수 없다.
    /// </summary>
    private static void SliceSheet(ActionDef action)
    {
        string path = $"{SheetFolder}/{action.Sheet}.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[8방향] 시트를 못 찾았다: {path}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;

        // 원본 크기를 알아야 격자를 나눌 수 있다. 임포트된 텍스처는 최대 크기에 걸려 줄었을 수 있으므로
        // 임포터가 보고하는 원본 크기를 쓴다.
        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
        int cols = width / Cell;
        int rows = height / Cell;

        var sheet = new List<SpriteMetaData>();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                // 이미지 맨 윗줄이 S인데 유니티 rect는 아래가 0이다. 그래서 뒤집는다.
                int rectY = height - (row + 1) * Cell;
                string dir = row < DirectionNames.Length ? DirectionNames[row] : $"row{row}";

                sheet.Add(new SpriteMetaData
                {
                    name = $"{action.Sheet}_{dir}_{col:00}",
                    rect = new Rect(col * Cell, rectY, Cell, Cell),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(0.5f, PivotY),
                });
            }
        }

#pragma warning disable CS0618 // spritesheet은 구식이지만 격자 슬라이스에는 여전히 가장 짧고 확실하다.
        importer.spritesheet = sheet.ToArray();
#pragma warning restore CS0618

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    /// <summary>한 액션의 방향 8개 클립을 만든다.</summary>
    private static AnimationClip[] BuildClips(ActionDef action, string spritePath)
    {
        string path = $"{SheetFolder}/{action.Sheet}.png";
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

        // 이름으로 방향/프레임을 되찾는다. 로드 순서는 보장되지 않는다.
        var byDirection = new Dictionary<string, SortedDictionary<int, Sprite>>();
        foreach (Object asset in assets)
        {
            if (asset is not Sprite sprite) continue;
            string[] parts = sprite.name.Split('_');
            if (parts.Length < 2) continue;

            string dir = parts[parts.Length - 2];
            if (!int.TryParse(parts[parts.Length - 1], out int frame)) continue;

            if (!byDirection.TryGetValue(dir, out var frames))
            {
                frames = new SortedDictionary<int, Sprite>();
                byDirection[dir] = frames;
            }
            frames[frame] = sprite;
        }

        var result = new AnimationClip[DirectionNames.Length];
        for (int d = 0; d < DirectionNames.Length; d++)
        {
            string dir = DirectionNames[d];
            if (!byDirection.TryGetValue(dir, out var frames) || frames.Count == 0)
            {
                Debug.LogWarning($"[8방향] {action.Sheet}의 {dir} 방향 스프라이트를 못 찾았다.");
                return null;
            }

            var clip = new AnimationClip { frameRate = action.Fps };

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = spritePath,
                propertyName = "m_Sprite",
            };

            var keys = new List<ObjectReferenceKeyframe>();
            int index = 0;
            foreach (KeyValuePair<int, Sprite> pair in frames)
            {
                keys.Add(new ObjectReferenceKeyframe { time = index / action.Fps, value = pair.Value });
                index++;
            }
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = action.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string clipPath = $"{ClipFolder}/{action.State}_{dir}.anim";
            AssetDatabase.CreateAsset(clip, clipPath);
            result[d] = clip;
        }

        return result;
    }

    /// <summary>
    /// 컨트롤러를 새로 만든다.
    ///
    /// 기존 컨트롤러를 고치지 않고 새 파일로 만드는 이유:
    /// 지금 컨트롤러는 2방향 클립을 참조하는 상태와 전이로 가득하다. 그걸 고쳐 쓰면 옛 상태가
    /// 남아 어느 것이 실제로 도는지 알 수 없게 된다. 새로 만들면 문제가 생겨도
    /// 프리팹의 컨트롤러만 되돌리면 원상복구된다.
    /// </summary>
    private static void BuildController(Dictionary<string, AnimationClip[]> clips)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
        foreach (ActionDef action in Actions)
        {
            if (action.Trigger != null)
                controller.AddParameter(action.Trigger, AnimatorControllerParameterType.Trigger);
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var states = new Dictionary<string, AnimatorState>();

        foreach (ActionDef action in Actions)
        {
            if (!clips.TryGetValue(action.State, out AnimationClip[] directional)) continue;

            AnimatorState state = controller.CreateBlendTreeInController(action.State, out BlendTree tree);
            tree.blendType = BlendTreeType.SimpleDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveY";
            tree.name = action.State;

            for (int d = 0; d < DirectionNames.Length; d++)
            {
                tree.AddChild(directional[d], DirectionVectors[d]);
            }

            states[action.State] = state;
        }

        if (states.TryGetValue("Idle", out AnimatorState idle))
        {
            machine.defaultState = idle;

            // 걷기 전환은 속도로. 픽셀아트라 전이 시간은 0이어야 프레임이 겹쳐 보이지 않는다.
            if (states.TryGetValue("Walk", out AnimatorState walk))
            {
                AnimatorStateTransition toWalk = idle.AddTransition(walk);
                toWalk.hasExitTime = false;
                toWalk.duration = 0f;
                toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

                AnimatorStateTransition toIdle = walk.AddTransition(idle);
                toIdle.hasExitTime = false;
                toIdle.duration = 0f;
                toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            }
        }

        // 액션은 AnyState에서 트리거로 들어가고, 끝나면 Idle로 돌아온다.
        // AnyState를 쓰는 이유: 걷는 중이든 서 있든 어느 상태에서나 공격이 나가야 한다.
        foreach (ActionDef action in Actions)
        {
            if (action.Trigger == null) continue;
            if (!states.TryGetValue(action.State, out AnimatorState state)) continue;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.duration = 0f;
            // 같은 상태로 다시 들어가는 것을 막지 않으면 트리거가 연타될 때 첫 프레임만 반복된다.
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, action.Trigger);

            // 사망은 돌아오지 않는다. 마지막 프레임에서 멈춰야 시체가 남는다.
            if (action.State == "Die") continue;

            if (idle != null)
            {
                AnimatorStateTransition exit = state.AddTransition(idle);
                exit.hasExitTime = true;
                exit.exitTime = 1f;
                exit.duration = 0f;
            }
        }

        AssetDatabase.SaveAssets();
        AssignToPlayer(controller);
    }

    /// <summary>만든 컨트롤러를 플레이어 프리팹에 꽂는다.</summary>
    private static void AssignToPlayer(AnimatorController controller)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[8방향] 플레이어 프리팹을 못 열었다: {PlayerPrefabPath}");
            return;
        }

        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning("[8방향] 플레이어 프리팹에 Animator가 없다.");
                return;
            }

            animator.runtimeAnimatorController = controller;
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 클립이 건드릴 SpriteRenderer의 경로를 프리팹에서 알아낸다.
    ///
    /// 경로를 문자열로 박지 않는 이유: 스프라이트가 루트에 있는지 자식에 있는지에 따라 달라지고,
    /// 틀리면 클립이 아무것도 안 바꾸는데 에러도 안 난다. 가장 찾기 어려운 종류의 사고다.
    /// </summary>
    private static string FindSpriteRendererPath()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[8방향] 플레이어 프리팹을 못 열었다: {PlayerPrefabPath}");
            return null;
        }

        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            var renderer = root.GetComponentInChildren<SpriteRenderer>(true);
            if (animator == null || renderer == null)
            {
                Debug.LogError("[8방향] 플레이어 프리팹에 Animator 또는 SpriteRenderer가 없다.");
                return null;
            }

            return AnimationUtility.CalculateTransformPath(renderer.transform, animator.transform);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
