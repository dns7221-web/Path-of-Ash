<#
    MirrorDamagedPlayerDirections.ps1
    ------------------------------------------------------------------
    손상된 8방향 시트의 한 행을, 온전한 반대 방향 행을 좌우 반전해서 채우는 도구.

    왜 필요한가:
    플레이어 8방향 시트 10장 중 7장에서 특정 방향 행의 <b>어두운 픽셀만 통째로 빠져 있다.</b>
    검은 망토와 어두운 갑옷이 사라지고 회색 후드·은발·갈색 가죽·주황 검만 남아서,
    게임에서는 몸에 구멍이 뚫린 채로 걸어다닌다. 정상 행은 어두운 픽셀이 23~40%인데
    손상 행은 6~10%다.

    자르는 도구 문제가 아니다. 격자는 정확하고 스프라이트 사각형이 셀 경계를 넘지도 않는다.
    Raw/ 백업본도 같은 수치라 이 저장소의 어떤 도구도 범인이 아니고,
    <b>생성된 원본 그림이 이미 그 상태였다.</b> 즉 지워진 픽셀은 정보가 없어서 복원할 수 없다.

    그래서 반전으로 메운다:
    8방향은 W<->E, NW<->NE, SW<->SE가 좌우 대칭 짝이다. 짝이 멀쩡하면 그걸 뒤집어 쓴다.
    <b>N과 S는 짝이 없다.</b> 정면/후면이라 뒤집어도 자기 자신이므로 이 도구로 못 고친다.
    그 행은 목록에만 남기고 건드리지 않는다 — 그림을 다시 뽑아야 한다.

    반전의 대가:
    캐릭터가 한 손에 검을 들고 등에 지팡이를 멨다. 뒤집으면 <b>그 좌우가 바뀐다.</b>
    몸이 갈가리 찢긴 것보다는 낫다는 판단이고, 공짜가 아니라는 걸 알고 쓰는 것이다.

    무엇을 하는가:
      1. 시트마다 행별로 "불투명 픽셀 중 아주 어두운 것의 비율"을 잰다
      2. 같은 시트 중앙값의 DamageRatio 미만이면 손상으로 본다
      3. 대칭 짝이 PartnerMinRatio 이상으로 멀쩡할 때만 반전 복사한다
      4. Apply 모드에서만 Raw\PreMirrorFix 에 백업 후 덮어쓴다

    짝에도 기준을 두는 이유:
    짝이 애매하게 상한 경우(ultimate SW 15.6%) 그걸 뒤집어 넣으면 손상을 반대편으로
    옮겨 심는 꼴이 된다. 그런 건 고쳤다고 착각하는 게 안 고친 것보다 나쁘다.

    사용법:
      미리보기(에셋 변경 없음): powershell -File Tools\MirrorDamagedPlayerDirections.ps1
      실제 적용:                powershell -File Tools\MirrorDamagedPlayerDirections.ps1 -Apply
#>
param(
    # 붙이면 실제 PNG를 수정한다. 없으면 측정 + 미리보기 이미지만 만든다.
    [switch]$Apply,

    # 이 밝기 미만을 "아주 어두운 픽셀"로 본다. 검은 망토/갑옷이 여기 들어간다.
    [int]$DarkThreshold = 40,

    # 행의 어두운 비율이 시트 중앙값의 이 배수 미만이면 손상으로 판정한다.
    [double]$DamageRatio = 0.5,

    # 대칭 짝이 시트 중앙값의 이 배수 이상일 때만 쓴다. 애매하게 상한 짝은 거절한다.
    [double]$PartnerMinRatio = 0.7,

    # 통계용 표본 간격. 6% 대 30%는 차이가 커서 4칸씩 건너뛰어도 판정이 흔들리지 않는다.
    # 2048x2048을 전부 세면 PowerShell 반복이 너무 느리다.
    [int]$SampleStride = 4,

    # 미리보기 PNG 저장 경로
    [string]$PreviewPath = "$env:TEMP\player_mirror_preview.png"
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$SheetDir  = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir"))
$BackupDir = Join-Path $SheetDir "Raw\PreMirrorFix"
$Cell = 256

# 이미지 위에서부터의 방향 순서. AshPlayerDirectionalAnimationBuilder의 DirectionNames와 같아야 한다.
$Dirs = @('S','SW','W','NW','N','NE','E','SE')

# 좌우 대칭 짝. S와 N은 없다(정면/후면이라 뒤집어도 자기 자신).
$Mirror = @{ 'W'='E'; 'E'='W'; 'NW'='NE'; 'NE'='NW'; 'SW'='SE'; 'SE'='SW' }

# 파일을 잠그지 않고 비트맵을 연다.
# Bitmap::FromFile은 파일 핸들을 붙들고 있어서, 나중에 같은 경로로 Save하면 GDI+ 오류가 난다.
# 새 비트맵으로 복사해두면 원본 핸들을 바로 놓을 수 있다.
function Open-Unlocked {
    param([string]$Path)
    $tmp = [System.Drawing.Image]::FromFile($Path)
    $bmp = New-Object System.Drawing.Bitmap $tmp
    $tmp.Dispose()
    return $bmp
}

# 행 하나의 "불투명 픽셀 중 아주 어두운 것"의 비율(%)을 잰다.
function Measure-RowDarkShare {
    param([System.Drawing.Bitmap]$Bitmap, [int]$Row, [int]$Cols)

    $rect = New-Object System.Drawing.Rectangle 0, ($Row * $Cell), ($Cols * $Cell), $Cell
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $Cell)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $Bitmap.UnlockBits($data)

    $opaque = 0
    $dark = 0
    $width = $Cols * $Cell
    for ($y = 0; $y -lt $Cell; $y += $SampleStride) {
        $base = $y * $stride
        for ($x = 0; $x -lt $width; $x += $SampleStride) {
            $i = $base + $x * 4
            if ($bytes[$i + 3] -le 8) { continue }
            $opaque++
            # 32bppArgb의 바이트 순서는 B,G,R,A다.
            $lum = 0.0722 * $bytes[$i] + 0.7152 * $bytes[$i + 1] + 0.2126 * $bytes[$i + 2]
            if ($lum -lt $DarkThreshold) { $dark++ }
        }
    }

    if ($opaque -eq 0) { return 0.0 }
    return 100.0 * $dark / $opaque
}

# 짝 행을 좌우 반전해서 대상 행에 덮어쓴다.
#
# 픽셀 루프 대신 RotateFlip + DrawImage를 쓰는 이유: 2048x256 한 행이 52만 픽셀이라
# PowerShell 반복으로 뒤집으면 행 하나에 수 초가 걸린다. GDI+는 같은 일을 네이티브로 한다.
function Copy-RowMirrored {
    param([System.Drawing.Bitmap]$Bitmap, [int]$FromRow, [int]$ToRow, [int]$Cols)

    $g = [System.Drawing.Graphics]::FromImage($Bitmap)
    # SourceCopy가 아니면 알파가 <b>합성</b>되어 손상된 원래 그림이 밑에 비쳐 남는다.
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

    for ($c = 0; $c -lt $Cols; $c++) {
        $src = New-Object System.Drawing.Rectangle ($c * $Cell), ($FromRow * $Cell), $Cell, $Cell
        # Clone은 복사본을 만들므로 같은 비트맵에 그려도 원본이 깨지지 않는다.
        $piece = $Bitmap.Clone($src, $Bitmap.PixelFormat)
        # 셀 중심을 기준으로 뒤집는다. 피벗 x가 0.5(셀 중앙)라 피벗과의 관계가 그대로 유지된다.
        $piece.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX)
        $g.DrawImage($piece, ($c * $Cell), ($ToRow * $Cell), $Cell, $Cell)
        $piece.Dispose()
    }

    $g.Dispose()
}

# 행 하나를 잘라낸 비트맵을 돌려준다(미리보기용).
function Get-RowImage {
    param([System.Drawing.Bitmap]$Bitmap, [int]$Row, [int]$Cols)
    $rect = New-Object System.Drawing.Rectangle 0, ($Row * $Cell), ($Cols * $Cell), $Cell
    return $Bitmap.Clone($rect, $Bitmap.PixelFormat)
}

# ------------------------------------------------------------------
# 본 처리
# ------------------------------------------------------------------
Write-Output "대상 폴더 : $SheetDir"
if ($Apply) { Write-Output "모드      : 적용(PNG 수정)" } else { Write-Output "모드      : 미리보기만" }
Write-Output "판정      : 어두움 < $DarkThreshold / 손상 = 중앙값의 $DamageRatio 미만 / 짝 조건 = 중앙값의 $PartnerMinRatio 이상"
Write-Output ""

$previewRows = @()
$fixedCount = 0
$skipped = @()

foreach ($file in (Get-ChildItem $SheetDir -Filter *.png -File | Sort-Object Name)) {
    $bmp = Open-Unlocked -Path $file.FullName
    $cols = [int]($bmp.Width / $Cell)
    $rows = [int]($bmp.Height / $Cell)

    if ($rows -ne 8) {
        Write-Output "$($file.Name) : 건너뜀 (행 $rows개, 8방향 시트가 아니다)"
        $bmp.Dispose()
        continue
    }

    # 1단계: 행별 측정
    $share = @()
    for ($r = 0; $r -lt $rows; $r++) {
        $share += (Measure-RowDarkShare -Bitmap $bmp -Row $r -Cols $cols)
    }

    $sorted = $share | Sort-Object
    $median = ($sorted[3] + $sorted[4]) / 2.0

    $line = ""
    for ($r = 0; $r -lt $rows; $r++) {
        $line += ("{0} {1,4:N1}  " -f $Dirs[$r], $share[$r])
    }
    Write-Output ("{0}   중앙값 {1:N1}%" -f $file.Name, $median)
    Write-Output ("   $line")

    # 2단계: 손상 행 찾기
    $damaged = @()
    for ($r = 0; $r -lt $rows; $r++) {
        if ($share[$r] -lt ($median * $DamageRatio)) { $damaged += $r }
    }

    if ($damaged.Count -eq 0) {
        Write-Output "   손상 없음"
        Write-Output ""
        $bmp.Dispose()
        continue
    }

    # 3단계: 짝을 확인하고 반전 복사
    $touched = $false
    foreach ($r in $damaged) {
        $dir = $Dirs[$r]

        if (-not $Mirror.ContainsKey($dir)) {
            Write-Output "   $dir : 손상 — 대칭 짝이 없다(정면/후면). 그림을 다시 뽑아야 한다."
            $skipped += "$($file.Name) $dir (짝 없음)"
            continue
        }

        $partner = $Mirror[$dir]
        $pr = [array]::IndexOf($Dirs, $partner)

        if ($share[$pr] -lt ($median * $PartnerMinRatio)) {
            Write-Output ("   {0} : 손상 — 짝 {1}도 {2:N1}%로 온전하지 않아 건너뛴다." -f $dir, $partner, $share[$pr])
            $skipped += "$($file.Name) $dir (짝 $partner 도 손상)"
            continue
        }

        $before = Get-RowImage -Bitmap $bmp -Row $r -Cols $cols
        Copy-RowMirrored -Bitmap $bmp -FromRow $pr -ToRow $r -Cols $cols
        $after = Get-RowImage -Bitmap $bmp -Row $r -Cols $cols

        $previewRows += @{ Label = "$($file.Name)  $dir <- $partner 반전"; Before = $before; After = $after }
        Write-Output ("   {0} : {1,4:N1}% -> 짝 {2}({3:N1}%)를 좌우 반전해 채움" -f $dir, $share[$r], $partner, $share[$pr])
        $touched = $true
        $fixedCount++
    }

    if ($Apply -and $touched) {
        if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
        $backup = Join-Path $BackupDir $file.Name
        # 백업은 최초 1회만. 두 번 돌려도 원본이 덮이지 않게 한다.
        if (-not (Test-Path $backup)) {
            Copy-Item $file.FullName $backup
            Write-Output "   원본 백업 -> Raw\PreMirrorFix\$($file.Name)"
        }
        else {
            Write-Output "   백업 이미 있음 (원본 유지)"
        }

        $bmp.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "   저장 완료"
    }

    $bmp.Dispose()
    Write-Output ""
}

# ------------------------------------------------------------------
# 미리보기 이미지: 고친 행마다 [수정 전 / 수정 후] 두 줄
# ------------------------------------------------------------------
if ($previewRows.Count -gt 0) {
    $width = 0
    foreach ($row in $previewRows) { if ($row.Before.Width -gt $width) { $width = $row.Before.Width } }
    $height = $Cell * 2 * $previewRows.Count

    $canvas = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::White)

    $font = New-Object System.Drawing.Font "Consolas", 13
    $black = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Black)

    $y = 0
    foreach ($row in $previewRows) {
        $g.DrawImage($row.Before, 0, $y)
        $g.DrawString("$($row.Label)  BEFORE", $font, $black, 6, ($y + 6))
        $y += $Cell

        $g.DrawImage($row.After, 0, $y)
        $g.DrawString("$($row.Label)  AFTER", $font, $black, 6, ($y + 6))
        $y += $Cell

        $row.Before.Dispose()
        $row.After.Dispose()
    }

    $g.Dispose()
    $canvas.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Write-Output "미리보기 저장 : $PreviewPath"
}

Write-Output ""
Write-Output "고칠 수 있는 행 : $fixedCount"
if ($skipped.Count -gt 0) {
    Write-Output "못 고치는 행    : $($skipped.Count)  (그림을 다시 뽑아야 한다)"
    foreach ($s in $skipped) { Write-Output "   - $s" }
}

if (-not $Apply) {
    Write-Output ""
    Write-Output "실제로 쓰려면 -Apply 를 붙여서 다시 실행해라."
}

Write-Output ""
Write-Output "참고: 이 도구는 게임이 실제로 쓰는 Production8Dir 시트만 고친다."
Write-Output "      Directional\ 아래 방향별 원본은 그대로 손상돼 있으므로,"
Write-Output "      거기서 시트를 다시 조립하면 이 수정이 사라진다."
