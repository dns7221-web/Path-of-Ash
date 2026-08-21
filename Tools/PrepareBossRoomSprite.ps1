<#
    PrepareBossRoomSprite.ps1
    ------------------------------------------------------------------
    보스 방 배경 PNG의 크로마키 초록 배경을 투명으로 바꾼다.

    왜 필요한가:
    ash-king-boss-room.png는 알파 채널이 아예 없고 방 바깥이 형광 초록(#00FF00 계열)으로
    채워져 있다. 전체 픽셀의 17.5%다. 그대로 쓰면 방 주위에 초록 테두리가 그려진다.

    무엇을 하는가:
      1. 초록으로 판정된 픽셀의 알파를 0으로
      2. 가장자리에 남는 초록 번짐(spill)을 깎아낸다
      3. 방 실제 영역과 안쪽 바닥 영역을 재서 출력한다 (콜라이더/스폰 배치에 쓰는 값)

    초록 판정을 단순 색 일치가 아니라 비율로 하는 이유:
    생성된 이미지라 배경이 완전한 단색이 아니고 압축 노이즈가 섞여 있다. "G가 R과 B보다
    뚜렷하게 크다"는 조건이 그런 얼룩까지 같이 걸러낸다.

    사용법:
      미리보기: powershell -File Tools\PrepareBossRoomSprite.ps1
      실제 적용: powershell -File Tools\PrepareBossRoomSprite.ps1 -Apply
#>
param(
    # 붙이면 원본 PNG를 덮어쓴다. 없으면 측정과 미리보기만 한다.
    [switch]$Apply,
    [string]$SpritePath = "",
    # 초록 판정 기준. G가 R/B보다 이 배수 이상 크면 배경으로 본다.
    [double]$GreenRatio = 1.35,
    # 바닥으로 볼 밝기 상한. 벽은 회색(80~130), 바닥은 거의 검다.
    [int]$FloorLuminance = 55,
    [string]$PreviewPath = "$env:TEMP\boss_room_preview.png"
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($SpritePath)) {
    $SpritePath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot `
        "..\Assets\Project\Art\Environment\BossRooms\ash-king-boss-room.png"))
}

$backupDir = Join-Path ([System.IO.Path]::GetDirectoryName($SpritePath)) "Raw"
$backupPath = Join-Path $backupDir ([System.IO.Path]::GetFileName($SpritePath))

$source = New-Object System.Drawing.Bitmap $SpritePath
$width = $source.Width
$height = $source.Height

# 픽셀을 한 번에 읽는다. GetPixel을 157만 번 부르면 몇 분씩 걸린다.
$rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
$data = $source.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$stride = $data.Stride
$source.UnlockBits($data)
$source.Dispose()

$keyed = 0
$minX = $width; $maxX = -1; $minY = $height; $maxY = -1

for ($y = 0; $y -lt $height; $y++) {
    $base = $y * $stride
    for ($x = 0; $x -lt $width; $x++) {
        $i = $base + $x * 4
        $b = [int]$bytes[$i]; $g = [int]$bytes[$i + 1]; $r = [int]$bytes[$i + 2]

        # 초록 배경 판정. G가 R과 B 양쪽보다 뚜렷하게 크고 충분히 밝을 때만.
        if ($g -gt 90 -and $g -gt ($r * $GreenRatio) -and $g -gt ($b * $GreenRatio)) {
            $bytes[$i + 3] = 0
            $keyed++
            continue
        }

        # 남는 픽셀 중 초록이 튀는 것은 번짐이므로 G를 R/B 최대치까지 깎는다.
        # 이걸 안 하면 방 테두리에 초록 실선이 남는다.
        $maxRB = [Math]::Max($r, $b)
        if ($g -gt $maxRB) { $bytes[$i + 1] = [byte]$maxRB }

        if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
        if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
    }
}

# 안쪽 바닥 영역을 잰다.
#
# 단순히 "어두운 픽셀"을 찾으면 안 된다. 벽 그림에도 돌 사이 어두운 이음새가 있어서
# 벽 한가운데가 바닥으로 잡히고, 결과적으로 벽 두께가 0에 가깝게 나온다.
# 대신 <b>연속으로 어두운 구간</b>을 찾는다. 벽의 이음새는 몇 픽셀이면 끝나지만
# 바닥은 수백 픽셀이 이어지므로, MinRun 이상 이어지는 첫 구간이 진짜 바닥이다.
function Get-FloorRange {
    param([int]$Fixed, [bool]$Horizontal, [int]$MinRun = 60)

    [int]$limit = if ($Horizontal) { $width } else { $height }
    [int]$runStart = -1
    [int]$bestStart = -1
    [int]$bestEnd = -1

    # 구간 목록을 배열로 모으지 않고 최장 구간만 그때그때 갱신한다.
    # PowerShell에서 배열의 배열을 다루면 요소가 통째로 풀려서 숫자 연산이 깨진다.
    for ([int]$k = 0; $k -lt $limit; $k++) {
        [int]$x = if ($Horizontal) { $k } else { $Fixed }
        [int]$y = if ($Horizontal) { $Fixed } else { $k }
        [int]$i = $y * $stride + $x * 4

        $isFloor = $false
        if ($bytes[$i + 3] -ge 32) {
            $luminance = (0.2126 * $bytes[$i + 2]) + (0.7152 * $bytes[$i + 1]) + (0.0722 * $bytes[$i])
            $isFloor = $luminance -le $FloorLuminance
        }

        if ($isFloor) {
            if ($runStart -lt 0) { $runStart = $k }
        }
        elseif ($runStart -ge 0) {
            if (($k - $runStart) -ge $MinRun -and ($k - 1 - $runStart) -gt ($bestEnd - $bestStart)) {
                $bestStart = $runStart
                $bestEnd = $k - 1
            }
            $runStart = -1
        }
    }

    if ($runStart -ge 0 -and ($limit - $runStart) -ge $MinRun -and
        ($limit - 1 - $runStart) -gt ($bestEnd - $bestStart)) {
        $bestStart = $runStart
        $bestEnd = $limit - 1
    }

    return @($bestStart, $bestEnd)
}

$centerY = [int](($minY + $maxY) / 2)
$centerX = [int](($minX + $maxX) / 2)
$floorX = Get-FloorRange -Fixed $centerY -Horizontal $true
$floorY = Get-FloorRange -Fixed $centerX -Horizontal $false

Write-Output "파일        : $SpritePath"
Write-Output "크기        : ${width}x${height}"
Write-Output "초록 제거   : $keyed 픽셀 ($([math]::Round($keyed * 100.0 / ($width * $height), 1))%)"
Write-Output "방 전체     : x $minX~$maxX, y $minY~$maxY  ($(($maxX - $minX + 1))x$(($maxY - $minY + 1)))"
Write-Output "안쪽 바닥   : x $($floorX[0])~$($floorX[1]), y $($floorY[0])~$($floorY[1])  ($(($floorX[1] - $floorX[0] + 1))x$(($floorY[1] - $floorY[0] + 1)))"

# 결과 비트맵 생성
$result = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$outData = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $outData.Scan0, $bytes.Length)
$result.UnlockBits($outData)

# 미리보기는 투명이 눈에 보이도록 자홍색 위에 겹쳐 그린다.
$preview = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($preview)
$graphics.Clear([System.Drawing.Color]::FromArgb(255, 255, 0, 255))
$graphics.DrawImage($result, 0, 0, $width, $height)
$graphics.Dispose()
$preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Output "미리보기    : $PreviewPath (자홍색 = 투명)"

if ($Apply) {
    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
    # 백업은 최초 1회만. 두 번 돌려도 원본이 덮이지 않는다.
    if (-not (Test-Path $backupPath)) {
        Copy-Item $SpritePath $backupPath
        Write-Output "원본 백업   : $backupPath"
    }

    $result.Save($SpritePath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "적용 완료   : 초록 배경을 투명으로 바꿨다."
}
else {
    Write-Output "적용 안 함  : -Apply 를 붙이면 원본을 덮어쓴다."
}

$result.Dispose()
