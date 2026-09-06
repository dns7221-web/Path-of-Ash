using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 잿불 자폭병의 폭발 이펙트 프리팹을 만들고 자폭병 프리팹에 연결한다.
/// 메뉴: Tools → 재의 길 → 자폭병 폭발 이펙트 생성
///
/// <b>왜 이펙트가 필수인가.</b> 자폭병 시트에는 터지는 그림이 없다. 재의 왕 궁극기에서
/// 배운 것과 같은 이유로 일부러 뺐다 — 폭발을 256 셀 안에 그리면 판정 반경을 전달할
/// 방법이 없다. 그래서 이 프리팹이 <b>유일한 폭발 연출이자, 반경을 알려주는 유일한 수단</b>이다.
/// 이게 없으면 플레이어는 아무것도 안 보이는데 체력만 깎인다.
///
/// 빈 오브젝트부터 쌓지 않고 <c>KingsEmber</c>를 복제하는 이유는 잿불 파도 빌더와 같다.
/// SpriteRenderer의 정렬 레이어, SpriteFrameAnimator의 재생 설정, 끝나면 스스로 사라지는
/// 처리가 이미 맞춰져 있다. 손으로 다시 조립하면 그중 하나가 어긋나는데, 그런 어긋남은
/// 에러가 아니라 "가끔 안 사라진다" 같은 형태로만 드러난다.
///
/// 복제 후 바꾸는 것은 두 가지다 — 프레임 그림, 크기.
/// </summary>
public static class AshBomberBlastBuilder
{
    private const string SourcePath = "Assets/Project/Prefabs/VFX/KingsEmber.prefab";
    private const string OutputPath = "Assets/Project/Prefabs/VFX/BomberBlast.prefab";
    private const string BomberPrefabPath = "Assets/Project/Prefabs/Enemies/AshBomber.prefab";

    private const string SheetPath =
        "Assets/Project/Art/Sprites/VFX/vfx_bomber_blast_6frames_1536x256.png";
    private const string SpritePrefix = "vfx_bomber_blast";
    private const int FrameCount = 6;

    /// <summary>
    /// 재생 속도. 6프레임 / 12fps = 0.5초.
    ///
    /// 판정은 한 순간에 끝나지만 <b>왜 맞았는지</b>는 그 뒤에도 보여야 한다. 너무 빠르면
    /// 맞은 직후 화면에 아무것도 안 남아서 자기가 뭘 잘못했는지 알 수 없고, 너무 느리면
    /// 이미 끝난 폭발이 계속 떠 있어 아직 위험한 것처럼 보인다.
    /// </summary>
    private const float BlastFps = 12f;

    /// <summary>
    /// 프리팹 자체의 크기. 실제 크기는 <c>EnemyBomber.explosionEffectScale</c>이 여기에 곱한다.
    ///
    /// 1로 두는 이유: 원본(KingsEmber)의 4.5는 <b>그 그림</b>에 맞춰 잰 값이라 여기 들고 오면
    /// 뜻이 없다. 폭발 반경에 맞추는 일은 자폭병 쪽 배율 하나에서만 하도록 자리를 하나로 둔다.
    /// </summary>
    private const float BaseScale = 1f;

    [MenuItem("Tools/재의 길/자폭병 폭발 이펙트 생성")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[자폭병 폭발] 원본 이펙트를 못 찾았다: {SourcePath}");
            return;
        }

        List<Sprite> frames = LoadFrames();
        if (frames.Count != FrameCount)
        {
            Debug.LogError($"[자폭병 폭발] {SheetPath}에서 프레임을 {frames.Count}개만 찾았다 " +
                           $"(필요: {FrameCount}). Tools → 재의 길 → VFX 스프라이트 슬라이스 를 " +
                           "먼저 실행해라.");
            return;
        }

        GameObject instance = Object.Instantiate(source);
        try
        {
            instance.name = "BomberBlast";
            instance.transform.localScale = Vector3.one * BaseScale;

            ApplyFrames(instance, frames);

            PrefabUtility.SaveAsPrefabAsset(instance, OutputPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"[자폭병 폭발] 프리팹 저장에 실패했다: {OutputPath}");
                return;
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        ConnectToBomber();
    }

    /// <summary>슬라이스된 폭발 프레임을 순서대로 읽는다.</summary>
    private static List<Sprite> LoadFrames()
    {
        var found = new Dictionary<string, Sprite>();
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SheetPath))
        {
            if (asset is Sprite sprite) found[sprite.name] = sprite;
        }

        // 이름 순서가 아니라 번호 순서로 담는다. LoadAllAssetsAtPath가 돌려주는 순서는
        // 보장되지 않아서, 그대로 쓰면 폭발이 뒤죽박죽으로 재생될 수 있다.
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
    /// 한 프레임 동안 원본(KingsEmber) 그림이 그대로 보인다. 0.016초지만 폭발은 늘
    /// 화면 한가운데에서 터지므로 눈에 띈다.
    /// </summary>
    private static void ApplyFrames(GameObject instance, List<Sprite> frames)
    {
        var renderer = instance.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer != null) renderer.sprite = frames[0];

        var animator = instance.GetComponentInChildren<SpriteFrameAnimator>(true);
        if (animator == null)
        {
            Debug.LogWarning("[자폭병 폭발] SpriteFrameAnimator를 못 찾았다. " +
                             "첫 프레임만 뜨고 애니메이션이 안 돈다.");
            return;
        }

        var serialized = new SerializedObject(animator);

        SerializedProperty list = serialized.FindProperty("frames");
        list.arraySize = frames.Count;
        for (int i = 0; i < frames.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

        serialized.FindProperty("fps").floatValue = BlastFps;

        // 폭발은 한 번 터지고 끝나야 한다. 반복하면 그 자리가 계속 위험해 보이고,
        // 스스로 안 사라지면 방마다 폭발 자국이 쌓인다.
        serialized.FindProperty("loop").boolValue = false;
        serialized.FindProperty("destroyWhenFinished").boolValue = true;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>만든 이펙트를 자폭병 프리팹의 Explosion Effect Prefab 칸에 꽂는다.</summary>
    private static void ConnectToBomber()
    {
        var blast = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
        if (blast == null)
        {
            Debug.LogError("[자폭병 폭발] 만든 프리팹을 다시 못 읽었다.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(BomberPrefabPath) == null)
        {
            Debug.Log($"[자폭병 폭발] {OutputPath} 생성 완료.\n" +
                      "자폭병 프리팹이 아직 없다. '잿불 자폭병 프리팹 생성'을 실행하면 이 이펙트가 꽂힌다.");
            return;
        }

        GameObject bomber = PrefabUtility.LoadPrefabContents(BomberPrefabPath);
        try
        {
            var ai = bomber.GetComponent<EnemyBomber>();
            if (ai == null)
            {
                Debug.LogError("[자폭병 폭발] 자폭병 프리팹에 EnemyBomber가 없다.");
                return;
            }

            var serialized = new SerializedObject(ai);
            serialized.FindProperty("explosionEffectPrefab").objectReferenceValue = blast;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(bomber, BomberPrefabPath);
            Debug.Log($"[자폭병 폭발] {OutputPath} 생성 후 자폭병에 연결했다.\n" +
                      "남은 일: 폭발 그림의 불투명 영역과 판정 반경이 맞는지 재고 " +
                      "Explosion Effect Scale을 조정해라. 셀 크기가 아니라 실제로 그려진 " +
                      "부분을 기준으로 재야 한다 — 그림이 셀을 다 안 채운다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(bomber);
        }
    }
}
