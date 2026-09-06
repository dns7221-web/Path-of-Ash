using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 재의 왕 2페이즈 전환 연출의 시트 이펙트 3개를 만든다.
/// 메뉴: Tools → 재의 길 → 보스 전환 이펙트 생성
///
/// <b>이 셋이 맡는 것은 "중심의 형상"이다.</b> 전환 연출은 둘로 나뉘어 있다 —
/// 넓은 공간에 흩어지는 재는 파티클이, 발밑 한 자리에서 벌어지는 <b>모양</b>은 이 시트가
/// 맡는다. 알이라는 모양은 파티클로 만들 수 없고, 화면을 덮고 멀리서 모여드는 이동은
/// 그림으로 그리면 프레임마다 위치가 고정돼 "화면 전체"가 안 된다.
///
/// 빈 오브젝트부터 쌓지 않고 <c>KingsEmber</c>를 복제하는 이유는 자폭병 폭발 빌더와 같다.
/// SpriteRenderer의 정렬 레이어, SpriteFrameAnimator의 재생 설정, 끝나면 스스로 사라지는
/// 처리가 이미 맞춰져 있다. 손으로 다시 조립하면 그중 하나가 어긋나는데, 그런 어긋남은
/// 에러가 아니라 "가끔 안 사라진다" 같은 형태로만 드러난다.
///
/// 수정(Timeline 도입) — <b>이제 만든 것을 보스 프리팹에 직접 넣는다.</b>
///
/// 처음에는 아무 데도 연결하지 않았다. 꽂을 곳이 없었고, 시트를 눈으로 보고 나서 시간표를
/// 짜기로 했기 때문이다. 지금은 시간표를 <c>PlayableDirector</c>가 들고 있는데, 그 바인딩은
/// <b>프리팹 안의 오브젝트만 가리킬 수 있다.</b> 런타임에 <c>Instantiate</c>로 만들어낸
/// 오브젝트는 타임라인이 켜고 끌 대상으로 잡을 수가 없다.
///
/// 그래서 셋 다 보스 프리팹의 <c>Transition</c> 자식으로 <b>꺼진 채</b> 들어간다.
/// 켜고 끄는 시각은 Activation Track이 소유한다.
/// </summary>
public static class AshBossTransitionBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/KingsEmber.prefab";
    private const string OutputFolder = "Assets/Project/Prefabs/VFX";
    private const string SheetFolder = "Assets/Project/Art/Sprites/VFX";
    private const int FrameCount = 6;

    // 추가 생성 — 만든 이펙트를 넣을 보스 프리팹과, 그 안에서 이펙트를 묶어둘 자식 이름.
    //
    // 하나로 묶는 이유: 보스 프리팹 루트에 이펙트 3개가 바로 붙으면 GroundShadow 같은
    // 원래 자식과 섞여서 <b>어디까지가 연출용인지</b>가 계층에서 안 읽힌다. 그리고 이
    // 컨테이너를 통째로 지웠다 다시 만드는 것이 곧 이 도구의 "다시 만들기"가 된다.
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemy/BossAshKing.prefab";
    private const string ContainerName = "Transition";

    /// <summary>
    /// 재생 속도. 6프레임 / 8fps = 0.75초로, 타임라인의 한 구간과 정확히 같다.
    ///
    /// 8을 고른 것은 그림이 6장뿐이기 때문이다. 더 빠르게 하면 한 구간이 0.75초보다 짧아져
    /// 시트가 끝난 뒤 <b>아무것도 없는 시간</b>이 생기고, 더 느리게 하면 다음 구간이 시작해도
    /// 이전 시트가 아직 돌고 있다. 프레임 수와 구간 길이가 fps를 이미 정해놓은 셈이다.
    /// </summary>
    private const float TransitionFps = 8f;

    /// <summary>
    /// 추가 생성 — 시트 3장의 배율. 자세한 근거는 아래 <see cref="Effects"/> 주석에 있다.
    /// 셋이 같은 값을 쓰므로 여기 한 곳만 고치면 된다.
    /// </summary>
    private const float SheetScale = 3f;

    /// <summary>
    /// 추가 생성 — 시트의 정렬 순서. 레이어는 원본(KingsEmber)에서 물려받은 <c>VFX</c>다.
    ///
    /// <c>VFX</c>는 이미 <c>Entity</c>(보스가 있는 레이어)보다 위라서 순서를 안 건드려도
    /// 보스 앞에 그려진다. 그런데도 명시하는 이유는 <b>같은 VFX 레이어 안에서의 다툼</b>
    /// 때문이다 — 재 파티클(장막 0, 파편 20)과 같은 레이어를 쓰므로, 순서를 비워두면
    /// 장막과 시트가 둘 다 0이 되어 어느 쪽이 앞인지가 상황에 따라 달라진다.
    ///
    /// 10은 장막(0)보다 앞, 파편(20)보다 뒤다. 알은 재 안개 앞에 서야 하고,
    /// 껍질 파편은 그 알보다도 앞으로 튀어나와야 한다.
    /// </summary>
    private const int SheetSortingOrder = 10;

    /// <summary>
    /// 만들 이펙트 목록.
    ///
    /// <b>loop만 다르고 destroy는 셋 다 꺼져 있다.</b>
    ///
    /// <c>gather</c>와 <c>shatter</c>는 한 번 재생하고 마지막 프레임에서 멈춘다.
    /// <c>egg</c>만 <b>반복한다</b> — 이 구간(1.625~2.375)은 일부러 둔 정적이고, 알은 그
    /// 0.75초 동안 <b>맥동해야</b> 한다. 한 번 재생하고 멈추면 정적이 아니라 정지가 된다.
    ///
    /// 수정(Timeline 도입) — <b>스스로 사라지는 이펙트가 하나도 없다.</b>
    ///
    /// 원래는 gather와 shatter가 재생을 마치면 자기 오브젝트를 지웠다. 이제 셋 다 보스
    /// 프리팹의 <b>자식으로 미리 들어가 꺼진 채</b> 시작하고, 켜고 끄는 일은 전환 타임라인의
    /// Activation Track이 맡는다. 자식이 스스로 파괴되면 <b>PlayableDirector의 바인딩이
    /// 끊긴다</b> — 그러면 그 판에서는 다시 켤 대상이 없어 아무 에러 없이 이펙트만 안 나온다.
    ///
    /// egg에만 적어뒀던 원칙("지우는 일은 시간표를 아는 쪽이 맡는다")이 셋 다에 적용된 셈이다.
    /// 수명을 여기서도 정하면 같은 시간이 두 곳에 적히고, 예전에 클립 0.875를 코드가 0.75로
    /// 알고 있어서 마지막 프레임을 못 봤던 것과 같은 어긋남이 다시 생긴다.
    ///
    /// 수정 3회차(녹화 프레임에서 실측) — <b>배율은 3이다.</b>
    ///
    /// 2.2로 올려도 여전히 작았다. 화면 1080px이 28유닛이므로 38.6px가 1유닛인데,
    /// 프레임에서 재보니 소용돌이가 430px(약 11유닛)이고 보스가 290px(약 7.5유닛)이었다.
    /// <b>보스의 1.5배밖에 안 된다.</b>
    ///
    /// 배율 2.2면 셀이 17.6유닛인데 실제로 보이는 그림은 11유닛이다. 즉 <b>그림이 셀의
    /// 63%만 채우고 있다.</b> 나머지는 투명한 여백이라 배율을 올려도 그만큼 안 커진다.
    /// 이 63%를 계산에 넣어야 원하는 크기가 나온다 — 보스의 2배(약 15유닛)를 보려면
    /// 셀이 15 ÷ 0.63 ≈ 24유닛이어야 하고, 그게 배율 3이다.
    ///
    /// 셀 24유닛은 화면 높이 28유닛 안이다. 여기가 상한이다.
    ///
    /// <b>여기까지 온 경위</b>(같은 자리를 세 번 고쳤으니 남겨둔다):
    ///
    /// <list type="bullet">
    /// <item><b>4.5~6.0</b> — 원본(KingsEmber)에서 그대로 베꼈다. 화면보다 컸다.</item>
    /// <item><b>1</b> — 셀을 유닛으로 환산해서 잡았다. 계산은 맞았지만 <b>그림이 셀을 다
    /// 안 채운다</b>는 것을 안 넣어서, 실제로 보이는 크기가 보스와 비슷했다.</item>
    /// <item><b>2.2</b> — 여백을 짐작으로 보정했다. 여전히 보스의 1.5배였다.</item>
    /// <item><b>3</b> — 녹화 프레임에서 픽셀을 재서 여백 비율(63%)을 구했다.</item>
    /// </list>
    ///
    /// 셀을 유닛으로 바꾸는 계산은 세 번 다 맞았다. 틀린 것은 <b>셀 크기와 그림 크기를
    /// 같은 것으로 본 것</b>이다. 그림이 셀의 63%만 채우니 둘은 애초에 다른 값이다.
    /// 자폭병 폭발 빌더의 경고("원본의 4.5는 그 그림에 맞춰 잰 값"), 즉 <b>배율은 그림마다
    /// 다시 재야 한다</b>는 말이 가리키던 것이 이것이었다.
    ///
    /// 환산의 바탕이 되는 실측값:
    ///
    /// <list type="bullet">
    /// <item>셀 256px / PPU 32 = <b>셀 하나가 8유닛</b></item>
    /// <item>카메라 orthographic size 14 = <b>화면 높이 28유닛</b></item>
    /// <item>보스(BossAshKing, 루트 스케일 1)의 그림 높이 = <b>약 7.5유닛</b></item>
    /// <item>시트 그림이 셀에서 차지하는 비율 = <b>약 63%</b></item>
    /// </list>
    ///
    /// 셋 다 같은 값으로 두고 <b>퍼지는 정도는 그림 자체에 맡긴다.</b> gather와 shatter가
    /// 알보다 넓어야 하는 것은 맞지만, 그건 시트에 이미 그렇게 그려져 있다. 여기서 배율로
    /// 또 벌리면 같은 의도가 두 곳에 적히고, 시트를 다시 뽑을 때마다 배율도 같이 맞춰야 한다.
    /// </summary>
    private static readonly (string sheet, string prefix, string output,
                             bool loop, bool destroyWhenFinished, float scale)[] Effects =
    {
        ("vfx_ashking_transition_gather_6frames_1536x256", "vfx_boss_transition_gather",
         "BossTransitionGather", false, false, SheetScale),

        ("vfx_ashking_transition_egg_6frames_1536x256", "vfx_boss_transition_egg",
         "BossTransitionEgg", true, false, SheetScale),

        ("vfx_ashking_transition_shatter_6frames_1536x256", "vfx_boss_transition_shatter",
         "BossTransitionShatter", false, false, SheetScale),
    };

    [MenuItem("Tools/재의 길/보스 전환 이펙트 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[보스 전환] 원본 이펙트를 못 찾았다: {SourcePath}");
            return;
        }

        int made = 0;
        foreach (var effect in Effects)
        {
            if (BuildOne(source, effect)) made++;
        }

        AssetDatabase.SaveAssets();

        if (made == 0)
        {
            Debug.LogError("[보스 전환] 하나도 못 만들었다. 위 로그를 봐라.");
            return;
        }

        // 추가 생성 — 재 파티클도 같이 만든다.
        //
        // 메뉴를 나누지 않은 이유: 시트와 파티클은 <b>같은 시간표를 나눠 맡은 한 벌</b>이다.
        // 따로 만들 수 있게 두면 하나만 다시 만드는 일이 생기고, 그러면 보스 프리팹 안의
        // 구성이 반쪽만 갱신된 채로 남는다.
        GameObject particles = AshBossTransitionParticleBuilder.Build();

        // 추가 생성 — 만든 프리팹을 보스 자식으로 넣는다.
        //
        // 전부 만들어진 뒤에 한 번만 하는 이유: 중간에 실패한 시트가 있으면 그 자리만
        // 비는 게 아니라 <b>컨테이너를 지웠다 다시 만드는 도중에 멈춘다.</b> 셋을 다 만들고
        // 나서 넣으면, 실패한 것은 경고로 알리고 나머지는 제자리에 들어간다.
        bool attached = AttachToBoss();

        // 추가 생성 — 시간표(타임라인)와 보스 쪽 배선. 자식이 들어간 뒤라야 바인딩할 대상이 있다.
        if (attached) AshBossTransitionTimelineBuilder.Build();

        Debug.Log($"[보스 전환] 시트 이펙트 {made}/{Effects.Length}개, " +
                  $"재 파티클 {(particles != null ? "1" : "0")}개 생성 완료 ({OutputFolder}).\n" +
                  (attached
                      ? $"보스 프리팹의 {ContainerName} 자식으로 넣었다(전부 꺼진 상태다). " +
                        "켜고 끄는 시각은 전환 타임라인의 Activation Track이 정한다.\n"
                      : "보스 프리팹에는 못 넣었다. 위 로그를 봐라.\n") +
                  "배율은 AshBossTransitionBuilder.Effects 표에서 고친다.");
    }

    /// <summary>
    /// 추가 생성 — 만든 이펙트 3개를 보스 프리팹의 <see cref="ContainerName"/> 자식으로 넣는다.
    /// 전부 <b>꺼진 채</b> 들어간다. 켜는 일은 전환 타임라인이 한다.
    ///
    /// <b>왜 프리팹 안에 넣는가:</b> <c>PlayableDirector</c>의 트랙 바인딩은 프리팹 안의
    /// 오브젝트만 가리킬 수 있다. 예전처럼 런타임에 <c>Instantiate</c>하면 타임라인이 켜고
    /// 끌 대상 자체가 없다.
    ///
    /// <b>localPosition을 0으로 두는 이유:</b> 이 프로젝트는 발바닥을 원점으로 쓰고, 시트
    /// 3장도 <c>Mode.GroundCenter</c>로 정규화해서 보스와 발 기준선이 같다. 여기서 y를
    /// 조금이라도 올리면 <b>같은 기준선을 두 곳에서 관리</b>하게 되고, 시트를 다시 뽑을 때
    /// 이 숫자도 같이 맞춰야 한다.
    ///
    /// <c>PrefabUtility.InstantiatePrefab</c>으로 넣어서 <b>프리팹 링크를 살려둔다.</b>
    /// 링크가 있으면 이 도구를 다시 돌려 VFX 프리팹만 갱신해도 보스 안의 것이 따라온다.
    /// </summary>
    private static bool AttachToBoss()
    {
        GameObject bossRoot = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        if (bossRoot == null)
        {
            Debug.LogError($"[보스 전환] 보스 프리팹을 못 찾았다: {BossPrefabPath}");
            return false;
        }

        try
        {
            // 이미 있으면 통째로 지우고 다시 만든다. 남겨두고 덧붙이면 실행할 때마다
            // 같은 이펙트가 하나씩 쌓이고, 겹쳐 그려지는 것은 "조금 진해졌다"로만 보인다.
            Transform existing = bossRoot.transform.Find(ContainerName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var container = new GameObject(ContainerName);
            container.transform.SetParent(bossRoot.transform, false);

            foreach (var effect in Effects)
                AddChild(container.transform, $"{OutputFolder}/{effect.output}.prefab", effect.output);

            // 추가 생성 — 재 파티클도 같은 컨테이너에 들어간다.
            AddChild(container.transform, AshBossTransitionParticleBuilder.PrefabPath,
                     "BossTransitionAsh");

            PrefabUtility.SaveAsPrefabAsset(bossRoot, BossPrefabPath);
        }
        finally
        {
            // 열어둔 프리팹 내용은 반드시 닫는다. 안 닫으면 에디터에 유령 씬이 남는다.
            PrefabUtility.UnloadPrefabContents(bossRoot);
        }

        return true;
    }

    /// <summary>
    /// 이펙트 하나를 만든다. 프레임을 못 찾으면 만들지 않고 false를 돌려준다.
    ///
    /// 하나가 실패해도 나머지를 계속 만드는 이유: 시트 3장이 각각 다른 이유로 실패할 수
    /// 있는데(한 장만 정규화를 안 돌렸다든지), 첫 실패에서 멈추면 <b>남은 두 장도 문제가
    /// 있는지</b>를 한 번에 알 수 없다. 세 번 실행하면서 하나씩 알아내게 된다.
    /// </summary>
    private static bool BuildOne(
        GameObject source,
        (string sheet, string prefix, string output, bool loop, bool destroyWhenFinished, float scale) effect)
    {
        string sheetPath = $"{SheetFolder}/{effect.sheet}.png";

        List<Sprite> frames = LoadFrames(sheetPath, effect.prefix);
        if (frames.Count != FrameCount)
        {
            Debug.LogError(
                $"[보스 전환] {effect.output}: {sheetPath}에서 프레임을 {frames.Count}개만 " +
                $"찾았다 (필요: {FrameCount}).\n" +
                "Tools → 재의 길 → 원본 시트 정규화 (고른 것만) 과 " +
                "Tools → 재의 길 → VFX 스프라이트 슬라이스 를 차례로 먼저 실행해라.");
            return false;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            instance.name = effect.output;
            instance.transform.localScale = Vector3.one * effect.scale;

            ApplyFrames(instance, frames, effect.loop, effect.destroyWhenFinished);

            string outputPath = $"{OutputFolder}/{effect.output}.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, outputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[보스 전환] 프리팹 저장에 실패했다: {outputPath}");
                return false;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        return true;
    }

    /// <summary>
    /// 추가 생성 — 프리팹 하나를 컨테이너 밑에 <b>꺼진 채</b> 넣는다.
    ///
    /// 못 찾으면 경고만 하고 넘어간다. 여기서 멈추면 뒤에 올 것들이 통째로 안 들어가는데,
    /// 그러면 "하나가 없다"가 "아무것도 안 들어갔다"로 보인다.
    /// </summary>
    private static void AddChild(Transform container, string prefabPath, string childName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[보스 전환] {childName}: 프리팹이 없어서 보스에 못 넣었다 ({prefabPath}).");
            return;
        }

        var child = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
        child.name = childName;
        child.transform.localPosition = Vector3.zero;

        // 꺼둔 채 들어간다. 켜져 있으면 보스가 방에 놓이는 순간부터 알이 맥동하고 재가 날린다.
        child.SetActive(false);
    }

    /// <summary>슬라이스된 프레임을 순서대로 읽는다.</summary>
    private static List<Sprite> LoadFrames(string sheetPath, string prefix)
    {
        var found = new Dictionary<string, Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
        {
            if (asset is Sprite sprite) found[sprite.name] = sprite;
        }

        // 이름 순서가 아니라 번호 순서로 담는다. LoadAllAssetsAtPath가 돌려주는 순서는
        // 보장되지 않아서, 그대로 쓰면 껍질이 깨졌다 다시 붙는 것처럼 재생될 수 있다.
        var frames = new List<Sprite>(FrameCount);
        for (int i = 0; i < FrameCount; i++)
        {
            if (found.TryGetValue($"{prefix}_{i:00}", out Sprite sprite)) frames.Add(sprite);
        }

        return frames;
    }

    /// <summary>
    /// 그림과 재생 설정을 복제본에 넣는다.
    ///
    /// SpriteRenderer의 첫 프레임까지 같이 넣는 이유: 애니메이터가 첫 프레임을 넣기 전
    /// 한 프레임 동안 원본(KingsEmber) 그림이 그대로 보인다. 0.016초지만 이 연출은
    /// 화면이 조용해진 순간에 나오므로 특히 눈에 띈다.
    /// </summary>
    private static void ApplyFrames(
        GameObject instance, List<Sprite> frames, bool loop, bool destroyWhenFinished)
    {
        var renderer = instance.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer != null)
        {
            renderer.sprite = frames[0];

            // 추가 생성 — 정렬 순서를 못박는다. 레이어(VFX)는 원본에서 물려받는다.
            renderer.sortingOrder = SheetSortingOrder;
        }

        var animator = instance.GetComponentInChildren<SpriteFrameAnimator>(true);
        if (animator == null)
        {
            Debug.LogWarning($"[보스 전환] {instance.name}: SpriteFrameAnimator를 못 찾았다. " +
                             "첫 프레임만 뜨고 애니메이션이 안 돈다.");
            return;
        }

        var serialized = new SerializedObject(animator);

        SerializedProperty list = serialized.FindProperty("frames");
        list.arraySize = frames.Count;
        for (int i = 0; i < frames.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

        serialized.FindProperty("fps").floatValue = TransitionFps;
        serialized.FindProperty("loop").boolValue = loop;
        serialized.FindProperty("destroyWhenFinished").boolValue = destroyWhenFinished;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
