<#
    NormalizeBossSheets.ps1
    ------------------------------------------------------------------
    재의 왕 보스 스프라이트 시트(1536x256, 256x256 x 6프레임) 정규화 도구.

    왜 필요한가:
    보스 시트 12장을 만들 때 위쪽(머리) 기준으로 정렬해 버려서,
    웅크린 포즈(slam, transition 등)는 발이 바닥에서 최대 62px 떠 있다.
    피벗이 바닥이라 게임에서는 "애니메이션 재생하면 보스가 작아지고 뜬다"로 보인다.

    무엇을 하는가 (몸 크기는 시트마다 이미 같으므로 확대/축소는 하지 않는다):
      1. 프레임마다 발 라인을 찾아 GroundLine(기본 238)으로 세로 이동
      2. 알파가 거의 없는 빈 프레임은 가장 가까운 정상 프레임으로 채움
      3. Apply 모드에서 원본을 Raw/PreNormalize 에 백업 후 덮어쓰고, .meta 피벗을 발밑으로 교체

    발 라인을 bbox 맨 아래가 아니라 "가로로 MassThreshold 이상 채워진 최하단 행"으로 잡는 이유:
    검 끝이나 불꽃 이펙트가 발보다 아래로 삐져나오는 프레임이 있어서,
    단순 bbox 하단을 쓰면 캐릭터가 그만큼 위로 밀려 올라간다.

    사용법:
      미리보기(에셋 변경 없음): powershell -File Tools\NormalizeBossSheets.ps1
      실제 적용:                powershell -File Tools\NormalizeBossSheets.ps1 -Apply
#>
param(
    # 붙이면 실제 PNG와 .meta를 수정한다. 없으면 측정 + 미리보기 이미지만 만든다.
    [switch]$Apply,
    # 발이 놓일 기준 행(프레임 위에서부터 센 픽셀). idle/walk의 실제 발 위치가 237~238이라 238로 잡았다.
    [int]$GroundLine = 238,
    # 한 행이 "발"로 인정받으려면 필요한 불투명 픽셀 수. 검 끝/이펙트를 걸러내는 값.
    [int]$MassThreshold = 12,
    # 프레임 하나로 인정할 최소 불투명 픽셀 수. 이보다 적으면 빈 프레임으로 보고 이웃에서 복제한다.
    [int]$EmptyThreshold = 500,
    # 미리보기 PNG 저장 경로
    [string]$PreviewPath = "$env:TEMP\boss_normalize_preview.png"
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$SheetDir   = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Assets\Project\Art\Characters\Boss\AshKing"))
$BackupDir  = Join-Path $SheetDir "Raw\PreNormalize"
$Cell       = 256
$FrameCount = 6

# 6프레임으로 슬라이스되지 않은 시트는 건드리지 않는다.
# ultimate 2장은 통짜 1스프라이트에 알파도 없어서 별도 작업 대상이다.
$Skip = @('ash-king-phase2-ultimate.png', 'ash-king-phase2-ultimate-playerlike.png')

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

# 프레임 하나의 세로 정보를 잰다.
#   Top      : 캐릭터로 볼 수 있는 최상단 행 (얇은 점 노이즈 제외)
#   Foot     : 가로로 MassThreshold 이상 채워진 최하단 행 = 발
#   BboxTop/BboxBot : 알파가 있는 진짜 범위 (검 끝/이펙트 포함, 잘림 검사용)
#   Pixels   : 불투명 픽셀 총합 (빈 프레임 판정용)
function Measure-Frame {
    param([byte[]]$Bytes, [int]$Stride, [int]$FrameIndex)

    $ox = $FrameIndex * $Cell
    $top = -1; $foot = -1; $bboxTop = -1; $bboxBot = -1; $pixels = 0

    for ($y = 0; $y -lt $Cell; $y++) {
        $rowCount = 0
        $base = $y * $Stride + $ox * 4
        for ($x = 0; $x -lt $Cell; $x++) {
            if ($Bytes[$base + $x * 4 + 3] -gt 32) { $rowCount++ }
        }
        $pixels += $rowCount
        if ($rowCount -gt 0) {
            if ($bboxTop -lt 0) { $bboxTop = $y }
            $bboxBot = $y
        }
        # 4픽셀 미만인 행은 시트에 섞여 있는 점 노이즈라 캐릭터로 치지 않는다.
        if ($rowCount -ge 4 -and $top -lt 0) { $top = $y }
        if ($rowCount -ge $MassThreshold) { $foot = $y }
    }

    return @{ Top = $top; Foot = $foot; BboxTop = $bboxTop; BboxBot = $bboxBot; Pixels = $pixels }
}

# 프레임 하나를 dy만큼 세로로 옮겨 목적지 버퍼에 복사한다.
# 행 단위 Array.Copy라 픽셀 루프보다 훨씬 빠르다.
function Copy-FrameShifted {
    param([byte[]]$Src, [byte[]]$Dst, [int]$Stride, [int]$SrcFrame, [int]$DstFrame, [int]$Dy)

    $srcX = $SrcFrame * $Cell * 4
    $dstX = $DstFrame * $Cell * 4
    $rowBytes = $Cell * 4

    for ($y = 0; $y -lt $Cell; $y++) {
        $srcY = $y - $Dy
        if ($srcY -lt 0 -or $srcY -ge $Cell) { continue }
        [System.Array]::Copy($Src, ($srcY * $Stride + $srcX), $Dst, ($y * $Stride + $dstX), $rowBytes)
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

# .meta의 프레임별 pivot을 발밑 기준으로 교체한다.
# Unity 피벗 y는 아래에서부터 재므로 (256 - GroundLine) / 256 이 된다.
function Update-MetaPivot {
    param([string]$MetaPath, [double]$PivotY)

    $text = [System.IO.File]::ReadAllText($MetaPath)
    $value = $PivotY.ToString("0.########", [System.Globalization.CultureInfo]::InvariantCulture)
    $updated = [regex]::Replace($text, 'pivot: \{x: [0-9.]+, y: [0-9.]+\}', "pivot: {x: 0.5, y: $value}")
    if ($updated -ne $text) {
        [System.IO.File]::WriteAllText($MetaPath, $updated)
        return $true
    }
    return $false
}

# ------------------------------------------------------------------
# 본 처리
# ------------------------------------------------------------------
$pivotY = [double]($Cell - $GroundLine) / [double]$Cell
Write-Output "대상 폴더 : $SheetDir"
Write-Output "기준 바닥 : $GroundLine 행 / 새 피벗 y = $pivotY"
if ($Apply) { Write-Output "모드      : 적용(PNG + .meta 수정)" } else { Write-Output "모드      : 미리보기만" }
Write-Output ""

# 미리보기에 담을 시트. 문제가 가장 큰 4장만 담아야 이미지가 읽을 만한 크기로 나온다.
$PreviewSheets = @('ash-king-slam.png', 'ash-king-phase-transition.png', 'ash-king-phase2-slam.png', 'ash-king-hit-death.png')
$previewRows = @()

foreach ($file in (Get-ChildItem $SheetDir -Filter *.png | Sort-Object Name)) {
    if ($Skip -contains $file.Name) {
        Write-Output "$($file.Name) : 건너뜀 (6프레임 시트 아님)"
        continue
    }

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

    # 2단계: 빈 프레임에 쓸 대체 프레임을 고른다 (인덱스가 가장 가까운 정상 프레임)
    $sourceOf = @()
    for ($i = 0; $i -lt $FrameCount; $i++) {
        if ($frames[$i].Pixels -ge $EmptyThreshold) { $sourceOf += $i; continue }
        $best = -1; $bestDist = 99
        for ($j = 0; $j -lt $FrameCount; $j++) {
            if ($frames[$j].Pixels -lt $EmptyThreshold) { continue }
            $d = [Math]::Abs($j - $i)
            if ($d -lt $bestDist) { $bestDist = $d; $best = $j }
        }
        $sourceOf += $best
    }

    # 3단계: 이동량 계산 후 새 버퍼에 복사
    $dst = New-Object byte[] $src.Length
    $report = @()
    for ($i = 0; $i -lt $FrameCount; $i++) {
        $s = $sourceOf[$i]
        if ($s -lt 0) { $report += "f${i}:정상프레임없음"; continue }

        $m = $frames[$s]
        $dy = $GroundLine - $m.Foot

        # 아래로 너무 밀면 검 끝이 프레임 밖으로 잘리고, 위로 너무 밀면 머리가 잘린다. 잘리지 않게 제한한다.
        if (($m.BboxTop + $dy) -lt 0) { $dy = -$m.BboxTop }
        if (($m.BboxBot + $dy) -gt ($Cell - 1)) { $dy = ($Cell - 1) - $m.BboxBot }

        Copy-FrameShifted -Src $src -Dst $dst -Stride $stride -SrcFrame $s -DstFrame $i -Dy $dy

        $tag = ""
        if ($s -ne $i) { $tag = "<-f$s 복제" }
        $report += ("f{0}:발{1}->{2}(dy {3}){4}" -f $i, $m.Foot, ($m.Foot + $dy), $dy, $tag)
    }

    Write-Output "$($file.Name)"
    Write-Output ("   " + ($report -join "  "))

    $result = New-BitmapFromBytes -Bytes $dst -Stride $stride -Width $bmp.Width -Height $bmp.Height

    if ($PreviewSheets -contains $file.Name) {
        # Before 이미지를 파일 경로로 다시 열면 PNG가 잠겨서 Apply 단계의 Save가 GDI+ 오류로 실패한다.
        # 이미 읽어둔 바이트 배열로 만들어 파일을 건드리지 않는다.
        $before = New-BitmapFromBytes -Bytes $src -Stride $stride -Width $bmp.Width -Height $bmp.Height
        $previewRows += @{ Name = $file.Name; Before = $before; After = (New-Object System.Drawing.Bitmap $result) }
    }

    if ($Apply) {
        if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
        $backup = Join-Path $BackupDir $file.Name
        # 백업은 최초 1회만. 두 번 돌려도 원본이 덮이지 않게 한다.
        if (-not (Test-Path $backup)) { Copy-Item $file.FullName $backup }

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
}

# 미리보기 이미지 생성: 시트마다 [수정 전 / 수정 후] 두 줄, 초록선이 기준 바닥
if ($previewRows.Count -gt 0) {
    $scale = 128
    $labelH = 18
    $rowH = $scale + $labelH
    $canvasW = $scale * $FrameCount + 20
    $canvasH = $previewRows.Count * $rowH * 2 + 20
    $canvas = New-Object System.Drawing.Bitmap $canvasW, $canvasH
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 38, 38, 46))
    $font = New-Object System.Drawing.Font "Consolas", 10
    $white = [System.Drawing.Brushes]::White
    $green = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 90, 220, 120)), 1

    $y = 10
    foreach ($row in $previewRows) {
        foreach ($kind in @('Before', 'After')) {
            $g.DrawString("$($row.Name)  [$kind]", $font, $white, 10, $y)
            $img = $row[$kind]
            $dstRect = New-Object System.Drawing.Rectangle 10, ($y + $labelH), ($scale * $FrameCount), $scale
            $g.DrawImage($img, $dstRect, 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel)
            $lineY = $y + $labelH + [int]($GroundLine * $scale / $Cell)
            $g.DrawLine($green, 10, $lineY, (10 + $scale * $FrameCount), $lineY)
            $y += $rowH
        }
        $row.Before.Dispose()
        $row.After.Dispose()
    }
    $g.Dispose()
    $canvas.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Write-Output ""
    Write-Output "미리보기 저장: $PreviewPath"
}
