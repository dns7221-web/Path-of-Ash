<#
    추가 생성(2026-09-29) — 생성된 3x2 보스 걷기 시트를 기존 6x1 규격으로 패킹한다.
    그림을 새로 그리거나 배경색으로 지우지 않는다. 생성된 알파를 그대로 보존하고,
    공통 배율로 줄인 뒤 머리 중심과 발바닥을 맞춘다. 프레임마다 키를 맞추면 보행 중
    웅크림까지 늘어나므로 여섯 장의 중앙값으로 배율을 한 번만 구한다.
#>
param(
    [Parameter(Mandatory = $true)] [string]$SourcePath,
    [Parameter(Mandatory = $true)] [string]$OutputPath,
    [int]$TargetHeight = 200,
    [int]$GroundLine = 216,
    # 추가 생성 — 교체 전 이동 시트의 왕관 중심 실측값. 새 그림으로 다시 측정하면 재실행 때 기준이 바뀐다.
    [double]$HeadCenterX = 158.5
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 추가 생성 — 실제 실루엣을 측정한다. 희미한 알파 가장자리는 기준점에 포함하지 않는다.
function Measure-Sprite([System.Drawing.Bitmap]$Bitmap, [System.Drawing.Rectangle]$Region) {
    $top = $Region.Height; $bottom = -1; $left = $Region.Width; $right = -1
    for ($y = 0; $y -lt $Region.Height; $y++) {
        $rowCount = 0
        for ($x = 0; $x -lt $Region.Width; $x++) {
            if ($Bitmap.GetPixel($Region.X + $x, $Region.Y + $y).A -lt 128) { continue }
            $rowCount++
            $left = [Math]::Min($left, $x); $right = [Math]::Max($right, $x)
        }
        # 추가 생성 — 왕관 끝은 한두 픽셀로 가늘어도 키에 포함해야 기존 보스보다 커지지 않는다.
        if ($rowCount -ge 1) { $top = [Math]::Min($top, $y) }
        if ($rowCount -ge 10) { $bottom = $y }
    }
    if ($bottom -le $top) { throw '빈 프레임이거나 알파 채널이 잘못됐다.' }
    $height = $bottom - $top + 1
    $headLeft = $Region.Width; $headRight = -1
    $headTop = $top + [int]($height * 0.06)
    $headBottom = $top + [int]($height * 0.14)
    for ($y = $headTop; $y -le $headBottom; $y++) {
        for ($x = 0; $x -lt $Region.Width; $x++) {
            if ($Bitmap.GetPixel($Region.X + $x, $Region.Y + $y).A -lt 128) { continue }
            $headLeft = [Math]::Min($headLeft, $x); $headRight = [Math]::Max($headRight, $x)
        }
    }
    return @{ Top = $top; Bottom = $bottom; Left = $left; Right = $right;
              Height = $height; HeadX = ($headLeft + $headRight) / 2.0 }
}

$source = [System.Drawing.Bitmap]::FromFile([System.IO.Path]::GetFullPath($SourcePath))
$sheet = New-Object System.Drawing.Bitmap 1536, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($sheet)
try {
    if ($source.Width % 3 -ne 0 -or $source.Height % 2 -ne 0) {
        throw '원본 크기는 정확한 3x2 격자로 나누어져야 한다.'
    }
    $cellW = [int]($source.Width / 3); $cellH = [int]($source.Height / 2)
    $regions = @(); $metrics = @()
    for ($i = 0; $i -lt 6; $i++) {
        $region = [System.Drawing.Rectangle]::new(($i % 3) * $cellW,
            [int][Math]::Floor($i / 3) * $cellH, $cellW, $cellH)
        $regions += $region
        $metrics += Measure-Sprite $source $region
    }
    $heights = @($metrics | ForEach-Object { $_.Height } | Sort-Object)
    $scale = $TargetHeight / (($heights[2] + $heights[3]) / 2.0)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    for ($i = 0; $i -lt 6; $i++) {
        $m = $metrics[$i]
        $offsetX = [int][Math]::Round($HeadCenterX - $m.HeadX * $scale)
        $offsetY = [int][Math]::Round($GroundLine - $m.Bottom * $scale)
        if ($offsetX + $m.Left * $scale -lt 1 -or $offsetX + $m.Right * $scale -gt 254 -or
            $offsetY + $m.Top * $scale -lt 1) {
            throw "프레임 $i 실루엣이 셀 안전영역을 벗어난다."
        }
        # 추가 생성 — 셀 단위로 그려 옆 프레임의 투명 여백이 이전 프레임을 지우지 않게 한다.
        $frame = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $fg = [System.Drawing.Graphics]::FromImage($frame)
        try {
            $fg.Clear([System.Drawing.Color]::Transparent)
            $fg.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $dest = [System.Drawing.Rectangle]::new($offsetX, $offsetY,
                [int][Math]::Round($cellW * $scale), [int][Math]::Round($cellH * $scale))
            $fg.DrawImage($source, $dest, $regions[$i], [System.Drawing.GraphicsUnit]::Pixel)
            $graphics.DrawImageUnscaled($frame, $i * 256, 0)
        }
        finally { $fg.Dispose(); $frame.Dispose() }
        Write-Output ('frame {0}: height={1}, foot={2}, headX={3}, offset={4},{5}' -f
            $i, $m.Height, $m.Bottom, $m.HeadX, $offsetX, $offsetY)
    }
    $destination = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    $sheet.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ('Saved {0}; common scale={1}; target headX={2}' -f $destination, $scale, $HeadCenterX)
}
finally { $graphics.Dispose(); $sheet.Dispose(); $source.Dispose() }
