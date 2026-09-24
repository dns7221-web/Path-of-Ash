using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 추가 생성(2026-09-19, 보스 궁극기 영상) — 영상 테스트 씬(Assets/Scenes/Video.unity)에 컷인 재생 장치를 꾸린다.
/// 메뉴: Tools → 재의 길 → 씬·세팅 → 영상 테스트 씬 구성
///
/// 씬을 손으로 조립하지 않고 도구로 두는 이유는 다른 빌더들과 같다. 캔버스·띠·화면·VideoPlayer·RenderTexture 사이의
/// 연결이 열 개 가까이 되는데, 하나만 빠져도 "검은 화면만 나온다"처럼 원인이 안 보이는 모양으로 드러난다.
///
/// 만드는 것(다시 돌리면 UltimateCutscene을 지우고 새로 만든다 — 테스트 씬이라 손으로 고친 값을 지킬 이유가 없다):
/// <list type="bullet">
/// <item>RenderTexture 1920×1080(없을 때만) — VideoPlayer가 그리고 RawImage가 보여준다</item>
/// <item>UltimateCutscene(오버레이 캔버스): 어둠 판 · 영상 화면(16:9 유지) · 위아래 띠 + VideoPlayer + <see cref="CutsceneVideo"/>
/// + <see cref="CutsceneVideoTester"/>(스페이스 재생, Esc 건너뛰기)</item>
/// <item>뒤 배경으로 던전 방 그림 — 띠와 어둠이 게임 화면 위에 어떻게 덮이는지 보이게</item>
/// </list>
/// </summary>
public static class AshVideoTestSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Video.unity";
    private const string Folder = "Assets/Project/Art/BossUltimateVideo";
    // 수정(2026-09-20) — v2 → v3. 사이 그림 12장을 넣어 다시 뽑은 판이다(컷 하나 안에서 자세가 바뀐다).
    // v1·v2는 지웠다. 형식(Baseline + BT.709)은 그대로다 — 유니티가 못 푸는 판을 피하려는 설정이라 v3도 같다.
    private const string ClipPath = Folder + "/boss_ultimate_v3.mp4";
    private const string TexturePath = Folder + "/BossUltimateVideo.renderTexture";
    private const string BackgroundPath = "Assets/Project/Art/Sprites/Dungeon/Room_v2.png";

    private const string RootName = "UltimateCutscene";
    private const string BackgroundName = "TestBackground";

    /// <summary>
    /// 추가 생성(2026-09-19) — 플레이 중에는 메뉴를 흐리게 막는다. 씬을 열고 저장하는 일은 플레이 모드에서 할 수 없어서
    /// (EditorSceneManager가 InvalidOperationException을 던진다) 눌러도 오류만 난다.
    /// </summary>
    [MenuItem("Tools/재의 길/씬·세팅/영상 테스트 씬 구성", true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/재의 길/씬·세팅/영상 테스트 씬 구성")]
    public static void Build()
    {
        // 추가 생성(2026-09-19) — 단축키 등으로 막힌 메뉴를 우회해 불렸을 때를 위한 두 번째 문. 이유는 CanBuild 참고.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[영상 테스트] 플레이 중에는 씬을 구성할 수 없다. 플레이를 멈추고 다시 눌러라.");
            return;
        }

        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(ClipPath);
        if (clip == null)
        {
            Debug.LogError($"[영상 테스트] 영상을 못 찾았다: {ClipPath}\n" +
                           "Tools/make_boss_ultimate_video.py 로 먼저 만들고, 유니티가 VideoClip으로 읽었는지 확인해라.");
            return;
        }

        // 지금 열린 씬에 저장 안 한 변경이 있으면 먼저 물어본다. 취소하면 아무것도 안 한다.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        RenderTexture texture = EnsureRenderTexture();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 수정(2026-09-24) — 예전에는 GameObject.Find(RootName)로 이름을 찾아 지웠다. Find는 켜진 오브젝트만 찾고
        // 이름이 겹치면 어느 쪽이 걸릴지 모른다 — 실제로 씬에 같은 이름의 "손으로 만든 판"(켜짐)과 옛 도구 판(꺼짐)이
        // 있어서, 메뉴를 누르면 손으로 만든 판이 지워지고 옛 도구 판은 남을 상황이었다.
        // 이제 이름이 아니라 CutsceneVideo 컴포넌트로, 꺼진 것까지 찾아 도구 판만 지운다.
        foreach (var old in Object.FindObjectsByType<CutsceneVideo>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);

        BuildCutscene(clip, texture, withTester: true);
        EnsureBackground();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[영상 테스트] Video 씬 구성 완료. 플레이하면 1초 뒤 한 번 재생된다 — 스페이스 = 다시 재생, Esc = 건너뛰기.\n" +
                  "확인할 것: 띠가 들어온 뒤 영상이 끊김 없이 시작하는지, 마지막 흰 화면이 걷히며 방 그림이 다시 보이는지, " +
                  "재생하는 동안 게임 시간이 멈춰 있어도(timeScale 0) 영상이 도는지.");
    }

    /// <summary>
    /// 추가 생성(2026-09-24) — 지금 열린 씬(보통 Game)에 궁극기 컷인을 넣는다. 테스터와 배경은 빼고 넣는다.
    /// 왕관 의식(CrownRitual)이 못 막았을 때 씬에서 CutsceneVideo를 찾아 재생한다.
    /// 이미 있으면 지우고 새로 만든다(이름이 아니라 컴포넌트로 찾는다). 저장은 사용자가 직접 한다(Ctrl+S).
    /// </summary>
    [MenuItem("Tools/재의 길/씬·세팅/궁극기 컷인 게임 씬에 추가", true)]
    private static bool CanAddToScene() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/재의 길/씬·세팅/궁극기 컷인 게임 씬에 추가")]
    public static void AddToCurrentScene()
    {
        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(ClipPath);
        if (clip == null)
        {
            Debug.LogError($"[궁극기 컷인] 영상을 못 찾았다: {ClipPath}");
            return;
        }

        foreach (var old in Object.FindObjectsByType<CutsceneVideo>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);

        BuildCutscene(clip, EnsureRenderTexture(), withTester: false);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[궁극기 컷인] 지금 씬에 넣었다. Ctrl+S로 저장해라. 왕관 의식을 못 막으면 이 컷인이 재생된다.");
    }

    /// <summary>1920×1080 RenderTexture. 이미 있으면 그대로 쓴다.</summary>
    private static RenderTexture EnsureRenderTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(TexturePath);
        if (existing != null) return existing;

        // 깊이 버퍼는 필요 없다 — 영상 프레임을 그대로 받기만 한다.
        var texture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32) { name = "BossUltimateVideo" };
        AssetDatabase.CreateAsset(texture, TexturePath);
        AssetDatabase.SaveAssets();
        return texture;
    }

    /// <summary>캔버스 · 어둠 · 화면 · 띠 · VideoPlayer · 재생 컴포넌트를 만들고 서로 잇는다.</summary>
    private static void BuildCutscene(VideoClip clip, RenderTexture texture, bool withTester)
    {
        var root = new GameObject(RootName);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 게임 HUD보다 위. 컷인 동안에는 체력바도 덮여야 한다.
        canvas.sortingOrder = 100;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 그리는 순서 = 자식 순서. 어둠 → 영상 → 띠(띠가 영상 위아래를 조금 덮는다).
        var dim = CreateUI("Dim", root.transform).gameObject.AddComponent<Image>();
        Stretch(dim.rectTransform);
        dim.color = new Color(0f, 0f, 0f, 0f);
        dim.raycastTarget = false;

        var screenRect = CreateUI("Screen", root.transform);
        var screen = screenRect.gameObject.AddComponent<RawImage>();
        screen.texture = texture;
        screen.color = new Color(1f, 1f, 1f, 0f);
        screen.raycastTarget = false;

        // 수정(2026-09-19, 검은 화면) — AspectRatioFitter를 걷어냈다. 이 도구로 만들 때 판 크기가 0×0으로 저장되고
        // 실행 중에도 안 바로잡혀서, 영상은 도는데 그림이 안 보였다. 이제 편집기에서는 화면 가득 펼쳐 두고,
        // 16:9 맞춤은 재생하는 순간 CutsceneVideo.FitScreen이 직접 계산한다.
        Stretch(screenRect);

        RectTransform topBar = CreateBar("TopBar", root.transform, top: true);
        RectTransform bottomBar = CreateBar("BottomBar", root.transform, top: false);

        var player = root.AddComponent<VideoPlayer>();
        player.source = VideoSource.VideoClip;
        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.playOnAwake = false;
        player.waitForFirstFrame = true;
        player.isLooping = false;
        // 수정(2026-09-24, 검은 화면) — true → false. 게임이 멈춘 실시간 재생에서 중간 프레임이 전부 버려졌다.
        // 이유는 CutsceneVideo.Awake 주석 참고(거기서도 한 번 더 못 박는다).
        player.skipOnDrop = false;
        player.aspectRatio = VideoAspectRatio.FitInside;
        // 소리는 게임 효과음으로 따로 낸다. 영상에는 소리 트랙이 없다.
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

        var cutscene = root.AddComponent<CutsceneVideo>();
        var serialized = new SerializedObject(cutscene);
        serialized.FindProperty("videoPlayer").objectReferenceValue = player;
        serialized.FindProperty("screen").objectReferenceValue = screen;
        serialized.FindProperty("dim").objectReferenceValue = dim;
        serialized.FindProperty("topBar").objectReferenceValue = topBar;
        serialized.FindProperty("bottomBar").objectReferenceValue = bottomBar;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 수정(2026-09-24) — 게임 씬에 넣을 때는 테스터(스페이스 재생·timeScale 조작)를 뺀다.
        if (!withTester) return;

        var tester = root.AddComponent<CutsceneVideoTester>();
        var testerSerialized = new SerializedObject(tester);
        testerSerialized.FindProperty("cutscene").objectReferenceValue = cutscene;
        testerSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 뒤 배경으로 던전 방 그림을 깐다. 카메라 크기를 게임과 같은 15.9로 맞추고 그림이 화면을 채우게 늘린다.
    /// 이미 있으면 건드리지 않는다.
    /// </summary>
    private static void EnsureBackground()
    {
        if (GameObject.Find(BackgroundName) != null) return;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        var camera = Camera.main;
        if (sprite == null || camera == null) return;

        camera.orthographic = true;
        camera.orthographicSize = 15.9f;

        var background = new GameObject(BackgroundName);
        var renderer = background.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;

        float viewHeight = camera.orthographicSize * 2f;
        float viewWidth = viewHeight * 16f / 9f;
        Vector3 size = sprite.bounds.size;
        float scale = Mathf.Max(viewWidth / size.x, viewHeight / size.y);
        background.transform.position = new Vector3(camera.transform.position.x, camera.transform.position.y, 0f);
        background.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private static RectTransform CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>화면 위(또는 아래)에 붙은 검은 띠. 높이 0에서 시작하고 CutsceneVideo가 키운다.</summary>
    private static RectTransform CreateBar(string name, Transform parent, bool top)
    {
        RectTransform rect = CreateUI(name, parent);
        float y = top ? 1f : 0f;
        rect.anchorMin = new Vector2(0f, y);
        rect.anchorMax = new Vector2(1f, y);
        rect.pivot = new Vector2(0.5f, y);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 0f);

        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        return rect;
    }
}
