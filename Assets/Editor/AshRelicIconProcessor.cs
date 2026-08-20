using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 낱장으로 뽑은 유물 아이콘 원본을 다듬어 게임에서 쓸 아이콘으로 만든다.
///
/// 메뉴: Tools → 재의 길 → 유물 아이콘 다듬기
///
/// <b>시트 정규화(<see cref="AshSpriteSheetNormalizer"/>)와 따로 만든 이유:</b>
/// 그쪽은 "한 장에 여러 칸이 가로로 붙어 있는 시트"를 전제로, 칸 사이 빈 틈을 찾아 프레임을
/// 나누는 일이 절반이다. 유물 아이콘은 파일 하나에 그림 하나라 나눌 것이 없다. 그 도구에
/// "칸이 하나뿐인 경우"를 끼워 넣으면 프레임 분리 로직 전체가 예외를 하나 더 안게 되는데,
/// 그 로직은 캐릭터·VFX 시트가 통째로 의존하는 부분이라 건드리는 값이 너무 크다.
///
/// 하는 일은 세 가지다.
///  1. 초록 배경을 투명으로 바꾼다 (게이지 다듬기와 같은 방식)
///  2. 그림이 실제로 차지하는 사각형만 잘라낸다
///  3. 정사각 캔버스 한가운데에 여백을 두고 얹는다
///
/// 3번이 중요한 이유: 원본은 그림마다 여백이 제각각이라, 그냥 잘라 쓰면 아이콘 바에
/// 늘어놨을 때 어떤 건 크고 어떤 건 작아 보인다. 같은 캔버스에 같은 여백으로 앉혀야
/// <b>열 개가 한 벌로 보인다.</b>
/// </summary>
public static class AshRelicIconProcessor
{
    private const string Folder = "Assets/Project/Art/UI/Relics";

    /// <summary>
    /// 결과 아이콘 한 변의 크기(픽셀). 시트에서 잘라 쓰는 relic_icon_00~02가 256이라 맞췄다.
    /// 크기가 다르면 UI 폴더 PPU(100) 기준으로 유물마다 월드 크기가 달라진다.
    /// </summary>
    private const int CanvasSize = 256;

    /// <summary>
    /// 캔버스 안에서 그림이 차지할 최대 크기. 나머지는 사방 여백이다.
    /// 여백이 없으면 아이콘 바에서 칸끼리 맞닿아 답답해 보이고,
    /// 픽업으로 튀어나왔을 때 외곽선이 잘린 것처럼 보인다.
    /// </summary>
    private const int ContentSize = 232;

    // 초록 판정 임계값. 게이지 다듬기 도구와 같은 값을 쓴다.
    private const int GreenBackgroundThreshold = 110;
    private const int GreenOpaqueThreshold = 45;

    [MenuItem("Tools/재의 길/유물 아이콘 다듬기")]
    public static void ProcessAll()
    {
        int done = 0;

        // 원본 파일 표는 AshRelicBuilder가 들고 있다. 순서가 곧 아이콘 번호라
        // 두 군데에 두면 어긋났을 때 엉뚱한 그림이 달린다.
        string[] sources = AshRelicBuilder.IconSources;

        for (int i = 0; i < sources.Length; i++)
        {
            if (string.IsNullOrEmpty(sources[i])) continue;
            if (Process(sources[i], i)) done++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[유물 아이콘] {done}개를 다듬었다 → {Folder}/relic_icon_NN.png");
    }

    private static bool Process(string sourceName, int index)
    {
        string sourcePath = $"{Folder}/{sourceName}.png";
        string outputPath = $"{Folder}/relic_icon_{index:00}.png";

        if (!File.Exists(sourcePath))
        {
            Debug.LogError($"[유물 아이콘] 원본을 못 찾았다: {sourcePath}");
            return false;
        }

        // 임포트 설정(Read/Write Enabled)에 상관없이 읽으려고 파일에서 직접 디코딩한다.
        // AssetDatabase로 Texture2D를 불러오면 isReadable이 꺼져 있을 때 GetPixels가 실패한다.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(sourcePath)))
        {
            Debug.LogError($"[유물 아이콘] PNG를 디코딩하지 못했다: {sourcePath}");
            Object.DestroyImmediate(texture);
            return false;
        }

        int width = texture.width;
        int height = texture.height;
        Color32[] pixels = texture.GetPixels32();
        Object.DestroyImmediate(texture);

        // ── 1단계: 초록 배경을 투명으로 ──
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = RemoveGreenBackground(pixels[i]);

        // ── 2단계: 그림이 실제로 차지하는 사각형 ──
        if (!TryGetOpaqueBounds(pixels, width, height,
                out int minX, out int maxX, out int minY, out int maxY))
        {
            Debug.LogError($"[유물 아이콘] {sourceName}에서 불투명한 픽셀을 찾지 못했다. " +
                           "배경이 초록이 맞는지 확인해라.");
            return false;
        }

        int cropWidth = maxX - minX + 1;
        int cropHeight = maxY - minY + 1;

        // ── 3단계: 비율을 지킨 채 ContentSize에 맞춰 줄인다 ──
        // 가로세로를 따로 맞추면 그림이 찌그러진다. 긴 변을 기준으로 한 배율을 양쪽에 쓴다.
        float scale = ContentSize / (float)Mathf.Max(cropWidth, cropHeight);
        int drawWidth = Mathf.Max(1, Mathf.RoundToInt(cropWidth * scale));
        int drawHeight = Mathf.Max(1, Mathf.RoundToInt(cropHeight * scale));

        Color32[] scaled = Downsample(pixels, width, minX, minY, cropWidth, cropHeight,
                                      drawWidth, drawHeight);

        // ── 4단계: 정사각 캔버스 한가운데에 얹는다 ──
        var canvas = new Color32[CanvasSize * CanvasSize];
        int offsetX = (CanvasSize - drawWidth) / 2;
        int offsetY = (CanvasSize - drawHeight) / 2;

        for (int y = 0; y < drawHeight; y++)
        {
            for (int x = 0; x < drawWidth; x++)
                canvas[(offsetY + y) * CanvasSize + (offsetX + x)] = scaled[y * drawWidth + x];
        }

        var output = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, false);
        output.SetPixels32(canvas);
        output.Apply();

        File.WriteAllBytes(outputPath, output.EncodeToPNG());
        Object.DestroyImmediate(output);

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

        Debug.Log($"[유물 아이콘] {sourceName} ({width}x{height}) → " +
                  $"relic_icon_{index:00} (그림 {drawWidth}x{drawHeight} / 캔버스 {CanvasSize})");
        return true;
    }

    /// <summary>
    /// 원본의 한 영역을 목표 크기로 줄인다. 넓이 평균(박스 필터)이다.
    ///
    /// 이중선형 보간이 아니라 넓이 평균인 이유: 1254px을 232px로 줄이면 배율이 5배가 넘는다.
    /// 이중선형은 목표 픽셀마다 원본 네 점만 보므로 그 사이 픽셀이 통째로 버려져서,
    /// 가는 선이 끊기고 외곽이 지글거린다. 넓이 평균은 해당 범위의 모든 픽셀을 쓴다.
    ///
    /// 알파를 곱해서 더하는(프리멀티플라이) 이유: 투명한 픽셀의 색까지 그냥 평균 내면
    /// 배경으로 쓰인 검정이 섞여 들어가 <b>외곽에 검은 테두리가 생긴다.</b>
    /// </summary>
    private static Color32[] Downsample(Color32[] source, int sourceWidth,
                                        int cropX, int cropY, int cropWidth, int cropHeight,
                                        int targetWidth, int targetHeight)
    {
        var result = new Color32[targetWidth * targetHeight];

        for (int y = 0; y < targetHeight; y++)
        {
            int y0 = cropY + y * cropHeight / targetHeight;
            int y1 = Mathf.Max(y0 + 1, cropY + (y + 1) * cropHeight / targetHeight);

            for (int x = 0; x < targetWidth; x++)
            {
                int x0 = cropX + x * cropWidth / targetWidth;
                int x1 = Mathf.Max(x0 + 1, cropX + (x + 1) * cropWidth / targetWidth);

                float r = 0f, g = 0f, b = 0f, a = 0f;
                int count = 0;

                for (int sy = y0; sy < y1; sy++)
                {
                    for (int sx = x0; sx < x1; sx++)
                    {
                        Color32 p = source[sy * sourceWidth + sx];
                        float alpha = p.a / 255f;

                        r += p.r * alpha;
                        g += p.g * alpha;
                        b += p.b * alpha;
                        a += alpha;
                        count++;
                    }
                }

                if (count == 0 || a <= 0f)
                {
                    result[y * targetWidth + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // 알파의 합으로 나눠 원래 색으로 되돌린다(언프리멀티플라이).
                result[y * targetWidth + x] = new Color32(
                    (byte)Mathf.Clamp(r / a, 0f, 255f),
                    (byte)Mathf.Clamp(g / a, 0f, 255f),
                    (byte)Mathf.Clamp(b / a, 0f, 255f),
                    (byte)Mathf.Clamp(a / count * 255f, 0f, 255f));
            }
        }

        return result;
    }

    /// <summary>
    /// 초록 배경을 투명으로 바꾼다. 게이지 다듬기 도구와 같은 방식이다.
    ///
    /// 가장자리에서 섞인 초록을 역산하는 이유: 경계 픽셀은 그림 색이 초록과 섞인 상태라,
    /// 그대로 두면 어두운 배경 위에서 외곽에 초록 테두리가 남는다.
    /// </summary>
    private static Color32 RemoveGreenBackground(Color32 pixel)
    {
        int greenness = pixel.g - Mathf.Max(pixel.r, pixel.b);

        if (greenness >= GreenBackgroundThreshold) return new Color32(0, 0, 0, 0);
        if (greenness <= GreenOpaqueThreshold) return new Color32(pixel.r, pixel.g, pixel.b, 255);

        float alpha = (GreenBackgroundThreshold - greenness) /
                      (float)(GreenBackgroundThreshold - GreenOpaqueThreshold);

        float bleed = (1f - alpha) * pixel.g;
        byte g = (byte)Mathf.Clamp((pixel.g - bleed) / alpha, 0f, 255f);

        return new Color32(pixel.r, g, pixel.b, (byte)Mathf.RoundToInt(alpha * 255f));
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
}
