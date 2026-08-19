param(
    [Parameter(Mandatory = $true)] [string] $InputPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath,
    [ValidateRange(1, 12)] [int] $FrameCount = 4,
    [ValidateRange(1, 240)] [int] $FirstFrameTargetHeight = 141
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$cellWidth = 256
$cellHeight = 256
$targetBottom = 216
$targetCenterX = 128

$source = [System.Drawing.Bitmap]::FromFile($InputPath)
$bounds = @()

# 네 프레임의 바운딩박스를 먼저 구한다. 스케일은 1번 프레임에서 한 번만 계산해
# 나머지 프레임에도 똑같이 적용해야 웅크리는 높이 변화가 보존된다.
for ($frame = 0; $frame -lt $frameCount; $frame++) {
    $regionLeft = [int][Math]::Floor($frame * $source.Width / [double]$frameCount)
    $regionRight = [int][Math]::Floor(($frame + 1) * $source.Width / [double]$frameCount) - 1
    $minX = $regionRight
    $minY = $source.Height - 1
    $maxX = -1
    $maxY = -1

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

    if ($maxX -lt 0 -or $maxY -lt 0) { throw "프레임 $frame 에서 캐릭터를 찾지 못했다." }
    $bounds += [pscustomobject]@{ X=$minX; Y=$minY; Width=$maxX-$minX+1; Height=$maxY-$minY+1 }
}

$scale = $firstFrameTargetHeight / [double]$bounds[0].Height

# Charge처럼 몸을 길게 눕히고 잔상이 붙는 동작은 높이 기준 스케일만 적용하면
# 가로 240px 안전영역을 넘을 수 있다. 가장 넓은 프레임을 기준으로 네 프레임을
# 동일 비율로 한 번 더 줄여, 동작 사이 캐릭터 크기가 달라지는 문제 없이 전부 보존한다.
$widestAtCurrentScale = 0
foreach ($bound in $bounds) {
    $scaledWidth = [int][Math]::Round($bound.Width * $scale)
    if ($scaledWidth -gt $widestAtCurrentScale) { $widestAtCurrentScale = $scaledWidth }
}
if ($widestAtCurrentScale -gt 240) {
    $scale *= 240.0 / $widestAtCurrentScale
}

$sheet = New-Object System.Drawing.Bitmap ($cellWidth * $frameCount), $cellHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

for ($frame = 0; $frame -lt $frameCount; $frame++) {
    $bound = $bounds[$frame]
    $targetWidth = [int][Math]::Round($bound.Width * $scale)
    $targetHeight = [int][Math]::Round($bound.Height * $scale)
    if ($targetWidth -gt 240) { throw "프레임 $frame 의 목표 폭 ${targetWidth}px가 안전영역을 넘는다." }

    $cutout = New-Object System.Drawing.Bitmap $bound.Width, $bound.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($localY = 0; $localY -lt $bound.Height; $localY++) {
        for ($localX = 0; $localX -lt $bound.Width; $localX++) {
            $color = $source.GetPixel($bound.X + $localX, $bound.Y + $localY)
            $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
            $minimum = [Math]::Min($color.R, [Math]::Min($color.G, $color.B))
            if ($maximum -le 12 -and ($maximum - $minimum) -le 8) {
                $cutout.SetPixel($localX, $localY, [System.Drawing.Color]::Transparent)
            } else {
                $cutout.SetPixel($localX, $localY, $color)
            }
        }
    }

    $targetX = ($frame * $cellWidth) + $targetCenterX - [int][Math]::Floor($targetWidth / 2.0)
    $targetY = $targetBottom - $targetHeight + 1
    $targetRect = New-Object System.Drawing.Rectangle $targetX, $targetY, $targetWidth, $targetHeight
    $sourceRect = New-Object System.Drawing.Rectangle 0, 0, $bound.Width, $bound.Height
    $graphics.DrawImage($cutout, $targetRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
    $cutout.Dispose()
}

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$sheet.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$sheet.Dispose()
$source.Dispose()
