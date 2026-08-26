<#
    NormalizeVfxSheets.ps1
    ------------------------------------------------------------------
    VFX 스프라이트 시트(1536x256, 256x256 x 6프레임) 바닥선 정규화 도구.

    왜 필요한가:
    Q 2단(vfx_sword_slam_forward_burst) 시트가 바닥이 아니라 위쪽 기준으로 정렬돼 있다.
    6프레임 전부 내용의 위쪽 끝이 216px(아래에서)로 고정이고, 바닥 슬래브만
    193 -> 113 -> 192로 움직인다. 스케일 2.4를 곱하면 6유닛, 플레이어 키만 한 거리다.
    게임에서는 "땅에서 파편이 위로 솟는" 게 아니라 "고정된 꼭대기에서 바닥이
    아래로 빠지는" 것으로 보인다. 1단(impact) 시트도 46px 흔들린다.

    NormalizeBossSheets.ps1이 보스 시트에 한 것과 같은 종류의 수정이다.
    보스와 플레이어 시트는 그때 정리했지만 VFX 시트는 한 번도 거치지 않았다.

    무엇을 하는가 (확대/축소 없이 세로 이동만 한다):
      1. 프레임마다 바닥선을 찾는다
      2. 어떤 프레임도 셀 밖으로 잘리지 않는 기준 바닥선을 구한다
      3. 각 프레임을 그 줄에 맞춰 세로로 옮긴다
      4. Apply 모드에서만 Raw\PreNormalize 에 백업 후 덮어쓰고 .meta 피벗을 바닥선으로 교체

    바닥선을 "최하단 행"이 아니라 "가로로 가장 넓은 행"으로 잡는 이유:
    보스는 발이 늘 최하단이라 최하단 질량 행이 곧 발이었다. VFX는 다르다.
    불티와 파편이 바닥 슬래브보다 아래로 흩어지는 프레임이 있어서 최하단을 쓰면
    그 불티에 맞춰 그림 전체가 위로 밀린다. 반면 슬래브/크레이터는 그림에서 가장
    넓은 요소이고 프레임마다 폭이 187~200으로 거의 같아 신호가 깨끗하다.

    기준 바닥선을 인자로 안 받고 계산하는 이유:
    위쪽 프레임에 맞추면 파편이 가장 높은 프레임이 셀 위로 잘리고, 아래로 너무
    내리면 슬래브 아래가 잘린다. 잘리지 않는 구간은 시트마다 다르므로 사람이
    숫자를 고르면 반드시 한 번은 잘린 채로 저장하게 된다.

    사용법:
      미리보기(에셋 변경 없음): powershell -File Tools\NormalizeVfxSheets.ps1
      실제 적용:                powershell -File Tools\NormalizeVfxSheets.ps1 -Apply
      전체 시트 측정만:         powershell -File Tools\NormalizeVfxSheets.ps1 -All
#>
param(
    # 붙이면 실제 PNG와 .meta를 수정한다. 없으면 측정 + 미리보기 이미지만 만든다.
    [switch]$Apply,

    # 붙이면 VFX 폴더의 6프레임 시트를 전부 측정한다. Apply 대상은 늘 $Sheets 뿐이다.
    # E(장판)처럼 프레임마다 모양이 통째로 바뀌는 시트까지 건드리면 안 되므로 측정과 적용을 나눴다.
    [switch]$All,

    # 실제로 고칠 시트. 기본값은 Q 1단/2단 둘뿐이다.
    [string[]]$Sheets = @(
        'vfx_sword_slam_impact_6frames_1536x256.png',
        'vfx_sword_slam_forward_burst_6frames_1536x256.png'
    ),

    # 기준 바닥선을 잘리지 않는 하한에서 이만큼 아래로 띄운다.
    # 하한에 딱 붙이면 가장 높은 프레임이 셀 맨 위 줄에 닿아서, 나중에 그림을 조금만
    # 손봐도 바로 잘린다. 8px 정도 여유를 둔다.
    [int]$Margin = 8,

    # 한 행이 "내용"으로 인정받는 최소 알파. 반투명 잔불을 걸러낸다.
    [int]$AlphaThreshold = 8,

    # 미리보기 PNG 저장 경로
    [string]$PreviewPath = "$env:TEMP\vfx_normalize_preview.png"
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$SheetDir   = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Assets\Project\Art\Sprites\VFX"))
$BackupDir  = Join-Path $SheetDir "Raw\PreNormalize"
$Cell       = 256
$FrameCount = 6

# 비트맵 전체를 32bpp ARGB 바이트 배열로 읽는다.
# GetPixel을 프레임마다 6만 번 부르면 너무 느려서 LockBits로 한 번에 가져온다.
function Read-SheetBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Bitmap.Width, $Bitmap.Height
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $Bitmap.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $Bitmap.UnlockBits($data)

    return @{ Bytes = $bytes; Stride = $stride }
}

# 프레임 하나의 세로 정보를 잰다. 좌표는 전부 프레임 위에서부터 센 행 번호다.
#   Ground   : 가로로 가장 넓게 채워진 행 = 바닥 슬래브/크레이터
#   BboxTop/BboxBot : 알파가 있는 진짜 범위 (잘림 검사에 쓴다)
#   Pixels   : 불투명 픽셀 총합 (빈 프레임 판정용)
function Measure-Frame {
    param([byte[]]$Bytes, [int]$Stride, [int]$FrameIndex)

    $ox = $FrameIndex * $Cell
    $ground = -1; $widest = 0; $bboxTop = -1; $bboxBot = -1; $pixels = 0

    for ($y = 0; $y -lt $Cell; $y++) {
        $rowCount = 0
        $base = $y * $Stride + $ox * 4
        for ($x = 0; $x -lt $Cell; $x++) {
            if ($Bytes[$base + $x * 4 + 3] -gt $AlphaThreshold) { $rowCount++ }
        }
        $pixels += $rowCount
        if ($rowCount -gt 0) {
            if ($bboxTop -lt 0) { $bboxTop = $y }
            $bboxBot = $y
        }
        # 같은 폭이면 아래쪽 행을 택한다. 슬래브는 두께가 있어서 여러 행이 같은 폭으로
        # 나오는데, 접지점은 그 덩어리의 아래쪽이라고 보는 편이 프레임 간에 흔들리지 않는다.
        if ($rowCount -ge $widest -and $rowCount -gt 0) { $widest = $rowCount; $ground = $y }
    }

    return @{ Ground = $ground; Widest = $widest; BboxTop = $bboxTop; BboxBot = $bboxBot; Pixels = $pixels }
}

# 어떤 프레임도 잘리지 않는 기준 바닥선을 고른다.
#
# 프레임 i를 dy만큼 옮기면 위쪽은 BboxTop+dy >= 0, 아래쪽은 BboxBot+dy <= Cell-1 이어야 한다.
# dy = G - Ground 이므로 두 조건은 G에 대한 하한/상한이 된다.
#   G >= Ground - BboxTop            (위가 안 잘릴 조건)
#   G <= Ground + (Cell-1) - BboxBot (아래가 안 잘릴 조건)
# 모든 프레임의 조건을 겹치면 쓸 수 있는 구간이 나온다.
function Get-GroundLine {
    param($Frames)

    $lo = 0
    $hi = $Cell - 1
    foreach ($f in $Frames) {
        if ($f.Ground -lt 0) { continue }
        $needTop = $f.Ground - $f.BboxTop
        $needBot = $f.Ground + ($Cell - 1) - $f.BboxBot
        if ($needTop -gt $lo) { $lo = $needTop }
        if ($needBot -lt $hi) { $hi = $needBot }
    }

    if ($lo -gt $hi) { return @{ Line = -1; Low = $lo; High = $hi } }

    $line = $lo + $Margin
    if ($line -gt $hi) { $line = $hi }
    return @{ Line = $line; Low = $lo; High = $hi }
}

# 프레임 하나를 dy만큼 세로로 옮겨 목적지 버퍼에 복사한다.
# 행 단위 Array.Copy라 픽셀 루프보다 훨씬 빠르다.
function Copy-FrameShifted {
    param([byte[]]$Src, [byte[]]$Dst, [int]$Stride, [int]$Frame, [int]$Dy)

    $x = $Frame * $Cell * 4
    $rowBytes = $Cell * 4

    for ($y = 0; $y -lt $Cell; $y++) {
        $srcY = $y - $Dy
        if ($srcY -lt 0 -or $srcY -ge $Cell) { continue }
        [System.Array]::Copy($Src, ($srcY * $Stride + $x), $Dst, ($y * $Stride + $x), $rowBytes)
    }
}

# 바이트 배열을 다시 Bitmap으로 만든다.
function New-BitmapFromBytes {
    param([byte[]]$Bytes, [int]$Stride, [int]$Width, [int]$Height)

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($Bytes, 0, $data.Scan0, $Bytes.Length)
    $bmp.UnlockBits($data)
    return $bmp
}

# .meta의 프레임별 pivot을 바닥선 기준으로 교체한다.
# Unity 피벗 y는 아래에서부터 재므로 (Cell - GroundLine) / Cell 이 된다.
#
# 줄 앞 공백까지 묶어서 찾는 이유: 그냥 'pivot:'으로 찾으면 파일 위쪽의
# spritePivot 줄에도 걸린다. 그쪽은 Multiple 모드에서 쓰이지 않는 기본값이라
# 건드릴 이유가 없다.
function Update-MetaPivot {
    param([string]$MetaPath, [double]$PivotY)

    $text = [System.IO.File]::ReadAllText($MetaPath)
    $value = $PivotY.ToString("0.########", [System.Globalization.CultureInfo]::InvariantCulture)
    $pattern = '(?m)^(\s+)pivot: \{x: [0-9.]+, y: [0-9.]+\}'
    $replacement = '${1}pivot: {x: 0.5, y: ' + $value + '}'
    $updated = [regex]::Replace($text, $pattern, $replacement)
    if ($updated -ne $text) {
        [System.IO.File]::WriteAllText($MetaPath, $updated)
        return $true
    }
    return $false
}

# ------------------------------------------------------------------
# 본 처리
# ------------------------------------------------------------------
Write-Output "대상 폴더 : $SheetDir"
if ($Apply) { Write-Output "모드      : 적용(PNG + .meta 수정)" } else { Write-Output "모드      : 미리보기만" }
Write-Output "적용 대상 : $($Sheets -join ', ')"
Write-Output ""

$previewRows = @()

foreach ($file in (Get-ChildItem $SheetDir -Filter *.png | Sort-Object Name)) {
    $isTarget = $Sheets -contains $file.Name
    if (-not $isTarget -and -not $All) { continue }

    $bmp = New-Object System.Drawing.Bitmap $file.FullName
    if ($bmp.Width -ne ($Cell * $FrameCount) -or $bmp.Height -ne $Cell) {
        Write-Output "$($file.Name) : 건너뜀 (크기 $($bmp.Width)x$($bmp.Height))"
        $bmp.Dispose()
        continue
    }

    $sheet = Read-SheetBytes -Bitmap $bmp
    $src = $sheet.Bytes
    $stride = $sheet.Stride

    # 1단계: 프레임 전부 측정
    $frames = @()
    for ($i = 0; $i -lt $FrameCount; $i++) {
        $frames += (Measure-Frame -Bytes $src -Stride $stride -FrameIndex $i)
    }

    # 2단계: 잘리지 않는 기준 바닥선을 구한다
    $pick = Get-GroundLine -Frames $frames
    $groundLine = $pick.Line

    $marker = ""
    if (-not $isTarget) { $marker = "  [측정만]" }
    Write-Output "$($file.Name)$marker"

    if ($groundLine -lt 0) {
        Write-Output "   건너뜀 - 잘리지 않는 기준선이 없다 (하한 $($pick.Low) > 상한 $($pick.High))"
        $bmp.Dispose()
        continue
    }

    # 3단계: 이동량 계산 후 새 버퍼에 복사
    $dst = New-Object byte[] $src.Length
    $report = @()
    $maxDrift = 0
    for ($i = 0; $i -lt $FrameCount; $i++) {
        $m = $frames[$i]
        if ($m.Ground -lt 0) { $report += "f${i}:빈프레임"; continue }

        $dy = $groundLine - $m.Ground
        if ([Math]::Abs($dy) -gt $maxDrift) { $maxDrift = [Math]::Abs($dy) }

        Copy-FrameShifted -Src $src -Dst $dst -Stride $stride -Frame $i -Dy $dy
        $report += ("f{0}:{1}->{2}(dy {3})" -f $i, $m.Ground, $groundLine, $dy)
    }

    $pivotY = [double]($Cell - $groundLine) / [double]$Cell
    Write-Output ("   기준 바닥선 $groundLine (쓸 수 있는 구간 $($pick.Low)~$($pick.High)) / 새 피벗 y = " + $pivotY.ToString("0.######"))
    Write-Output ("   " + ($report -join "  "))
    Write-Output ("   최대 보정량 $maxDrift px")

    $result = New-BitmapFromBytes -Bytes $dst -Stride $stride -Width $bmp.Width -Height $bmp.Height

    if ($isTarget) {
        # Before 이미지를 파일 경로로 다시 열면 PNG가 잠겨서 Apply 단계의 Save가 GDI+ 오류로 실패한다.
        # 이미 읽어둔 바이트 배열로 만들어 파일을 건드리지 않는다.
        $before = New-BitmapFromBytes -Bytes $src -Stride $stride -Width $bmp.Width -Height $bmp.Height
        $previewRows += @{ Name = $file.Name; Before = $before; After = (New-Object System.Drawing.Bitmap $result); Ground = $groundLine }
    }

    if ($Apply -and $isTarget) {
        if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
        $backup = Join-Path $BackupDir $file.Name
        # 백업은 최초 1회만. 두 번 돌려도 원본이 덮이지 않게 한다.
        if (-not (Test-Path $backup)) {
            Copy-Item $file.FullName $backup
            Write-Output "   원본 백업 -> Raw\PreNormalize\$($file.Name)"
        }
        else {
            Write-Output "   백업 이미 있음 (원본 유지)"
        }

        $bmp.Dispose()
        $result.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
        $result.Dispose()

        $meta = "$($file.FullName).meta"
        if (Test-Path $meta) {
            if (Update-MetaPivot -MetaPath $meta -PivotY $pivotY) { Write-Output "   .meta 피벗 갱신" }
        }
    }
    else {
        $bmp.Dispose()
        $result.Dispose()
    }

    Write-Output ""
}

# ------------------------------------------------------------------
# 미리보기 이미지: 시트마다 [수정 전 / 수정 후] 두 줄, 빨간 선이 기준 바닥
# ------------------------------------------------------------------
if ($previewRows.Count -gt 0) {
    $rowH = $Cell
    $width = $Cell * $FrameCount
    $height = $rowH * 2 * $previewRows.Count

    $canvas = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::White)

    $red = New-Object System.Drawing.Pen ([System.Drawing.Color]::Red), 2
    $font = New-Object System.Drawing.Font "Consolas", 12
    $black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)

    $y = 0
    foreach ($row in $previewRows) {
        $g.DrawImage($row.Before, 0, $y, $width, $rowH)
        $g.DrawLine($red, 0, ($y + $row.Ground), $width, ($y + $row.Ground))
        $g.DrawString("$($row.Name)  BEFORE", $font, $black, 6, ($y + 6))
        $y += $rowH

        $g.DrawImage($row.After, 0, $y, $width, $rowH)
        $g.DrawLine($red, 0, ($y + $row.Ground), $width, ($y + $row.Ground))
        $g.DrawString("$($row.Name)  AFTER", $font, $black, 6, ($y + 6))
        $y += $rowH

        $row.Before.Dispose()
        $row.After.Dispose()
    }

    $g.Dispose()
    $canvas.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Write-Output "미리보기 저장 : $PreviewPath"
    Write-Output "빨간 선이 기준 바닥선이다. AFTER 줄에서 6프레임의 바닥이 모두 그 선에 붙어야 한다."
}

if (-not $Apply) {
    Write-Output ""
    Write-Output "실제로 쓰려면 -Apply 를 붙여서 다시 실행해라."
}
