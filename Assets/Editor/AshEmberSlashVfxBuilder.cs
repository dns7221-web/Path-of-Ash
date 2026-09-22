using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 기본 공격(Ctrl, 잿불 베기)의 검 궤적 이펙트 프리팹을 만들고
/// <c>Skill_Basic_AshSlash</c>에 연결한다.
///
/// 메뉴: Tools → 재의 길 → 프리팹 → 기본 공격 베기 이펙트 생성
///
/// <b>왜 이 이펙트가 필요한가.</b> 기본 공격은 QWER 네 스킬과 달리 이펙트가 하나도 없었다.
/// 캐릭터가 검을 휘두르는 그림은 있지만 <b>검이 지나간 자리에 아무것도 안 남는다.</b>
/// 게임에서는 그게 "때렸다"가 아니라 "팔을 흔들었다"로 읽힌다. 히트스톱까지 넣어서 타격감을
/// 만들어놨는데 정작 무엇에 맞았는지 보여주는 그림이 기본 공격에만 없는 상태였다.
///
/// 빈 오브젝트부터 쌓지 않고 <c>SlamImpact</c>를 복제하는 이유는 자폭병 폭발 빌더와 같다.
/// SpriteRenderer의 정렬 레이어(VFX 레이어 5 — Entity 위에 그려진다), 재생 설정, 끝나면
/// 스스로 사라지는 처리가 이미 맞춰져 있다. 손으로 다시 조립하면 그중 하나가 어긋나는데,
/// 그런 어긋남은 에러가 아니라 "가끔 궤적이 캐릭터 뒤에 숨는다" 같은 형태로만 드러난다.
///
/// 복제 후 바꾸는 것은 세 가지다 — 프레임 그림, 재생 속도, 크기.
/// </summary>
public static class AshEmberSlashVfxBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/SlamImpact.prefab";
    private const string OutputPath = "Assets/Project/Prefabs/VFX/EmberSlash.prefab";
    private const string SkillPath = "Assets/Project/Data/Skills/Skill_Basic_AshSlash.asset";

    private const string SheetPath =
        "Assets/Project/Art/Sprites/VFX/vfx_ember_slash_6frames_1536x256.png";
    private const string SpritePrefix = "vfx_ember_slash";
    private const int FrameCount = 6;

    /// <summary>
    /// 재생 속도. 6프레임 / 20fps = 0.30초.
    ///
    /// 스킬 에셋의 숫자에서 나온 값이다. 기본 공격은 모션이 0.43초이고 이펙트는
    /// hitboxDelay(0.12초)에 생긴다. 남는 시간이 0.31초뿐이라 그 안에 여섯 프레임을 끝내야
    /// <b>모션이 끝나 다시 움직이는데 궤적만 공중에 남아 있는</b> 그림이 안 나온다.
    ///
    /// 자폭병 폭발(12fps)이나 Q 충격파(14fps)보다 빠른 이유: 저쪽은 "여기가 위험하다"를
    /// 계속 보여줘야 하는 장판성 연출이고, 이건 한 번 스치고 지나가는 검이다.
    /// 느리면 궤적이 허공에 걸려 있어서 벤 게 아니라 벽에 그림을 붙인 것처럼 보인다.
    /// </summary>
    private const float SlashFps = 20f;

    /// <summary>
    /// 프리팹 크기. 그림 200px을 VFX PPU 32로 나누면 6.25유닛인데, 그대로 두면
    /// 초승달이 캐릭터 키(6.67유닛)만 해진다. 기본 공격 한 번에 화면 절반이 번쩍인다.
    ///
    /// 0.65는 초승달 높이를 4.06유닛(캐릭터 키의 61%)으로 만드는 값이다. 검 판정
    /// (AttackHitbox 세로 3.67유닛)보다 조금 크게 잡았다 — 판정과 정확히 같은 크기로 두면
    /// 그림 가장자리에 닿았는데 안 맞는 것처럼 보이는 구간이 생긴다. 그림이 판정보다
    /// <b>조금 넉넉한</b> 편이 "닿았는데 안 맞았다"는 불평을 덜 만든다.
    ///
    /// 눈으로 맞추는 값이라 정답은 없다. Play 모드에서 보고 이 상수를 고친 뒤 메뉴를 다시 돌린다.
    /// </summary>
    private const float BaseScale = 0.65f;

    [MenuItem("Tools/재의 길/프리팹/기본 공격 베기 이펙트 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[기본 공격 베기] 복제할 원본 이펙트를 못 찾았다: {SourcePath}");
            return;
        }

        List<Sprite> frames = LoadFrames();
        if (frames.Count != FrameCount)
        {
            Debug.LogError($"[기본 공격 베기] {SheetPath}에서 프레임을 {frames.Count}개만 찾았다 " +
                           $"(필요: {FrameCount}).\n" +
                           "Tools → 재의 길 → 그림 → 원본 시트 정규화 (고른 것만) 으로 " +
                           "vfx_ember_slash_6frames_raw.png 를 정규화하고, " +
                           "Tools → 재의 길 → 그림 → VFX 스프라이트 슬라이스 를 먼저 실행해라.");
            return;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            // 추가 생성(2026-09-17, 플레이어 파티클) — 원본(SlamImpact)의 곁들임(돌 파편·재 먼지)은 가져오지 않는다.
            // 참격에 붙일 불티는 저장한 뒤 아래 EnsureGarnish가 따로 넣는다.
            AshPlayerParticleBuilder.StripGarnish(instance);

            instance.name = "EmberSlash";
            instance.transform.localScale = Vector3.one * BaseScale;

            ApplyFrames(instance, frames);

            PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[기본 공격 베기] 프리팹 저장에 실패했다: {OutputPath}");
                return;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        ConnectToSkill();

        // 추가 생성(2026-09-17) — 새로 구운 프리팹에는 곁들임이 없다. 참격 불티(공격-1)를 다시 넣는다.
        AshPlayerParticleBuilder.EnsureGarnish(OutputPath);
    }

    /// <summary>슬라이스된 궤적 프레임을 순서대로 읽는다.</summary>
    private static List<Sprite> LoadFrames()
    {
        var found = new Dictionary<string, Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SheetPath))
        {
            if (asset is Sprite sprite) found[sprite.name] = sprite;
        }

        // 이름 순서가 아니라 번호 순서로 담는다. LoadAllAssetsAtPath가 돌려주는 순서는
        // 보장되지 않아서, 그대로 쓰면 초승달이 자랐다 줄었다 하며 뒤죽박죽 재생된다.
        var frames = new List<Sprite>(FrameCount);
        for (int i = 0; i < FrameCount; i++)
        {
            if (found.TryGetValue($"{SpritePrefix}_{i:00}", out Sprite sprite)) frames.Add(sprite);
        }

        return frames;
    }

    /// <summary>
    /// 그림과 재생 설정을 복제본에 넣는다.
    ///
    /// SpriteRenderer의 첫 프레임까지 같이 넣는 이유: 애니메이터가 첫 프레임을 넣기 전
    /// 한 프레임 동안 원본(SlamImpact) 그림이 그대로 보인다. 0.016초지만 기본 공격은
    /// 게임에서 가장 자주 쓰는 동작이라 그 한 프레임을 수백 번 보게 된다.
    /// </summary>
    private static void ApplyFrames(GameObject instance, List<Sprite> frames)
    {
        var renderer = instance.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer != null) renderer.sprite = frames[0];

        var animator = instance.GetComponentInChildren<SpriteFrameAnimator>(true);
        if (animator == null)
        {
            Debug.LogWarning("[기본 공격 베기] SpriteFrameAnimator를 못 찾았다. " +
                             "첫 프레임만 뜨고 애니메이션이 안 돈다.");
            return;
        }

        var serialized = new SerializedObject(animator);

        SerializedProperty list = serialized.FindProperty("frames");
        list.arraySize = frames.Count;
        for (int i = 0; i < frames.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

        serialized.FindProperty("fps").floatValue = SlashFps;

        // 한 번 베고 끝나야 한다. 반복하면 검을 계속 휘두르는 것처럼 보이고,
        // 스스로 안 사라지면 공격할 때마다 궤적이 방에 쌓인다 — 기본 공격은 쿨다운이
        // 0.65초라 다른 어떤 스킬보다 많이 쌓인다.
        serialized.FindProperty("loop").boolValue = false;
        serialized.FindProperty("destroyWhenFinished").boolValue = true;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>만든 이펙트를 기본 공격 스킬 에셋의 Effect Prefab 칸에 꽂는다.</summary>
    private static void ConnectToSkill()
    {
        var slash = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        if (slash == null)
        {
            Debug.LogError("[기본 공격 베기] 만든 프리팹을 다시 못 읽었다.");
            return;
        }

        var skill = AssetDatabase.LoadAssetAtPath<MeleeSkillData>(SkillPath);
        if (skill == null)
        {
            Debug.LogError($"[기본 공격 베기] 기본 공격 스킬 에셋을 못 찾았다: {SkillPath}");
            return;
        }

        var serialized = new SerializedObject(skill);
        serialized.FindProperty("effectPrefab").objectReferenceValue = slash;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(skill);
        AssetDatabase.SaveAssets();

        Debug.Log($"[기본 공격 베기] {OutputPath} 생성 후 기본 공격에 연결했다.\n" +
                  "남은 일(눈으로 맞추는 값): Play 모드에서 여덟 방향으로 다 쳐보고 " +
                  "궤적이 검을 따라가는지 확인해라. 어긋나면 스킬 에셋의 Effect Height(가슴 높이), " +
                  "Effect Forward Offset(앞 거리), 이 스크립트의 BaseScale(크기)을 고친다.");
    }
}
