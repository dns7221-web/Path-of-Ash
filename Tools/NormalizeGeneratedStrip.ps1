<#
    NormalizeGeneratedStrip.ps1
    ------------------------------------------------------------------
    이미지 생성기가 준 원본(3x2 격자 또는 한 줄)을 게임 규격 6프레임 한 줄(1536x256)로 만든다.

    왜 필요한가:
    생성기는 요청한 크기를 지키지 않는다. 넓게 달라고 하면 2172x724, 격자는 1536x1024로 온다.
    캐릭터 크기도 매번 다르다(키 400~520px). 게임은 256 셀에 키 190px, 발바닥 216행이어야 한다.
    걷기 때는 시험본마다 좌표를 손으로 박아 잘랐다(_ArtReview/20260909-north-walk6-v1).
    공격은 5방향을 뽑아야 해서 매번 손으로 할 수 없다.

    무엇을 하는가:
      1. 원본을 Layout(가로 칸 x 세로 칸)대로 똑같이 나눈다. 읽는 순서는 왼→오, 위→아래다
      2. 크로마키 초록을 투명으로 바꾸고 남은 픽셀의 초록 번짐을 깎는다
         (RemoveChromaKeyGreen.ps1과 같은 판정 기준)
      3. 기준 프레임(StandingFrame, 기본은 마지막 = 대기 자세)의 키가 Height가 되는 배율을 구해
         <b>모든 프레임에 같은 배율</b>을 쓴다. 프레임마다 키를 맞추면 웅크린 자세가 커져 버린다
      4. 프레임마다 발바닥 행을 GroundLine에, 원본 칸의 가운데를 셀 가운데에 맞춘다
      5. 최근접 표본으로 줄인다. 픽셀 아트라 색을 섞으면 안 된다
      6. 선택: 마지막 프레임을 실제 대기 그림으로 바꾼다(IdleSheet). 대기로 돌아갈 때 안 튄다.
         2026-09-11 공격 E는 생성기가 그린 마지막 프레임이 실제 대기보다 머리가 10px 왼쪽이었다

    발바닥 행을 "가장 아래 불투명 행"으로 잡지 않는 이유:
    후속 동작에서 칼끝이 발보다 아래로 내려오는 프레임이 있다. 그래서 불투명 픽셀이
    칸 폭의 MassFraction 이상인 가장 아래 행을 발로 본다. 칼은 가늘어서 이 문턱을 못 넘고
    부츠는 넘는다.

    가로를 뒷발이 아니라 칸 가운데로 맞추는 이유:
    프롬프트가 "엉덩이를 칸 가운데에" 두게 했다. 뒷발 기준으로 맞추면 찌르기 프레임에서 몸이
    앞으로 84px 나가 칼끝이 셀 밖으로 잘린다(2026-09-11 실측).

    결과는 새 파일로만 쓴다. 에셋을 덮어쓰지 않으므로 -Apply가 없다. 설치는 다음 단계의 일이다.

    사용법:
      powershell -File Tools\NormalizeGeneratedStrip.ps1 `
          -SourcePath output\imagegen\basic-attack-grid-3x2.png `
          -OutputPath _ArtReview\20260911-attack-v1\player_attack_E_6frames_1536x256.png `
          -IdleSheet Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_idle.png -IdleRow 6
#>
param(
    # 생성기가 준 원본 PNG.
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    # 결과 PNG. 비우면 원본 옆에 "_6frames_1536x256"을 붙여 쓴다.
    [string]$OutputPath = "",

    # 원본의 칸 배치(가로 x 세로). 3x2 격자, 6x1 한 줄, 3x1(세 프레임씩 두 번 뽑은 것) 등.
    [string]$Layout = "3x2",

    # 게임 셀 한 변(px). 플레이어 시트는 전부 256이다.
    [int]$Cell = 256,

    # 기준 프레임의 키(후드 꼭대기~발바닥, px). 걷기·대기 시트가 190이다.
    [int]$Height = 190,

    # 발바닥이 놓일 행. 플레이어 시트 피벗(아래에서 39px)과 같은 줄이다.
    [int]$GroundLine = 216,

    # 키를 재는 기준 프레임(0부터). -1이면 마지막 프레임(대기 자세로 돌아온 프레임)이다.
    [int]$StandingFrame = -1,

    # 발바닥 행 판정 문턱. 한 행의 불투명 픽셀이 원본 칸 폭의 이 비율 이상이어야 발로 본다.
    # 512 칸에서 31px, 724 칸에서 43px. 부츠 한 짝이 45px 안팎이고 칼날은 12px 안팎이다.
    [double]$MassFraction = 0.06,

    # 크로마키 판정. RemoveChromaKeyGreen.ps1과 같은 값이다.
    [double]$GreenRatio = 1.35,
    [int]$GreenFloor = 90,

    # 마지막 프레임을 바꿔 넣을 대기 시트와 그 안의 행·열. 비우면 바꾸지 않는다.
    [string]$IdleSheet = "",
    [int]$IdleRow = -1,
    [int]$IdleFrame = 0
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

# 상대 경로는 프로젝트 루트 기준으로 푼다. 다른 도구들과 같은 규칙이다.
function Resolve-ProjectPath([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $p }
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    return [System.IO.Path]::GetFullPath((Join-Path $root $p))
}

# PNG를 BGRA 바이트로 꺼낸다. 바이트만 옮기고 바로 닫는다.
# new Bitmap(image)로 복사하면 GDI+가 반투명 픽셀을 미리곱한 알파로 한 번 거쳐 값이 틀어진다.
function Read-Pixels([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $path
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $result = @{ Width = $bmp.Width; Height = $bmp.Height; Stride = $data.Stride; Bytes = $bytes }
    $bmp.UnlockBits($data)
    $bmp.Dispose()
    return $result
}

$source = Resolve-ProjectPath $SourcePath
if (-not (Test-Path $source)) { Write-Error "원본을 못 찾았다: $source"; return }

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($source),
        [System.IO.Path]::GetFileNameWithoutExtension($source) + "_6frames_1536x256.png")
}
$output = Resolve-ProjectPath $OutputPath

$layoutParts = $Layout.ToLower().Split("x")
$gridCols = [int]$layoutParts[0]
$gridRows = [int]$layoutParts[1]
$frameCount = $gridCols * $gridRows

$src = Read-Pixels $source
$W = $src.Width
$H = $src.Height
$stride = $src.Stride
$bytes = $src.Bytes

# 칸 크기는 내림으로 나눈다. [int](W / cols)는 반올림이라 칸이 그림 밖으로 1px 나갈 수 있다.
$cellW = [int][Math]::Floor($W / $gridCols)
$cellH = [int][Math]::Floor($H / $gridRows)

Write-Output ("원본      : {0}x{1}, 배치 {2} → 칸 {3}x{4}, 프레임 {5}개" -f $W, $H, $Layout, $cellW, $cellH, $frameCount)

# --- 1) 크로마키: 초록 배경은 알파 0, 나머지는 불투명으로 두고 초록 번짐을 깎는다 ---
# 생성 원본은 알파 채널이 없어 전부 255로 들어온다. 초록 판정에 걸리지 않은 픽셀이 곧 그림이다.
$opaque = New-Object bool[] ($W * $H)
for ($y = 0; $y -lt $H; $y++) {
    $rowBase = $y * $stride
    for ($x = 0; $x -lt $W; $x++) {
        $o = $rowBase + ($x * 4)
        $b = [int]$bytes[$o]; $g = [int]$bytes[$o + 1]; $r = [int]$bytes[$o + 2]
        if ($g -gt $GreenFloor -and $g -gt ($r * $GreenRatio) -and $g -gt ($b * $GreenRatio)) {
            $bytes[$o + 3] = 0
            continue
        }
        # 실루엣 가장자리의 초록기를 R/B 최대치까지만 내린다. 원래 초록기가 없던 픽셀은 그대로다.
        $maxRB = [Math]::Max($r, $b)
        if ($g -gt $maxRB) { $bytes[$o + 1] = [byte]$maxRB }
        $bytes[$o + 3] = 255
        $opaque[($y * $W) + $x] = $true
    }
}

# --- 2) 프레임마다 맨 윗줄과 발바닥 행을 잰다 ---
$massThreshold = [int][Math]::Ceiling($cellW * $MassFraction)
$tops = New-Object int[] $frameCount
$soles = New-Object int[] $frameCount
for ($k = 0; $k -lt $frameCount; $k++) {
    $x0 = ($k % $gridCols) * $cellW
    $y0 = [int][Math]::Floor($k / $gridCols) * $cellH
    $tops[$k] = -1
    $soles[$k] = -1
    for ($yy = 0; $yy -lt $cellH; $yy++) {
        $rowBase = ($y0 + $yy) * $W
        $count = 0
        for ($xx = 0; $xx -lt $cellW; $xx++) { if ($opaque[$rowBase + $x0 + $xx]) { $count++ } }
        if ($count -gt 0 -and $tops[$k] -lt 0) { $tops[$k] = $yy }
        if ($count -ge $massThreshold) { $soles[$k] = $yy }
    }
    if ($tops[$k] -lt 0 -or $soles[$k] -lt 0) {
        Write-Error "프레임 $($k + 1)에서 캐릭터를 못 찾았다. Layout이 원본과 맞는지 확인해라."
        return
    }
}

# --- 3) 모든 프레임에 같은 배율 ---
$standing = if ($StandingFrame -lt 0) { $frameCount - 1 } else { $StandingFrame }
$standingHeight = $soles[$standing] - $tops[$standing] + 1
$scale = $Height / [double]$standingHeight
Write-Output ("배율      : {0:N3} (프레임 {1}의 키 {2}px → {3}px, 발바닥 문턱 {4}px)" -f $scale, ($standing + 1), $standingHeight, $Height, $massThreshold)

# --- 4) 최근접 표본으로 셀에 옮긴다 ---
$outW = $Cell * $frameCount
$outStride = $outW * 4
$out = New-Object byte[] ($outStride * $Cell)
$half = $Cell / 2.0

for ($k = 0; $k -lt $frameCount; $k++) {
    $x0 = ($k % $gridCols) * $cellW
    $y0 = [int][Math]::Floor($k / $gridCols) * $cellH
    $centerX = $x0 + ($cellW / 2.0)
    # 원본 발바닥 행의 아래 가장자리(soles + 1)가 셀 GroundLine 행의 아래 가장자리에 오게 한다.
    $soleEdge = $y0 + $soles[$k] + 1

    for ($ty = 0; $ty -lt $Cell; $ty++) {
        $sy = [int][Math]::Floor($soleEdge + (($ty + 0.5) - ($GroundLine + 1)) / $scale)
        if ($sy -lt $y0 -or $sy -ge ($y0 + $cellH)) { continue }
        for ($tx = 0; $tx -lt $Cell; $tx++) {
            $sx = [int][Math]::Floor($centerX + (($tx + 0.5) - $half) / $scale)
            if ($sx -lt $x0 -or $sx -ge ($x0 + $cellW)) { continue }
            $so = ($sy * $stride) + ($sx * 4)
            if ($bytes[$so + 3] -eq 0) { continue }
            $to = ($ty * $outStride) + ((($k * $Cell) + $tx) * 4)
            $out[$to] = $bytes[$so]; $out[$to + 1] = $bytes[$so + 1]
            $out[$to + 2] = $bytes[$so + 2]; $out[$to + 3] = $bytes[$so + 3]
        }
    }

    # 잘림 검사. 원본 칸의 그림이 셀 밖으로 나가는 픽셀 수를 센다. 셀 밖은 옆 프레임 자리다.
    $clipped = 0
    for ($yy = 0; $yy -lt $cellH; $yy++) {
        $ty = [Math]::Floor(($GroundLine + 1) + (($y0 + $yy + 0.5) - $soleEdge) * $scale)
        $rowBase = ($y0 + $yy) * $W
        for ($xx = 0; $xx -lt $cellW; $xx++) {
            if (-not $opaque[$rowBase + $x0 + $xx]) { continue }
            $tx = [Math]::Floor($half + (($x0 + $xx + 0.5) - $centerX) * $scale)
            if ($tx -lt 0 -or $tx -ge $Cell -or $ty -lt 0 -or $ty -ge $Cell) { $clipped++ }
        }
    }

    # 결과 셀의 범위를 보고한다. 발바닥이 GroundLine 근처인지, 위아래 여유가 있는지 본다.
    $minX = $Cell; $maxX = -1; $minY = $Cell; $maxY = -1
    for ($ty = 0; $ty -lt $Cell; $ty++) {
        for ($tx = 0; $tx -lt $Cell; $tx++) {
            if ($out[($ty * $outStride) + ((($k * $Cell) + $tx) * 4) + 3] -eq 0) { continue }
            if ($tx -lt $minX) { $minX = $tx }; if ($tx -gt $maxX) { $maxX = $tx }
            if ($ty -lt $minY) { $minY = $ty }; if ($ty -gt $maxY) { $maxY = $ty }
        }
    }
    $clipText = if ($clipped -gt 0) { "잘림 $clipped px (원본 원소 기준) — 셀 밖으로 나간다" } else { "셀 안" }
    Write-Output ("  f{0}: 결과 x {1}-{2}, y {3}-{4} | {5}" -f ($k + 1), $minX, $maxX, $minY, $maxY, $clipText)
}

# --- 5) 마지막 프레임을 실제 대기 그림으로 ---
if (-not [string]::IsNullOrWhiteSpace($IdleSheet)) {
    $idlePath = Resolve-ProjectPath $IdleSheet
    if ($IdleRow -lt 0) { Write-Error "IdleSheet를 쓰려면 IdleRow(방향 행)를 지정해야 한다."; return }
    $idle = Read-Pixels $idlePath
    $last = $frameCount - 1
    for ($ty = 0; $ty -lt $Cell; $ty++) {
        $so = (($IdleRow * $Cell) + $ty) * $idle.Stride + ($IdleFrame * $Cell * 4)
        $to = ($ty * $outStride) + ($last * $Cell * 4)
        [System.Array]::Copy($idle.Bytes, $so, $out, $to, $Cell * 4)
    }
    Write-Output ("대기 교체 : f{0} ← {1} (행 {2}, 프레임 {3})" -f $frameCount, [System.IO.Path]::GetFileName($idlePath), $IdleRow, $IdleFrame)
}

# --- 6) 저장 ---
$outDir = [System.IO.Path]::GetDirectoryName($output)
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$result = New-Object System.Drawing.Bitmap $outW, $Cell, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$rect = New-Object System.Drawing.Rectangle 0, 0, $outW, $Cell
$resultData = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                               [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
[System.Runtime.InteropServices.Marshal]::Copy($out, 0, $resultData.Scan0, $out.Length)
$result.UnlockBits($resultData)
$result.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
$result.Dispose()
Write-Output "저장      : $output"
