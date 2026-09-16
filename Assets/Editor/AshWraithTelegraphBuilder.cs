using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-15) — 잿불 망령의 돌진 예고선 프리팹을 만들고 망령 프리팹에 연결한다.
/// 메뉴: Tools → 재의 길 → 망령 돌진 예고선 생성
///
/// 순서: 원본 정규화(Tools/NormalizeVfxStrip.ps1 -AnchorX LeftEdge -PivotX 28) → VFX 스프라이트 슬라이스 → 이 메뉴.
///
/// 빈 오브젝트부터 쌓지 않고 <c>DashBurst</c>를 복제하는 이유는 자폭병 폭발 빌더와 같다. SpriteRenderer의
/// 머티리얼과 SpriteFrameAnimator 구성이 이미 게임에서 도는 이펙트라, 손으로 다시 조립하다 어긋날 곳이 없다.
/// 여러 이펙트 중 대시 자국을 고른 이유는 같은 종류라서다 — 둘 다 "출발점에서 오른쪽으로 뻗게 그리고,
/// 방향으로 돌려 놓는" 그림이다.
///
/// 복제 후 바꾸는 것: 프레임 그림, 재생 설정(반복 안 함·끝나도 안 지움), 정렬, 그림 속 선 길이 측정값.
///
/// 수정(2026-09-15) — 두 가지를 더했다.
/// - 그림 속 선의 <b>굵기</b>도 잰다. 예고선이 판정 굵기에 맞춰 세로로도 늘어나게 됐기 때문이다(TelegraphLine 설명 참고).
/// - 메뉴 "망령 돌진 출발 자국 생성"을 같은 클래스에 뒀다. 둘 다 망령 돌진에 붙는 그림이고, 대시 자국을 복제해
///   프레임을 갈아 끼우고 망령 프리팹에 꽂는 흐름이 같다. 따로 만들면 그 흐름을 두 벌 들고 있게 된다.
///   출발 자국 순서: 원본 정규화(Tools/NormalizeVfxStrip.ps1 -SeparateBlobs -AnchorX LeftEdge -AnchorY WidestRow -PivotX 28)
///   → VFX 스프라이트 슬라이스 → 이 메뉴.
/// </summary>
public static class AshWraithTelegraphBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/DashBurst.prefab";
    private const string OutputPath = "Assets/Project/Prefabs/VFX/WraithChargeTelegraph.prefab";
    private const string WraithPrefabPath = "Assets/Project/Prefabs/Enemies/AshEmberWraith.prefab";

    // 추가 생성(2026-09-15) — 출발 자국.
    private const string LaunchOutputPath = "Assets/Project/Prefabs/VFX/WraithChargeLaunch.prefab";
    private const string LaunchSheetPath =
        "Assets/Project/Art/Sprites/VFX/vfx_wraith_charge_launch_6frames_1536x256.png";
    private const string LaunchSpritePrefix = "vfx_wraith_launch";

    /// <summary>
    /// 추가 생성(2026-09-15) — 출발 자국 재생 속도. 6프레임 / 16fps = 0.375초.
    /// 돌진(0.34초)보다 조금 길게 남아서, 몸이 멈춘 뒤에도 "어디서 튀어나왔는지"가 한 박자 보인다. 생성 프롬프트가 요구한 속도다.
    /// </summary>
    private const float LaunchFps = 16f;

    /// <summary>
    /// 추가 생성(2026-09-15) — 출발 자국 크기. 배율 1에서 고리 폭이 약 4유닛으로, 새 망령의 몸 콜라이더 폭(4)과 같다.
    /// 딛고 나간 발자리보다 크면 폭발처럼 읽혀서 공격 판정이 있는 것으로 오해한다.
    /// </summary>
    private const float LaunchScale = 1f;

    private const string SheetPath =
        "Assets/Project/Art/Sprites/VFX/vfx_wraith_charge_telegraph_6frames_1536x256.png";
    private const string SpritePrefix = "vfx_wraith_telegraph";
    private const int FrameCount = 6;

    /// <summary>
    /// 기본 재생 속도. 6프레임 / 15fps = 0.4초 = 망령의 예비동작(windupSeconds).
    ///
    /// 게임에서는 망령이 예비동작 시간을 넘겨 다시 계산한다(<see cref="SpriteFrameAnimator.Restart"/>).
    /// 여기 값은 프리팹을 씬에 끌어다 놓고 볼 때의 속도이고, 원본 프롬프트가 요구한 속도와 같다.
    /// </summary>
    private const float DefaultFps = 15f;

    /// <summary>
    /// 정렬 레이어. Decal은 "바닥에 눌어붙는 것"의 자리다(AshProjectSetup — 핏자국, 그을음, 장판).
    ///
    /// 예고선은 바닥에 그어지는 선이라 캐릭터(Entity)보다 아래에 그린다. 그래야 망령의 몸이 선의 출발점을
    /// 덮고, 선 위에 선 플레이어가 선에 가려지지 않는다. VFX 레이어(대시 자국의 자리)에 두면 캐릭터 위를
    /// 가로질러 그려져서 바닥의 선이 아니라 공중에 뜬 화살처럼 보인다.
    /// </summary>
    private const string SortingLayer = "Decal";

    /// <summary>
    /// 같은 Decal 레이어의 바닥 소품(잔해·제단·계단, 순서 0)보다 위. 경고가 장식에 묻히면 안 된다.
    /// </summary>
    private const int SortingOrder = 1;

    [MenuItem("Tools/재의 길/망령 돌진 예고선 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[망령 예고선] 복제할 원본 이펙트를 못 찾았다: {SourcePath}");
            return;
        }

        // 수정(2026-09-15) — 시트 경로와 접두어를 넘기게 바꿨다. 출발 자국 메뉴도 같은 함수를 쓴다.
        List<Sprite> frames = LoadFrames(SheetPath, SpritePrefix);
        if (frames.Count != FrameCount)
        {
            Debug.LogError($"[망령 예고선] {SheetPath}에서 프레임을 {frames.Count}개만 찾았다 " +
                           $"(필요: {FrameCount}). Tools → 재의 길 → VFX 스프라이트 슬라이스 를 먼저 실행해라.");
            return;
        }

        float drawnLength = MeasureDrawnLength(frames);
        if (drawnLength <= 0f)
        {
            Debug.LogError($"[망령 예고선] {SheetPath}에서 그려진 선을 못 찾았다. 시트가 비었거나 PNG를 못 읽었다.");
            return;
        }

        // 추가 생성(2026-09-15) — 선 굵기. 길이를 잰 뒤라 시트는 이미 읽힌다는 것이 확인된 상태다.
        float drawnThickness = MeasureDrawnThickness(frames);

        GameObject instance = Object.Instantiate(source);
        try
        {
            instance.name = "WraithChargeTelegraph";

            // 원본(대시 자국)의 1.2배는 그 그림에 맞춘 값이다. 예고선의 크기는 망령이 돌진마다 정하므로 1로 둔다.
            instance.transform.localScale = Vector3.one;

            // 수정(2026-09-15) — 재생 속도·끝나면 지울지를 넘기게 바꿨다. 예고선은 15fps, 지우지 않는다(아래 ApplyVisual 설명).
            ApplyVisual(instance, frames, DefaultFps, false);
            ApplyLine(instance, drawnLength, drawnThickness);

            PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[망령 예고선] 프리팹 저장에 실패했다: {OutputPath}");
                return;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        ConnectToWraith(drawnLength, drawnThickness);
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 망령 돌진 출발 자국 프리팹을 만들고 망령 프리팹에 연결한다.
    /// 메뉴: Tools → 재의 길 → 망령 돌진 출발 자국 생성
    ///
    /// 예고선과 달리 <b>끝나면 스스로 지운다.</b> 망령에 붙어 재사용되는 것이 아니라 돌진마다 출발 자리에 새로 놓이는 자국이라서다
    /// (복제 원본인 대시 자국과 같은 설정). 정렬은 예고선과 같은 Decal 1이다 — 바닥이 갈라져 터진 자리라 캐릭터 밑에 깔려야 하고,
    /// VFX 레이어면 튀어 나가는 망령의 몸 위에 갈라진 바닥이 그려진다. 선이 걷히는 순간 자국이 나오므로 둘이 같은 자리를 다투지 않는다.
    /// </summary>
    [MenuItem("Tools/재의 길/망령 돌진 출발 자국 생성")]
    public static void BuildLaunch()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[망령 출발 자국] 복제할 원본 이펙트를 못 찾았다: {SourcePath}");
            return;
        }

        List<Sprite> frames = LoadFrames(LaunchSheetPath, LaunchSpritePrefix);
        if (frames.Count != FrameCount)
        {
            Debug.LogError($"[망령 출발 자국] {LaunchSheetPath}에서 프레임을 {frames.Count}개만 찾았다 " +
                           $"(필요: {FrameCount}). Tools → 재의 길 → VFX 스프라이트 슬라이스 를 먼저 실행해라.");
            return;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            instance.name = "WraithChargeLaunch";
            instance.transform.localScale = Vector3.one * LaunchScale;

            ApplyVisual(instance, frames, LaunchFps, true);

            PrefabUtility.SaveAsPrefabAsset(instance, LaunchOutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[망령 출발 자국] 프리팹 저장에 실패했다: {LaunchOutputPath}");
                return;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();

        var launchRoot = AssetDatabase.LoadAssetAtPath<GameObject>(LaunchOutputPath);
        if (launchRoot == null)
        {
            Debug.LogError($"[망령 출발 자국] 만든 프리팹을 다시 못 읽었다: {LaunchOutputPath}");
            return;
        }

        SetWraithField("chargeLaunchEffectPrefab", launchRoot,
            $"[망령 출발 자국] {LaunchOutputPath} 생성 후 망령에 연결했다. {FrameCount}프레임 / {LaunchFps}fps, 배율 {LaunchScale}.",
            $"[망령 출발 자국] {LaunchOutputPath} 생성 완료.");
    }

    /// <summary>
    /// 슬라이스된 프레임을 번호 순서대로 읽는다.
    /// 수정(2026-09-15) — 예고선 전용이던 것을 시트 경로와 접두어를 받게 바꿨다. 출발 자국도 쓴다.
    /// </summary>
    private static List<Sprite> LoadFrames(string sheetPath, string spritePrefix)
    {
        var found = new Dictionary<string, Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
        {
            if (asset is Sprite sprite) found[sprite.name] = sprite;
        }

        // LoadAllAssetsAtPath가 돌려주는 순서는 보장되지 않는다. 그대로 담으면 선이 뒤죽박죽으로 자란다.
        var frames = new List<Sprite>(FrameCount);
        for (int i = 0; i < FrameCount; i++)
        {
            if (found.TryGetValue($"{spritePrefix}_{i:00}", out Sprite sprite)) frames.Add(sprite);
        }

        return frames;
    }

    /// <summary>
    /// 그림 속 선이 피벗(출발점)부터 가장 먼 불투명 픽셀까지 몇 유닛인지 잰다. 여섯 프레임 중 가장 긴 값을 쓴다.
    ///
    /// 칸 크기(256px)가 아니라 실제로 그려진 끝을 재는 이유: 칸 오른쪽에는 여백이 있어서, 칸 크기를 기준으로
    /// 늘리면 화살촉이 판정 끝보다 짧게 멈춘다. 자폭병 폭발 배율을 불투명 영역으로 잰 것과 같은 판단이다.
    ///
    /// 임포트된 텍스처가 아니라 PNG 파일을 직접 읽는 이유: 텍스처 픽셀을 읽으려면 임포트 설정의 Read/Write를
    /// 켜야 하는데, 그러면 게임 텍스처가 메모리에 한 벌 더 남는다. 빌드 때 한 번 재는 일에 그 값을 치를 이유가 없다.
    /// </summary>
    private static float MeasureDrawnLength(List<Sprite> frames)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(SheetPath))) return 0f;

            // GetPixels32는 아래 줄부터 담는다. Sprite.rect도 아래가 y=0이라 좌표를 그대로 쓸 수 있다.
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            float longest = 0f;

            foreach (Sprite frame in frames)
            {
                Rect rect = frame.rect;
                int x0 = Mathf.Max(0, (int)rect.xMin);
                int x1 = Mathf.Min(width, (int)rect.xMax);
                int y0 = Mathf.Max(0, (int)rect.yMin);
                int y1 = Mathf.Min(height, (int)rect.yMax);

                // 오른쪽 끝 열부터 왼쪽으로 훑어 처음 만나는 불투명 열이 선의 끝이다.
                int right = -1;
                for (int x = x1 - 1; x >= x0 && right < 0; x--)
                {
                    for (int y = y0; y < y1; y++)
                    {
                        if (pixels[y * width + x].a == 0) continue;
                        right = x;
                        break;
                    }
                }

                if (right < 0) continue;

                // Sprite.pivot은 칸 왼쪽 아래 기준 픽셀 좌표다. 끝 픽셀의 오른쪽 가장자리(right + 1)까지 잰다.
                float pivotX = rect.xMin + frame.pivot.x;
                float length = (right + 1 - pivotX) / frame.pixelsPerUnit;
                longest = Mathf.Max(longest, length);
            }

            return longest;
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 그림 속 선의 굵기(유닛)를 잰다. 프레임마다 불투명 픽셀의 세로 범위를 재서 <b>가운데 값</b>을 쓴다.
    ///
    /// 가장 큰 값을 쓰지 않는 이유: 마지막 프레임은 돌진 직전에 선이 갈라지며 위아래로 퍼지는 그림(지금 시트 84px)이라,
    /// 그걸 기준으로 삼으면 선이 자라는 동안 대부분의 프레임이 판정보다 가늘게 늘어난다. 가장 작은 값은 가늘게 시작하는
    /// 첫 프레임(40px)이다. 가운데 값으로 맞추면 플레이어가 선을 읽고 판단하는 중반 이후 프레임이 판정 폭 이상이 된다.
    ///
    /// PNG를 직접 읽는 이유는 <see cref="MeasureDrawnLength"/>와 같다.
    /// </summary>
    private static float MeasureDrawnThickness(List<Sprite> frames)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(SheetPath))) return 0f;

            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            var extents = new List<float>(frames.Count);

            foreach (Sprite frame in frames)
            {
                Rect rect = frame.rect;
                int x0 = Mathf.Max(0, (int)rect.xMin);
                int x1 = Mathf.Min(width, (int)rect.xMax);
                int y0 = Mathf.Max(0, (int)rect.yMin);
                int y1 = Mathf.Min(height, (int)rect.yMax);

                // GetPixels32는 아래 줄부터 담는다. 세로 범위만 재므로 위아래가 뒤집혀도 결과는 같다.
                int bottom = -1;
                int top = -1;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        if (pixels[y * width + x].a == 0) continue;
                        if (bottom < 0) bottom = y;
                        top = y;
                        break;
                    }
                }

                if (bottom < 0) continue;
                extents.Add((top - bottom + 1) / frame.pixelsPerUnit);
            }

            if (extents.Count == 0) return 0f;

            extents.Sort();
            int middle = extents.Count / 2;
            return extents.Count % 2 == 1 ? extents[middle] : (extents[middle - 1] + extents[middle]) * 0.5f;
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// 그림, 정렬, 재생 설정을 복제본에 넣는다.
    ///
    /// SpriteRenderer의 첫 프레임까지 넣는 이유: 재생기가 첫 프레임을 넣기 전 한 프레임 동안
    /// 원본(대시 자국) 그림이 그대로 보인다.
    ///
    /// 수정(2026-09-15) — 재생 속도와 끝나면 지울지를 받게 바꿨다. 예고선(15fps, 안 지움)과 출발 자국(16fps, 지움)이 같이 쓴다.
    /// </summary>
    private static void ApplyVisual(GameObject instance, List<Sprite> frames, float fps, bool destroyWhenFinished)
    {
        var renderer = instance.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            renderer.sprite = frames[0];
            renderer.sortingLayerName = SortingLayer;
            renderer.sortingOrder = SortingOrder;
        }

        var animator = instance.GetComponent<SpriteFrameAnimator>();
        if (animator == null)
        {
            Debug.LogWarning("[망령 예고선] SpriteFrameAnimator를 못 찾았다. 첫 프레임만 뜨고 선이 자라지 않는다.");
            return;
        }

        var serialized = new SerializedObject(animator);

        SerializedProperty list = serialized.FindProperty("frames");
        list.arraySize = frames.Count;
        for (int i = 0; i < frames.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

        // 수정(2026-09-15) — 상수(DefaultFps) 대신 넘겨받은 값을 쓴다. 예고선은 여전히 DefaultFps를 넘긴다.
        serialized.FindProperty("fps").floatValue = fps;

        // 반복하지 않는다. 한 번 자라서 갈라지는 그림이고, 갈라지는 순간이 돌진이다.
        // (출발 자국도 한 번 터지고 가라앉는 그림이라 반복하지 않는다.)
        serialized.FindProperty("loop").boolValue = false;

        // 끝나도 지우지 않는다. 망령에 자식으로 붙어 돌진마다 다시 켜진다.
        // 원본(대시 자국)은 지우는 설정이라, 그대로 두면 첫 돌진 뒤 선이 사라져 두 번째 돌진부터 선이 없다.
        // 수정(2026-09-15) — 출발 자국은 돌진마다 새로 만들어지는 월드 오브젝트라 반대로 지워야 한다. 그래서 값을 넘겨받는다.
        serialized.FindProperty("destroyWhenFinished").boolValue = destroyWhenFinished;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 예고선 컴포넌트를 붙이고 잰 선 길이를 넣는다.
    /// 수정(2026-09-15) — 잰 선 굵기도 넣는다. 0이면(못 쟀으면) 컴포넌트의 기본값을 그대로 둔다.
    /// </summary>
    private static void ApplyLine(GameObject instance, float drawnLength, float drawnThickness)
    {
        var line = instance.GetComponent<TelegraphLine>();
        if (line == null) line = instance.AddComponent<TelegraphLine>();

        var serialized = new SerializedObject(line);
        serialized.FindProperty("drawnLength").floatValue = drawnLength;
        if (drawnThickness > 0f) serialized.FindProperty("drawnThickness").floatValue = drawnThickness;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 만든 예고선을 망령 프리팹의 Charge Telegraph Prefab 칸에 꽂는다.
    ///
    /// 망령 프리팹이 아직 없으면 조용히 넘어간다. 망령 프리팹 빌더도 이 예고선이 이미 있으면 알아서 꽂으므로
    /// 어느 쪽을 먼저 돌려도 된다(사수 화살 빌더와 같은 약속).
    ///
    /// 수정(2026-09-15) — 프리팹을 열어 칸 하나를 바꾸는 부분을 SetWraithField로 뺐다. 출발 자국도 같은 일을 한다.
    /// </summary>
    private static void ConnectToWraith(float drawnLength, float drawnThickness)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        var line = root != null ? root.GetComponent<TelegraphLine>() : null;
        if (line == null)
        {
            Debug.LogError("[망령 예고선] 만든 프리팹에서 TelegraphLine을 못 찾았다.");
            return;
        }

        SetWraithField("chargeTelegraphPrefab", line,
            $"[망령 예고선] {OutputPath} 생성 후 망령에 연결했다.\n" +
            $"그림 속 선 길이 {drawnLength:F3}유닛, 굵기 {drawnThickness:F3}유닛(배율 1). " +
            "게임에서는 돌진마다 판정이 닿는 거리까지 가로로, 판정 폭까지 세로로 늘린다.",
            $"[망령 예고선] {OutputPath} 생성 완료.");
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 망령 프리팹의 EnemyWraith 칸 하나에 참조를 꽂고 저장한다.
    ///
    /// 망령 프리팹이 아직 없으면 조용히 넘어간다. 망령 프리팹 빌더(AshEnemyPrefabBuilder)가 이미 만들어진 예고선·출발 자국을
    /// 알아서 꽂으므로 어느 쪽을 먼저 돌려도 된다.
    /// </summary>
    /// <param name="fieldName">EnemyWraith의 직렬화 필드 이름.</param>
    /// <param name="value">꽂을 에셋.</param>
    /// <param name="doneLog">연결했을 때 남길 로그.</param>
    /// <param name="noWraithLog">망령 프리팹이 없을 때 남길 로그의 앞부분.</param>
    private static void SetWraithField(string fieldName, Object value, string doneLog, string noWraithLog)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(WraithPrefabPath) == null)
        {
            Debug.Log($"{noWraithLog}\n망령 프리팹이 아직 없다. '잿불 망령 프리팹 생성'을 실행하면 꽂힌다.");
            return;
        }

        // 프리팹을 새로 조립하지 않고 열어서 칸 하나만 바꾼다. 사람이 인스펙터에서 맞춘 값(콜라이더 등)을 지키기 위해서다.
        GameObject wraith = PrefabUtility.LoadPrefabContents(WraithPrefabPath);
        try
        {
            var ai = wraith.GetComponent<EnemyWraith>();
            if (ai == null)
            {
                Debug.LogError("[망령 프리팹] 망령 프리팹에 EnemyWraith가 없다.");
                return;
            }

            var serialized = new SerializedObject(ai);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[망령 프리팹] EnemyWraith에 '{fieldName}' 칸이 없다. 스크립트가 컴파일됐는지 확인해라.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(wraith, WraithPrefabPath);
            Debug.Log(doneLog);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(wraith);
        }
    }
}
