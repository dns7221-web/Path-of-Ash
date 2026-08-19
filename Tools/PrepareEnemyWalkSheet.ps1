param(
    [Parameter(Mandatory = $true)] [string] $InputPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$frameCount = 6
$cellWidth = 256
$cellHeight = 256
$targetTop = 76
$targetBottom = 216
$targetHeight = $targetBottom - $targetTop + 1
$targetCenterX = 128

$source = [System.Drawing.Bitmap]::FromFile($InputPath)
$sheet = New-Object System.Drawing.Bitmap ($cellWidth * $frameCount), $cellHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sheetGraphics = [System.Drawing.Graphics]::FromImage($sheet)
$sheetGraphics.Clear([System.Drawing.Color]::Transparent)
$sheetGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$sheetGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$sheetGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sheetGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

for ($frame = 0; $frame -lt $frameCount; $frame++) {
    $regionLeft = [int][Math]::Floor($frame * $source.Width / [double]$frameCount)
    $regionRight = [int][Math]::Floor(($frame + 1) * $source.Width / [double]$frameCount) - 1
    $minX = $regionRight
    $minY = $source.Height - 1
    $maxX = -1
    $maxY = -1

    # 검은 생성 배경과 캐릭터를 분리하면서 이 프레임의 실제 바운딩박스를 구한다.
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = $regionLeft; $x -le $regionRight; $x++) {
            $color = $source.GetPixel($x, $y)
            $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
            $minimum = [Math]::Min($color.R, [Math]::Min($color.G, $color.B))
            if ($maximum -le 12 -and ($maximum - $minimum) -le 8) { continue }

            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }

    if ($maxX -lt 0 -or $maxY -lt 0) {
        throw "프레임 $frame 에서 캐릭터를 찾지 못했다."
    }

    $sourceWidth = $maxX - $minX + 1
    $sourceHeight = $maxY - $minY + 1
    $targetWidth = [int][Math]::Round($sourceWidth * ($targetHeight / [double]$sourceHeight))
    if ($targetWidth -gt 240) {
        throw "프레임 $frame 의 목표 폭이 ${targetWidth}px라 셀 안전영역 240px을 넘는다."
    }

    # 검은 배경을 실제 투명 픽셀로 바꾼 임시 프레임을 만든다.
    $cutout = New-Object System.Drawing.Bitmap $sourceWidth, $sourceHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($localY = 0; $localY -lt $sourceHeight; $localY++) {
        for ($localX = 0; $localX -lt $sourceWidth; $localX++) {
            $color = $source.GetPixel($minX + $localX, $minY + $localY)
            $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
            $minimum = [Math]::Min($color.R, [Math]::Min($color.G, $color.B))
            if ($maximum -le 12 -and ($maximum - $minimum) -le 8) {
                $cutout.SetPixel($localX, $localY, [System.Drawing.Color]::Transparent)
            }
            else {
                $cutout.SetPixel($localX, $localY, $color)
            }
        }
    }

    $targetX = ($frame * $cellWidth) + $targetCenterX - [int][Math]::Floor($targetWidth / 2.0)
    $targetRect = New-Object System.Drawing.Rectangle $targetX, $targetTop, $targetWidth, $targetHeight
    $sourceRect = New-Object System.Drawing.Rectangle 0, 0, $sourceWidth, $sourceHeight
    $sheetGraphics.DrawImage($cutout, $targetRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
    $cutout.Dispose()
}

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$sheet.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sheetGraphics.Dispose()
$sheet.Dispose()
$source.Dispose()
