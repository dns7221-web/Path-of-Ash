using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
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
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemies/BossAshKing.prefab";
    private const string ContainerName = "Transition";

    private const string TimelineFolder = "Assets/Project/Animations/Boss";
    private const string SignalFolder = "Assets/Project/Animations/Boss/Signals";
    private const string TimelinePath = TimelineFolder + "/BossTransition.playable";

    // 추가 생성(2026-10-03) — 연출 손잡이(Director·Animator·BossTransitionSequence)가 사는 자식.
    //
    // Transition 컨테이너와 따로 두는 이유: 이펙트 빌더(AshBossTransitionBuilder.AttachToBoss)가
    // Transition을 <b>통째로 지웠다 다시 만든다.</b> 거기 같이 살면 빌더를 돌릴 때마다 인스펙터에서 맞춘
    // 값(흔들림 세기, 음악 페이드)이 날아간다.
    //
    // 보스 루트가 아닌 이유: Animation Track은 Animator에 붙는다. 루트(보스 본체)의 Animator에 걸면 타임라인이
    // 도는 동안 무릎 꿇는 모션을 덮어쓴다. 1페이즈 등장이 Intro 자식을 따로 둔 것과 같은 이유다.
    private const string CinematicName = "TransitionCinematic";

    // 추가 생성(2026-10-03) — 곡선 클립(조명·잿빛·카드와 체력) 폴더와 트랙 이름.
    private const string ClipFolder = TimelineFolder + "/Transition";
    private const string LightTrack = "조명";
    private const string AshTrack = "잿빛";
    private const string UiTrack = "이름 카드 · 체력 채움";

    // 추가 생성(2026-10-03) — 2페이즈 이름 카드의 부제. 이름은 체력바의 2페이즈 이름을 실행 중에 넣는다.
    // 1페이즈 카드의 부제가 "재를 두른 자"라서, 재를 깨고 나온 모습에 맞춰 짝을 지었다. 프리팹에서 바로 고쳐도 된다.
    private const string NameCardSubtitle = "재에서 깨어난 자";

    /// <summary>
    /// 계획표(2026-09-01 기록의 표)의 시각. <b>여기 값은 뼈대를 처음 세울 때만 쓰인다.</b>
    /// 그 뒤의 조정은 Timeline 창에서 하고, 이 상수는 다시 읽히지 않는다.
    /// </summary>
    ///
    /// 수정(2026-09-21, 전환 흐름 B "재의 알에서 깨어남") — 3.125초 계획표를 <b>2.5초</b>로 다시 세웠다.
    /// <list type="bullet">
    /// <item><c>0.00</c> 무릎 꿇고 갑옷에 금(전환 모션 앞 2장, 한 장 0.2초) · 재 장막</item>
    /// <item><c>0.50</c> 갑옷 붕괴 — 보스가 흐려지며 사라지고, 힘의 장이 방의 재를 발밑으로 모은다</item>
    /// <item><c>1.25</c> 재의 알</item>
    /// <item><c>2.00</c> 알이 깨짐 — 이름·체력바가 바뀌고 파편이 터진다</item>
    /// <item><c>2.10</c> 2페이즈 모습이 깨진 알 속에서 드러난다(한 번만)</item>
    /// <item><c>2.50</c> 끝</item>
    /// </list>
    /// 예전 계획표(0.875 / 1.625 / 2.375 / 2.875 / 3.125)에서는 전환 모션이 여자까지 다 보여준 뒤
    /// 알에서 같은 모습이 또 나왔다(보스가 두 번 드러남). 모임·알 구간 길이(0.75초)는 그대로라
    /// 힘의 장 세기(55)는 다시 맞출 필요가 없다.
    ///
    /// 추가 생성(2026-09-21) — <b>internal</b>로 열었다. 재 파티클 빌더가 같은 시각을 따로 적어 두던
    /// 것을 여기서 읽게 바꿨다 — 시각이 한 곳에만 적혀야 흐름을 바꿀 때 파편만 옛 시각에 터지는 일이 없다.
    ///
    /// 수정(2026-10-03, 전환 연출 ③ "느리게 + 1페이즈 등장의 장치") — 2.5초를 <b>4.0초</b>로 늘렸다.
    /// <list type="bullet">
    /// <item><c>0.00</c> 무릎 꿇음 · 조작 잠금 · 음악 꺼짐 · 방이 어두워짐(0.5초 동안)</item>
    /// <item><c>0.50</c> 갑옷이 잿빛으로 굳기 시작(1.0초에 다 굳는다)</item>
    /// <item><c>1.00</c> 갑옷 붕괴 (0.5 → 1.0. 체력이 다 닳았다는 게 보이도록 무릎 꿇은 모습을 더 보여준다)</item>
    /// <item><c>1.75</c> 재의 알 (구간 0.75 → 1.25초. 알 시트는 반복 재생이라 늘려도 빈 화면이 없다)</item>
    /// <item><c>3.00</c> 알이 깨짐 · 방이 번쩍</item>
    /// <item><c>3.10</c> 2페이즈 등장</item>
    /// <item><c>3.20</c> 이름 카드 · 체력바 차오름 · 보스 곡 다시</item>
    /// <item><c>3.50</c> 깨짐 시트 끝 (6장 ÷ 12fps = 0.5초)</item>
    /// <item><c>4.00</c> 끝 · 조작 해제</item>
    /// </list>
    /// 모임 구간(0.75초)과 깨짐 구간(0.5초)은 그대로다. 두 시트는 반복하지 않아서 구간을 늘리면 시트가
    /// 먼저 끝나고 빈 화면이 생긴다. 그래서 타임라인 전체를 느리게 재생하지 않고, 늘려도 되는 구간만 늘렸다.
    /// 모임 구간 길이가 같으니 힘의 장 세기(55)도 다시 맞출 필요가 없다.
    internal const double ArmorBrokenTime = 1.0;
    internal const double EggTime = 1.75;
    internal const double ShatterTime = 3.0;
    internal const double BossReturnsTime = 3.1;

    // 추가 생성(2026-10-03) — 이름 카드가 뜨는 시각. 체력바 채움과 보스 곡도 여기서 시작한다.
    internal const double NameCardTime = 3.2;

    // 추가 생성(2026-10-03) — 깨짐 시트가 끝나는 시각. 예전에는 깨짐이 끝(TotalTime)까지 켜져 있었는데
    // 그때는 둘이 같은 시각(2.5)이었다. 끝이 4.0으로 밀리면 깨진 마지막 장이 0.5초 더 화면에 남는다.
    internal const double ShatterEndTime = ShatterTime + 0.5;

    internal const double TotalTime = 4.0;

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
        // 수정(2026-10-03) — 끝 시각 TotalTime → ShatterEndTime (위 상수 주석 참고).
        ("shatter", "BossTransitionShatter", ShatterTime, ShatterEndTime),
    };

    /// <summary>
    /// 추가 생성(2026-09-21) — 타임라인 에셋을 <b>휴지통으로</b> 보낸다. "보스 전환 다시 만들기" 메뉴가 부른다.
    ///
    /// 이 빌더는 타임라인이 이미 있으면 트랙 시각을 덮어쓰지 않는다(Timeline 창에서 손으로 맞춘 값을
    /// 지키려고). 그래서 계획표 자체를 바꾸려면 에셋을 지우고 새로 만들어야 한다. 완전히 지우지 않고
    /// 휴지통으로 보내는 이유: 손으로 맞춘 옛 타임라인을 되살릴 길을 남긴다.
    /// </summary>
    internal static bool TrashTimeline()
    {
        // 추가 생성(2026-10-03) — 곡선 클립 폴더도 함께 휴지통으로 보낸다. 남겨 두면 새 타임라인이
        // 옛 계획표 시각으로 그려진 곡선(이미 있는 클립)을 그대로 다시 쓴다.
        if (AssetDatabase.IsValidFolder(ClipFolder) && !AssetDatabase.MoveAssetToTrash(ClipFolder)) return false;

        if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) == null) return true;
        return AssetDatabase.MoveAssetToTrash(TimelinePath);
    }

    private const string ArmorBrokenSignalName = "BossTransitionArmorBroken";
    private const string RevealedSignalName = "BossTransitionRevealed";
    private const string BossReturnsSignalName = "BossTransitionBossReturns";

    // 추가 생성(2026-10-03) — 이름 카드 신호.
    private const string NameCardSignalName = "BossTransitionNameCard";

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

        // 추가 생성(2026-10-03) — 이름 카드 신호. 마커는 WireBoss에서 없을 때만 놓는다(있는 타임라인에도 들어가게).
        SignalAsset nameCard = LoadOrCreateSignal(NameCardSignalName);

        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        bool created = timeline == null;

        if (created)
        {
            timeline = CreateTimeline(armorBroken, revealed, bossReturns);
            if (timeline == null) return false;
        }

        // 수정(2026-10-03) — 이름 카드 신호도 넘긴다.
        bool wired = WireBoss(timeline, armorBroken, revealed, bossReturns, nameCard);

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
        SignalAsset armorBroken, SignalAsset revealed, SignalAsset bossReturns, SignalAsset nameCard)
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

            // 추가 생성(2026-10-03) — 예전처럼 보스 루트에 붙어 있던 연출 컴포넌트를 걷어낸다(RemoveRootLeftovers 주석).
            RemoveRootLeftovers(bossRoot);

            // 수정(2026-10-03) — Director·BossTransitionSequence를 보스 루트 대신 TransitionCinematic 자식에 둔다.
            // 이유는 CinematicName 주석 참고(Animation Track이 보스 본체 Animator를 덮지 않게).
            Transform cinematic = EnsureChild(bossRoot.transform, CinematicName);

            var director = EnsureComponent<PlayableDirector>(cinematic.gameObject);

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

            // 추가 생성(2026-10-03) — 곡선 트랙이 값을 쓸 Animator. 컨트롤러가 없어야 곡선 값만 적용된다.
            // AlwaysAnimate인 이유: 화면 밖에서도(카메라가 흔들려 잠깐 벗어나도) 곡선이 멈추지 않게.
            var animator = EnsureComponent<Animator>(cinematic.gameObject);
            animator.runtimeAnimatorController = null;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // 이미 있는 타임라인이면 여기서 새 트랙이 채워진다. 없는 트랙에는 바인딩할
            // 대상도 없으므로 반드시 바인딩보다 먼저 와야 한다.
            EnsureActivationTracks(timeline);

            // 추가 생성(2026-10-03) — 곡선 트랙 3개와 이름 카드 신호도 같은 규칙(없는 것만)으로 채운다.
            EnsureAnimationTracks(timeline);
            EnsureSignalMarker(timeline, NameCardTime, nameCard);

            foreach (var entry in Activations)
                BindTrack(director, timeline, entry.track, container, entry.childPath);

            var sequence = EnsureComponent<BossTransitionSequence>(cinematic.gameObject);

            // 추가 생성(2026-10-03) — 1페이즈 등장과 같은 장치(보스 빛·이름 카드)를 TransitionCinematic 아래에 만든다.
            // 만드는 함수는 등장 빌더의 것을 그대로 쓴다. 같은 모양을 두 곳에서 따로 만들면 한쪽만 고치게 된다.
            //
            // 보스 빛은 모든 정렬 레이어를 비춘다. 보스가 사라진 동안(재의 알) 바닥에 빛이 뛰어야
            // 심장 박동이 보이기 때문이다. 보스 레이어만 비추면 그 구간에 빛이 아무것도 안 비춘다.
            SpriteRenderer bossRenderer = bossRoot.GetComponent<SpriteRenderer>();
            int[] allLayers = SortingLayer.layers.Select(layer => layer.id).ToArray();
            Light2D bossLight = AshBossIntroBuilder.EnsureBossLight(cinematic, allLayers);
            CanvasGroup card = AshBossIntroBuilder.EnsureCard(cinematic, NameCardSubtitle);
            TMP_Text title = card.transform.Find("Panel/Name").GetComponent<TMP_Text>();
            Material ashMaterial = AshBossIntroShaderBuilder.Build();

            var serialized = new SerializedObject(sequence);
            serialized.FindProperty("armorBrokenSignal").objectReferenceValue = armorBroken;
            serialized.FindProperty("revealedSignal").objectReferenceValue = revealed;
            serialized.FindProperty("bossReturnsSignal").objectReferenceValue = bossReturns;

            // 추가 생성(2026-10-03) — 새 참조. 필드 이름이 바뀌면 조용히 비지 않고 여기서 멈춘다(SetReference 주석).
            SetReference(serialized, "nameCardSignal", nameCard);
            SetReference(serialized, "bossRenderer", bossRenderer);
            SetReference(serialized, "ashMaterial", ashMaterial);
            SetReference(serialized, "bossLight", bossLight);
            SetReference(serialized, "nameCard", card.gameObject);
            SetReference(serialized, "nameLabel", title);
            SetReference(serialized, "cardGroup", card);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 추가 생성(시그널이 안 오던 문제) — SignalReceiver를 붙이고 신호 트랙에 바인딩한다.
            // 수정(2026-10-03) — 이름 카드 신호도 넘긴다.
            WireSignalReceiver(director, timeline, sequence, armorBroken, revealed, bossReturns, nameCard);

            // 추가 생성(2026-10-03) — 곡선 트랙 세 개를 TransitionCinematic의 Animator에 잇는다.
            foreach (string trackName in new[] { LightTrack, AshTrack, UiTrack })
            {
                TrackAsset track = FindTrack(timeline, trackName);
                if (track != null)
                {
                    director.SetGenericBinding(track, animator);
                    continue;
                }

                Debug.LogWarning($"[보스 전환] 타임라인에 '{trackName}' 트랙이 없다. " +
                                 "Timeline 창에서 이름을 바꿨다면 이 빌더의 이름도 같이 고쳐라.");
            }

            PrefabUtility.SaveAsPrefabAsset(bossRoot, BossPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(bossRoot);
        }

        return true;
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 예전 배치(보스 루트의 PlayableDirector·BossTransitionSequence·SignalReceiver)를 걷어낸다.
    ///
    /// 안 걷으면 연출이 둘이 된다. 보스는 <c>GetComponentInChildren</c>으로 찾는데 루트가 먼저 잡혀서
    /// 새 장치(조명·이름 카드)가 하나도 안 붙은 옛 연출이 돈다. 에러 없이 "아무것도 안 바뀌었다"로만 보이는 종류다.
    ///
    /// 지우는 순서: BossTransitionSequence가 <c>[RequireComponent(typeof(PlayableDirector))]</c>라서
    /// Director를 먼저 지우면 유니티가 거부한다. 받는 쪽 → 연출 → Director 순서로 지운다.
    /// </summary>
    private static void RemoveRootLeftovers(GameObject bossRoot)
    {
        var oldSequence = bossRoot.GetComponent<BossTransitionSequence>();
        if (oldSequence == null) return;

        var oldReceiver = bossRoot.GetComponent<SignalReceiver>();
        if (oldReceiver != null) Object.DestroyImmediate(oldReceiver);

        Object.DestroyImmediate(oldSequence);

        var oldDirector = bossRoot.GetComponent<PlayableDirector>();
        if (oldDirector != null) Object.DestroyImmediate(oldDirector);

        Debug.Log($"[보스 전환] 보스 루트의 옛 연출 컴포넌트를 걷어내고 {CinematicName} 자식으로 옮겼다.");
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 곡선 트랙 세 개 중 <b>없는 것만</b> 만든다. Activation Track과 같은 규칙이다.
    /// 이미 있는 트랙은 Timeline 창에서 손으로 맞춘 곡선일 수 있으니 건드리지 않는다.
    ///
    /// 곡선의 시각은 전부 위 상수에서 계산한다. 계획표를 바꾸면 곡선도 같이 따라간다.
    /// </summary>
    private static void EnsureAnimationTracks(TimelineAsset timeline)
    {
        float armor = (float)ArmorBrokenTime;
        float egg = (float)EggTime;
        float shatter = (float)ShatterTime;
        float card = (float)NameCardTime;
        float end = (float)TotalTime;

        if (FindTrack(timeline, AshTrack) == null)
        {
            // 0.5초부터 굳기 시작해 갑옷 붕괴 순간에 다 굳는다. 1페이즈 등장의 곡선(1 → 0)을 거꾸로 쓴 것이다.
            AddAnimation(timeline, AshTrack, LoadOrCreateFloatClip("Ash", new[]
            {
                ("ash", AshBossIntroBuilder.Curve((0f, 0f), (0.5f, 0f), (armor, 1f), (end, 1f)))
            }));
        }

        if (FindTrack(timeline, LightTrack) == null)
        {
            AddAnimation(timeline, LightTrack, LoadOrCreateFloatClip("Lights", new[]
            {
                // 방: 0.5초 동안 어두워지고, 알이 깨질 때 한 번 번쩍(1보다 크게), 마지막 0.4초에 원래대로.
                ("roomLightMultiplier", AshBossIntroBuilder.Curve(
                    (0f, 1f), (0.5f, 0.35f),
                    (shatter - 0.02f, 0.35f), (shatter, 2.2f), (shatter + 0.25f, 0.35f),
                    (end - 0.4f, 0.35f), (end, 1f))),

                // 보스 빛: 방이 어두워지는 동안 켜지고, 알 구간에 심장처럼 두 번 뛴 뒤, 마지막에 꺼진다.
                ("bossLightIntensity", AshBossIntroBuilder.Curve(
                    (0f, 0f), (0.5f, 1.1f),
                    (egg + 0.3f, 1.1f), (egg + 0.45f, 2.2f), (egg + 0.6f, 1.1f),
                    (egg + 0.7f, 1.1f), (egg + 0.85f, 2.2f), (egg + 1.0f, 1.1f),
                    (end - 0.4f, 1.1f), (end, 0f)))
            }));
        }

        if (FindTrack(timeline, UiTrack) == null)
        {
            AddAnimation(timeline, UiTrack, LoadOrCreateFloatClip("CardAndHealth", new[]
            {
                // 이름 카드: 0.2초에 떠올라 머물다가 끝나기 0.2초 전부터 사라진다.
                ("cardAlpha", AshBossIntroBuilder.Curve(
                    (0f, 0f), (card, 0f), (card + 0.2f, 1f), (end - 0.2f, 1f), (end, 0f))),

                // 체력바: 이름 카드와 함께 0.6초 동안 0에서 가득 찬다.
                ("healthFill", AshBossIntroBuilder.Curve(
                    (0f, 0f), (card, 0f), (card + 0.6f, 1f), (end, 1f)))
            }));
        }
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 곡선 트랙 하나와 연출 전체를 덮는 클립 하나를 놓는다.
    /// <c>ApplySceneOffsets</c>는 여러 곡선 트랙이 같은 Animator에서 함께 돌 때 위치를 건드리지 않게 한다(1페이즈 등장과 같은 설정).
    /// </summary>
    private static void AddAnimation(TimelineAsset timeline, string name, AnimationClip animation)
    {
        var track = timeline.CreateTrack<AnimationTrack>(null, name);
        track.trackOffset = TrackOffset.ApplySceneOffsets;

        TimelineClip clip = track.CreateClip<AnimationPlayableAsset>();
        var playable = (AnimationPlayableAsset)clip.asset;
        playable.clip = animation;
        playable.removeStartOffset = false;
        clip.displayName = animation.name;
        clip.start = 0;
        clip.duration = TotalTime;
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — <see cref="BossTransitionSequence"/>의 public 필드를 움직이는 곡선 클립을 만든다.
    /// 이미 있으면 그대로 쓴다(손으로 고친 곡선을 지킨다). 계획표를 바꿀 때는 <see cref="TrashTimeline"/>이 폴더째 치운다.
    /// </summary>
    private static AnimationClip LoadOrCreateFloatClip(string name, (string field, AnimationCurve curve)[] curves)
    {
        EnsureFolder(ClipFolder);
        string path = $"{ClipFolder}/BossTransition{name}.anim";

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;

        var clip = new AnimationClip { name = "BossTransition" + name, frameRate = 60 };
        foreach (var entry in curves)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(BossTransitionSequence), entry.field), entry.curve);
        }

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 이 시그널을 쏘는 마커가 신호 트랙에 없을 때만 하나 놓는다.
    /// 이름 카드 신호가 생기기 전에 만든 타임라인에도 새 신호가 들어가게 하려는 것이다.
    /// </summary>
    private static void EnsureSignalMarker(TimelineAsset timeline, double time, SignalAsset asset)
    {
        SignalTrack signalTrack = null;
        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (track is not SignalTrack candidate) continue;
            if (signalTrack == null) signalTrack = candidate;

            foreach (IMarker marker in candidate.GetMarkers())
                if (marker is SignalEmitter emitter && emitter.asset == asset) return;
        }

        if (signalTrack == null) signalTrack = timeline.CreateTrack<SignalTrack>(null, "신호");
        AddSignal(signalTrack, time, asset);
    }

    /// <summary>
    /// 추가 생성(2026-10-03) — 직렬화 필드에 참조를 넣는다. 필드가 없으면 예외로 멈춘다.
    /// 조용히 넘어가면 그 장치만 빠진 채 연출이 돌아서, 에러 없이 "조명이 안 바뀐다"로만 보인다.
    /// </summary>
    private static void SetReference(SerializedObject target, string field, Object value)
    {
        SerializedProperty property = target.FindProperty(field);
        if (property == null)
            throw new System.InvalidOperationException("BossTransitionSequence 필드가 없다: " + field);
        property.objectReferenceValue = value;
    }

    /// <summary>추가 생성(2026-10-03) — 자식이 있으면 그대로 쓰고(인스펙터 조정값 보존), 없을 때만 만든다.</summary>
    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing;

        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    /// <summary>추가 생성(2026-10-03) — 빌더를 여러 번 돌려도 같은 컴포넌트가 겹쳐 붙지 않게 한다.</summary>
    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        // 유니티의 빠진 컴포넌트는 에디터에서 가짜 null일 수 있어 ?? 대신 유니티의 == 검사를 쓴다.
        T component = target.GetComponent<T>();
        return component == null ? target.AddComponent<T>() : component;
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
    // 수정(2026-10-03) — 이름 카드 신호(nameCard) 매개변수를 더했다.
    private static void WireSignalReceiver(
        PlayableDirector director, TimelineAsset timeline, BossTransitionSequence sequence,
        SignalAsset armorBroken, SignalAsset revealed, SignalAsset bossReturns, SignalAsset nameCard)
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

        // 추가 생성(2026-10-03) — 이름 카드 신호.
        AddReaction(receiver, nameCard, sequence.RaiseNameCard);

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
