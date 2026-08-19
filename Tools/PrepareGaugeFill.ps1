param(
    [Parameter(Mandatory = $true)] [string] $InputPath,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Bitmap]::FromFile($InputPath)
$transparent = New-Object System.Drawing.Bitmap $source.Width, $source.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$minX = $source.Width
$minY = $source.Height
$maxX = -1
$maxY = -1

for ($y = 0; $y -lt $source.Height; $y++) {
    for ($x = 0; $x -lt $source.Width; $x++) {
        $color = $source.GetPixel($x, $y)
        $maximum = [Math]::Max($color.R, [Math]::Max($color.G, $color.B))
        $minimum = [Math]::Min($color.R, [Math]::Min($color.G, $color.B))
        $luminance = (0.2126 * $color.R) + (0.7152 * $color.G) + (0.0722 * $color.B)

        # 생성 모델의 검은 배경만 제거한다. 붉은색은 어두워도 채도가 높으므로 보존된다.
        if ($luminance -le 12 -and ($maximum - $minimum) -le 16) {
            $transparent.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
            continue
        }

        $transparent.SetPixel($x, $y, $color)
        if ($x -lt $minX) { $minX = $x }
        if ($x -gt $maxX) { $maxX = $x }
        if ($y -lt $minY) { $minY = $y }
        if ($y -gt $maxY) { $maxY = $y }
    }
}

if ($maxX -lt 0 -or $maxY -lt 0) {
    throw '배경을 제거한 뒤 남은 Fill 픽셀이 없다.'
}

$padding = 4
$cropX = [Math]::Max(0, $minX - $padding)
$cropY = [Math]::Max(0, $minY - $padding)
$cropRight = [Math]::Min($source.Width - 1, $maxX + $padding)
$cropBottom = [Math]::Min($source.Height - 1, $maxY + $padding)
$cropWidth = $cropRight - $cropX + 1
$cropHeight = $cropBottom - $cropY + 1

$targetWidth = 1024
$targetHeight = [Math]::Max(1, [int][Math]::Round($cropHeight * ($targetWidth / [double]$cropWidth)))
$result = New-Object System.Drawing.Bitmap $targetWidth, $targetHeight, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($result)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$sourceRect = New-Object System.Drawing.Rectangle $cropX, $cropY, $cropWidth, $cropHeight
$targetRect = New-Object System.Drawing.Rectangle 0, 0, $targetWidth, $targetHeight
$graphics.DrawImage($transparent, $targetRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)

$directory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$result.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$result.Dispose()
$transparent.Dispose()
$source.Dispose()
