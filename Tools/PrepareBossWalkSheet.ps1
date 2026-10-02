<#
    추가 생성(2026-09-29) — 생성된 3x2 보스 걷기 시트를 기존 6x1 규격으로 패킹한다.
    그림을 새로 그리거나 배경색으로 지우지 않는다. 생성된 알파를 그대로 보존하고,
    공통 배율로 줄인 뒤 머리 중심과 발바닥을 맞춘다. 프레임마다 키를 맞추면 보행 중
    웅크림까지 늘어나므로 여섯 장의 중앙값으로 배율을 한 번만 구한다.
    추가 생성(2026-10-02) — ReferenceFrame이 있으면 지정 프레임의 키와 머리 중심만 사용해
    일어서기 동작에서도 배율과 x 이동을 일정하게 유지한다. 기준 번호는 1부터 시작한다.
#>
param(
    [Parameter(Mandatory = $true)] [string]$SourcePath,
    [Parameter(Mandatory = $true)] [string]$OutputPath,
    [int]$TargetHeight = 200,
    [int]$GroundLine = 216,
    # 추가 생성 — 머리 중심을 둘 x 좌표. 원래 값은 옛 시트의 왕관 위치(158.5)였는데,
    # 새 그림은 상체가 앞으로 숙여져 있어서 발 중심이 피벗(x=128)보다 24px(=1유닛, PPU 24)
    # 오른쪽에 찍혔다. 그러면 idle↔walk 전환 때 1유닛, flipX로 돌아설 때 2유닛씩 몸이 튄다.
    # 여섯 프레임의 발 중심 중앙값이 128이 되도록 24px 당긴 134.5를 쓴다.
    [double]$HeadCenterX = 134.5,
    # 추가 생성(2026-10-02) — 일어서기처럼 키가 크게 변하는 동작은 지정한 서 있는 프레임(1~6)으로
    # 배율과 x 이동을 한 번만 계산한다. 0은 기존 걷기 시트의 중앙값/개별 머리 정렬을 유지한다.
    [ValidateRange(0, 6)] [int]$ReferenceFrame = 0,
    # 추가 생성(2026-10-02) — 생성 그림은 idle을 픽셀 단위로 재현할 수 없으므로 지정 프레임을
    # 기존 256px idle 첫 셀로 복사하여 연출 종료 시 자세와 실루엣이 바뀌는 현상을 막는다.
    [string]$EndpointReferencePath,
    [ValidateRange(1, 6)] [int[]]$EndpointFrames = @(6)
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
    # 추가 생성(2026-10-02) — 무릎 꿇은 프레임의 작은 키가 전체 배율을 키우지 않도록
    # 서 있는 기준 프레임 하나를 사용하고, 고개 움직임이 좌우 이동으로 보이지 않게 x도 고정한다.
    $scaleHeight = if ($ReferenceFrame -gt 0) { $metrics[$ReferenceFrame - 1].Height }
                   else { ($heights[2] + $heights[3]) / 2.0 }
    $scale = $TargetHeight / $scaleHeight
    $commonOffsetX = if ($ReferenceFrame -gt 0) {
        [int][Math]::Round($HeadCenterX - $metrics[$ReferenceFrame - 1].HeadX * $scale)
    } else { 0 }
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    for ($i = 0; $i -lt 6; $i++) {
        $m = $metrics[$i]
        $offsetX = if ($ReferenceFrame -gt 0) { $commonOffsetX }
                   else { [int][Math]::Round($HeadCenterX - $m.HeadX * $scale) }
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
    # 추가 생성(2026-10-02) — 리샘플링 없이 픽셀을 그대로 복사해야 마지막 프레임이 기존 idle과
    # 완전히 같아진다. 기존 시트는 수정하지 않고 새 시트의 전환 지점에만 복사한다.
    if ($EndpointReferencePath) {
        $endpoint = [System.Drawing.Bitmap]::FromFile([System.IO.Path]::GetFullPath($EndpointReferencePath))
        try {
            if ($endpoint.Width -lt 256 -or $endpoint.Height -ne 256) {
                throw '끝 자세 기준 시트는 256x256 셀을 사용하는 가로 시트여야 한다.'
            }
            foreach ($endpointFrame in $EndpointFrames) {
                for ($y = 0; $y -lt 256; $y++) {
                    for ($x = 0; $x -lt 256; $x++) {
                        $sheet.SetPixel(($endpointFrame - 1) * 256 + $x, $y, $endpoint.GetPixel($x, $y))
                    }
                }
                Write-Output ('frame {0}: exact endpoint copied from {1}' -f ($endpointFrame - 1), $EndpointReferencePath)
            }
        }
        finally { $endpoint.Dispose() }
    }
    $destination = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    $sheet.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ('Saved {0}; common scale={1}; target headX={2}' -f $destination, $scale, $HeadCenterX)
}
finally { $graphics.Dispose(); $sheet.Dispose(); $source.Dispose() }
