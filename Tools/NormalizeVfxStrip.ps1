<#
    NormalizeVfxStrip.ps1
    ------------------------------------------------------------------
    이미지 생성기가 준 VFX 한 줄(보통 2172x724, 6프레임)을 게임 규격 1536x256(256 셀 x 6)으로 만든다.

    왜 필요한가:
    Tools → 재의 길 → 원본 시트 정규화(AshSpriteSheetNormalizer)는 <초록 배경>만 읽는다.
    초록이 아닌 픽셀을 전부 불투명으로 만들기 때문에, 배경이 이미 투명한 원본을 넣으면 투명한
    바탕까지 검은 불투명이 되어 캔버스 전체가 한 덩어리로 잡힌다. 2026-09-13에 뽑은 VFX 7장 중
    6장이 투명 원본이다. 궁극기 VFX는 그래서 도구 없이 손으로 처리했고, 남은 7장은 같은 일을
    되풀이하지 않으려고 이 도구를 만들었다.

    무엇을 하는가:
      1. 배경 판정: 네 모서리의 알파가 전부 0이면 투명 원본, 아니면 크로마키 초록 원본
      2. 알파 문턱(AlphaCut) 미만을 지운다. 생성기는 "반투명 가장자리 금지"를 요구해도 불티 둘레에
         붉은 반투명 번짐을 깐다(2026-09-14의 6장에서 캔버스 픽셀의 3~12%). 픽셀 아트 사이에서 그것만 뿌옇게 뜬다
      3. 남은 픽셀의 초록 번짐을 깎는다(G를 max(R,B)까지). 이 팔레트는 전부 R >= G라 그림은 안 변한다
      4. 프레임 분리: 빈 열 중 가장 넓은 (프레임 수 - 1)개를 경계로 쓴다. C# 정규화 도구와 같은 규칙이다
      5. 프레임마다 기준점을 잰다(AnchorX / AnchorY) — 아래 "기준점" 참고
      6. 모든 프레임에 같은 배율: 기준점을 셀의 Pivot 자리에 뒀을 때 어느 프레임도 Margin 밖으로
         안 나가는 가장 큰 값, 그리고 긴 변이 MaxSize를 넘지 않는 값 중 작은 쪽
      7. 최근접 표본으로 옮긴다. 픽셀 아트라 색을 섞지 않는다(궁극기 VFX 처리와 같은 방식)

    기준점:
    생성기는 "모든 프레임을 같은 자리에"를 지키지 않는다. 대시 원본은 1번 프레임의 고리가 칸 가운데,
    2~4번은 칸 왼쪽 끝에 있고, 화살 발사 원본은 2번 프레임이 자기 칸 밖까지 나가 있다.
    그래서 칸 격자를 믿지 않고 <그림 안의 변하지 않는 요소>를 프레임마다 찾아 한 점에 모은다.

      AnchorX  BoxCenter       그림 범위의 가로 가운데
               WidestRowCenter 가장 넓은 행(바닥 타원·균열선)의 가운데 — 파편이 한쪽으로 튀어도 안 흔들린다
               LumaCentroid    밝기로 가중한 무게중심 — 흰 심지가 있는 폭발. 잿조각(어두움)은 거의 안 끈다
               LeftEdge        왼쪽 끝 — 출발점에서 오른쪽으로 뻗는 것(대시 자국, 활 발사)
               RightEdge       오른쪽 끝 — 화살촉
      AnchorY  BoxCenter       그림 범위의 세로 가운데
               WidestRow       가장 넓은 행 — 바닥에 누운 타원·균열선의 높이

    세로 기준점만 <프레임별 값의 중앙값 하나>를 전 프레임에 쓴다. 원본은 6프레임이 한 줄에 있어서
    세로 좌표계를 공유하고, 생성기가 축(수평선·바닥선)은 비교적 잘 지킨다. 프레임마다 따로 맞추면
    파편이 위로 튀는 프레임마다 그림 전체가 아래로 밀려 위아래로 떤다.

    Pivot은 셀 안의 좌표(px, 위에서 센다)다. 유니티 스프라이트 피벗은 (PivotX/256, 1 - PivotY/256)이고
    끝에 출력한다. 지면선 피벗(AshPlayerSpriteSheets.Pivot)은 PivotY 217, 화살촉은 PivotX 228,
    앞으로 뻗는 것의 출발점은 PivotX 28이다(C# 정규화 도구의 TipRightInset·ForwardEffectLeftInset과 같은 값).

    사용법:
      미리보기(%TEMP%에 씀):
        powershell -File Tools\NormalizeVfxStrip.ps1 `
            -SourcePath Assets\Project\Art\NewPlayerImages\Staff\staff_vfx_v1_raw.png `
            -TargetPath Assets\Project\Art\Sprites\VFX\vfx_ash_staff_ground_spell_6frames_1536x256.png `
            -AnchorX WidestRowCenter -AnchorY WidestRow -PivotY 217
      설치(기존 시트는 시트 옆 Raw\<BackupName>에 백업):  위 명령에 -Apply

    2026-09-14에 쓴 인자는 DEVELOPMENT_LOG.md의 같은 날짜 항목에 표로 남겼다.
#>
param(
    # 생성기가 준 원본 PNG.
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    # 게임 시트 경로. -Apply가 없으면 이 이름으로 %TEMP%에 미리보기만 쓴다.
    [Parameter(Mandatory = $true)]
    [string]$TargetPath,

    # 붙이면 TargetPath에 쓴다. 이미 있으면 Raw\<BackupName>에 백업한 뒤 덮어쓴다.
    [switch]$Apply,

    # 백업 폴더 이름(시트 옆 Raw\ 아래).
    [string]$BackupName = "PreNewCharacterVfx",

    [ValidateSet("BoxCenter", "WidestRowCenter", "LumaCentroid", "LeftEdge", "RightEdge")]
    [string]$AnchorX = "BoxCenter",

    [ValidateSet("BoxCenter", "WidestRow")]
    [string]$AnchorY = "BoxCenter",

    # 기준점이 놓일 셀 안의 자리(px, 위에서 센다).
    [double]$PivotX = 128,
    [double]$PivotY = 128,

    # 이 알파 미만은 지운다. 64에서 번짐은 사라지고 불꽃의 계단 가장자리는 남는다(0.25 불투명).
    [int]$AlphaCut = 64,

    # 크로마키 원본에서 초록기(G - max(R,B))가 이보다 크면 배경으로 본다.
    # 기본 판정(G가 R·B의 1.35배)만 쓰면 주황과 초록이 섞인 가장자리가 살아남아
    # 초록을 깎은 뒤 올리브색 점으로 남는다(화살 명중 원본에서 1338개).
    [int]$GreenKey = 24,

    # 긴 변 상한(px)과 셀 가장자리 여백(px). 여백은 슬라이스한 뒤 옆 칸 픽셀이 비치지 않게 둔다.
    [int]$MaxSize = 240,
    [int]$Margin = 8,

    # 0보다 크면 배율을 계산하지 않고 이 값을 쓴다. 넘치는 프레임은 경고한다.
    [double]$Scale = 0,

    # 프레임 경계를 원본 x 좌표로 직접 준다(프레임 수 - 1개). 자동 분리가 틀릴 때만 쓴다.
    [int[]]$Cuts = @(),

    [int]$Frames = 6,
    [int]$Cell = 256
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

# 상대 경로는 프로젝트 루트 기준으로 푼다. 다른 도구들과 같은 규칙이다.
function Resolve-ProjectPath([string]$p) {
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    return [System.IO.Path]::GetFullPath((Join-Path $root $p))
}

$source = Resolve-ProjectPath $SourcePath
$target = Resolve-ProjectPath $TargetPath
if (-not (Test-Path -LiteralPath $source)) { Write-Error "원본을 못 찾았다: $source"; return }

# --- 원본 읽기 ---------------------------------------------------------------
# 바이트만 옮기고 바로 닫는다. new Bitmap(image)로 복사하면 GDI+가 반투명 픽셀을
# 미리곱한 알파로 한 번 거쳐 값이 틀어진다(NormalizeGeneratedStrip.ps1과 같은 이유).
$bmp = New-Object System.Drawing.Bitmap $source
$hasAlphaChannel = [System.Drawing.Image]::IsAlphaPixelFormat($bmp.PixelFormat)
$W = $bmp.Width
$H = $bmp.Height
$rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                      [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$px = New-Object byte[] ($stride * $H)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
$bmp.UnlockBits($data)
$bmp.Dispose()

# --- 1) 배경 판정 ------------------------------------------------------------
# 모서리 네 곳이 전부 투명이면 투명 원본이다. 알파 채널이 없는 PNG(24bpp)는 LockBits가 알파를
# 255로 채워 주므로 자연히 크로마키 쪽으로 간다.
$cornerAlpha = @(
    $px[3],
    $px[(($W - 1) * 4) + 3],
    $px[(($H - 1) * $stride) + 3],
    $px[(($H - 1) * $stride) + (($W - 1) * 4) + 3]
)
$isAlphaSource = $hasAlphaChannel -and (($cornerAlpha | Measure-Object -Maximum).Maximum -eq 0)
$kindText = if ($isAlphaSource) { "투명 배경" } else { "크로마키 초록" }
Write-Output ("원본      : {0}x{1}, {2}" -f $W, $H, $kindText)

# --- 2~3) 알파 문턱, 초록 번짐 ------------------------------------------------
$columnCounts = New-Object int[] $W
$removedSoft = 0
for ($y = 0; $y -lt $H; $y++) {
    $rowBase = $y * $stride
    for ($x = 0; $x -lt $W; $x++) {
        $o = $rowBase + ($x * 4)
        $b = [int]$px[$o]; $g = [int]$px[$o + 1]; $r = [int]$px[$o + 2]; $a = [int]$px[$o + 3]
        $maxRB = [Math]::Max($r, $b)

        if (-not $isAlphaSource) {
            $isBackground = ($g -gt 90 -and $g -gt ($r * 1.35) -and $g -gt ($b * 1.35)) -or
                            (($g - $maxRB) -gt $GreenKey)
            $a = if ($isBackground) { 0 } else { 255 }
        }

        if ($a -lt $AlphaCut) {
            if ($a -gt 0) { $removedSoft++ }
            $px[$o + 3] = 0
            continue
        }

        $px[$o + 3] = [byte]$a
        if ($g -gt $maxRB) { $px[$o + 1] = [byte]$maxRB }
        $columnCounts[$x]++
    }
}
if ($isAlphaSource) {
    Write-Output ("번짐 제거 : 알파 1~{0} 픽셀 {1}개" -f ($AlphaCut - 1), $removedSoft)
}

# --- 4) 프레임 분리 ----------------------------------------------------------
# 세 픽셀보다 얇은 세로줄은 내용으로 치지 않는다. 흩날린 티끌 하나가 빈 구간을 끊지 않게 한다.
$minColumnPixels = 3
$contentStart = -1
$contentEnd = -1
for ($x = 0; $x -lt $W; $x++) {
    if ($columnCounts[$x] -lt $minColumnPixels) { continue }
    if ($contentStart -lt 0) { $contentStart = $x }
    $contentEnd = $x
}
if ($contentStart -lt 0) { Write-Error "원본에 남은 그림이 없다. AlphaCut이 너무 높거나 배경 판정이 틀렸다."; return }

$ranges = New-Object System.Collections.Generic.List[object]
if ($Cuts.Count -gt 0) {
    if ($Cuts.Count -ne ($Frames - 1)) { Write-Error "Cuts는 $($Frames - 1)개여야 한다(지금 $($Cuts.Count)개)."; return }
    $from = $contentStart
    foreach ($cut in ($Cuts | Sort-Object)) {
        $ranges.Add(@($from, ($cut - 1)))
        $from = $cut
    }
    $ranges.Add(@($from, $contentEnd))
    Write-Output "프레임 경계: 수동 지정 ($($Cuts -join ', '))"
} else {
    # 고정 간격 대신 순위를 쓰는 이유는 C# 정규화 도구의 주석과 같다 — 원하는 프레임 수가 N이면
    # 경계는 정확히 N-1개라, 빈 구간을 넓은 순서로 세우면 조정할 숫자 없이 갈린다.
    # 2026-09-14의 7장은 전부 이 규칙으로 맞게 갈렸다(대시·명중·발사처럼 끝 프레임이 불티로 흩어진 것 포함).
    $gaps = New-Object System.Collections.Generic.List[object]
    $gapStart = -1
    for ($x = $contentStart; $x -le $contentEnd; $x++) {
        if ($columnCounts[$x] -lt $minColumnPixels) {
            if ($gapStart -lt 0) { $gapStart = $x }
            continue
        }
        if ($gapStart -ge 0) { $gaps.Add(@($gapStart, ($x - 1))); $gapStart = -1 }
    }
    if ($gaps.Count -lt ($Frames - 1)) {
        Write-Error "빈 구간이 $($gaps.Count)개뿐이라 $Frames 프레임으로 못 나눈다. -Cuts로 경계를 직접 줘라."
        return
    }
    $separators = $gaps | Sort-Object -Property @{ Expression = { $_[1] - $_[0] }; Descending = $true } |
                  Select-Object -First ($Frames - 1) | Sort-Object -Property @{ Expression = { $_[0] } }
    $from = $contentStart
    foreach ($sep in $separators) {
        $ranges.Add(@($from, ($sep[0] - 1)))
        $from = $sep[1] + 1
    }
    $ranges.Add(@($from, $contentEnd))
}

# --- 5) 프레임마다 범위와 기준점 재료 ------------------------------------------
$measures = @()
$useLuma = $AnchorX -eq "LumaCentroid"
for ($k = 0; $k -lt $Frames; $k++) {
    $x0 = $ranges[$k][0]; $x1 = $ranges[$k][1]
    $minX = $W; $maxX = -1; $minY = $H; $maxY = -1
    $rowCounts = New-Object int[] $H
    $lumaSum = 0.0; $lumaXSum = 0.0

    for ($y = 0; $y -lt $H; $y++) {
        $rowBase = $y * $stride
        for ($x = $x0; $x -le $x1; $x++) {
            $o = $rowBase + ($x * 4)
            $a = [int]$px[$o + 3]
            if ($a -eq 0) { continue }
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            $rowCounts[$y]++

            # 밝기를 제곱해서 가중한다. 흰 심지(#FFF2C0)가 무게를 거의 다 가져가고
            # 흑요석 파편(#1A1718)은 사실상 0이라, 파편이 한쪽으로 날아가도 중심이 안 끌려간다.
            # 픽셀마다 드는 계산이라 그 기준점을 고른 경우에만 한다.
            #
            # 변수 이름을 $weight로 둔 이유: PowerShell 변수는 대소문자를 가리지 않아서 $w로 쓰면
            # 원본 폭 $W를 덮어쓴다. 처음 그렇게 짰다가 두 번째 프레임부터 범위 계산이 망가져
            # 배율이 0.06으로 나왔다.
            if ($useLuma) {
                $luma = (0.299 * $px[$o + 2] + 0.587 * $px[$o + 1] + 0.114 * $px[$o]) / 255.0
                $weight = ($a / 255.0) * $luma
                $weight = $weight * $weight
                $lumaSum += $weight
                $lumaXSum += $weight * ($x + 0.5)
            }
        }
    }

    $wideRow = 0
    for ($y = 1; $y -lt $H; $y++) { if ($rowCounts[$y] -gt $rowCounts[$wideRow]) { $wideRow = $y } }
    $wideMin = $x1; $wideMax = $x0
    $rowBase = $wideRow * $stride
    for ($x = $x0; $x -le $x1; $x++) {
        if ($px[$rowBase + ($x * 4) + 3] -eq 0) { continue }
        if ($x -lt $wideMin) { $wideMin = $x }; if ($x -gt $wideMax) { $wideMax = $x }
    }

    $ax = switch ($AnchorX) {
        "BoxCenter"       { ($minX + $maxX + 1) / 2.0 }
        "WidestRowCenter" { ($wideMin + $wideMax + 1) / 2.0 }
        "LumaCentroid"    { if ($lumaSum -gt 0) { $lumaXSum / $lumaSum } else { ($minX + $maxX + 1) / 2.0 } }
        "LeftEdge"        { [double]$minX }
        "RightEdge"       { [double]($maxX + 1) }
    }
    $ayFrame = switch ($AnchorY) {
        "BoxCenter" { ($minY + $maxY + 1) / 2.0 }
        "WidestRow" { $wideRow + 0.5 }
    }

    $measures += [pscustomobject]@{
        X0 = $x0; X1 = $x1; MinX = $minX; MaxX = $maxX; MinY = $minY; MaxY = $maxY
        AnchorX = $ax; AnchorYFrame = $ayFrame
    }
}

# 세로 기준점은 중앙값 하나로 묶는다(머리 주석 참고).
$sortedY = @($measures | ForEach-Object { $_.AnchorYFrame } | Sort-Object)
$ay = ($sortedY[[int][Math]::Floor(($Frames - 1) / 2)] + $sortedY[[int][Math]::Ceiling(($Frames - 1) / 2)]) / 2.0

# --- 6) 공통 배율 ------------------------------------------------------------
$fit = [double]::MaxValue
$fitReason = ""
foreach ($m in $measures) {
    $left = $m.AnchorX - $m.MinX; $right = ($m.MaxX + 1) - $m.AnchorX
    $up = $ay - $m.MinY;          $down = ($m.MaxY + 1) - $ay
    $candidates = @(
        @(($(if ($left -gt 0) { ($PivotX - $Margin) / $left } else { [double]::MaxValue })), "왼쪽"),
        @(($(if ($right -gt 0) { ($Cell - $Margin - $PivotX) / $right } else { [double]::MaxValue })), "오른쪽"),
        @(($(if ($up -gt 0) { ($PivotY - $Margin) / $up } else { [double]::MaxValue })), "위"),
        @(($(if ($down -gt 0) { ($Cell - $Margin - $PivotY) / $down } else { [double]::MaxValue })), "아래"),
        @(($MaxSize / [double][Math]::Max($m.MaxX - $m.MinX + 1, $m.MaxY - $m.MinY + 1)), "긴 변 상한")
    )
    foreach ($c in $candidates) {
        if ($c[0] -lt $fit) { $fit = $c[0]; $fitReason = "{0} (프레임 {1})" -f $c[1], ([array]::IndexOf($measures, $m) + 1) }
    }
}
$s = if ($Scale -gt 0) { $Scale } else { $fit }
if ($Scale -gt 0) {
    Write-Output ("배율      : {0:N3} (수동 지정, 계산값은 {1:N3})" -f $s, $fit)
} else {
    Write-Output ("배율      : {0:N3} (묶은 조건: {1})" -f $s, $fitReason)
}
Write-Output ("기준점    : 가로 {0}, 세로 {1} = 원본 y {2:N1} → 셀 ({3}, {4})" -f $AnchorX, $AnchorY, $ay, $PivotX, $PivotY)

# --- 7) 최근접 표본으로 옮긴다 -------------------------------------------------
$outW = $Cell * $Frames
$outStride = $outW * 4
$out = New-Object byte[] ($outStride * $Cell)

for ($k = 0; $k -lt $Frames; $k++) {
    $m = $measures[$k]
    # 안쪽 루프에서 속성을 매번 읽으면 PowerShell이 느려서 지역 변수로 꺼내 둔다.
    $anchorXk = $m.AnchorX; $frameX0 = $m.X0; $frameX1 = $m.X1
    for ($ty = 0; $ty -lt $Cell; $ty++) {
        # 출력 픽셀의 가운데를 원본 좌표로 되돌려 그 자리의 픽셀을 가져온다.
        $sy = [int][Math]::Floor($ay + (($ty + 0.5) - $PivotY) / $s)
        if ($sy -lt 0 -or $sy -ge $H) { continue }
        $srcRow = $sy * $stride
        $dstRow = $ty * $outStride
        for ($tx = 0; $tx -lt $Cell; $tx++) {
            $sx = [int][Math]::Floor($anchorXk + (($tx + 0.5) - $PivotX) / $s)
            # 자기 프레임 범위 밖은 옆 프레임 그림이다. 셀이 넓게 잡혀도 끌어오지 않는다.
            if ($sx -lt $frameX0 -or $sx -gt $frameX1) { continue }
            $so = $srcRow + ($sx * 4)
            if ($px[$so + 3] -eq 0) { continue }
            $to = $dstRow + ((($k * $Cell) + $tx) * 4)
            $out[$to] = $px[$so]; $out[$to + 1] = $px[$so + 1]
            $out[$to + 2] = $px[$so + 2]; $out[$to + 3] = $px[$so + 3]
        }
    }

    # 실제로 그려진 픽셀을 다시 잰다. 넣은 값만 보고는 배치 계산이 틀렸는지 알 수 없다.
    $minX = $Cell; $maxX = -1; $minY = $Cell; $maxY = -1
    for ($ty = 0; $ty -lt $Cell; $ty++) {
        for ($tx = 0; $tx -lt $Cell; $tx++) {
            if ($out[($ty * $outStride) + ((($k * $Cell) + $tx) * 4) + 3] -eq 0) { continue }
            if ($tx -lt $minX) { $minX = $tx }; if ($tx -gt $maxX) { $maxX = $tx }
            if ($ty -lt $minY) { $minY = $ty }; if ($ty -gt $maxY) { $maxY = $ty }
        }
    }
    $clip = ""
    if ($minX -eq 0 -or $minY -eq 0 -or $maxX -eq ($Cell - 1) -or $maxY -eq ($Cell - 1)) { $clip = " | 셀 가장자리에 닿음 — 잘렸을 수 있다" }
    if ($maxX -lt 0) {
        Write-Output ("  f{0}: 원본 x {1}-{2} | 비어 있음" -f ($k + 1), $m.X0, $m.X1)
    } else {
        Write-Output ("  f{0}: 원본 x {1}-{2}, 기준 x {3:N1} → 결과 x {4}-{5}, y {6}-{7}{8}" -f
                      ($k + 1), $m.X0, $m.X1, $m.AnchorX, $minX, $maxX, $minY, $maxY, $clip)
    }
}

# --- 저장 ---------------------------------------------------------------------
$result = New-Object System.Drawing.Bitmap $outW, $Cell, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$outRect = New-Object System.Drawing.Rectangle 0, 0, $outW, $Cell
$outData = $result.LockBits($outRect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
[System.Runtime.InteropServices.Marshal]::Copy($out, 0, $outData.Scan0, $out.Length)
$result.UnlockBits($outData)

if ($Apply) {
    if (Test-Path -LiteralPath $target) {
        $backupDir = Join-Path ([System.IO.Path]::GetDirectoryName($target)) "Raw\$BackupName"
        if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }
        $backup = Join-Path $backupDir ([System.IO.Path]::GetFileName($target))
        # 이미 백업이 있으면 덮지 않는다. 두 번째 실행에서 덮으면 원래 그림이 아니라
        # 첫 실행 결과가 백업으로 남는다.
        if (Test-Path -LiteralPath $backup) { Write-Output "백업      : 이미 있음 → 그대로 둔다 ($backup)" }
        else { Copy-Item -LiteralPath $target -Destination $backup; Write-Output "백업      : $backup" }
    }
    $targetDir = [System.IO.Path]::GetDirectoryName($target)
    if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }
    $result.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "설치      : $target"
} else {
    $preview = Join-Path $env:TEMP ([System.IO.Path]::GetFileNameWithoutExtension($target) + "-preview.png")
    $result.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "미리보기  : $preview (설치하려면 -Apply)"
}
$result.Dispose()

Write-Output ("스프라이트 피벗: ({0}, {1}) — AshVfxSpriteSlicer 표의 피벗과 같아야 붙는 자리가 안 어긋난다" -f
              ($PivotX / $Cell), (1 - ($PivotY / $Cell)))
