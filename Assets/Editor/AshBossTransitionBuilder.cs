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
/// <b>이 도구는 아무 데도 연결하지 않는다.</b> 자폭병 폭발 빌더는 만든 프리팹을 자폭병에
/// 꽂아주지만, 여기서 꽂을 곳(<c>BossTransitionSequence</c>)은 아직 없다. 시트를 먼저
/// 만들어 눈으로 보고 나서 시간표를 짜기로 했기 때문이다 — 3.125초 타임라인은 결국
/// 그림을 보면서 조정할 값이라, 뼈대부터 세우면 빈 화면에 숫자만 맞추게 된다.
/// </summary>
public static class AshBossTransitionBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/KingsEmber.prefab";
    private const string OutputFolder = "Assets/Project/Prefabs/VFX";
    private const string SheetFolder = "Assets/Project/Art/Sprites/VFX";
    private const int FrameCount = 6;

    /// <summary>
    /// 재생 속도. 6프레임 / 8fps = 0.75초로, 타임라인의 한 구간과 정확히 같다.
    ///
    /// 8을 고른 것은 그림이 6장뿐이기 때문이다. 더 빠르게 하면 한 구간이 0.75초보다 짧아져
    /// 시트가 끝난 뒤 <b>아무것도 없는 시간</b>이 생기고, 더 느리게 하면 다음 구간이 시작해도
    /// 이전 시트가 아직 돌고 있다. 프레임 수와 구간 길이가 fps를 이미 정해놓은 셈이다.
    /// </summary>
    private const float TransitionFps = 8f;

    /// <summary>
    /// 만들 이펙트 목록.
    ///
    /// <b>loop와 destroy가 셋 다 다른 것이 핵심이다.</b>
    ///
    /// <c>gather</c>는 한 번 재생하고 스스로 사라진다. 0.75초 뒤가 곧 알이 서는 시각이라,
    /// 알과 겹쳐 남아 있으면 모여들던 재가 알 위에 덧그려진다.
    ///
    /// <c>egg</c>는 <b>반복하고 스스로 사라지지 않는다.</b> 이 구간(1.625~2.375)은 일부러
    /// 둔 정적이고, 알은 그 0.75초 동안 <b>맥동해야</b> 한다. 한 번 재생하고 멈추면 정적이
    /// 아니라 정지가 된다 — 둘은 화면에서 전혀 다르게 읽힌다. 지우는 일은 시간표를 아는
    /// 쪽(BossTransitionSequence)이 맡는다. 여기서 수명을 정하면 길이를 두 곳에 적게 된다.
    ///
    /// <c>shatter</c>는 한 번 재생하고 사라진다. 깨진 껍질이 화면에 남아 있으면
    /// 2페이즈 보스가 등장하는 자리를 가린다.
    ///
    /// 배율은 <b>눈으로 맞출 값이라 여기 모아둔다.</b> 지금 값은 원본(KingsEmber, 4.5)을
    /// 기준으로 어림잡은 것이다. 장막에서 모여드는 gather와 파편이 퍼지는 shatter는
    /// 알보다 넓은 자리를 쓰므로 크게 잡았다.
    /// </summary>
    private static readonly (string sheet, string prefix, string output,
                             bool loop, bool destroyWhenFinished, float scale)[] Effects =
    {
        ("vfx_ashking_transition_gather_6frames_1536x256", "vfx_boss_transition_gather",
         "BossTransitionGather", false, true, 6.0f),

        ("vfx_ashking_transition_egg_6frames_1536x256", "vfx_boss_transition_egg",
         "BossTransitionEgg", true, false, 4.5f),

        ("vfx_ashking_transition_shatter_6frames_1536x256", "vfx_boss_transition_shatter",
         "BossTransitionShatter", false, true, 6.0f),
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

        Debug.Log($"[보스 전환] 이펙트 {made}/{Effects.Length}개 생성 완료 ({OutputFolder}).\n" +
                  "보스 방에 끌어다 놓고 크기와 발 높이를 눈으로 확인해라. " +
                  "배율은 AshBossTransitionBuilder.Effects 표에서 고친다.");
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
        if (renderer != null) renderer.sprite = frames[0];

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
