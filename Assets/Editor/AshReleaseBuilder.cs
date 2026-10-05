using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 배포) — Windows 빌드를 메뉴 한 번으로 만든다. 검사 → 빌드 → 배포하면 안 되는 폴더 삭제 → zip → 용량 보고서.
/// 메뉴: Tools → 재의 길 → 배포
///
/// <b>왜 Build Profiles 창 대신 메뉴를 따로 두는가.</b> 창에서 빌드하면 매번 사람이 해야 하는 일이 남는다 —
/// 출력 폴더 이름 정하기, 버스트 디버그 폴더 지우기, zip 만들기, 효과음 팩이 있는지 확인하기.
/// 하나라도 빠뜨리면 그 빌드가 그대로 itch.io에 올라간다. 순서를 코드로 고정하면 빠뜨릴 수가 없다.
/// 빌드 자체는 유니티 내장 <see cref="BuildPipeline.BuildPlayer(BuildPlayerOptions)"/>가 하고, 이 클래스는 앞뒤 정리만 한다.
///
/// <b>왜 클라우드(CI)가 아니라 로컬 빌드인가.</b> 공격·피격 효과음(Minifantasy Dungeon SFX)은 재배포 금지라 저장소에 없다.
/// 저장소만 받아서 빌드하는 CI는 소리가 빠진 게임을 만든다. 그래서 효과음 팩이 있는 개발 PC에서만 빌드한다.
///
/// 결과물(전부 Builds/Windows, .gitignore에 등록됨):
/// <list type="bullet">
/// <item><c>PathOfAsh_v1.0.0/</c> — 게임 폴더. butler로 itch.io에 올리는 대상</item>
/// <item><c>PathOfAsh_v1.0.0_Windows.zip</c> — 웹 업로드·지인 전달용 압축본</item>
/// <item><c>PathOfAsh_v1.0.0_report.txt</c> — 빌드 시간·용량·가장 큰 에셋 목록</item>
/// </list>
/// </summary>
public static class AshReleaseBuilder
{
    /// <summary>실행 파일 이름. 한글 제품명(재의 길)을 파일명에 쓰면 일부 압축 프로그램·명령줄에서 깨지므로 영문으로 둔다.</summary>
    private const string ExeName = "PathOfAsh";

    /// <summary>빌드 출력 위치(프로젝트 루트 기준). .gitignore의 /Builds/에 걸려 커밋되지 않는다.</summary>
    private const string BuildRoot = "Builds/Windows";

    /// <summary>게임 아이콘 원본. 1024×1024 PNG를 이 경로에 넣고 "게임 아이콘 적용" 메뉴를 누른다.</summary>
    private const string IconPath = "Assets/Project/Art/UI/Icon/GameIcon.png";

    /// <summary>저장소에 없는 유료·재배포 금지 효과음 팩. 없으면 공격·피격 소리가 빠진 빌드가 나온다.</summary>
    private const string LicensedSfxFolder = "Assets/Project/Audio/SFX/MinifantasyDungeon";

    /// <summary>유니티가 새 프로젝트에 넣는 회사 이름. 이대로 배포하면 안 된다(<see cref="AshReleaseBuildGuard"/> 참고).</summary>
    internal const string PlaceholderCompany = "DefaultCompany";

    /// <summary>보고서에 적을 "가장 큰 에셋" 개수.</summary>
    private const int ReportTopAssetCount = 15;

    /// <summary>
    /// 배포본에 넣으면 안 되는 폴더 이름 패턴. 유니티가 빌드 폴더 안에 만들지만 이름 그대로 "배포 금지"다.
    /// Burst는 2D Animation 패키지가 끌고 들어와서 이 프로젝트에도 생긴다. IL2CPP 폴더는 백엔드를 바꿀 때를 대비한다.
    /// </summary>
    private static readonly string[] DoNotShipPatterns =
    {
        "*_BurstDebugInformation_DoNotShip",
        "*_BackUpThisFolder_ButDontShipItWithYourGame",
    };

    // ───────────────────────── 메뉴 ─────────────────────────

    /// <summary>배포용 빌드. 빌드 중 에러가 하나라도 나면 실패시키고(StrictMode), 끝나면 zip과 보고서를 만든다.</summary>
    [MenuItem("Tools/재의 길/배포/Windows 빌드 (zip 포함)", priority = 0)]
    public static void BuildRelease()
    {
        Build(development: false);
    }

    /// <summary>
    /// 개발 빌드. 실행하면 에디터 Profiler가 자동으로 붙는다.
    /// 에디터에서는 안 보이는 문제(씬 전환 멈칫함, 첫 로딩)를 실제 빌드에서 재는 용도다. zip은 만들지 않는다.
    /// </summary>
    [MenuItem("Tools/재의 길/배포/Windows 개발 빌드 (프로파일러 연결)", priority = 1)]
    public static void BuildDevelopment()
    {
        Build(development: true);
    }

    /// <summary>
    /// <see cref="IconPath"/>의 그림을 게임 기본 아이콘으로 지정한다.
    /// Player Settings 창에서 끌어다 놓아도 되지만, 그러면 임포트 설정(압축·밉맵)을 손으로 맞춰야 해서 메뉴로 묶었다.
    /// </summary>
    [MenuItem("Tools/재의 길/배포/게임 아이콘 적용", priority = 20)]
    public static void ApplyIcon()
    {
        var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("아이콘 파일 없음",
                "아래 경로에 1024×1024 PNG를 넣은 뒤 다시 실행하세요.\n\n" + IconPath, "확인");
            return;
        }

        // 아이콘은 유니티가 원본에서 16~256 크기를 직접 줄여 만든다. 원본이 압축·축소돼 있으면 작은 아이콘이 뭉개지므로
        // 무압축 원본 그대로 두고, 화면에 그릴 일이 없으니 밉맵도 끈다.
        importer.textureType = TextureImporterType.Default;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);

        // NamedBuildTarget.Unknown = Player Settings 맨 위의 "Default Icon" 칸. Windows 빌드는 여기서 exe 아이콘을 만든다.
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log("[배포] 게임 아이콘 적용: " + IconPath);
    }

    /// <summary>빌드 결과 폴더를 탐색기로 연다.</summary>
    [MenuItem("Tools/재의 길/배포/빌드 폴더 열기", priority = 21)]
    public static void OpenBuildFolder()
    {
        string root = ToFullPath(BuildRoot);
        Directory.CreateDirectory(root);
        EditorUtility.RevealInFinder(root);
    }

    // ───────────────────────── 빌드 흐름 ─────────────────────────

    /// <summary>검사 → 빌드 → 정리 → (배포용이면) zip → 보고서. 중간에 실패하면 거기서 멈춘다.</summary>
    private static void Build(bool development)
    {
        if (!RunPreflightChecks()) return;

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("빌드할 씬 없음", "Build Profiles의 Scene List에 켜진 씬이 없습니다.", "확인");
            return;
        }

        // 폴더 이름에 버전을 넣는다. 버전마다 폴더가 따로 남아서 "1.0.0과 1.0.1이 뭐가 달랐지?"를 바로 비교할 수 있다.
        string version = PlayerSettings.bundleVersion.Trim();
        string folderName = ExeName + "_v" + version + (development ? "_dev" : "");
        string outDir = ToFullPath(Path.Combine(BuildRoot, folderName));

        // 같은 버전을 다시 빌드하면 예전 파일이 섞이지 않게 폴더째 지운다.
        // 유니티는 빌드 폴더를 덮어쓰기만 하고, 이번 빌드에 없는 파일은 지우지 않는다.
        if (Directory.Exists(outDir)) FileUtil.DeleteFileOrDirectory(outDir);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outDir, ExeName + ".exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            // 배포용: StrictMode — 빌드 중 에러가 하나라도 찍히면 "성공"으로 치지 않는다. 에러를 품은 빌드가 나가는 것을 막는 문이다.
            // 개발용: Development + ConnectWithProfiler — 실행하면 에디터 Profiler에 자동으로 붙는다.
            options = development
                ? BuildOptions.Development | BuildOptions.ConnectWithProfiler
                : BuildOptions.StrictMode,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[배포] 빌드 실패: " + summary.result + " / 에러 " + summary.totalErrors + "개. 위쪽 콘솔 에러를 확인하세요.");
            EditorUtility.DisplayDialog("빌드 실패",
                "결과: " + summary.result + "\n에러: " + summary.totalErrors + "개\n\n콘솔의 빨간 줄이 원인입니다.", "확인");
            return;
        }

        if (!development) RemoveDoNotShipFolders(outDir);

        string zipPath = null;
        if (!development) zipPath = CreateZip(outDir, folderName);

        string reportPath = ToFullPath(Path.Combine(BuildRoot, folderName + "_report.txt"));
        File.WriteAllText(reportPath, BuildReportText(report, outDir, zipPath), Encoding.UTF8);

        Debug.Log("[배포] 빌드 완료 — " + folderName
                  + " / 폴더 " + EditorUtility.FormatBytes(DirectorySize(outDir))
                  + (zipPath != null ? " / zip " + EditorUtility.FormatBytes(new FileInfo(zipPath).Length) : "")
                  + " / " + summary.totalTime.TotalSeconds.ToString("0") + "초\n보고서: " + reportPath);

        EditorUtility.RevealInFinder(zipPath ?? outDir);
    }

    /// <summary>
    /// 빌드 전에 확인할 것들. 막아야 하는 것(회사 이름)은 막고, 사람이 판단할 것(효과음·아이콘)은 물어본다.
    /// 반환값 false면 빌드하지 않는다.
    /// </summary>
    private static bool RunPreflightChecks()
    {
        // 1) 회사 이름 — 바꾸지 않았으면 무조건 막는다. 이유는 AshReleaseBuildGuard 주석 참고.
        if (PlayerSettings.companyName == PlaceholderCompany)
        {
            EditorUtility.DisplayDialog("회사 이름 미설정",
                "Player Settings의 Company Name이 아직 DefaultCompany입니다.\n배포 전에 정해야 합니다(설정 저장 위치가 바뀜).", "확인");
            return false;
        }

        // 2) 저장 안 된 씬 — 빌드는 디스크에 저장된 씬을 쓴다. 저장 안 한 수정은 빌드에 안 들어가므로 물어본다.
        //    프로젝트 규칙(도구는 씬을 자동 저장하지 않는다)을 지키려고 직접 저장하지 않고 유니티 내장 확인 창을 띄운다.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

        // 3) 효과음 팩 — 없으면 소리 빠진 빌드가 나온다. 테스트용으로는 괜찮을 수 있으니 막지 않고 묻는다.
        if (!AssetDatabase.IsValidFolder(LicensedSfxFolder) &&
            !EditorUtility.DisplayDialog("효과음 팩 없음",
                "Minifantasy Dungeon SFX 폴더가 없습니다:\n" + LicensedSfxFolder +
                "\n\n이대로 빌드하면 공격·피격·상자·발소리가 빠집니다. 그래도 빌드할까요?", "그래도 빌드", "취소"))
            return false;

        // 4) 아이콘 — 없으면 유니티 기본 아이콘으로 나간다. 역시 묻기만 한다.
        bool hasIcon = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any).Any(t => t != null);
        if (!hasIcon &&
            !EditorUtility.DisplayDialog("아이콘 없음",
                "게임 아이콘이 비어 있어 유니티 기본 아이콘으로 나갑니다.\n(Tools → 재의 길 → 배포 → 게임 아이콘 적용)\n\n그래도 빌드할까요?",
                "그래도 빌드", "취소"))
            return false;

        return true;
    }

    /// <summary>
    /// 빌드 폴더 안의 "DoNotShip" 폴더를 지운다. 버스트 디버그 정보는 크래시 분석용이라 개발 PC에만 있으면 되고,
    /// 배포본에 넣으면 용량만 커진다. itch.io에 올리는 것은 이 폴더 전체라서 여기서 지워야 한다.
    /// </summary>
    private static void RemoveDoNotShipFolders(string outDir)
    {
        foreach (string pattern in DoNotShipPatterns)
        {
            foreach (string dir in Directory.GetDirectories(outDir, pattern))
            {
                FileUtil.DeleteFileOrDirectory(dir);
                Debug.Log("[배포] 배포 금지 폴더 삭제: " + Path.GetFileName(dir));
            }
        }
    }

    /// <summary>
    /// 게임 폴더를 zip으로 묶는다. butler는 폴더를 직접 올리므로 zip은 웹 업로드·지인 전달·보관용이다.
    /// 실패해도 빌드는 성공이므로 경고만 남기고 null을 돌려준다(폴더는 그대로 butler로 올릴 수 있다).
    ///
    /// <b>왜 .NET ZipFile이 아니라 Windows 내장 tar.exe인가.</b> 유니티에는 zip API가 없고, .NET의 ZipFile은
    /// System.IO.Compression.FileSystem.dll에 있어서 에디터 어셈블리의 기본 참조에 없을 수 있다(없으면 컴파일 에러).
    /// tar.exe는 Windows 10(1803)부터 기본 포함이고 <c>-a</c>를 주면 확장자(.zip)를 보고 zip으로 만든다.
    /// 프로세스 실행(System.Diagnostics.Process)은 기본 참조인 System.dll에 있어 컴파일 위험이 없다.
    /// </summary>
    private static string CreateZip(string outDir, string folderName)
    {
        string zipPath = ToFullPath(Path.Combine(BuildRoot, folderName + "_Windows.zip"));
        if (File.Exists(zipPath)) File.Delete(zipPath);

        // PATH의 "tar"가 아니라 System32의 tar.exe를 직접 지정한다.
        // Git for Windows의 GNU tar가 PATH 앞에 있으면 그쪽이 불리는데, GNU tar는 zip을 못 만든다.
        string tarPath = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tarPath))
        {
            Debug.LogWarning("[배포] tar.exe가 없어 zip을 건너뜁니다(Windows 10 1803 이상 필요). 빌드 폴더는 정상입니다: " + outDir);
            return null;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = tarPath,
            // -a 확장자로 형식 결정(zip) / -c 만들기 / -f 출력 파일 / -C 이 폴더로 들어가서 묶기
            // 폴더 이름째 묶어서, 압축을 풀면 PathOfAsh_v1.0.0 폴더 하나가 나온다.
            // 파일만 묶으면 수십 개가 다운로드 폴더에 흩어진다(받는 사람 입장에서 가장 흔한 불만).
            Arguments = "-a -c -f \"" + zipPath + "\" -C \"" + Path.GetDirectoryName(outDir) + "\" \"" + folderName + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        try
        {
            EditorUtility.DisplayProgressBar("배포", "zip 만드는 중… " + Path.GetFileName(zipPath), 0.5f);
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                // 에러 출력을 먼저 끝까지 읽고 기다린다. 반대 순서면 출력 버퍼가 차서 서로 기다리다 멈출 수 있다.
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Debug.LogWarning("[배포] zip 생성 실패(tar 종료 코드 " + process.ExitCode + "): " + error + "\n빌드 폴더는 정상입니다: " + outDir);
                    return null;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return zipPath;
    }

    /// <summary>
    /// 빌드 보고서를 글로 만든다. 유니티 내장 <see cref="BuildReport"/>의 packedAssets에서
    /// "어떤 에셋이 빌드에 몇 바이트로 들어갔는지"를 모아 큰 순서로 적는다. 용량을 줄일 때 어디부터 볼지 알려 준다.
    /// </summary>
    private static string BuildReportText(BuildReport report, string outDir, string zipPath)
    {
        BuildSummary summary = report.summary;
        var sb = new StringBuilder();
        sb.AppendLine("재의 길 빌드 보고서");
        sb.AppendLine("생성: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        sb.AppendLine("버전: " + PlayerSettings.bundleVersion + " / 회사: " + PlayerSettings.companyName + " / 대상: " + summary.platform);
        sb.AppendLine("결과: " + summary.result + " / 에러 " + summary.totalErrors + " / 경고 " + summary.totalWarnings);
        sb.AppendLine("빌드 시간: " + summary.totalTime.TotalSeconds.ToString("0.0") + "초");
        sb.AppendLine("게임 폴더: " + EditorUtility.FormatBytes(DirectorySize(outDir)));
        if (zipPath != null) sb.AppendLine("zip: " + EditorUtility.FormatBytes(new FileInfo(zipPath).Length));
        sb.AppendLine();

        // 같은 원본 에셋이 여러 묶음에 나뉘어 들어갈 수 있어서 경로별로 합친다.
        var sizeByAsset = new Dictionary<string, ulong>();
        foreach (PackedAssets packed in report.packedAssets)
        {
            foreach (PackedAssetInfo info in packed.contents)
            {
                string path = string.IsNullOrEmpty(info.sourceAssetPath) ? "(내장 리소스)" : info.sourceAssetPath;
                sizeByAsset.TryGetValue(path, out ulong size);
                sizeByAsset[path] = size + info.packedSize;
            }
        }

        sb.AppendLine("가장 큰 에셋 " + ReportTopAssetCount + "개");
        foreach (var pair in sizeByAsset.OrderByDescending(p => p.Value).Take(ReportTopAssetCount))
            sb.AppendLine("  " + EditorUtility.FormatBytes((long)pair.Value).PadLeft(10) + "  " + pair.Key);

        return sb.ToString();
    }

    // ───────────────────────── 보조 ─────────────────────────

    /// <summary>프로젝트 루트 기준 상대 경로를 절대 경로로. Application.dataPath는 Assets 폴더라서 그 부모가 루트다.</summary>
    private static string ToFullPath(string relative)
    {
        return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, relative));
    }

    /// <summary>폴더 안 모든 파일 크기의 합(바이트). 보고서와 콘솔에 "받는 사람이 내려받을 크기"를 보여 주려고 쓴다.</summary>
    private static long DirectorySize(string dir)
    {
        return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }
}

/// <summary>
/// 추가 생성(2026-10-05, 배포) — 회사 이름이 DefaultCompany면 <b>어떤 경로로 빌드하든</b> 빌드를 멈춘다.
///
/// <b>왜 막는가.</b> 회사 이름과 제품 이름은 PlayerPrefs 저장 위치를 정한다
/// (Windows: 레지스트리 HKCU\Software\회사\제품, 로그: %USERPROFILE%\AppData\LocalLow\회사\제품\Player.log).
/// 첫 배포 뒤에 바꾸면 위치가 달라져서 플레이어의 볼륨·창 크기·키 설정이 전부 초기화된 것처럼 보인다.
/// 그래서 "배포 전에 반드시 정한다"를 사람의 기억이 아니라 빌드 파이프라인에 맡긴다.
///
/// <b>왜 메뉴 검사만으로는 부족한가.</b> 메뉴(<see cref="AshReleaseBuilder"/>)를 안 거치고 Build Profiles 창에서 빌드할 수도 있다.
/// 유니티 내장 콜백 <see cref="IPreprocessBuildWithReport"/>는 모든 플레이어 빌드 직전에 불리므로 빠져나갈 길이 없다.
/// </summary>
public class AshReleaseBuildGuard : IPreprocessBuildWithReport
{
    /// <summary>다른 빌드 전처리보다 먼저 돈다(숫자가 작을수록 먼저).</summary>
    public int callbackOrder => 0;

    /// <summary>빌드 직전 호출. BuildFailedException을 던지면 유니티가 빌드를 취소한다.</summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        if (PlayerSettings.companyName == AshReleaseBuilder.PlaceholderCompany)
            throw new BuildFailedException("[배포] Player Settings의 Company Name이 DefaultCompany입니다. 배포 전에 정해야 합니다.");
    }
}
