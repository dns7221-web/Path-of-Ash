using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GPT가 뽑아준 원본 그림을 프로젝트 규격의 스프라이트 시트로 바꾸는 도구.
///
/// 메뉴: Tools → 재의 길 → 원본 시트 정규화
///
/// <b>왜 만들었나.</b> 프롬프트에 좌표 규칙(셀 256, 발끝 y=216, 중심 x=128)을 적어 GPT가
/// 맞춰주기를 기대했지만, 캔버스 비율·배경·시점·좌표가 한꺼번에 어긋나는 일이 반복됐다.
/// 1536x256은 가로세로 6:1인데 이미지 생성 모델은 정해진 몇 가지 비율만 출력할 수 있어서
/// 애초에 못 맞추는 경우가 많다.
///
/// 그래서 규격은 코드가 책임진다. GPT에게는 세 가지만 요구한다 — 단색 초록 배경,
/// 프레임마다 같은 키, 같은 바닥선. 저 셋만 코드로 고칠 수 없기 때문이다.
///
/// 처리 순서: 배경 제거 → 프레임 분리 → 공통 배율 계산 → 셀에 재배치.
/// </summary>
public static class AshSpriteSheetNormalizer
{
    private const string PlayerFolder = "Assets/Project/Art/Sprites/Player";
    private const string VfxFolder = "Assets/Project/Art/Sprites/VFX";

    /// <summary>
    /// 배치 방식. 기준점이 서로 다르다.
    ///
    /// 캐릭터는 발끝이 지면선에 닿아야 하고, 바닥 이펙트는 접지면이 같은 지면선에 놓여야
    /// 캐릭터 발 위치에 그냥 겹쳐 놓을 수 있다. 공중에 뜨는 것(투사체)만 정중앙이다.
    ///
    /// 처음엔 이펙트를 전부 프레임별 정중앙에 놓았는데, 원본을 재보니 두 검 이펙트 모두
    /// 공통 바닥선을 갖고 있었다(forward_burst는 6프레임 전부 y=488). 중앙을 맞추면
    /// 그 바닥선이 깨져서 이펙트가 위아래로 떠다닌다.
    ///
    /// 전방 폭발은 가로도 다르다. 검이 박힌 지점에서 앞으로 자라는 그림이라, 프레임마다
    /// 중앙을 맞추면 커질수록 시작점이 뒤로 밀려 폭발이 뒤로 미끄러져 보인다.
    /// </summary>
    private enum Mode
    {
        Character,     // 발끝 y=216, 다리 중심 x=128
        GroundCenter,  // 바닥 y=216, 가로 중앙 — 그 자리에서 사방으로 퍼지는 것
        GroundForward, // 바닥 y=216, 왼쪽 끝 고정 — 바닥을 따라 앞으로 자라는 것
        FloatCenter,   // 셀 정중앙 — 공중에 뜬 것(투사체, 공중 폭발)
    }

    /// <summary>
    /// 정규화할 파일 목록.
    ///
    /// <b>targetHeight</b>는 가장 큰 프레임을 몇 픽셀로 맞출지다. 0이면 기본값
    /// (캐릭터 160px / 이펙트 200px)을 쓴다.
    ///
    /// 시트마다 따로 줄 수 있게 만든 이유: 내려찍기는 대검을 머리 위로 치켜드는 프레임이
    /// 가장 높은데, 그 높이의 상당 부분이 몸이 아니라 검이다. 전부 160px에 맞추면 검까지
    /// 160 안에 들어가느라 몸이 다른 애니메이션보다 작아진다. 목표를 키우면 몸이 다시
    /// 160 근처가 된다.
    /// </summary>
    private static readonly (string folder, string source, string output,
                             int frames, Mode mode, int targetHeight)[] Jobs =
    {
        (PlayerFolder, "player_bow_6frames_raw.png",
                       "player_bow_6frames_1536x256.png", 6, Mode.Character, 0),

        // 210 = 몸 160 + 머리 위로 치켜든 검 약 50. 여전히 작으면 더 올린다.
        (PlayerFolder, "player_sword_slam_6frames_raw.png",
                       "player_sword_slam_6frames_1536x256.png", 6, Mode.Character, 210),

        // 지팡이도 머리 위로 치켜드는 프레임이 가장 높다. 내려찍기와 같은 이유로 목표를 키운다.
        (PlayerFolder, "player_staff_6frames_raw.png",
                       "player_staff_6frames_1536x256.png", 6, Mode.Character, 200),

        // R 필살기. 검을 머리 위로 치켜드는 프레임이 있어 목표를 키운다.
        (PlayerFolder, "player_ultimate_kings_ember_execution_6poses_v3_raw.png",
                       "player_ultimate_6frames_1536x256.png", 6, Mode.Character, 210),

        // 왕의 잿불 폭발. 발밑에서 사방으로 퍼진다.
        (VfxFolder, "vfx_kings_ember_full_room_6frames_raw.png",
                    "vfx_kings_ember_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 지팡이 주문은 바닥에서 솟는 잿불 기둥이다. 그 자리에서 위로 퍼진다.
        (VfxFolder, "vfx_ash_staff_ground_spell_6frames_raw.png",
                    "vfx_ash_staff_ground_spell_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 검이 박힌 지점의 충격파. 그 점을 중심으로 사방으로 퍼진다.
        (VfxFolder, "vfx_sword_slam_impact_6frames_raw.png",
                    "vfx_sword_slam_impact_6frames_1536x256.png", 6, Mode.GroundCenter, 0),

        // 검이 박힌 지점에서 앞으로 터져 나간다. 시작점 고정.
        (VfxFolder, "vfx_sword_slam_forward_burst_6frames_raw.png",
                    "vfx_sword_slam_forward_burst_6frames_1536x256.png", 6, Mode.GroundForward, 0),

        // 화살은 공중을 나는 투사체다. 바닥에 닿지 않는다.
        (VfxFolder, "vfx_ember_arrow_flight_6frames_raw.png",
                    "vfx_ember_arrow_flight_6frames_1536x256.png", 6, Mode.FloatCenter, 0),

        (VfxFolder, "vfx_ember_arrow_impact_6frames_raw.png",
                    "vfx_ember_arrow_impact_6frames_1536x256.png", 6, Mode.FloatCenter, 0),

        // vfx_ember_slash_A/B는 검 스킬이 내려찍기로 바뀌면서 쓰지 않는다. 목록에서 뺐다.
    };

    /// <summary>앞으로 자라는 이펙트의 시작점(셀 왼쪽에서의 거리, px).</summary>
    private const int ForwardEffectLeftInset = 28;

    /// <summary>
    /// 이펙트가 셀 안에서 차지할 최대 크기(px). 256 셀에 양옆 28px씩 여유를 둔 값이다.
    /// 셀 경계에 딱 붙으면 슬라이스한 뒤 옆 프레임 픽셀이 한 줄 비쳐 보인다.
    /// </summary>
    private const int EffectTargetSize = 200;

    // ── 배경 판정 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 초록기 = G - max(R, B). 이 값이 클수록 배경이다.
    ///
    /// "G가 크면 배경"으로 하면 밝은 회색(R=G=B=200)도 배경이 되어 캐릭터에 구멍이 뚫린다.
    /// R·B와의 차이를 보면 순수 초록만 걸러진다. 프롬프트에서 "캐릭터에 초록을 쓰지 마라"를
    /// 요구한 것이 여기서 값을 한다.
    /// </summary>
    private const int BackgroundGreenness = 110;

    /// <summary>이 값 아래면 완전 불투명. 사이 구간은 안티에일리어싱된 가장자리다.</summary>
    private const int OpaqueGreenness = 45;

    // ── 프레임 분리 ───────────────────────────────────────────────────────

    // 고정 임계값(예: "30픽셀 이하 틈은 같은 프레임")은 쓰지 않는다. 활 시트의 5·6번 인물
    // 사이가 12픽셀이라 둘이 합쳐졌고, 임계값을 12 아래로 낮추면 이번엔 인물 안의 빈틈
    // (활과 몸 사이 1~3픽셀)에서 갈라진다. 어떤 고정값도 전부를 만족시키지 못한다.
    //
    // 대신 프레임 수를 제약으로 쓴다. 6프레임을 원하면 경계는 정확히 5개이므로, 빈 구간 중
    // 가장 넓은 5개를 경계로 삼는다. 간격의 절대값이 아니라 순위로 판단하므로 조정할 값이 없다.

    /// <summary>이 픽셀 수보다 얇은 세로줄은 프레임으로 세지 않는다. 흩날린 티끌을 거른다.</summary>
    private const int MinColumnPixels = 3;

    /// <summary>
    /// 몸 중심을 찾을 때 볼 아래쪽 비율.
    ///
    /// 바운딩박스 중앙을 쓰면 안 되는 이유: 활이 오른쪽으로 뻗어 있어서 중심이 왼쪽으로
    /// 밀린다. 다리와 발은 무기처럼 튀어나오지 않으므로, 아래쪽 20%의 가로 중심이
    /// 몸이 실제로 서 있는 위치를 그대로 나타낸다.
    /// </summary>
    private const float LegBandRatio = 0.2f;

    [MenuItem("Tools/재의 길/원본 시트 정규화")]
    public static void NormalizeAll()
    {
        foreach (var (folder, source, output, frames, mode, targetHeight) in Jobs)
            Normalize(folder, source, output, frames, mode, targetHeight);

        AssetDatabase.Refresh();
    }

    private static void Normalize(string folder, string sourceName, string outputName,
                                  int expectedFrames, Mode mode, int targetHeight)
    {
        string sourcePath = $"{folder}/{sourceName}";
        if (!File.Exists(sourcePath))
        {
            Debug.LogError($"[시트 정규화] 원본을 못 찾았다: {sourcePath}");
            return;
        }

        // 임포트 설정(Read/Write)에 상관없이 읽으려고 파일에서 직접 디코딩한다.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(sourcePath)))
        {
            Debug.LogError($"[시트 정규화] PNG 디코딩 실패: {sourcePath}");
            Object.DestroyImmediate(texture);
            return;
        }

        int width = texture.width;
        int height = texture.height;
        Color32[] pixels = texture.GetPixels32();
        Object.DestroyImmediate(texture);

        RemoveBackground(pixels);

        var figures = FindFigures(pixels, width, height, expectedFrames);
        if (figures.Count != expectedFrames)
        {
            Debug.LogError($"[시트 정규화] {sourceName}에서 프레임을 {figures.Count}개 만들었는데 " +
                           $"{expectedFrames}개를 기대했다. 원본에 내용이 없거나 배경 판정이 잘못됐다.");
            return;
        }

        Compose(pixels, width, height, figures, folder, outputName, mode, targetHeight);
    }

    // ── 1단계: 배경 제거 ──────────────────────────────────────────────────

    /// <summary>
    /// 초록 배경을 알파로 바꾼다.
    ///
    /// 가장자리에서 섞인 초록을 역산하는 이유: 경계 픽셀은 캐릭터 색이 초록과 섞인 상태다.
    /// 그대로 두면 어두운 던전 배경 위에 얹었을 때 외곽에 초록 테두리가 남는다.
    /// </summary>
    private static void RemoveBackground(Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            int greenness = p.g - Mathf.Max(p.r, p.b);

            if (greenness >= BackgroundGreenness)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
                continue;
            }

            if (greenness <= OpaqueGreenness)
            {
                pixels[i] = new Color32(p.r, p.g, p.b, 255);
                continue;
            }

            float alpha = (BackgroundGreenness - greenness) /
                          (float)(BackgroundGreenness - OpaqueGreenness);

            float bleed = (1f - alpha) * p.g;
            byte g = (byte)Mathf.Clamp((p.g - bleed) / alpha, 0f, 255f);

            pixels[i] = new Color32(p.r, g, p.b, (byte)Mathf.RoundToInt(alpha * 255f));
        }
    }

    // ── 2단계: 프레임 분리 ────────────────────────────────────────────────

    private struct Figure
    {
        public int MinX, MaxX, MinY, MaxY;
        public int LegCenterX;

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
    }

    /// <summary>
    /// 세로줄마다 불투명 픽셀 수를 세어 프레임을 나눈다.
    /// 원하는 프레임 수가 N이면 경계는 N-1개다. 빈 구간을 넓은 순서로 정렬해 위쪽 N-1개를 쓴다.
    /// </summary>
    private static List<Figure> FindFigures(
        Color32[] pixels, int width, int height, int expectedFrames)
    {
        var columnCounts = new int[width];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[row + x].a > 16) columnCounts[x]++;
            }
        }

        int contentStart = -1;
        int contentEnd = -1;
        for (int x = 0; x < width; x++)
        {
            if (columnCounts[x] < MinColumnPixels) continue;

            if (contentStart < 0) contentStart = x;
            contentEnd = x;
        }

        var figures = new List<Figure>();
        if (contentStart < 0) return figures;

        var gaps = new List<(int start, int end)>();
        int gapStart = -1;

        for (int x = contentStart; x <= contentEnd; x++)
        {
            if (columnCounts[x] < MinColumnPixels)
            {
                if (gapStart < 0) gapStart = x;
                continue;
            }

            if (gapStart >= 0)
            {
                gaps.Add((gapStart, x - 1));
                gapStart = -1;
            }
        }

        int needed = expectedFrames - 1;

        if (gaps.Count < needed)
        {
            // 프레임끼리 붙어 있어 나눌 틈이 없다. 대개 같은 간격으로 그려주므로
            // 내용 범위를 균등 분할하는 추정이 잘 맞고, 아무것도 못 하는 것보다 낫다.
            Debug.LogWarning($"[시트 정규화] 빈 구간이 {gaps.Count}개뿐이라 {expectedFrames}개로 " +
                             "나눌 수 없다. 내용 범위를 균등 분할한다.");

            float span = (contentEnd - contentStart + 1) / (float)expectedFrames;
            for (int i = 0; i < expectedFrames; i++)
            {
                int from = contentStart + Mathf.RoundToInt(i * span);
                int to = contentStart + Mathf.RoundToInt((i + 1) * span) - 1;
                figures.Add(MeasureFigure(pixels, width, height, from, Mathf.Min(to, contentEnd)));
            }

            return figures;
        }

        gaps.Sort((a, b) => (b.end - b.start) - (a.end - a.start));
        var separators = gaps.GetRange(0, needed);
        separators.Sort((a, b) => a.start - b.start);

        int figureStart = contentStart;
        foreach (var separator in separators)
        {
            figures.Add(MeasureFigure(pixels, width, height, figureStart, separator.start - 1));
            figureStart = separator.end + 1;
        }

        figures.Add(MeasureFigure(pixels, width, height, figureStart, contentEnd));

        return figures;
    }

    /// <summary>프레임 하나의 세로 범위와 다리 중심을 잰다.</summary>
    private static Figure MeasureFigure(
        Color32[] pixels, int width, int height, int minX, int maxX)
    {
        int minY = height;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                if (pixels[row + x].a <= 16) continue;

                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                break;
            }
        }

        int bandTop = maxY - Mathf.RoundToInt((maxY - minY) * LegBandRatio);
        int legMinX = maxX;
        int legMaxX = minX;

        for (int y = bandTop; y <= maxY; y++)
        {
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                if (pixels[row + x].a <= 16) continue;

                if (x < legMinX) legMinX = x;
                if (x > legMaxX) legMaxX = x;
            }
        }

        int legCenter = legMaxX >= legMinX ? (legMinX + legMaxX) / 2 : (minX + maxX) / 2;

        return new Figure
        {
            MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY, LegCenterX = legCenter,
        };
    }

    // ── 3단계: 재배치 ─────────────────────────────────────────────────────

    private static void Compose(Color32[] pixels, int width, int height, List<Figure> figures,
                                string folder, string outputName, Mode mode, int targetHeight)
    {
        int cell = AshPlayerSpriteSheets.CellSize;
        int groundY = AshPlayerSpriteSheets.GroundLineY - 1; // 발끝이 놓일 행(216)

        // 배율을 프레임마다 따로 구하면 안 된다. 웅크리는 동작은 일부러 키가 낮고, 이펙트도
        // 1번은 작고 3번이 크게 터지는 게 연출이다. 가장 큰 프레임 기준 하나를 전부에 적용한다.
        int tallest = 0;
        int widest = 0;
        foreach (var f in figures)
        {
            tallest = Mathf.Max(tallest, f.Height);
            widest = Mathf.Max(widest, f.Width);
        }

        float scale;
        if (mode == Mode.Character)
        {
            int wanted = targetHeight > 0 ? targetHeight : AshPlayerSpriteSheets.CharacterPixelHeight;
            scale = tallest > 0 ? wanted / (float)tallest : 1f;
        }
        else
        {
            // 이펙트는 가로로 퍼지기도 세로로 솟기도 해서, 긴 쪽을 기준으로 맞춰야 셀을 안 벗어난다.
            int longest = Mathf.Max(tallest, widest);
            int wanted = targetHeight > 0 ? targetHeight : EffectTargetSize;
            scale = longest > 0 ? wanted / (float)longest : 1f;
        }

        int outWidth = cell * figures.Count;
        var output = new Color32[outWidth * cell];

        // 배치 좌표를 여기서 계산해 넘긴다. 계산과 그리기가 한 함수에 섞여 있으면
        // 결과가 어긋났을 때 어느 쪽이 틀린 건지 밖에서 볼 수가 없다.
        for (int i = 0; i < figures.Count; i++)
        {
            Figure figure = figures[i];

            int drawWidth = Mathf.Max(1, Mathf.RoundToInt(figure.Width * scale));
            int drawHeight = Mathf.Max(1, Mathf.RoundToInt(figure.Height * scale));

            int destTop = mode == Mode.FloatCenter
                ? (cell - drawHeight) / 2
                : groundY - drawHeight + 1;

            int destLeft;
            if (mode == Mode.Character)
            {
                destLeft = (cell / 2) - Mathf.RoundToInt((figure.LegCenterX - figure.MinX) * scale);
            }
            else if (mode == Mode.GroundForward)
            {
                destLeft = ForwardEffectLeftInset;
            }
            else
            {
                destLeft = (cell - drawWidth) / 2;
            }

            DrawFigure(pixels, width, height, figure, output, outWidth, cell, i,
                       scale, drawWidth, drawHeight, destTop, destLeft);
        }

        var result = new Texture2D(outWidth, cell, TextureFormat.RGBA32, false);
        result.SetPixels32(output);
        result.Apply();

        string outputPath = $"{folder}/{outputName}";
        File.WriteAllBytes(outputPath, result.EncodeToPNG());
        Object.DestroyImmediate(result);

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

        string anchor = mode switch
        {
            Mode.Character => $"발끝 y={groundY}, 다리 중심 x={cell / 2}",
            Mode.GroundForward => $"바닥 y={groundY}, 왼쪽 끝 x={ForwardEffectLeftInset} 고정",
            Mode.FloatCenter => $"공중 — 셀 정중앙 ({cell / 2}, {cell / 2})",
            _ => $"바닥 y={groundY}, 가로 중앙 x={cell / 2}",
        };

        // 넣은 값이 아니라 실제로 그려진 픽셀을 다시 잰다. 배치 계산이 틀리면 넣은 값만
        // 보고는 알 수 없고, 결과 PNG를 밖에서 열어 재봐야 알게 된다.
        var report = new System.Text.StringBuilder();
        for (int i = 0; i < figures.Count; i++)
        {
            int x0 = i * cell;
            int minX = cell, maxX = -1, minY = cell, maxY = -1;

            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    if (output[y * outWidth + x0 + x].a <= 16) continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0) { report.Append($"\n  {i}: 비어 있음"); continue; }

            report.Append($"\n  {i}: 가로 {minX}~{maxX} 세로 {minY}~{maxY} " +
                          $"(바닥 y={maxY}, 높이 {maxY - minY + 1})");
        }

        Debug.Log($"[시트 정규화] {outputName} 생성 완료 ({outWidth}x{cell}, {figures.Count}프레임)\n" +
                  $"  공통 배율 {scale:F3} (가장 큰 프레임 {widest}x{tallest}px)\n" +
                  $"  기준 {anchor}\n" +
                  $"  실측 결과:{report}");
    }

    /// <summary>
    /// 프레임 하나를 시킨 자리에 축소해 그려 넣는다.
    /// 배치 좌표는 호출하는 쪽이 정한다 — 여기서는 판단하지 않는다.
    /// </summary>
    private static void DrawFigure(
        Color32[] source, int srcWidth, int srcHeight, Figure figure,
        Color32[] output, int outWidth, int cell, int cellIndex, float scale,
        int drawWidth, int drawHeight, int destTop, int destLeft)
    {
        int cellX = cellIndex * cell;

        for (int dy = 0; dy < drawHeight; dy++)
        {
            int outY = destTop + dy;
            if (outY < 0 || outY >= cell) continue;

            for (int dx = 0; dx < drawWidth; dx++)
            {
                int outX = cellX + destLeft + dx;
                if (outX < cellX || outX >= cellX + cell) continue;

                float srcX = figure.MinX + (dx + 0.5f) / scale;
                float srcY = figure.MinY + (dy + 0.5f) / scale;

                output[outY * outWidth + outX] = SampleBilinear(source, srcWidth, srcHeight, srcX, srcY);
            }
        }
    }

    /// <summary>
    /// 이중선형 보간으로 한 점을 뽑는다.
    ///
    /// 알파를 곱한 상태로 섞는 이유: 투명한 픽셀도 RGB 값을 갖고 있어서, 그냥 섞으면
    /// 가장자리에 투명 영역의 색이 배어 나온다. 알파를 곱해두면 그 기여도가 0이 된다.
    /// </summary>
    private static Color32 SampleBilinear(Color32[] source, int width, int height, float x, float y)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x - 0.5f), 0, width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y - 0.5f), 0, height - 1);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);

        float fx = Mathf.Clamp01(x - 0.5f - x0);
        float fy = Mathf.Clamp01(y - 0.5f - y0);

        float r = 0f, g = 0f, b = 0f, a = 0f;

        Accumulate(source[y0 * width + x0], (1f - fx) * (1f - fy), ref r, ref g, ref b, ref a);
        Accumulate(source[y0 * width + x1], fx * (1f - fy), ref r, ref g, ref b, ref a);
        Accumulate(source[y1 * width + x0], (1f - fx) * fy, ref r, ref g, ref b, ref a);
        Accumulate(source[y1 * width + x1], fx * fy, ref r, ref g, ref b, ref a);

        if (a <= 0.001f) return new Color32(0, 0, 0, 0);

        return new Color32(
            (byte)Mathf.Clamp(r / a, 0f, 255f),
            (byte)Mathf.Clamp(g / a, 0f, 255f),
            (byte)Mathf.Clamp(b / a, 0f, 255f),
            (byte)Mathf.Clamp(a * 255f, 0f, 255f));
    }

    private static void Accumulate(
        Color32 pixel, float weight, ref float r, ref float g, ref float b, ref float a)
    {
        float alpha = pixel.a / 255f;
        float w = weight * alpha;

        r += pixel.r * w;
        g += pixel.g * w;
        b += pixel.b * w;
        a += w;
    }
}
