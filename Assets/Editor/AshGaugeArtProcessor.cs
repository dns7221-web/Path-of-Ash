using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GPT가 흰 배경 위에 그려준 게이지 원본을 실제로 쓸 수 있는 스프라이트로 다듬는 도구.
///
/// 메뉴: Tools → 재의 길 → 게이지 원본 이미지 다듬기
///
/// 하는 일 두 가지:
/// 1) <b>흰 배경을 투명으로.</b> HealthGaugeFrame_raw는 전체가 불투명(#FDFDFD 배경)이라
///    그대로 UI에 얹으면 화면에 흰 사각형이 뜬다.
/// 2) <b>여백 잘라내기.</b> 1904x826 캔버스에서 실제 그림은 세로 189px 띠 하나뿐이다.
///    세로의 77%가 빈 공간인데, 이걸 안 자르면 프레임 안쪽 영역을 비율로 계산하는
///    AshGameHudBuilder의 앵커가 전부 어긋난다.
///
/// 포토샵으로 한 번 하면 될 일을 도구로 만든 이유는 이 프로젝트의 다른 도구들과 같다.
/// 게이지 원본을 다시 뽑을 때마다 같은 처리를 손으로 반복해야 하고, 그때 임계값을 조금
/// 다르게 잡으면 이전 것과 미묘하게 다른 결과가 나온다. 숫자를 코드에 두면 항상 같다.
///
/// <b>원본(_raw)은 건드리지 않는다.</b> 결과를 접미사 없는 이름으로 따로 쓴다.
/// 배경 제거는 되돌릴 수 없는 처리라, 임계값을 잘못 잡았을 때 되돌아갈 곳이 있어야 한다.
/// </summary>
public static class AshGaugeArtProcessor
{
    private const string UiFolder = "Assets/Project/Art/UI";

    /// <summary>
    /// 이 값 이상으로 밝은(= 흰 배경에 가까운) 픽셀은 완전히 투명으로 만든다.
    /// 세 채널 중 <b>가장 작은</b> 값을 기준으로 한다 — 붉은 용암(R 높고 G/B 낮음)처럼
    /// 한 채널만 밝은 색을 배경으로 오인하지 않기 위해서다.
    /// </summary>
    private const int BackgroundThreshold = 238;

    /// <summary>
    /// 이 값 이하로 어두운 픽셀은 완전히 불투명으로 둔다.
    /// 둘 사이(200~238)는 안티에일리어싱된 가장자리로 보고 알파를 부드럽게 깎는다.
    /// 이 구간이 없으면 가장자리가 계단처럼 끊긴다.
    /// </summary>
    private const int OpaqueThreshold = 200;

    /// <summary>다듬을 파일 목록. 원본 → 결과.</summary>
    private static readonly (string source, string output)[] Targets =
    {
        ("HealthGaugeFrame_raw.png", "HealthGaugeFrame.png"),
        ("HealthGaugeFill_raw.png", "HealthGaugeFill.png"),
    };

    [MenuItem("Tools/재의 길/게이지 원본 이미지 다듬기")]
    public static void ProcessAll()
    {
        foreach (var (source, output) in Targets)
            Process(source, output);

        AssetDatabase.Refresh();
    }

    private static void Process(string sourceName, string outputName)
    {
        string sourcePath = $"{UiFolder}/{sourceName}";
        string outputPath = $"{UiFolder}/{outputName}";

        if (!File.Exists(sourcePath))
        {
            Debug.LogError($"[게이지 다듬기] 원본을 못 찾았다: {sourcePath}");
            return;
        }

        // 임포트 설정(Read/Write Enabled)에 상관없이 읽으려고 파일에서 직접 디코딩한다.
        // AssetDatabase로 Texture2D를 불러오면 isReadable이 꺼져 있을 때 GetPixels가 실패한다.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(sourcePath)))
        {
            Debug.LogError($"[게이지 다듬기] PNG를 디코딩하지 못했다: {sourcePath}");
            Object.DestroyImmediate(texture);
            return;
        }

        int width = texture.width;
        int height = texture.height;
        Color32[] pixels = texture.GetPixels32();

        // ── 1단계: 흰 배경을 알파로 바꾼다 ──
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = RemoveWhiteBackground(pixels[i]);

        // ── 2단계: 남은 불투명 영역의 사각형을 구한다 ──
        if (!TryGetOpaqueBounds(pixels, width, height,
                out int minX, out int maxX, out int minY, out int maxY))
        {
            Debug.LogError($"[게이지 다듬기] {sourceName}에서 불투명한 픽셀을 찾지 못했다. " +
                           $"임계값({BackgroundThreshold})이 너무 낮은지 확인해라.");
            Object.DestroyImmediate(texture);
            return;
        }

        int cropWidth = maxX - minX + 1;
        int cropHeight = maxY - minY + 1;

        // ── 3단계: 잘라내서 저장 ──
        var cropped = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);
        var croppedPixels = new Color32[cropWidth * cropHeight];

        for (int y = 0; y < cropHeight; y++)
        {
            for (int x = 0; x < cropWidth; x++)
                croppedPixels[y * cropWidth + x] = pixels[(minY + y) * width + (minX + x)];
        }

        cropped.SetPixels32(croppedPixels);
        cropped.Apply();

        File.WriteAllBytes(outputPath, cropped.EncodeToPNG());

        Debug.Log($"[게이지 다듬기] {sourceName} ({width}x{height}) → " +
                  $"{outputName} ({cropWidth}x{cropHeight})");

        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(cropped);

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

        // 잘라낸 결과의 안쪽 채움 영역을 같이 알려준다. AshGameHudBuilder가 앵커를 잡을 때
        // 이 숫자가 필요한데, 눈으로 재면 반드시 몇 픽셀씩 어긋난다.
        LogInteriorRegion(outputName, croppedPixels, cropWidth, cropHeight);
    }

    /// <summary>
    /// 흰 배경을 투명으로 바꾼다.
    ///
    /// 세 채널 중 최솟값으로 판단하는 이유: 붉은 용암은 R이 250이어도 G/B가 낮다.
    /// 평균 밝기로 판단하면 밝은 주황색 부분이 배경으로 오인되어 구멍이 뚫린다.
    ///
    /// 반투명 구간에서 색을 되돌리는(un-premultiply) 이유: 가장자리 픽셀은 원래 색이 흰
    /// 배경과 섞인 상태다. 그대로 두면 어두운 배경 위에 얹었을 때 테두리에 흰 띠가 남는다.
    /// 섞이기 전 색을 역산해서 넣으면 그 띠가 사라진다.
    /// </summary>
    private static Color32 RemoveWhiteBackground(Color32 pixel)
    {
        int minChannel = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));

        if (minChannel >= BackgroundThreshold)
            return new Color32(0, 0, 0, 0);

        if (minChannel <= OpaqueThreshold)
            return new Color32(pixel.r, pixel.g, pixel.b, 255);

        // 가장자리: 임계값 사이를 0~1로 환산해 알파를 만든다.
        float alpha = (BackgroundThreshold - minChannel) /
                      (float)(BackgroundThreshold - OpaqueThreshold);

        // C = a*F + (1-a)*255  →  F = (C - (1-a)*255) / a
        float inverse = (1f - alpha) * 255f;
        byte r = (byte)Mathf.Clamp((pixel.r - inverse) / alpha, 0f, 255f);
        byte g = (byte)Mathf.Clamp((pixel.g - inverse) / alpha, 0f, 255f);
        byte b = (byte)Mathf.Clamp((pixel.b - inverse) / alpha, 0f, 255f);

        return new Color32(r, g, b, (byte)Mathf.RoundToInt(alpha * 255f));
    }

    /// <summary>알파가 남아 있는 영역의 바운딩 박스를 구한다.</summary>
    private static bool TryGetOpaqueBounds(Color32[] pixels, int width, int height,
        out int minX, out int maxX, out int minY, out int maxY)
    {
        minX = width; maxX = -1; minY = height; maxY = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a <= 8) continue;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return maxX >= 0;
    }

    /// <summary>
    /// 프레임 안쪽에서 채움이 들어가도 되는 사각형을 찾아 로그로 남긴다.
    ///
    /// 스태미나 프레임에서 겪은 문제 때문에 넣었다. 그 프레임은 양 끝 장식이 중앙 높이로
    /// 파고들어 있어서, 가장 넓은 행에 맞춰 채우면 세로 가운데에서 채움이 장식 위로 삐져나왔다.
    /// 그래서 "모든 행에 공통으로 안전한 사각형"을 구한다 — 각 행이 허용하는 좌우 한계 중
    /// 가장 안쪽 값을 취하는 방식이다.
    ///
    /// 결과를 코드에 자동으로 반영하지는 않는다. 이 값은 사람이 보고 판단해서
    /// AshGameHudBuilder의 상수에 넣어야 한다 — 프레임 디자인에 따라 "안쪽"의 정의가
    /// 달라질 수 있어서 자동화하면 오히려 조용히 틀린 값이 들어간다.
    /// </summary>
    private static void LogInteriorRegion(string name, Color32[] pixels, int width, int height)
    {
        // 어두운 내부(밝기 합이 낮고 불투명한 픽셀)를 안쪽으로 본다.
        const int darkSum = 100;

        int innerLeft = 0, innerRight = width - 1;
        int innerTop = -1, innerBottom = -1;

        for (int y = 0; y < height; y++)
        {
            int rowLeft = -1, rowRight = -1;

            for (int x = 0; x < width; x++)
            {
                Color32 p = pixels[y * width + x];
                if (p.a < 200) continue;
                if (p.r + p.g + p.b > darkSum) continue;

                if (rowLeft < 0) rowLeft = x;
                rowRight = x;
            }

            // 내부라고 부를 만큼 긴 행만 센다. 짧은 어두운 조각(장식의 그림자 등)은 무시한다.
            if (rowLeft < 0 || rowRight - rowLeft < width / 3) continue;

            if (innerTop < 0) innerTop = y;
            innerBottom = y;

            if (rowLeft > innerLeft) innerLeft = rowLeft;
            if (rowRight < innerRight) innerRight = rowRight;
        }

        if (innerTop < 0)
        {
            Debug.Log($"[게이지 다듬기] {name}: 어두운 내부 영역을 못 찾았다(채움 이미지이면 정상).");
            return;
        }

        Debug.Log($"[게이지 다듬기] {name} 안쪽 채움 영역 = " +
                  $"X {innerLeft}~{innerRight} / Y {innerTop}~{innerBottom}\n" +
                  $"  AshGameHudBuilder 상수 기준 → 좌 {innerLeft} / 우 {width - 1 - innerRight} / " +
                  $"상 {innerTop} / 하 {height - 1 - innerBottom}  (텍스처 {width}x{height})");
    }
}
