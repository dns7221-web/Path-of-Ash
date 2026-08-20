using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 타이틀 화면 연출을 씬에 붙이는 도구.
///
/// 메뉴: Tools → 재의 길 → 타이틀 연출 구성
///
/// 컨셉은 <b>"불이 꺼진 뒤에도 잿더미는 아직 뜨겁다"</b> — 정지 이미지가 아니라 식어가는
/// 중인 화면으로 보이게 한다. 타이틀 아트에 이미 붉은 잉걸이 흩뿌려져 있으므로,
/// 새로 그리지 않고 <b>이미 그려진 잉걸을 움직이게 하는</b> 방향이 가장 싸고 확실하다.
///
/// 밋밋했던 원인은 연출 코드가 없어서가 아니라 <see cref="TitleCameraDrift"/>와
/// <see cref="BlinkingText"/>가 완성돼 있는데 <b>씬에 붙어 있지 않았기</b> 때문이다.
/// 그래서 이 도구가 하는 일의 절반은 "이미 있는 것을 연결"하는 것이다.
///
/// <b>이미 붙어 있으면 건드리지 않는다.</b> 인스펙터에서 맞춰둔 값을 도구가 되돌리면
/// 조정할 때마다 다시 맞춰야 한다. 소품 배치 도구와 같은 판단이다.
/// </summary>
public static class AshTitleSceneBuilder
{
    private const string TargetSceneName = "Title";
    private const string EmberRootName = "TitleEmbers";

    [MenuItem("Tools/재의 길/타이틀 연출 구성")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != TargetSceneName)
        {
            Debug.LogError($"[타이틀 연출] 활성 씬이 '{scene.name}'이다. Title 씬을 열고 실행해라.");
            return;
        }

        int added = 0;

        added += AttachCameraDrift();
        added += AttachBlinkingText();
        added += CreateEmberParticles();
        added += CreateVolume();
        added += AttachIntroSequence();

        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[타이틀 연출] {added}개 구성 완료. 씬을 저장해라(Ctrl+S).\n" +
                  "Bloom과 비네트는 URP Volume 설정이라 Project Settings에서 따로 켜야 한다.");
    }

    /// <summary>
    /// 카메라에 켄번스 드리프트를 붙인다.
    ///
    /// 배경 스프라이트가 아니라 카메라를 움직이는 이유: UI가 Screen Space - Overlay라
    /// 카메라의 영향을 안 받는다. 배경과 파티클만 흐르고 제목은 제자리에 고정된다.
    /// </summary>
    private static int AttachCameraDrift()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("[타이틀 연출] Main Camera를 못 찾았다.");
            return 0;
        }

        if (camera.GetComponent<TitleCameraDrift>() != null) return 0;

        camera.gameObject.AddComponent<TitleCameraDrift>();
        return 1;
    }

    /// <summary>안내 문구에 맥동을 붙인다.</summary>
    private static int AttachBlinkingText()
    {
        var target = GameObject.Find("PressAnyKeyText");
        if (target == null)
        {
            Debug.LogWarning("[타이틀 연출] PressAnyKeyText를 못 찾았다.");
            return 0;
        }

        if (target.GetComponent<BlinkingText>() != null) return 0;

        target.AddComponent<BlinkingText>();
        return 1;
    }

    /// <summary>
    /// 화면 아래에서 천천히 올라오는 잉걸 파티클.
    ///
    /// 카메라가 20 x 11.25유닛을 비추므로 화면 아래쪽 바깥(y = -7)에서 뿜어 올린다.
    /// 수명을 8초 안팎으로 길게 둔 이유: 불티가 화면을 가로질러 올라가는 동안 계속 보여야
    /// "식어가는 중"으로 읽힌다. 빨리 사라지면 그냥 반짝임이 된다.
    /// </summary>
    private static int CreateEmberParticles()
    {
        // 수정: "이미 있으면 건너뛰기"에서 "매번 다시 만들기"로 바꿨다.
        //
        // 건너뛰게 두니 색·크기를 조정하고 도구를 돌려도 화면이 그대로였다. 손으로 지우고
        // 다시 실행해야 했는데, 그 단계를 잊으면 "코드는 고쳤는데 안 바뀐다"가 된다.
        // 파티클은 지금 값을 계속 만지는 중이라 매번 새로 만드는 쪽이 맞다.
        // (인스펙터에서 직접 맞춘 값이 있다면 이 실행으로 사라진다.)
        var previous = GameObject.Find(EmberRootName);
        if (previous != null) Object.DestroyImmediate(previous);

        var root = new GameObject(EmberRootName);
        root.transform.position = new Vector3(0f, -7f, 0f);

        var particles = root.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.duration = 10f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 10f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        // 수정(첫 확인 후): 주황 잉걸에서 회색 재로. 크기도 줄였다.
        //
        // 주황으로 두니 불티가 배경보다 튀어서 픽셀 조각을 뿌려놓은 것처럼 보였다.
        // 이 화면의 주인공은 배경 일러스트와 인물이고 파티클은 공기여야 한다.
        // 재는 회색이고, 붉은 것은 아직 안 식은 일부뿐이다 — 그 관계를 아래
        // colorOverLifetime이 만든다(아래쪽은 살짝 붉고 올라갈수록 회색).
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.69f, 0.66f, 1f), new Color(0.42f, 0.40f, 0.39f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;

        var emission = particles.emission;
        emission.rateOverTime = 14f;

        // 화면 가로 전체에서 올라오게 한다. 한 점에서 나오면 굴뚝처럼 보인다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 0.5f, 1f);

        // 흔들림. 이게 없으면 일직선으로 올라가서 기계처럼 보인다.
        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.15f;

        // 올라가면서 사그라든다. 불티는 위로 갈수록 식는다.
        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        // 막 피어오른 재만 잉걸빛이 남아 있고, 올라가면서 식어 회색이 된다.
        // 이 색 변화가 "불이 꺼진 뒤에도 잿더미는 아직 뜨겁다"를 화면에서 말해주는 부분이다.
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.52f, 0.24f), 0f),   // 아래 — 아직 뜨겁다
                new GradientColorKey(new Color(0.85f, 0.72f, 0.62f), 0.2f),
                new GradientColorKey(Color.white, 0.5f),                 // 위 — 다 식은 재
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.8f, 0.15f),  // 55%는 너무 눌러서 밋밋했다
                new GradientAlphaKey(0.6f, 0.6f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // 추가 — 재 조각은 납작해서 공중에서 팔랑거린다. 회전이 없으면 네모가 그대로
        // 위로 미끄러져서 "떠다니는 먼지"가 아니라 "올라가는 점"으로 보인다.
        var rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-90f * Mathf.Deg2Rad, 90f * Mathf.Deg2Rad);

        var startRotation = particles.main.startRotation;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        _ = startRotation;

        // 추가 — 옆으로도 흘러야 공기가 있는 것처럼 보인다. 위로만 가면 분수가 된다.
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;

        // 세 축을 모두 같은 모드(두 상수 범위)로 줘야 한다.
        // x만 범위로 주고 y·z를 단일 상수로 두면 유니티가
        // "Particle Velocity curves must all be in the same mode" 에러를 낸다.
        velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0.15f);   // 살짝 더 밀어 올린다
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

        // 배경 위, UI 아래에 그린다. 배경 스프라이트와 같은 정렬 레이어면 순서가 뒤집힌다.
        renderer.sortingOrder = 10;

        CreateSparkParticles();
        CreateSwordSparks();

        return 1;
    }

    /// <summary>
    /// 인물이 든 검날에서만 튀는 불티 (기획서 B3).
    ///
    /// 화면 전체에 뿌리는 재·잉걸과 목적이 다르다. 저 둘은 공기를 만들지만 이건
    /// <b>시선을 인물로 끌어오는</b> 역할이다. 그래서 위치를 검날에 붙이고, 수명을 짧게
    /// (0.5~1.4초) 둬서 그 자리에서 튀었다 사라지게 한다. 멀리 흘러가면 배경 잉걸과 섞여서
    /// 구분이 안 된다.
    ///
    /// 방출기를 기울인 이유: 검이 인물 손(우상)에서 계단 쪽(좌하)으로 뻗어 있다.
    /// 네모난 상자를 그대로 두면 검날 밖 허공에서도 불티가 나온다.
    ///
    /// 좌표는 타이틀 아트 기준 추정치다. 카메라가 (0,0)/size 5.625라 화면이
    /// 가로 -10~10, 세로 -5.6~5.6이고, 검날은 대략 그 사이를 대각선으로 지난다.
    /// 그림과 어긋나면 씬에서 이 오브젝트를 직접 옮겨 맞추면 된다.
    /// </summary>
    private static void CreateSwordSparks()
    {
        const string sparkName = "TitleSwordSparks";

        var previous = GameObject.Find(sparkName);
        if (previous != null) Object.DestroyImmediate(previous);

        var root = new GameObject(sparkName);
        root.transform.position = new Vector3(3.2f, -2.8f, 0f);
        root.transform.rotation = Quaternion.Euler(0f, 0f, -25f);

        var particles = root.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.duration = 5f;
        main.loop = true;

        // 짧다. 검 주변에서 튀었다 바로 꺼져야 "달궈진 쇠"로 읽힌다.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.62f, 0.22f, 1f), new Color(1f, 0.95f, 0.72f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        var emission = particles.emission;
        emission.rateOverTime = 12f;

        // 검날을 따라 길고 얇게. 오브젝트가 -25도 기울어 있으니 상자도 같이 기운다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(5.5f, 0.25f, 1f);

        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.4f;

        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.7f, 0.5f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

        // 배경 잉걸(11)보다 위. 인물 앞에서 튀어야 검에서 나오는 것으로 보인다.
        renderer.sortingOrder = 12;
    }

    /// <summary>
    /// 등장 연출(D1/D2/D4)을 붙이고 페이드 대상을 연결한다.
    ///
    /// 제목·안내에 CanvasGroup을 같이 붙인다. UI는 SpriteRenderer처럼 색 알파를 직접
    /// 만질 수가 없어서, 자식까지 한 번에 흐리게 하려면 CanvasGroup이 필요하다.
    /// </summary>
    private static int AttachIntroSequence()
    {
        var titleScreen = Object.FindFirstObjectByType<TitleScreen>();
        if (titleScreen == null)
        {
            Debug.LogWarning("[타이틀 연출] TitleScreen을 못 찾았다. 등장 연출을 건너뛴다.");
            return 0;
        }

        if (titleScreen.GetComponent<TitleIntroSequence>() != null) return 0;

        var sequence = titleScreen.gameObject.AddComponent<TitleIntroSequence>();

        var serialized = new SerializedObject(sequence);
        serialized.FindProperty("titleScreen").objectReferenceValue = titleScreen;

        // 배경은 씬에서 가장 큰 SpriteRenderer 하나다. 이름("Title")로 찾으면
        // 나중에 오브젝트 이름을 바꿨을 때 조용히 끊긴다.
        var background = Object.FindFirstObjectByType<SpriteRenderer>();
        if (background != null)
            serialized.FindProperty("background").objectReferenceValue = background;

        serialized.FindProperty("titleGroup").objectReferenceValue = EnsureCanvasGroup("TitleText");
        serialized.FindProperty("promptGroup").objectReferenceValue = EnsureCanvasGroup("PressAnyKeyText");

        // RunResultData는 프로젝트에 하나뿐이라 경로로 찾는다.
        var result = AssetDatabase.LoadAssetAtPath<RunResultData>(
            "Assets/Project/Data/RunResultData.asset");
        if (result != null)
            serialized.FindProperty("result").objectReferenceValue = result;
        else
            Debug.LogWarning("[타이틀 연출] RunResultData를 못 찾았다. 재진입 스킵이 동작하지 않는다. " +
                             "인스펙터에서 직접 연결해라.");

        serialized.ApplyModifiedPropertiesWithoutUndo();
        return 1;
    }

    /// <summary>이름으로 오브젝트를 찾아 CanvasGroup을 붙이고 돌려준다.</summary>
    private static CanvasGroup EnsureCanvasGroup(string objectName)
    {
        var target = GameObject.Find(objectName);
        if (target == null)
        {
            Debug.LogWarning($"[타이틀 연출] {objectName}을 못 찾았다.");
            return null;
        }

        var group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }

    /// <summary>
    /// 타이틀 전용 Volume(Bloom + 비네트)을 만든다.
    ///
    /// <b>전역 프로필(DefaultVolumeProfile)을 안 건드리는 이유:</b> Bloom을 전역으로 켜면
    /// 게임 화면의 잉걸 균열·검날·체력 게이지까지 번진다. 타이틀은 분위기를 팔지만 게임
    /// 화면은 정보를 읽히게 해야 해서 목적이 반대다. 씬에 로컬 Volume을 두면 타이틀에서만 적용된다.
    ///
    /// Bloom 임계값을 0.9로 높게 잡은 것이 핵심이다. 낮추면 회색 재까지 빛나서 화면이
    /// 뿌옇게 뜬다. 높게 두면 <b>정말 밝은 것 — 잉걸과 검날 —만</b> 빛난다.
    /// </summary>
    private static int CreateVolume()
    {
        const string profilePath = "Assets/Project/Settings/TitleVolumeProfile.asset";
        const string volumeName = "TitleVolume";

        if (GameObject.Find(volumeName) != null) return 0;

        if (!AssetDatabase.IsValidFolder("Assets/Project/Settings"))
            AssetDatabase.CreateFolder("Assets/Project", "Settings");

        var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.9f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;

            var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.35f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.45f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        var root = new GameObject(volumeName);
        var volume = root.AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        return 1;
    }

    /// <summary>
    /// 회색 재 사이에 드물게 섞이는 밝은 잉걸.
    ///
    /// 재만 뿌리면 화면이 균일해져서 밋밋하다. 눈은 <b>대비</b>에 붙는다 — 다수의 흐린 회색
    /// 사이에 소수의 밝은 점이 있어야 화면이 살아 있는 것처럼 읽힌다.
    /// 처음에 주황 하나로 다 칠했다가 튄 것과, 전부 회색으로 눌러 밋밋해진 것의 중간이다.
    ///
    /// 개수를 재의 1/5로 두는 것이 핵심이다. 이게 많아지면 다시 주황 화면이 된다.
    /// </summary>
    private static void CreateSparkParticles()
    {
        const string sparkName = "TitleSparks";

        var previous = GameObject.Find(sparkName);
        if (previous != null) Object.DestroyImmediate(previous);

        var root = new GameObject(sparkName);
        root.transform.position = new Vector3(0f, -7f, 0f);

        var particles = root.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.duration = 10f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.55f, 0.18f, 1f), new Color(1f, 0.85f, 0.45f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = particles.emission;
        emission.rateOverTime = 3f; // 재(14)의 약 1/5

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(20f, 0.5f, 1f);

        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.25f;

        // 잉걸은 깜빡이며 사그라든다. 알파를 두 번 오르내리게 해서 반짝임을 만든다.
        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(0.35f, 0.45f),
                new GradientAlphaKey(0.9f, 0.65f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        renderer.sortingOrder = 11;
    }
}
