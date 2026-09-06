using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 임포트된 텍스처의 <b>실제</b> 포맷과 메모리를 세어 보고하는 일회성 감사 도구.
///
/// 왜 필요한가: 임포트 설정을 바꾼 뒤 "설정이 바뀌었다"를 확인하는 것으로는 부족하다.
/// BC 계열은 가로세로가 4의 배수가 아니면 <b>경고 없이 무압축으로 돌아가고</b>, 인스펙터에는
/// 여전히 "Compressed"로 보인다. 그래서 TextureImporter가 아니라 <see cref="Texture2D.format"/>
/// 을 읽어야 진짜가 나온다.
///
/// 개발 로그에 적힌 교훈("쓴 뒤에 다시 읽어 확인하지 않으면 성공 로그가 거짓말을 한다")을
/// 도구로 만든 것이다.
/// </summary>
public static class AshTextureFormatAudit
{
    /// <summary>감사 대상. 캐릭터 규칙이 걸리는 뿌리와 같아야 의미가 있다.</summary>
    private static readonly string[] Roots =
    {
        "Assets/Project/Art",
    };

    [MenuItem("Tools/재의 길/텍스처 포맷 감사")]
    public static void Audit()
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D", Roots);

        long totalBytes = 0;
        long compressedBytes = 0;
        long uncompressedBytes = 0;

        // 포맷별 집계. 어떤 포맷이 몇 장에 얼마인지 한눈에 보려는 것이다.
        var byFormat = new Dictionary<TextureFormat, (int count, long bytes)>();

        // 4의 배수가 아니라 압축이 막힌 것들. 이게 이 도구의 핵심 산출물이다.
        var blocked = new List<string>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) continue;

            // Profiler.GetRuntimeMemorySizeLong이 실제 GPU 업로드 크기를 돌려준다.
            // width * height * 4 로 직접 계산하면 압축 포맷과 밉맵을 반영하지 못한다.
            long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            totalBytes += bytes;

            var format = texture.format;
            if (!byFormat.TryGetValue(format, out var entry)) entry = (0, 0);
            byFormat[format] = (entry.count + 1, entry.bytes + bytes);

            bool isCompressed = format != TextureFormat.RGBA32 &&
                                format != TextureFormat.ARGB32 &&
                                format != TextureFormat.RGB24;

            if (isCompressed) compressedBytes += bytes;
            else uncompressedBytes += bytes;

            // 압축이 안 걸린 것 중에 크기가 원인인 것만 따로 뽑는다.
            // 규칙상 무압축이어야 하는 것(배경, UI)과 구분이 안 되므로 크기 조건을 같이 본다.
            if (!isCompressed && (texture.width % 4 != 0 || texture.height % 4 != 0))
                blocked.Add($"  {texture.width}x{texture.height}  {bytes / 1024 / 1024f:F1}MB  {path}");
        }

        var report = new StringBuilder();
        report.AppendLine($"[텍스처 감사] {guids.Length}장 / 합계 {totalBytes / 1024 / 1024f:F1}MB");
        report.AppendLine($"  압축됨   {compressedBytes / 1024 / 1024f:F1}MB");
        report.AppendLine($"  무압축   {uncompressedBytes / 1024 / 1024f:F1}MB");
        report.AppendLine();
        report.AppendLine("포맷별:");

        foreach (var kv in byFormat.OrderByDescending(k => k.Value.bytes))
            report.AppendLine($"  {kv.Key,-16} {kv.Value.count,4}장  {kv.Value.bytes / 1024 / 1024f,8:F1}MB");

        if (blocked.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"4의 배수가 아니라 압축이 막힌 것 {blocked.Count}장:");
            foreach (var line in blocked) report.AppendLine(line);
        }

        // 추가 생성 — 캐릭터 시트만 따로 뽑는다.
        //
        // 왜 따로 보는가: 위 합계는 Art 폴더 전체(Raw 백업과 안 쓰는 습작 포함)라
        // 규칙이 실제로 먹었는지가 안 드러난다. 캐릭터 규칙은 "전부 BC7"이 목표이므로
        // <b>여기 무압축이 하나라도 남아 있으면 그게 곧 실패</b>다.
        report.AppendLine();
        report.AppendLine("캐릭터 시트(Raw 제외) 포맷:");

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Raw/") || path.Contains("/Pre")) continue;

            bool isCharacter = path.Contains("/Player/") || path.Contains("/Boss/") ||
                               path.Contains("/Enemy/") || path.Contains("/Characters/");
            if (!isCharacter) continue;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) continue;

            long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            string mark = texture.format == TextureFormat.BC7 ? "  " : "!!";

            report.AppendLine($"  {mark} {texture.format,-10} {texture.width}x{texture.height,-6} " +
                              $"{bytes / 1024 / 1024f,6:F1}MB  {System.IO.Path.GetFileName(path)}");
        }

        Debug.Log(report.ToString());
    }
}
