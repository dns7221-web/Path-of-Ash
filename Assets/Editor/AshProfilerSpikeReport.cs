using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-15, 조사용) — 프로파일러에 녹화된 프레임 중 가장 느린 프레임들을 골라,
/// 어디서 시간을 썼는지 텍스트 파일로 뽑는다.
/// 메뉴: Tools → 재의 길 → 조사 → 프로파일러 스파이크 보고서
///
/// <b>왜 만들었나.</b> 프로파일러 창은 녹화 중에 프레임 내역을 보여주지 않고, 멈춘 뒤에도 스파이크 프레임을
/// 손으로 집어 계층을 하나씩 펼쳐야 한다. 타이틀 → 튜토리얼, 보스 방 입장처럼 "한 순간 멈추는" 문제는
/// 그 한 프레임의 내역이 원인의 전부라, 녹화만 해 두면 코드가 가장 느린 프레임을 찾아 펼쳐 두게 했다.
///
/// 프로파일러 창의 데이터를 직접 읽는 공개 API(<see cref="ProfilerDriver"/>, <see cref="HierarchyFrameDataView"/>)를
/// 쓴다. 창에서 보는 Hierarchy 표와 같은 숫자다.
///
/// <b>수정(2026-09-16) — 줄 세우는 기준을 프레임 전체 시간에서 게임 시간(PlayerLoop)으로 바꿨다.</b>
/// 에디터에서 녹화하면 프레임 시간의 대부분이 <c>EditorLoop</c>인 경우가 흔하다 — 프로파일러 창을 다시 그리는 비용,
/// 플레이 모드를 시작·종료하는 순간, 이 도구를 실행한 프레임까지 전부 거기 들어간다. 전체 시간으로 줄을 세우면
/// 상위가 전부 에디터 프레임이라 <b>정작 게임이 멈춘 프레임이 목록 밖으로 밀린다.</b>
/// 09-16 녹화가 그랬다 — 1·2등이 에디터(2949ms·427ms)였고, 진짜 원인인 씬 로드(게임 233ms)가 3등이었다.
/// 빌드에 남는 시간은 PlayerLoop 쪽이므로 그쪽으로 고른다. 에디터 시간은 참고로 같이 적는다.
///
/// 같이 더한 것: <b>GC 할당이 큰 프레임 순위</b>와 <b>평범한 프레임의 할당 분해</b>.
/// 씬 전환에서 <c>GC.Collect</c>가 190ms를 먹는 것이 확인됐는데, 그 값은 매 프레임 쌓아 둔 쓰레기의 결과라
/// "어디서 매 프레임 할당하는가"를 같은 보고서에서 봐야 한다.
///
/// 원인을 잡고 최적화가 끝나면 지운다. 에디터 폴더라 빌드에는 들어가지 않는다.
/// </summary>
public static class AshProfilerSpikeReport
{
    /// <summary>보고서에 자세히 펼칠 느린 프레임 수.</summary>
    private const int SpikeCount = 5;

    /// <summary>이보다 짧은 항목은 펼치지 않는다(ms). 전부 펼치면 수천 줄이라 원인이 묻힌다.</summary>
    private const float MinExpandMs = 1f;

    /// <summary>프레임 시간의 이 비율보다 작은 항목도 펼치지 않는다. 긴 프레임일수록 문턱이 올라간다.</summary>
    private const float MinExpandRatio = 0.02f;

    /// <summary>계층을 펼칠 최대 깊이.</summary>
    private const int MaxDepth = 16;

    /// <summary>"자기 시간(Self)"이 큰 순서로 몇 개를 따로 모을지. 계층의 어느 깊이에 있든 실제로 일한 곳이 여기 나온다.</summary>
    private const int TopSelfCount = 20;

    /// <summary>추가 생성(2026-09-16) — "느리다"고 볼 기준(ms). 60fps 한 프레임(16.7ms)의 세 배쯤이라 눈에 보이는 멈춤만 걸린다.</summary>
    private const float SlowFrameMs = 50f;

    /// <summary>추가 생성(2026-09-16) — GC 할당이 큰 프레임을 몇 개나 줄 세울지.</summary>
    private const int AllocFrameCount = 5;

    /// <summary>추가 생성(2026-09-16) — 할당 분해에서 이름별로 몇 줄을 보여줄지.</summary>
    private const int AllocTopCount = 12;

    /// <summary>추가 생성(2026-09-16) — 할당 상위 몇 개까지 콜스택을 붙일지. 하나가 여러 줄이라 많이 붙이면 보고서가 콜스택으로 덮인다.</summary>
    private const int CallstackCount = 3;

    /// <summary>추가 생성(2026-09-16) — 콜스택 한 개에서 남길 줄 수. 위쪽 몇 줄이면 어느 코드인지 갈린다.</summary>
    private const int CallstackLines = 8;

    /// <summary>추가 생성(2026-09-16) — 콜스택 기록 토글 메뉴 경로. 체크 표시를 켜고 끄려면 경로 문자열이 하나여야 한다.</summary>
    private const string CallstackMenu = "Tools/재의 길/조사/GC 할당 콜스택 기록";

    /// <summary>
    /// 추가 생성(2026-09-16) — 프레임 하나에서 미리 읽어 둔 값.
    ///
    /// 프레임마다 계층을 다시 여는 것은 비싸서, 줄 세우기에 필요한 값(게임·에디터 시간, 할당)만 한 번에 꺼내 담는다.
    /// </summary>
    private readonly struct FrameInfo
    {
        /// <summary>프로파일러 프레임 번호.</summary>
        public readonly int Index;

        /// <summary>프레임 전체 시간(에디터 포함). 프로파일러 창 그래프의 높이와 같은 값이다.</summary>
        public readonly float TotalMs;

        /// <summary>PlayerLoop — 빌드에서도 남는 게임 시간.</summary>
        public readonly float GameMs;

        /// <summary>EditorLoop — 에디터에서만 드는 시간. 빌드에는 없다.</summary>
        public readonly float EditorMs;

        /// <summary>PlayerLoop 아래에서 일어난 GC 할당.</summary>
        public readonly float GameBytes;

        public FrameInfo(int index, float totalMs, float gameMs, float editorMs, float gameBytes)
        {
            Index = index;
            TotalMs = totalMs;
            GameMs = gameMs;
            EditorMs = editorMs;
            GameBytes = gameBytes;
        }

        /// <summary>펼칠 항목의 문턱을 정할 기준 시간. 게임이 일한 프레임은 게임 시간으로, 에디터뿐이면 전체로 잰다.</summary>
        public float ExpandBasisMs => GameMs > 0f ? GameMs : TotalMs;

        /// <summary>보고서 제목 줄에 쓰는 한 줄 요약.</summary>
        public string Summary =>
            $"게임 {GameMs:0.0}ms / 에디터 {EditorMs:0.0}ms / 전체 {TotalMs:0.0}ms / 할당 {FormatBytes(GameBytes)}";
    }

    /// <summary>
    /// 추가 생성(2026-09-16) — GC 할당마다 콜스택을 같이 녹화할지 켜고 끈다.
    ///
    /// <b>왜 필요한가.</b> 콜스택 없이 녹화하면 할당이 <c>GC.Alloc</c> 한 줄로만 남는다. "매 프레임 736B"까지는 보이는데
    /// <b>누가 할당했는지가 안 보인다.</b> 켜면 그 자리의 호출 스택이 같이 저장되고, 이 도구가 보고서에 붙인다.
    ///
    /// 켠 채로 두지 않는다 — 할당마다 스택을 뜨느라 프레임 시간이 늘어서, 시간 재기와 같이 하면 숫자가 부풀려진다.
    /// 할당 출처를 찾을 때만 켜고 끈다. 프로파일러 창의 CPU 모듈에 있는 Call Stacks 버튼과 같은 설정이다.
    /// </summary>
    [MenuItem(CallstackMenu)]
    private static void ToggleCallstacks()
    {
        bool on = ProfilerDriver.memoryRecordMode == ProfilerMemoryRecordMode.GCAlloc;
        ProfilerDriver.memoryRecordMode = on ? ProfilerMemoryRecordMode.None : ProfilerMemoryRecordMode.GCAlloc;

        Debug.Log(on
            ? "[프로파일러 보고서] GC 할당 콜스택 기록을 껐다. 시간을 잴 때는 꺼진 상태가 맞다."
            : "[프로파일러 보고서] GC 할당 콜스택 기록을 켰다. 이 상태로 다시 녹화하면 보고서에 할당 자리의 호출 스택이 붙는다. " +
              "프레임 시간이 늘어나므로 출처를 찾은 뒤에는 다시 꺼라.");
    }

    /// <summary>메뉴에 지금 상태를 체크로 보여준다. 켜 둔 것을 잊고 시간을 재는 일을 막는다.</summary>
    [MenuItem(CallstackMenu, true)]
    private static bool ToggleCallstacksValidate()
    {
        Menu.SetChecked(CallstackMenu, ProfilerDriver.memoryRecordMode == ProfilerMemoryRecordMode.GCAlloc);
        return true;
    }

    [MenuItem("Tools/재의 길/조사/프로파일러 스파이크 보고서")]
    public static void Write()
    {
        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;
        if (first < 0 || last < first)
        {
            Debug.LogWarning("[프로파일러 보고서] 녹화된 프레임이 없다. Window → Analysis → Profiler에서 녹화를 켜고 " +
                             "문제 장면(타이틀 → 튜토리얼, 보스 방 입장)을 지나간 뒤 다시 실행해라.");
            return;
        }

        // 1. 프레임마다 전체·게임·에디터 시간과 할당을 읽는다.
        //
        // 수정(2026-09-16) — 예전에는 전체 시간만 읽었다. 게임 시간으로 줄을 세우려면 계층의 뿌리를 봐야 해서
        // 프레임마다 계층을 한 번 연다. 2000프레임이면 몇 초 걸리므로 진행 막대를 띄운다.
        var frames = new List<FrameInfo>();
        int frameCount = last - first + 1;
        try
        {
            for (int i = first; i <= last; i++)
            {
                // 막대를 매 프레임 갱신하면 그리는 비용이 읽는 비용보다 커진다.
                if ((i - first) % 25 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "프로파일러 스파이크 보고서",
                        $"프레임 {i - first + 1} / {frameCount} 읽는 중",
                        (i - first) / (float)Mathf.Max(1, frameCount)))
                {
                    // 취소해도 지금까지 읽은 것으로 보고서를 만든다. 긴 녹화에서 앞부분만 보고 싶을 때가 있다.
                    break;
                }

                float totalMs;
                using (RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(i, 0))
                {
                    // float로 맞춰 담는다. 프레임 시간을 비교·정렬하는 용도라 double 정밀도는 필요 없다.
                    if (raw == null || !raw.valid) continue;
                    totalMs = (float)raw.frameTimeMs;
                }

                MeasureRoots(i, out float gameMs, out float editorMs, out float gameBytes);
                frames.Add(new FrameInfo(i, totalMs, gameMs, editorMs, gameBytes));
            }
        }
        finally
        {
            // 예외가 나도 막대는 반드시 걷는다. 안 걷으면 에디터가 막대를 띄운 채 멈춰 있는 것처럼 보인다.
            EditorUtility.ClearProgressBar();
        }

        if (frames.Count == 0)
        {
            Debug.LogWarning("[프로파일러 보고서] 프레임 데이터를 읽지 못했다. 녹화를 멈춘 뒤 다시 실행해라.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"프로파일러 스파이크 보고서 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"녹화 범위: 프레임 {frames[0].Index} ~ {frames[frames.Count - 1].Index} ({frames.Count}개). " +
                          "버퍼가 모자라면 Preferences → Analysis → Profiler → Frame count를 늘린다.");
        report.AppendLine($"게임(PlayerLoop) 평균 {frames.Average(f => f.GameMs):0.0}ms, 중앙값 {Median(frames.Select(f => f.GameMs)):0.0}ms " +
                          $"— 전체 평균 {frames.Average(f => f.TotalMs):0.0}ms, 중앙값 {Median(frames.Select(f => f.TotalMs)):0.0}ms");
        report.AppendLine("전체에는 에디터 시간(EditorLoop)이 섞여 있다. 빌드에 남는 것은 게임 쪽이다.");
        report.AppendLine();

        // 2. 게임이 실제로 멈춘 프레임 목록. 한 번 튄 것인지, 연달아 느린 것인지를 먼저 본다.
        var slowGame = frames.Where(f => f.GameMs >= SlowFrameMs).ToList();
        report.AppendLine($"== 게임 시간 {SlowFrameMs:0}ms 넘은 프레임 {slowGame.Count}개 ==");
        foreach (FrameInfo f in slowGame) report.AppendLine($"  프레임 {f.Index}: {f.Summary}");
        report.AppendLine();

        // 3. 에디터만 느렸던 프레임은 한 줄씩만 남긴다. 펼쳐 봐야 EditorLoop 한 줄이라 자리만 차지한다.
        var slowEditorOnly = frames.Where(f => f.TotalMs >= SlowFrameMs && f.GameMs < SlowFrameMs).ToList();
        report.AppendLine($"== 참고: 에디터만 느렸던 프레임 {slowEditorOnly.Count}개 (빌드에는 없는 시간) ==");
        foreach (FrameInfo f in slowEditorOnly)
            report.AppendLine($"  프레임 {f.Index}: 에디터 {f.EditorMs:0.0}ms / 전체 {f.TotalMs:0.0}ms");
        report.AppendLine();

        // 4. GC 할당이 큰 프레임. 씬 전환의 GC.Collect 값은 여기 쌓인 것의 결과다.
        report.AppendLine($"== GC 할당 상위 {AllocFrameCount} 프레임 ==");
        foreach (FrameInfo f in frames.OrderByDescending(f => f.GameBytes).Take(AllocFrameCount))
            report.AppendLine($"  프레임 {f.Index}: {FormatBytes(f.GameBytes)} (게임 {f.GameMs:0.0}ms)");
        report.AppendLine();

        // 5. 평범한 프레임 하나를 골라 할당을 분해한다. "매 프레임 몇 백 바이트"의 출처가 여기 나온다.
        var allocating = frames.Where(f => f.GameBytes > 0f).OrderBy(f => f.GameMs).ToList();
        if (allocating.Count > 0)
        {
            FrameInfo typical = allocating[allocating.Count / 2];
            report.AppendLine($"== 평범한 프레임의 할당 — 프레임 {typical.Index} " +
                              $"(게임 {typical.GameMs:0.0}ms, 할당 {FormatBytes(typical.GameBytes)}) ==");
            report.AppendLine("  한 프레임 양이 작아도 매 프레임 쌓이면 씬 전환의 GC.Collect 시간이 된다.");

            // 추가 생성(2026-09-16) — 콜스택 기록이 꺼져 있으면 할당이 GC.Alloc 한 줄로만 남는다. 그때 무엇을 해야 하는지 적어 둔다.
            if (ProfilerDriver.memoryRecordMode != ProfilerMemoryRecordMode.GCAlloc)
            {
                report.AppendLine($"  콜스택 기록이 꺼져 있어 할당한 자리의 이름만 나온다. 누가 할당했는지 보려면 " +
                                  $"{CallstackMenu} 를 켜고 다시 녹화해라.");
            }

            AppendAllocations(report, typical.Index, 0);
            report.AppendLine();
        }

        // 6. 게임이 가장 오래 멈춘 프레임들을 펼친다.
        foreach (FrameInfo spike in frames.OrderByDescending(f => f.GameMs).Take(SpikeCount))
        {
            report.AppendLine($"==================== 프레임 {spike.Index} — {spike.Summary} ====================");
            AppendNeighbors(report, frames, spike.Index);
            AppendThread(report, spike.Index, 0, spike.ExpandBasisMs);

            int renderThread = FindThread(spike.Index, "Render Thread");
            if (renderThread > 0) AppendThread(report, spike.Index, renderThread, spike.ExpandBasisMs);

            report.AppendLine();
        }

        string folder = Path.GetFullPath("Logs/ProfilerSpikes");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"spikes-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(true));

        FrameInfo worst = frames.OrderByDescending(f => f.GameMs).First();
        Debug.Log($"[프로파일러 보고서] 저장: {path}\n" +
                  $"게임이 가장 오래 멈춘 프레임 {worst.Index} = 게임 {worst.GameMs:0.0}ms(전체 {worst.TotalMs:0.0}ms), " +
                  $"게임 시간 {SlowFrameMs:0}ms 넘은 프레임 {slowGame.Count}개.");
    }

    /// <summary>
    /// 추가 생성(2026-09-16) — 메인 스레드 계층의 뿌리에서 게임(PlayerLoop)과 에디터(EditorLoop)를 갈라 잰다.
    ///
    /// 뿌리의 직계 자식만 본다. 더 깊이 들어갈 이유가 없고, 프레임 수천 개를 도는 자리라 싸야 한다.
    /// 이름으로 찾는 이유: 뿌리 자식의 순서는 유니티 버전과 상황에 따라 달라진다.
    /// </summary>
    private static void MeasureRoots(int frameIndex, out float gameMs, out float editorMs, out float gameBytes)
    {
        gameMs = 0f;
        editorMs = 0f;
        gameBytes = 0f;

        using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                   frameIndex, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                   HierarchyFrameDataView.columnTotalTime, false))
        {
            if (view == null || !view.valid) return;

            var children = new List<int>();
            view.GetItemChildren(view.GetRootItemID(), children);

            foreach (int id in children)
            {
                string name = view.GetItemName(id);
                if (name == "PlayerLoop")
                {
                    gameMs += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                    gameBytes += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                }
                else if (name == "EditorLoop")
                {
                    editorMs += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                }
            }
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-16) — 한 프레임의 GC 할당을 <b>실제로 할당한 자리</b> 기준으로 줄 세운다.
    ///
    /// 계층의 할당 값은 자식 것까지 더한 값이라, 그대로 줄을 세우면 PlayerLoop가 1등이고 아무것도 안 알려준다.
    /// 자기 할당(전체 − 자식 합)으로 바꿔야 실제로 메모리를 잡은 자리가 남는다. 이름이 같은 자리는 합친다.
    ///
    /// 할당이 0인 가지는 통째로 건너뛴다 — 할당은 부모로 더해져 올라오므로 부모가 0이면 그 아래도 0이다.
    /// </summary>
    private static void AppendAllocations(StringBuilder report, int frameIndex, int threadIndex)
    {
        using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                   frameIndex, threadIndex, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                   HierarchyFrameDataView.columnGcMemory, false))
        {
            if (view == null || !view.valid) return;

            var selfByName = new Dictionary<string, float>();

            // 추가 생성(2026-09-16) — 이름마다 "가장 많이 할당한 항목"의 번호를 기억한다. 콜스택은 항목 하나에 붙어 있어서,
            // 이름으로 합친 뒤에는 대표를 하나 골라야 스택을 꺼낼 수 있다.
            var biggestItemByName = new Dictionary<string, (int id, float self)>();
            var children = new List<int>();
            var stack = new Stack<int>();

            view.GetItemChildren(view.GetRootItemID(), children);
            foreach (int id in children) stack.Push(id);

            while (stack.Count > 0)
            {
                int id = stack.Pop();
                float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                if (total <= 0f) continue;

                children.Clear();
                view.GetItemChildren(id, children);

                float childSum = 0f;
                foreach (int child in children)
                {
                    childSum += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
                    stack.Push(child);
                }

                float self = total - childSum;
                if (self <= 0f) continue;

                string name = view.GetItemName(id);
                selfByName.TryGetValue(name, out float sum);
                selfByName[name] = sum + self;

                if (!biggestItemByName.TryGetValue(name, out var biggest) || self > biggest.self)
                    biggestItemByName[name] = (id, self);
            }

            int callstacksLeft = CallstackCount;
            var callstack = new List<ulong>();
            foreach (var pair in selfByName.OrderByDescending(p => p.Value).Take(AllocTopCount))
            {
                report.AppendLine($"    {FormatBytes(pair.Value),9}  {pair.Key}");

                // 추가 생성(2026-09-16) — 위에서 몇 개만 콜스택을 붙인다. 기록이 꺼져 있으면 빈 문자열이 와서 저절로 넘어간다.
                if (callstacksLeft <= 0 || !biggestItemByName.TryGetValue(pair.Key, out var biggest)) continue;

                callstacksLeft--;

                // 추가 생성(2026-09-16) — 계층 경로를 먼저 적는다.
                //
                // 콜스택 기록을 켜면 유니티가 <c>GC.Alloc</c>이라는 항목을 따로 만들어 달아서, 이름만 보면 전부 "GC.Alloc"이다.
                // 그 항목이 <b>어느 갱신 단계 안에 들어 있는지</b>가 곧 범인이라, 경로가 이름보다 많은 것을 말해 준다.
                string path = view.GetItemPath(biggest.id);
                if (!string.IsNullOrEmpty(path)) report.AppendLine($"                 경로: {path}");

                // 콜스택은 주소 목록으로 온다. 이름이 풀리는 것만 적는다 —
                // 엔진 안쪽(네이티브) 프레임은 심볼이 없어 주소로만 남고, 그 줄은 읽어도 아무것도 알려주지 않는다.
                callstack.Clear();
                view.GetItemCallstack(biggest.id, callstack);
                foreach (ulong address in callstack.Take(CallstackLines))
                {
                    FrameDataView.MethodInfo method = view.ResolveMethodInfo(address);
                    if (string.IsNullOrEmpty(method.methodName)) continue;

                    string text = method.methodName;
                    if (!string.IsNullOrEmpty(method.sourceFileName))
                        text += $"  ({Path.GetFileName(method.sourceFileName)}:{method.sourceFileLine})";

                    report.AppendLine($"                 {text}");
                }
            }
        }
    }

    /// <summary>
    /// 스파이크 앞뒤 두 프레임의 시간. 한 프레임만 튄 "멈춤"인지, 여러 프레임에 걸친 "버벅임"인지 가른다.
    /// 수정(2026-09-16) — 게임 시간을 같이 적는다. 앞뒤가 에디터 때문에 느린 것인지 바로 갈린다.
    /// </summary>
    private static void AppendNeighbors(StringBuilder report, List<FrameInfo> frames, int index)
    {
        var around = frames.Where(f => Math.Abs(f.Index - index) <= 2 && f.Index != index)
                           .Select(f => $"{f.Index}: 게임 {f.GameMs:0.0} / 전체 {f.TotalMs:0.0}ms");
        report.AppendLine($"앞뒤 프레임 — {string.Join(" | ", around)}");
    }

    /// <summary>
    /// 한 스레드의 계층(같은 이름 합치기)을 시간이 큰 것부터 펼치고, 자기 시간 순위를 붙인다.
    /// 수정(2026-09-16) — 펼치는 문턱의 기준을 프레임 전체 시간이 아니라 게임 시간으로 받는다.
    /// 전체 시간(에디터 3초)을 기준으로 잡으면 문턱이 60ms까지 올라가 게임 쪽이 한 줄도 안 펼쳐진다.
    /// </summary>
    private static void AppendThread(StringBuilder report, int frameIndex, int threadIndex, float basisMs)
    {
        using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                   frameIndex, threadIndex, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                   HierarchyFrameDataView.columnTotalTime, false))
        {
            if (view == null || !view.valid) return;

            report.AppendLine($"--- {view.threadName} ---");
            report.AppendLine("  (전체 ms / 자기 ms / 호출 수 / GC 할당)");

            float threshold = Mathf.Max(MinExpandMs, basisMs * MinExpandRatio);
            var selfByName = new Dictionary<string, float>();
            var children = new List<int>();

            // 재귀 대신 스택으로 깊이 우선 순회한다. 정렬은 view가 이미 전체 시간 내림차순으로 해 준다.
            var stack = new Stack<(int id, int depth)>();
            view.GetItemChildren(view.GetRootItemID(), children);
            for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], 1));

            while (stack.Count > 0)
            {
                var (id, depth) = stack.Pop();
                string name = view.GetItemName(id);
                float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                float self = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);

                // 자기 시간은 문턱과 상관없이 전부 모은다. 작은 조각이 여러 곳에 흩어진 원인도 합치면 보인다.
                selfByName.TryGetValue(name, out float sum);
                selfByName[name] = sum + self;

                bool expand = total >= threshold && depth <= MaxDepth;
                if (expand)
                {
                    float calls = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                    float gcBytes = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                    report.AppendLine($"{new string(' ', depth * 2)}{name}  {total:0.0} / {self:0.0} / {calls:0} / {FormatBytes(gcBytes)}");
                }

                // 펼치지 않는 항목의 자식도 자기 시간 합산을 위해 계속 훑는다. 출력만 안 한다.
                children.Clear();
                view.GetItemChildren(id, children);
                for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], expand ? depth + 1 : MaxDepth + 1));
            }

            report.AppendLine($"  [자기 시간 상위 {TopSelfCount}]");
            foreach (var pair in selfByName.OrderByDescending(p => p.Value).Take(TopSelfCount))
                report.AppendLine($"    {pair.Value,8:0.0}ms  {pair.Key}");
        }
    }

    /// <summary>이름으로 스레드 번호를 찾는다. 없으면 -1. 스레드 번호는 녹화마다 달라서 이름으로 찾는다.</summary>
    private static int FindThread(int frameIndex, string threadName)
    {
        for (int t = 0; t < 256; t++)
        {
            using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frameIndex, t))
            {
                if (view == null || !view.valid) return -1;
                if (view.threadName == threadName) return t;
            }
        }

        return -1;
    }

    private static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0f : sorted[sorted.Count / 2];
    }

    private static string FormatBytes(float bytes)
    {
        if (bytes <= 0f) return "0";
        if (bytes < 1024f) return $"{bytes:0}B";
        if (bytes < 1024f * 1024f) return $"{bytes / 1024f:0.0}KB";
        return $"{bytes / (1024f * 1024f):0.0}MB";
    }
}
