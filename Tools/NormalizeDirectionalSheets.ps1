<#
    NormalizeDirectionalSheets.ps1
    ------------------------------------------------------------------
    8방향 플레이어 시트(6~8열 x 8행, 256px 격자)의 발 라인을 셀마다 정렬한다.

    행 순서 (그림을 직접 보고 확인한 값):
      0=S(아래) 1=SW 2=W(왼쪽) 3=NW 4=N(위) 5=NE 6=E(오른쪽) 7=SE
      즉 S에서 시작해 반시계 방향으로 돈다.

    왜 필요한가:
    셀이 48개(walk는 64개)나 되는데 생성물은 셀마다 캐릭터가 조금씩 다른 높이에 놓인다.
    이대로 넣으면 걷는 동안 캐릭터가 위아래로 떨고, 방향을 바꿀 때 툭 튄다.
    프레임 수가 적을 때는 눈감아도 되지만 8방향에서는 어긋남이 8배로 늘어난다.

    왜 발 라인이 216행인가:
    기존 .meta 피벗이 y=0.15234(프레임 아래에서 39px = 216~217행)다. 같은 줄에 맞추면
    피벗이 곧 발밑이 되고, 2방향 시트에서 쓰던 설정을 그대로 재사용할 수 있다.

    사용법:
      미리보기: powershell -File Tools\NormalizeDirectionalSheets.ps1
      적용:     powershell -File Tools\NormalizeDirectionalSheets.ps1 -Apply
#>
param(
    [switch]$Apply,
    [string]$SheetDir = "",
    [int]$GroundLine = 216,
    # 한 행이 "발"로 인정받으려면 필요한 불투명 픽셀 수. 검 끝이나 옷자락을 걸러낸다.
    [int]$MassThreshold = 12,
    # 이 값보다 불투명 픽셀이 적은 셀은 빈 셀로 보고 건너뛴다.
    [int]$EmptyThreshold = 400
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$Cell = 256

if ([string]::IsNullOrEmpty($SheetDir)) {
    $SheetDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot `
        "..\Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir"))
}
$BackupDir = Join-Path $SheetDir "Raw"

$DirectionNames = @("S", "SW", "W", "NW", "N", "NE", "E", "SE")

foreach ($file in (Get-ChildItem $SheetDir -Filter *.png | Sort-Object Name)) {
    $bmp = New-Object System.Drawing.Bitmap $file.FullName
    $w = $bmp.Width; $h = $bmp.Height
    $cols = [int]($w / $Cell); $rows = [int]($h / $Cell)

    if ($cols -lt 1 -or $rows -lt 1 -or ($w % $Cell) -ne 0 -or ($h % $Cell) -ne 0) {
        Write-Output ("{0,-24} 256px 격자가 아니라 건너뜀 ({1}x{2})" -f $file.Name, $w, $h)
        $bmp.Dispose(); continue
    }

    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $bmp.UnlockBits($data); $bmp.Dispose()

    $dst = New-Object byte[] $bytes.Length
    $moved = 0; $clamped = 0; $empty = 0
    $rowReport = @()

    for ($row = 0; $row -lt $rows; $row++) {
        $shifts = @()

        for ($col = 0; $col -lt $cols; $col++) {
            $ox = $col * $Cell
            $oy = $row * $Cell
            $foot = -1; $bboxTop = -1; $bboxBot = -1; $pixels = 0

            for ($y = 0; $y -lt $Cell; $y++) {
                $count = 0
                $base = ($oy + $y) * $stride + $ox * 4
                for ($x = 0; $x -lt $Cell; $x++) { if ($bytes[$base + $x * 4 + 3] -gt 32) { $count++ } }
                $pixels += $count
                if ($count -gt 0) {
                    if ($bboxTop -lt 0) { $bboxTop = $y }
                    $bboxBot = $y
                }
                if ($count -ge $MassThreshold) { $foot = $y }
            }

            if ($pixels -lt $EmptyThreshold -or $foot -lt 0) { $empty++; continue }

            $dy = $GroundLine - $foot
            # 머리나 무기가 셀 밖으로 잘리지 않게 제한한다. 잘림은 되돌릴 수 없다.
            $wanted = $dy
            if (($bboxTop + $dy) -lt 0) { $dy = -$bboxTop }
            if (($bboxBot + $dy) -gt ($Cell - 1)) { $dy = ($Cell - 1) - $bboxBot }
            if ($dy -ne $wanted) { $clamped++ }
            if ($dy -ne 0) { $moved++ }

            $rowBytes = $Cell * 4
            for ($y = 0; $y -lt $Cell; $y++) {
                $srcY = $y - $dy
                if ($srcY -lt 0 -or $srcY -ge $Cell) { continue }
                [System.Array]::Copy($bytes, (($oy + $srcY) * $stride + $ox * 4), $dst, (($oy + $y) * $stride + $ox * 4), $rowBytes)
            }

            $shifts += $dy
        }

        if ($shifts.Count -gt 0) {
            $min = ($shifts | Measure-Object -Minimum).Minimum
            $max = ($shifts | Measure-Object -Maximum).Maximum
            $rowReport += ("{0}:{1}~{2}" -f $DirectionNames[$row], $min, $max)
        }
    }

    $line = "{0,-24} {1}x{2}열행  이동 {3}셀 / 제한 {4} / 빈셀 {5}" -f $file.Name, $cols, $rows, $moved, $clamped, $empty
    Write-Output $line
    Write-Output ("   " + ($rowReport -join "  "))

    if ($Apply) {
        if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
        $backup = Join-Path $BackupDir $file.Name
        if (-not (Test-Path $backup)) { Copy-Item $file.FullName $backup }

        $result = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $od = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        [System.Runtime.InteropServices.Marshal]::Copy($dst, 0, $od.Scan0, $dst.Length)
        $result.UnlockBits($od)
        $result.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
        $result.Dispose()
    }
}

Write-Output ""
if ($Apply) { Write-Output "적용 완료. 원본 백업: $BackupDir" }
else { Write-Output "미리보기만 했다. -Apply 를 붙이면 원본을 덮어쓴다." }
