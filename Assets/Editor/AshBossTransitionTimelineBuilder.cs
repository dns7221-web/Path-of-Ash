using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 추가 생성 — 재의 왕 2페이즈 전환의 <b>시간표 에셋</b>과 보스 쪽 배선을 만든다.
/// <see cref="AshBossTransitionBuilder"/>가 이펙트를 보스에 넣은 뒤에 이어서 부른다.
///
/// <b>이 도구는 이미 있는 타임라인을 절대 덮어쓰지 않는다.</b> 타임라인을 도입한 이유가
/// "그림을 보면서 시각을 손으로 맞추기 위해서"인데, 빌더가 실행될 때마다 계획표의 숫자로
/// 되돌려버리면 <b>맞춘 것이 매번 날아간다.</b> 그러면 도입한 의미가 없고, 결국 조정을
/// 다시 코드(이 파일의 상수)에서 하게 된다.
///
/// 그래서 하는 일이 실행 시점에 따라 다르다:
/// <list type="bullet">
/// <item>타임라인이 <b>없으면</b> — 계획표대로 트랙과 클립, 시그널을 만든다(뼈대 세우기).</item>
/// <item>타임라인이 <b>있으면</b> — 있는 트랙과 클립은 손대지 않고, <b>없는 트랙만</b>
/// 채워 넣은 뒤 바인딩을 다시 잇는다.</item>
/// </list>
///
/// 없는 트랙을 채우는 것까지 하는 이유: 연출을 고치다 보면 트랙이 늘어난다(재를 알로
/// 이어붙이면서 "재 장막"이 그렇게 늘었다). 안 채워주면 <b>그 트랙이 하는 일만 조용히
/// 빠진 채</b> 나머지가 정상으로 돌아가서, 무엇이 빠졌는지 알아채기가 제일 어렵다.
///
/// 바인딩을 매번 다시 잇는 것은 이펙트 프리팹을 다시 만들면 보스 안의 자식 오브젝트가
/// 새것으로 바뀌기 때문이다. 그때 바인딩이 <b>끊긴 채로 남으면 에러 없이 그 이펙트만
/// 안 나온다.</b>
/// </summary>
public static class AshBossTransitionTimelineBuilder
{
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemy/BossAshKing.prefab";
    private const string ContainerName = "Transition";

    private const string TimelineFolder = "Assets/Project/Animations/Boss";
    private const string SignalFolder = "Assets/Project/Animations/Boss/Signals";
    private const string TimelinePath = TimelineFolder + "/BossTransition.playable";

    /// <summary>
    /// 계획표(2026-09-01 기록의 표)의 시각. <b>여기 값은 뼈대를 처음 세울 때만 쓰인다.</b>
    /// 그 뒤의 조정은 Timeline 창에서 하고, 이 상수는 다시 읽히지 않는다.
    /// </summary>
    private const double ArmorBrokenTime = 0.875;
    private const double EggTime = 1.625;
    private const double ShatterTime = 2.375;
    private const double BossReturnsTime = 2.875;
    private const double TotalTime = 3.125;

    /// <summary>
    /// 추가 생성 — Activation Track 목록. 트랙 이름, 켤 자식의 경로, 켜고 끄는 시각.
    ///
    /// 한 표로 묶은 이유: 예전에는 트랙을 만드는 곳과 바인딩하는 곳에 <b>같은 이름을
    /// 따로 적었다.</b> 한쪽만 고치면 트랙은 있는데 바인딩이 빈 상태가 되는데,
    /// 그건 에러 없이 "그 이펙트만 조용히 안 나오는" 형태로만 드러난다.
    ///
    /// <b>"재 장막"이 "재 파티클"과 따로 있는 이유가 이 연출의 핵심이다.</b>
    /// 컨테이너(재 파티클)는 끝까지 켜져 있어야 파편 버스트가 2.375에 터진다. 그런데
    /// 장막은 <b>알이 서는 1.625에 꺼져야</b> 한다 — 그래야 모여든 재가 사라지는 순간과
    /// 알이 나타나는 순간이 겹쳐서 "재가 알이 됐다"로 읽힌다. 둘을 한 트랙으로 묶으면
    /// 둘 중 하나를 포기해야 한다.
    /// </summary>
    private static readonly (string track, string childPath, double start, double end)[] Activations =
    {
        ("재 파티클", "BossTransitionAsh", 0.0, TotalTime),
        ("재 장막", "BossTransitionAsh/Veil", 0.0, EggTime),
        ("힘의 장", "BossTransitionAsh/AshField", ArmorBrokenTime, EggTime),
        ("gather", "BossTransitionGather", ArmorBrokenTime, EggTime),
        ("egg", "BossTransitionEgg", EggTime, ShatterTime),
        ("shatter", "BossTransitionShatter", ShatterTime, TotalTime),
    };

    private const string ArmorBrokenSignalName = "BossTransitionArmorBroken";
    private const string RevealedSignalName = "BossTransitionRevealed";
    private const string BossReturnsSignalName = "BossTransitionBossReturns";

    /// <summary>
    /// 타임라인을 준비하고 보스 프리팹에 <see cref="PlayableDirector"/>와
    /// <see cref="BossTransitionSequence"/>를 붙인 뒤 바인딩을 잇는다.
    /// </summary>
    public static bool Build()
    {
        EnsureFolder(SignalFolder);

        SignalAsset armorBroken = LoadOrCreateSignal(ArmorBrokenSignalName);
        SignalAsset revealed = LoadOrCreateSignal(RevealedSignalName);
        SignalAsset bossReturns = LoadOrCreateSignal(BossReturnsSignalName);

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        bool created = timeline == null;

        if (created)
        {
            timeline = CreateTimeline(armorBroken, revealed, bossReturns);
            if (timeline == null) return false;
        }

        bool wired = WireBoss(timeline, armorBroken, revealed, bossReturns);

        AssetDatabase.SaveAssets();

        Debug.Log(created
            ? $"[보스 전환] 타임라인을 새로 만들었다: {TimelinePath}\n" +
              "이제부터 시각 조정은 Timeline 창에서 한다. 이 빌더를 다시 돌려도 덮어쓰지 않는다."
            : $"[보스 전환] 타임라인이 이미 있다: {TimelinePath}\n" +
              "있는 트랙의 시각은 그대로 두고, 없는 트랙만 채운 뒤 바인딩을 다시 이었다.\n" +
              "계획표 값으로 처음부터 다시 만들려면 이 파일을 지우고 실행해라.");

        return wired;
    }

    /// <summary>
    /// 계획표대로 트랙과 클립, 시그널을 놓는다. 처음 한 번만 실행된다.
    ///
    /// <b>Activation Track만 쓰는 이유:</b> 이 연출에서 시간표가 하는 일은 전부 "무엇을
    /// 언제 켜고 끄는가"다. 이펙트 안에서 벌어지는 일(프레임 넘기기, 재 방출량, 파편이
    /// 터지는 시각)은 이미 각 프리팹이 스스로 안다. 타임라인이 그것까지 들고 있으면
    /// 같은 값이 두 곳에 적힌다.
    /// </summary>
    private static TimelineAsset CreateTimeline(
        SignalAsset armorBroken, SignalAsset revealed, SignalAsset bossReturns)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

        // 트랙을 만들기 전에 파일부터 만든다. 트랙과 마커는 타임라인 에셋의 <b>하위 에셋</b>으로
        // 붙는데, 부모가 아직 파일이 아니면 붙을 데가 없어서 저장 뒤에 사라진다.
        AssetDatabase.CreateAsset(timeline, TimelinePath);

        EnsureActivationTracks(timeline);

        var signalTrack = timeline.CreateTrack<SignalTrack>(null, "신호");
        AddSignal(signalTrack, ArmorBrokenTime, armorBroken);
        AddSignal(signalTrack, ShatterTime, revealed);
        AddSignal(signalTrack, BossReturnsTime, bossReturns);

        EditorUtility.SetDirty(timeline);
        return timeline;
    }

    /// <summary>
    /// 추가 생성 — <see cref="Activations"/>의 트랙 중 <b>없는 것만</b> 만든다.
    ///
    /// 이미 있는 트랙은 손대지 않는다. 타임라인을 도입한 이유가 "시각을 손으로 맞추기
    /// 위해서"인데, 빌더가 매번 계획표 값으로 되돌리면 맞춘 것이 날아간다.
    ///
    /// 그래도 <b>새 트랙은 채워 넣어야</b> 한다. 연출을 고치다 보면 트랙이 늘어나는데
    /// (실제로 "재 장막"이 그렇게 늘었다), 있는 타임라인에 안 넣어주면 그 트랙이 하는 일만
    /// 조용히 빠진 채 돌아간다. 그게 제일 찾기 어려운 형태다.
    /// </summary>
    private static void EnsureActivationTracks(TimelineAsset timeline)
    {
        foreach (var entry in Activations)
        {
            if (FindTrack(timeline, entry.track) != null) continue;

            AddActivation(timeline, entry.track, entry.start, entry.end);
        }
    }

    /// <summary>이름으로 트랙을 찾는다. 없으면 null.</summary>
    private static TrackAsset FindTrack(TimelineAsset timeline, string trackName)
    {
        foreach (TrackAsset candidate in timeline.GetOutputTracks())
        {
            if (candidate.name == trackName) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Activation Track 하나와 그 위의 클립 하나를 놓는다.
    ///
    /// <c>postPlaybackState</c>를 <c>Inactive</c>로 두는 것이 중요하다. 기본값으로 두면
    /// 연출이 끝난 뒤 마지막 상태가 그대로 남아서, <b>깨진 껍질이 화면에 계속 서 있게 된다.</b>
    /// 이 값 덕분에 <see cref="BossTransitionSequence.StopAndReset"/>이 이펙트를 하나씩
    /// 끄지 않아도 된다.
    /// </summary>
    private static void AddActivation(TimelineAsset timeline, string name, double start, double end)
    {
        var track = timeline.CreateTrack<ActivationTrack>(null, name);
        track.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;

        TimelineClip clip = track.CreateDefaultClip();
        clip.start = start;
        clip.duration = end - start;
    }

    /// <summary>
    /// 시그널 마커 하나를 놓는다.
    ///
    /// <c>retroactive</c>를 끄는 이유: 켜두면 재생을 시작할 때 <b>이미 지난 시각의 시그널이
    /// 한꺼번에 날아온다.</b> 연출을 중간부터 다시 트는 일이 생기면 갑옷도 안 무너졌는데
    /// 체력바부터 다시 차는 식이 된다.
    ///
    /// <c>emitOnce</c>는 한 번 재생에 한 번만 울리게 한다. 2페이즈 전환은 한 판에 한 번이라
    /// 지금은 차이가 없지만, 없으면 프레임이 튈 때 두 번 울릴 여지가 남는다.
    /// </summary>
    private static void AddSignal(SignalTrack track, double time, SignalAsset asset)
    {
        var emitter = track.CreateMarker<SignalEmitter>(time);
        emitter.asset = asset;
        emitter.retroactive = false;
        emitter.emitOnce = true;
    }

    /// <summary>
    /// 보스 프리팹에 <see cref="PlayableDirector"/>와 <see cref="BossTransitionSequence"/>를
    /// 붙이고, 트랙 이름으로 자식 오브젝트를 찾아 바인딩한다.
    ///
    /// <b>이름으로 찾는 것이 마음에 걸리지만 대안이 없다.</b> 트랙과 오브젝트를 잇는 열쇠는
    /// 트랙 이름뿐이다. 대신 못 찾으면 조용히 넘어가지 않고 경고를 띄운다 — 바인딩이 빈
    /// 트랙은 <b>에러 없이 그 이펙트만 안 나오는</b> 형태로만 드러나기 때문이다.
    /// </summary>
    private static bool WireBoss(
        TimelineAsset timeline,
        SignalAsset armorBroken, SignalAsset revealed, SignalAsset bossReturns)
    {
        GameObject bossRoot = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        if (bossRoot == null)
        {
            Debug.LogError($"[보스 전환] 보스 프리팹을 못 찾았다: {BossPrefabPath}");
            return false;
        }

        try
        {
            Transform container = bossRoot.transform.Find(ContainerName);
            if (container == null)
            {
                Debug.LogError($"[보스 전환] 보스 프리팹에 {ContainerName} 자식이 없다. " +
                               "이펙트 생성을 먼저 실행해라.");
                return false;
            }

            var director = bossRoot.GetComponent<PlayableDirector>();
            if (director == null) director = bossRoot.AddComponent<PlayableDirector>();

            director.playableAsset = timeline;

            // 보스가 방에 놓이는 순간 연출이 시작되면 안 된다. 시작은 체력이 절반이 될 때다.
            director.playOnAwake = false;

            // 끝난 뒤 상태를 유지하지 않는다. Activation Track의 postPlaybackState가
            // 정리를 맡으므로 여기서 붙잡고 있으면 서로 반대되는 말을 하게 된다.
            director.extrapolationMode = DirectorWrapMode.None;

            // 스케일 시간으로 돈다(기본값이지만 명시한다). 인벤토리(I)나 보스 열쇠 화면(T)으로
            // PauseGate가 Time.timeScale을 0으로 만들면 <b>연출도 같이 멈춰야</b> 한다.
            // 여기가 UnscaledGameTime이면 멈춘 화면 위에서 보스만 혼자 변신한다.
            director.timeUpdateMode = DirectorUpdateMode.GameTime;

            // 이미 있는 타임라인이면 여기서 새 트랙이 채워진다. 없는 트랙에는 바인딩할
            // 대상도 없으므로 반드시 바인딩보다 먼저 와야 한다.
            EnsureActivationTracks(timeline);

            foreach (var entry in Activations)
                BindTrack(director, timeline, entry.track, container, entry.childPath);

            var sequence = bossRoot.GetComponent<BossTransitionSequence>();
            if (sequence == null) sequence = bossRoot.AddComponent<BossTransitionSequence>();

            var serialized = new SerializedObject(sequence);
            serialized.FindProperty("armorBrokenSignal").objectReferenceValue = armorBroken;
            serialized.FindProperty("revealedSignal").objectReferenceValue = revealed;
            serialized.FindProperty("bossReturnsSignal").objectReferenceValue = bossReturns;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 추가 생성(시그널이 안 오던 문제) — SignalReceiver를 붙이고 신호 트랙에 바인딩한다.
            WireSignalReceiver(director, timeline, sequence, armorBroken, revealed, bossReturns);

            PrefabUtility.SaveAsPrefabAsset(bossRoot, BossPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(bossRoot);
        }

        return true;
    }

    /// <summary>
    /// 추가 생성 — <see cref="SignalReceiver"/>를 붙이고 신호 3개를
    /// <see cref="BossTransitionSequence"/>의 메서드에 잇는다.
    ///
    /// <b>왜 이게 필요했나:</b> 처음에는 <c>INotificationReceiver</c>만 구현하고 신호 트랙의
    /// 바인딩을 비워뒀다. 문서에는 바인딩이 없으면 <c>PlayableDirector</c>가 붙은 오브젝트로
    /// 신호가 간다고 되어 있는데, <b>실제로는 한 번도 안 왔다.</b> 증상이 고약했다 —
    /// 에러도 경고도 없이 연출만 반쯤 돌았다:
    ///
    /// <list type="bullet">
    /// <item>보스 스프라이트가 안 숨겨져서 <b>알 속에 뭐가 들어 있는지 처음부터 다 보였다</b></item>
    /// <item>체력바가 안 차고 이름이 "???"로 남았다</item>
    /// <item><c>isPhase2</c>가 false로 남아 <b>전환이 두 번 발동했다</b></item>
    /// </list>
    ///
    /// <c>SignalTrack</c>의 바인딩 타입은 원래 <c>SignalReceiver</c>다. 그 경로가 유니티가
    /// 보증하는 길이라 그쪽으로 옮겼다.
    ///
    /// <b>UnityEvent 배선을 손으로 안 하는 이유:</b> 인스펙터에서 이으면 보스 프리팹을 다시
    /// 만들 때마다 배선이 날아가고, 날아간 것을 알려주는 것이 없다. 여기서 코드로 이으면
    /// 빌더를 돌릴 때마다 같은 배선이 다시 선다.
    /// </summary>
    private static void WireSignalReceiver(
        PlayableDirector director, TimelineAsset timeline, BossTransitionSequence sequence,
        SignalAsset armorBroken, SignalAsset revealed, SignalAsset bossReturns)
    {
        var receiver = sequence.GetComponent<SignalReceiver>();
        if (receiver == null) receiver = sequence.gameObject.AddComponent<SignalReceiver>();

        // 이미 있던 반응을 전부 걷어낸다. 안 걷으면 빌더를 돌릴 때마다 같은 반응이 쌓이고,
        // 쌓인 만큼 <b>한 번의 신호에 같은 함수가 여러 번 불린다.</b>
        foreach (SignalAsset registered in new List<SignalAsset>(receiver.GetRegisteredSignals()))
            receiver.Remove(registered);

        AddReaction(receiver, armorBroken, sequence.RaiseArmorBroken);
        AddReaction(receiver, revealed, sequence.RaiseRevealed);
        AddReaction(receiver, bossReturns, sequence.RaiseBossReturns);

        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (track is not SignalTrack) continue;

            director.SetGenericBinding(track, receiver);
        }
    }

    /// <summary>
    /// 신호 하나에 메서드 하나를 잇는다.
    ///
    /// <c>UnityEventTools</c>로 <b>영구 리스너</b>를 다는 것이 요점이다. 코드에서
    /// <c>evt.AddListener</c>로 달면 저장이 안 돼서 <b>에디터를 껐다 켜면 사라진다.</b>
    /// </summary>
    private static void AddReaction(
        SignalReceiver receiver, SignalAsset signal, UnityAction reaction)
    {
        var evt = new UnityEvent();
        UnityEventTools.AddVoidPersistentListener(evt, reaction);

        receiver.AddReaction(signal, evt);
    }

    /// <summary>트랙 이름으로 트랙을 찾고, 경로로 자식을 찾아 잇는다.</summary>
    private static void BindTrack(
        PlayableDirector director, TimelineAsset timeline,
        string trackName, Transform container, string childPath)
    {
        TrackAsset track = FindTrack(timeline, trackName);

        if (track == null)
        {
            Debug.LogWarning($"[보스 전환] 타임라인에 '{trackName}' 트랙이 없다. " +
                             "Timeline 창에서 이름을 바꿨다면 이 빌더의 이름도 같이 고쳐라.");
            return;
        }

        Transform child = container.Find(childPath);
        if (child == null)
        {
            Debug.LogWarning($"[보스 전환] 보스 프리팹에서 '{childPath}'를 못 찾았다. " +
                             $"'{trackName}' 트랙이 빈 채로 남는다 — 그 이펙트만 조용히 안 나온다.");
            return;
        }

        director.SetGenericBinding(track, child.gameObject);
    }

    /// <summary>시그널 에셋을 읽거나, 없으면 만든다. 이미 있으면 그대로 쓴다.</summary>
    private static SignalAsset LoadOrCreateSignal(string name)
    {
        string path = $"{SignalFolder}/{name}.signal";

        var existing = AssetDatabase.LoadAssetAtPath<SignalAsset>(path);
        if (existing != null) return existing;

        var signal = ScriptableObject.CreateInstance<SignalAsset>();
        AssetDatabase.CreateAsset(signal, path);
        return signal;
    }

    /// <summary>폴더가 없으면 만든다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        string leaf = path.Substring(split + 1);

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
