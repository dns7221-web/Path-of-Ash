using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-10-02) — 잿빛 석상이 깨어나는 등장 연출의 에셋과 보스 프리팹 배선을 만든다.
/// 이미 있는 Timeline 트랙과 클립은 보존하여 에디터에서 맞춘 시각을 다시 덮어쓰지 않는다.
/// </summary>
public static class AshBossIntroBuilder
{
    private const string BossPath = "Assets/Project/Prefabs/Enemies/BossAshKing.prefab";
    private const string ArtFolder = "Assets/Project/Art/Characters/Boss/AshKing/";
    private const string AnimationFolder = "Assets/Project/Animations/Boss/Intro";
    private const string TimelinePath = "Assets/Project/Animations/Boss/BossIntro.playable";
    private const string FontPath = "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";
    private const string SpriteTrack = "보스 기상 · 검 선언";
    private const string AshTrack = "재 덮임 _Ash";
    private const string LightTrack = "방과 보스 조명";
    private const string UiTrack = "이름 카드 · 체력 채움";
    private const string SignalTrackName = "등장 신호";

    // 추가 생성(2026-10-02) — 이 값은 에셋을 처음 만들 때만 사용한다. 재생 시간은 Timeline에서 읽는다.
    private const double InitialDuration = 4.5;
    private static readonly (string track, string child, double start, double end)[] Activations =
    {
        ("몸에서 떨어지는 재", "FallingAsh", 0.95, 2.65),
        ("위로 흐르는 잉걸", "RisingEmbers", 0.8, InitialDuration),
        ("왕관 불꽃", "CrownFlare", 2.4, 3.3),
        ("이름 카드", "NameCard", 2.8, InitialDuration)
    };

    /// <summary>씬을 열거나 저장하지 않고 등장 에셋과 지정된 보스 프리팹만 갱신한다.</summary>
    [MenuItem("Tools/재의 길/프리팹/보스 등장 연출 생성 · 배선")]
    public static void BuildBatch()
    {
        EnsureFolder(AnimationFolder);
        EnsureFolder(AnimationFolder + "/Signals");
        var idle = LoadFrames("ash-king-idle.png");
        if (idle.Length == 0) throw new InvalidOperationException("보스 idle 스프라이트가 없다.");
        Sprite[] awaken = SliceSheet("ash-king-intro-awaken.png", "ashking_intro_awaken", idle[0]);
        Sprite[] plant = SliceSheet("ash-king-intro-plant.png", "ashking_intro_plant", idle[0]);
        Material ashMaterial = AshBossIntroShaderBuilder.Build();
        SignalAsset[] signals = new[] { "DoorSlam", "EyesIgnite", "SwordPlant", "NameCard" }
            .Select(EnsureSignal).ToArray();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = InitialDuration;
            timeline.editorSettings.frameRate = 60;
        }

        EnsureTimeline(timeline, awaken, plant, idle[0], signals);
        WirePrefab(timeline, awaken.Length > 0 ? awaken[0] : idle[0], ashMaterial, signals);
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();
        Debug.Log("[보스 등장] 생성 및 배선 완료. 기존 Timeline 시각은 보존했다: " + TimelinePath);
    }

    /// <summary>없는 트랙만 추가하여 기존 연출 편집 결과와 클립 배치를 지킨다.</summary>
    private static void EnsureTimeline(TimelineAsset timeline, Sprite[] awaken, Sprite[] plant,
        Sprite idle, SignalAsset[] signals)
    {
        if (FindTrack(timeline, SpriteTrack) == null)
            AddAnimation(timeline, SpriteTrack, CreateSpriteClip(awaken, plant, idle));
        if (FindTrack(timeline, AshTrack) == null)
            AddAnimation(timeline, AshTrack, CreateFloatClip("Ash", new[]
            {
                ("ash", EyeIgnitionAshCurve())
            }));
        RepairInitialAshCurve();
        if (FindTrack(timeline, LightTrack) == null)
            AddAnimation(timeline, LightTrack, CreateFloatClip("Lights", new[]
            {
                ("roomLightMultiplier", Curve((0f, 1f), (0.3f, 0.28f), (2.8f, 0.28f), (4.5f, 1f))),
                ("bossLightIntensity", Curve((0f, 0f), (0.3f, 0.35f), (0.8f, 1.1f), (2.8f, 1.1f), (4.5f, 0f)))
            }));
        if (FindTrack(timeline, UiTrack) == null)
            AddAnimation(timeline, UiTrack, CreateFloatClip("CardAndHealth", new[]
            {
                ("cardAlpha", Curve((0f, 0f), (2.8f, 0f), (3.05f, 1f), (3.95f, 1f), (4.5f, 0f))),
                ("healthFill", Curve((0f, 0f), (2.8f, 0f), (3.65f, 1f), (4.5f, 1f)))
            }));

        foreach (var entry in Activations)
        {
            if (FindTrack(timeline, entry.track) != null) continue;
            var track = timeline.CreateTrack<ActivationTrack>(null, entry.track);
            track.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
            TimelineClip clip = track.CreateDefaultClip();
            clip.start = entry.start;
            clip.duration = entry.end - entry.start;
        }

        var signalTrack = FindTrack(timeline, SignalTrackName) as SignalTrack;
        if (signalTrack == null)
        {
            signalTrack = timeline.CreateTrack<SignalTrack>(null, SignalTrackName);
            double[] times = { 0.0, 0.8, 2.4, 2.8 };
            for (int i = 0; i < signals.Length; i++)
            {
                var marker = signalTrack.CreateMarker<SignalEmitter>(times[i]);
                marker.asset = signals[i];
                marker.retroactive = false;
                marker.emitOnce = true;
            }
        }
        if (!signalTrack.GetMarkers().Any(marker => marker is BossIntroRepeatMarker))
        {
            var repeat = signalTrack.CreateMarker<BossIntroRepeatMarker>(1.5);
            repeat.name = "RepeatDuration";
        }
    }

    /// <summary>스프라이트만 제어하므로 기존 위치와 Animator Controller의 전투 상태는 기록하지 않는다.</summary>
    private static AnimationClip CreateSpriteClip(Sprite[] awaken, Sprite[] plant, Sprite idle)
    {
        string path = AnimationFolder + "/BossIntroSprites.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        var clip = new AnimationClip { name = "BossIntroSprites", frameRate = 60 };
        var keys = new List<ObjectReferenceKeyframe>();
        float[] awakenTimes = { 0f, 0.8f, 1.05f, 1.35f, 1.65f, 2f };
        float[] plantTimes = { 2.05f, 2.18f, 2.3f, 2.4f, 2.53f, 2.72f };
        for (int i = 0; i < awakenTimes.Length; i++)
            keys.Add(new ObjectReferenceKeyframe { time = awakenTimes[i], value = FrameOrIdle(awaken, i, idle) });
        for (int i = 0; i < plantTimes.Length; i++)
            keys.Add(new ObjectReferenceKeyframe { time = plantTimes[i], value = FrameOrIdle(plant, i, idle) });
        keys.Add(new ObjectReferenceKeyframe { time = (float)InitialDuration, value = idle });
        AnimationUtility.SetObjectReferenceCurve(clip,
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys.ToArray());
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>아트 생성 전에도 배선 검증이 가능하도록 빠진 프레임은 기존 idle로 대체한다.</summary>
    private static Sprite FrameOrIdle(Sprite[] frames, int index, Sprite idle)
        => frames.Length > index ? frames[index] : idle;

    /// <summary>MPB·Light2D·UI 적용을 담당하는 어댑터의 값만 애니메이션으로 저장한다.</summary>
    private static AnimationClip CreateFloatClip(string name, (string field, AnimationCurve curve)[] curves)
    {
        string path = AnimationFolder + "/BossIntro" + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        var clip = new AnimationClip { name = "BossIntro" + name, frameRate = 60 };
        foreach (var curve in curves)
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(BossIntroSequence), curve.field), curve.curve);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>값 사이에는 부드러운 보간을 써서 재와 조명, 이름 카드가 갑자기 튀지 않게 한다.</summary>
    private static AnimationCurve Curve(params (float time, float value)[] values)
    {
        var curve = new AnimationCurve(values.Select(value => new Keyframe(value.time, value.value)).ToArray());
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        return curve;
    }

    /// <summary>눈이 켜지는 프레임에 재를 일부 벗겨 주황색이 회색으로 숨지 않도록 한다.</summary>
    private static AnimationCurve EyeIgnitionAshCurve()
        => Curve((0f, 1f), (0.79f, 1f), (0.8f, 0.72f), (1.2f, 0.65f), (1.7f, 0.35f), (2f, 0f), (4.5f, 0f));

    /// <summary>
    /// 추가 생성(2026-10-02) — 최초 생성본의 눈 발화 곡선만 한 번 보정한다.
    /// 모든 키가 이전 기본값과 일치할 때만 갱신하므로 사용자가 편집한 곡선은 그대로 둔다.
    /// </summary>
    private static void RepairInitialAshCurve()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + "/BossIntroAsh.anim");
        if (clip == null) return;
        var binding = EditorCurveBinding.FloatCurve("", typeof(BossIntroSequence), "ash");
        AnimationCurve existing = AnimationUtility.GetEditorCurve(clip, binding);
        (float time, float value)[] original = { (0f, 1f), (0.8f, 1f), (1.2f, 0.82f),
            (1.7f, 0.35f), (2f, 0f), (4.5f, 0f) };
        if (existing == null || existing.length != original.Length) return;
        Keyframe[] keys = existing.keys;
        for (int i = 0; i < keys.Length; i++)
            if (!Mathf.Approximately(keys[i].time, original[i].time)
                || !Mathf.Approximately(keys[i].value, original[i].value)) return;
        AnimationUtility.SetEditorCurve(clip, binding, EyeIgnitionAshCurve());
        EditorUtility.SetDirty(clip);
    }

    /// <summary>서로 다른 속성의 트랙이 함께 재생되도록 루트 변환 적용과 자동 오프셋을 끈다.</summary>
    private static void AddAnimation(TimelineAsset timeline, string name, AnimationClip animation)
    {
        var track = timeline.CreateTrack<AnimationTrack>(null, name);
        track.trackOffset = TrackOffset.ApplySceneOffsets;
        TimelineClip clip = track.CreateClip<AnimationPlayableAsset>();
        ((AnimationPlayableAsset)clip.asset).clip = animation;
        ((AnimationPlayableAsset)clip.asset).removeStartOffset = false;
        clip.displayName = animation.name;
        clip.start = 0;
        clip.duration = InitialDuration;
        // 클립이 Timeline 전체를 덮으므로 외삽 구간이 생기지 않는다.
    }

    /// <summary>
    /// 추가 생성(2026-10-02) — 2페이즈 Director와 분리된 Intro 자식에 모든 등장 연출을 모은다.
    /// 루트 Director를 재사용하면 기존 변신 Timeline 에셋과 신호 배선이 덮어써지기 때문이다.
    /// </summary>
    private static void WirePrefab(TimelineAsset timeline, Sprite dormant, Material material, SignalAsset[] signals)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BossPath);
        try
        {
            Transform intro = EnsureChild(root.transform, "Intro");
            var director = EnsureComponent<PlayableDirector>(intro.gameObject);
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            var animator = EnsureComponent<Animator>(intro.gameObject);
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var sequence = EnsureComponent<BossIntroSequence>(intro.gameObject);
            SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
            EnsureParticles(intro);
            Light2D light = EnsureBossLight(intro, renderer.sortingLayerID);
            CanvasGroup card = EnsureCard(intro);
            TMP_Text title = card.transform.Find("Panel/Name").GetComponent<TMP_Text>();

            var serialized = new SerializedObject(sequence);
            SetReference(serialized, "bossRenderer", renderer);
            SetReference(serialized, "dormantSprite", dormant);
            SetReference(serialized, "ashMaterial", material);
            SetReference(serialized, "nameCard", card.gameObject);
            SetReference(serialized, "nameLabel", title);
            SetReference(serialized, "cardGroup", card);
            SetReference(serialized, "bossLight", light);
            SetReference(serialized, "doorSlamSignal", signals[0]);
            SetReference(serialized, "eyesIgniteSignal", signals[1]);
            SetReference(serialized, "swordPlantSignal", signals[2]);
            SetReference(serialized, "nameCardSignal", signals[3]);
            SerializedProperty effects = serialized.FindProperty("effects");
            effects.arraySize = 3;
            for (int i = 0; i < effects.arraySize; i++)
                effects.GetArrayElementAtIndex(i).objectReferenceValue = intro.Find(Activations[i].child).gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            SignalReceiver receiver = EnsureComponent<SignalReceiver>(intro.gameObject);
            foreach (SignalAsset registered in receiver.GetRegisteredSignals().ToArray()) receiver.Remove(registered);
            AddReaction(receiver, signals[0], sequence.RaiseDoorSlam);
            AddReaction(receiver, signals[1], sequence.RaiseEyesIgnite);
            AddReaction(receiver, signals[2], sequence.RaiseSwordPlant);
            AddReaction(receiver, signals[3], sequence.RaiseNameCard);
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track.name == SpriteTrack) director.SetGenericBinding(track, root.GetComponent<Animator>());
                else if (track.name == AshTrack || track.name == LightTrack || track.name == UiTrack)
                    director.SetGenericBinding(track, animator);
                else if (track is SignalTrack) director.SetGenericBinding(track, receiver);
            }
            foreach (var entry in Activations)
                director.SetGenericBinding(FindTrack(timeline, entry.track), intro.Find(entry.child).gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, BossPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>저장 가능한 리스너로 연결하여 에디터 재시작 뒤에도 시그널이 유지되게 한다.</summary>
    private static void AddReaction(SignalReceiver receiver, SignalAsset signal, UnityAction callback)
    {
        var reaction = new UnityEvent();
        UnityEventTools.AddPersistentListener(reaction, callback);
        receiver.AddReaction(signal, reaction);
    }

    /// <summary>참조 필드가 바뀌면 조용히 누락하지 않고 빌더 단계에서 원인을 알린다.</summary>
    private static void SetReference(SerializedObject target, string field, UnityEngine.Object value)
    {
        SerializedProperty property = target.FindProperty(field);
        if (property == null) throw new InvalidOperationException("BossIntroSequence 필드가 없다: " + field);
        property.objectReferenceValue = value;
    }

    /// <summary>보스 렌더러 레이어만 밝히므로 방 전체를 밝게 되돌리지 않고 석상을 강조할 수 있다.</summary>
    private static Light2D EnsureBossLight(Transform parent, int sortingLayer)
    {
        Transform existing = parent.Find("BossLight");
        if (existing != null) return existing.GetComponent<Light2D>();
        Transform child = EnsureChild(parent, "BossLight");
        child.localPosition = new Vector3(0f, 3f, -1f);
        var light = child.gameObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.color = new Color(1f, 0.72f, 0.43f);
        light.intensity = 0f;
        light.pointLightInnerRadius = 3f;
        light.pointLightOuterRadius = 7.5f;
        var serialized = new SerializedObject(light);
        SerializedProperty layers = serialized.FindProperty("m_ApplyToSortingLayers");
        layers.arraySize = 1;
        layers.GetArrayElementAtIndex(0).intValue = sortingLayer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return light;
    }

    /// <summary>이름은 런타임 체력바에서 채우며 부제만 이 카드에 저장해 본명 공개를 막는다.</summary>
    private static CanvasGroup EnsureCard(Transform parent)
    {
        Transform existing = parent.Find("NameCard");
        if (existing != null) return existing.GetComponent<CanvasGroup>();
        var card = new GameObject("NameCard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        card.transform.SetParent(parent, false);
        Canvas canvas = card.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        CanvasScaler scaler = card.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        CanvasGroup group = card.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        RectTransform panel = CreateRect(card.transform, "Panel", new Vector2(1120f, 225f), new Vector2(0f, -260f));
        var backdrop = panel.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0.025f, 0.023f, 0.022f, 0.76f);
        backdrop.raycastTarget = false;
        RectTransform rule = CreateRect(panel, "Rule", new Vector2(680f, 2f), new Vector2(0f, -5f));
        var line = rule.gameObject.AddComponent<Image>();
        line.color = new Color(0.59f, 0.41f, 0.25f, 0.9f);
        line.raycastTarget = false;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        CreateText(panel, "Name", "", font, 62f, new Vector2(0f, 53f), new Color(0.94f, 0.89f, 0.80f));
        CreateText(panel, "Subtitle", "재를 두른 자", font, 28f, new Vector2(0f, -56f), new Color(0.71f, 0.64f, 0.55f));
        card.SetActive(false);
        return group;
    }

    /// <summary>한글 폰트를 공유하고 UI가 게임 입력을 가로채지 않도록 텍스트 레이캐스트를 끈다.</summary>
    private static void CreateText(Transform parent, string name, string text, TMP_FontAsset font,
        float size, Vector2 position, Color color)
    {
        RectTransform rect = CreateRect(parent, name, new Vector2(980f, 90f), position);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>카드 내부의 모든 요소를 가운데 기준으로 배치하여 화면 비율 변경을 견딘다.</summary>
    private static RectTransform CreateRect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var child = new GameObject(name, typeof(RectTransform));
        var rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    /// <summary>파티클의 활성 시각은 Timeline이 갖고 각 시스템은 입자의 모양과 수명만 담당한다.</summary>
    private static void EnsureParticles(Transform parent)
    {
        if (parent.Find("FallingAsh") == null)
        {
            ParticleSystem ash = CreateParticles(parent, "FallingAsh", new Vector3(0f, 4f, 0f),
                new Color(0.68f, 0.65f, 0.60f), 0.06f, 0.15f, 0.6f, 1.1f);
            var emission = ash.emission;
            emission.rateOverTime = 75f;
            SetVelocity(ash, -1f, 1f, -2.5f, -0.8f);
            var shape = ash.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3.6f, 4.8f, 0f);
            var main = ash.main;
            main.gravityModifier = 0.18f;
            var noise = ash.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.35f;
        }
        if (parent.Find("RisingEmbers") == null)
        {
            ParticleSystem embers = CreateParticles(parent, "RisingEmbers", new Vector3(0f, 3f, 0f),
                new Color(1f, 0.39f, 0.07f), 0.05f, 0.11f, 0.7f, 1.3f);
            var emission = embers.emission;
            emission.rateOverTime = 15f;
            SetVelocity(embers, -0.3f, 0.3f, 0.8f, 1.6f);
            var shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3.2f, 4f, 0f);
        }
        if (parent.Find("CrownFlare") == null)
        {
            ParticleSystem crown = CreateParticles(parent, "CrownFlare", new Vector3(0f, 6.4f, 0f),
                new Color(1f, 0.48f, 0.09f), 0.09f, 0.20f, 0.22f, 0.60f);
            var main = crown.main;
            main.loop = false;
            main.duration = 0.65f;
            var emission = crown.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });
            SetVelocity(crown, -1f, 1f, 1.5f, 3f);
            var shape = crown.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.5f, 0.5f, 0f);
        }
    }

    /// <summary>텍스처 없는 사각 입자로 만들며 GameTime을 사용해 PauseGate와 함께 멈춘다.</summary>
    private static ParticleSystem CreateParticles(Transform parent, string name, Vector3 position,
        Color color, float sizeMin, float sizeMax, float lifetimeMin, float lifetimeMax)
    {
        Transform child = EnsureChild(parent, name);
        child.localPosition = position;
        var particles = child.gameObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.useUnscaledTime = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed = 0f;
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 220;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerName = "VFX";
        renderer.sortingOrder = 6;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        fade.color = gradient;
        child.gameObject.SetActive(false);
        return particles;
    }

    /// <summary>2D 평면 안에서만 움직이도록 세 축을 같은 커브 모드로 설정한다.</summary>
    private static void SetVelocity(ParticleSystem particles, float xMin, float xMax, float yMin, float yMax)
    {
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(xMin, xMax);
        velocity.y = new ParticleSystem.MinMaxCurve(yMin, yMax);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    /// <summary>6칸 시트를 idle과 같은 PPU·지면 피벗으로 잘라 일어서는 순간 위치가 튀지 않게 한다.</summary>
    private static Sprite[] SliceSheet(string file, string prefix, Sprite idle)
    {
        string path = ArtFolder + file;
        if (!File.Exists(path))
        {
            Debug.LogWarning("[보스 등장] 시트가 없어 idle로 배선한다: " + path);
            return Array.Empty<Sprite>();
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null || texture.width != 1536 || texture.height != 256)
            throw new InvalidOperationException("보스 등장 시트는 1536×256이어야 한다: " + path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = idle.pixelsPerUnit;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var previous = provider.GetSpriteRects().ToDictionary(rect => rect.name, rect => rect.spriteID);
        var rects = new SpriteRect[6];
        for (int i = 0; i < rects.Length; i++)
        {
            string name = prefix + "_" + i.ToString("00");
            rects[i] = new SpriteRect
            {
                name = name, rect = new Rect(i * 256, 0, 256, 256), alignment = SpriteAlignment.Custom,
                pivot = idle.pivot / idle.rect.size, border = Vector4.zero,
                spriteID = previous.TryGetValue(name, out GUID id) ? id : GUID.Generate()
            };
        }
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(
            rects.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)));
        provider.Apply();
        importer.SaveAndReimport();
        return LoadFrames(file);
    }

    /// <summary>파일 내부 순서 대신 스프라이트 이름순으로 읽어서 프레임 순서를 고정한다.</summary>
    private static Sprite[] LoadFrames(string file) => AssetDatabase.LoadAllAssetsAtPath(ArtFolder + file)
        .OfType<Sprite>().OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToArray();

    /// <summary>같은 신호 에셋을 재사용하여 타임라인에서 지정한 참조를 보존한다.</summary>
    private static SignalAsset EnsureSignal(string name)
    {
        string path = AnimationFolder + "/Signals/BossIntro" + name + ".signal";
        var signal = AssetDatabase.LoadAssetAtPath<SignalAsset>(path);
        if (signal != null) return signal;
        signal = ScriptableObject.CreateInstance<SignalAsset>();
        signal.name = "BossIntro" + name;
        AssetDatabase.CreateAsset(signal, path);
        return signal;
    }

    /// <summary>같은 이름의 기존 트랙이 있으면 생성하지 않고 그 편집 결과를 사용한다.</summary>
    private static TrackAsset FindTrack(TimelineAsset timeline, string name)
        => timeline.GetOutputTracks().FirstOrDefault(track => track.name == name);

    /// <summary>기존 자식과 그 인스펙터 조정값을 보존하고 없는 경우에만 생성한다.</summary>
    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing;
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    /// <summary>빌더를 반복 실행해도 같은 컴포넌트가 중복되지 않게 한다.</summary>
    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        // Unity의 누락 컴포넌트는 편집기에서 fake-null일 수 있어 C# ?? 대신 Unity == 검사를 쓴다.
        T component = target.GetComponent<T>();
        return component == null ? target.AddComponent<T>() : component;
    }

    /// <summary>AssetDatabase에 폴더를 등록하여 하위 에셋을 즉시 저장할 수 있게 한다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
