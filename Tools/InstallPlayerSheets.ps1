<#
    InstallPlayerSheets.ps1
    ------------------------------------------------------------------
    새로 생성한 탑다운 플레이어 시트를 정규화해서 기존 파일 자리에 설치한다.

    무엇을 하는가:
      1. 프레임마다 발 라인을 찾아 GroundLine(기본 216행)으로 세로 이동
      2. 머리나 무기가 프레임 밖으로 잘리지 않게 이동량을 제한
      3. 원본을 Raw/PreInstall 에 백업하고 기존 파일 이름 그대로 덮어쓴다

    왜 216행인가:
    기존 .meta의 피벗이 y=0.15234(= 프레임 아래에서 39px = 216~217행)다. 발을 그 줄에 맞추면
    피벗이 곧 발밑이 되어 캐릭터가 바닥에 정확히 선다. 무엇보다 <b>.meta를 하나도 안 고쳐도 된다</b> —
    슬라이스·피벗·PPU가 그대로 유효하므로 애니메이션 클립도 안 깨진다.

    왜 파일 이름을 기존 것으로 되돌리는가:
    애니메이션 클립 10개가 스프라이트를 <b>파일 GUID</b>로 참조한다. 새 이름으로 넣으면 GUID가
    달라서 클립이 전부 빈 참조가 된다. 같은 경로에 내용만 덮어쓰면 GUID가 유지돼 클립이 그대로 산다.

    사용법:
      미리보기: powershell -File Tools\InstallPlayerSheets.ps1
      설치:     powershell -File Tools\InstallPlayerSheets.ps1 -Apply
#>
param(
    [switch]$Apply,
    # 발이 놓일 행. 기존 피벗(아래에서 39px)과 맞춘 값이다.
    [int]$GroundLine = 216,
    # 한 행이 "발"로 인정받으려면 필요한 불투명 픽셀 수. 검 끝이나 이펙트를 걸러낸다.
    [int]$MassThreshold = 12
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$PlayerRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Assets\Project\Art\Sprites\Player"))
$Production = Join-Path $PlayerRoot "Topdown35\Production"
$BackupDir = Join-Path $PlayerRoot "Raw\PreInstall"

# 새 파일 -> 설치될 기존 파일. 클립이 참조하는 이름이라 반드시 이 이름으로 들어가야 한다.
$Mapping = @{
    "player_idle.png"       = "player_idle_6frames_1536x256.png"
    "player_attack.png"     = "player_attack_6frames_1536x256.png"
    "player_bow.png"        = "player_bow_6frames_1536x256.png"
    "player_dash_hit.png"   = "player_dash_hit_6frames_1536x256.png"
    "player_death.png"      = "player_death_6frames_1536x256.png"
    "player_staff.png"      = "player_staff_6frames_1536x256.png"
    "player_sword_slam.png" = "player_sword_slam_6frames_1536x256.png"
    "player_ultimate.png"   = "player_ultimate_6frames_1536x256.png"
    "player_walk.png"       = "player_walk_8frames_2048x256.png"
}

# idle은 Production이 아니라 Player 폴더에 먼저 들어와 있다.
$IdleSource = Join-Path $PlayerRoot "player_idle_topdown35_6frames_1536x256.png"
$IdleTarget = "player_idle_6frames_1536x256.png"

function Get-Sheet([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $path
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $result = @{ Bytes = $bytes; Stride = $data.Stride; Width = $bmp.Width; Height = $bmp.Height }
    $bmp.UnlockBits($data)
    $bmp.Dispose()
    return $result
}

function Convert-Sheet([hashtable]$sheet, [string]$name) {
    $bytes = $sheet.Bytes; $stride = $sheet.Stride
    $cell = 256
    $frames = [int]($sheet.Width / $cell)
    $dst = New-Object byte[] $bytes.Length
    $report = @()

    for ($i = 0; $i -lt $frames; $i++) {
        $ox = $i * $cell
        $foot = -1; $bboxTop = -1; $bboxBot = -1

        for ($y = 0; $y -lt $cell; $y++) {
            $count = 0
            $base = $y * $stride + $ox * 4
            for ($x = 0; $x -lt $cell; $x++) { if ($bytes[$base + $x * 4 + 3] -gt 32) { $count++ } }
            if ($count -gt 0) {
                if ($bboxTop -lt 0) { $bboxTop = $y }
                $bboxBot = $y
            }
            if ($count -ge $MassThreshold) { $foot = $y }
        }

        if ($foot -lt 0) { $report += "f${i}:빈프레임"; continue }

        $dy = $GroundLine - $foot
        # 위아래 어느 쪽으로도 프레임 밖으로 잘리지 않게 제한한다.
        if (($bboxTop + $dy) -lt 0) { $dy = -$bboxTop }
        if (($bboxBot + $dy) -gt ($cell - 1)) { $dy = ($cell - 1) - $bboxBot }

        $rowBytes = $cell * 4
        $srcX = $ox * 4
        for ($y = 0; $y -lt $cell; $y++) {
            $srcY = $y - $dy
            if ($srcY -lt 0 -or $srcY -ge $cell) { continue }
            [System.Array]::Copy($bytes, ($srcY * $stride + $srcX), $dst, ($y * $stride + $srcX), $rowBytes)
        }

        $report += ("f{0}:{1}->{2}" -f $i, $foot, ($foot + $dy))
    }

    $line = "{0,-34} {1}" -f $name, ($report -join "  "); [Console]::WriteLine($line)
    return ,([byte[]]$dst)
}

function Install-Sheet([string]$sourcePath, [string]$targetName) {
    if (-not (Test-Path $sourcePath)) {
        $line = "{0,-34} 원본 없음 - 건너뜀" -f $targetName; [Console]::WriteLine($line)
        return
    }

    $targetPath = Join-Path $PlayerRoot $targetName
    $sheet = Get-Sheet $sourcePath
    $dst = Convert-Sheet $sheet $targetName

    if (-not $Apply) { return }

    if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
    $backup = Join-Path $BackupDir $targetName
    # 백업은 최초 1회만. 두 번 돌려도 진짜 원본이 유지된다.
    if ((Test-Path $targetPath) -and -not (Test-Path $backup)) { Copy-Item $targetPath $backup }

    $result = New-Object System.Drawing.Bitmap $sheet.Width, $sheet.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $sheet.Width, $sheet.Height
    $od = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($dst, 0, $od.Scan0, $dst.Length)
    $result.UnlockBits($od)
    $result.Save($targetPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $result.Dispose()
}

Write-Output ("기준 발 라인: {0}행 / 모드: {1}" -f $GroundLine, $(if ($Apply) { "설치" } else { "미리보기" }))
Write-Output ""

Install-Sheet $IdleSource $IdleTarget
foreach ($key in ($Mapping.Keys | Sort-Object)) {
    Install-Sheet (Join-Path $Production $key) $Mapping[$key]
}

Write-Output ""
if ($Apply) {
    Write-Output "설치 완료. 기존 파일 백업: $BackupDir"
    Write-Output "파일 이름과 .meta를 그대로 뒀으므로 애니메이션 클립은 손댈 필요 없다."
}
else {
    Write-Output "미리보기만 했다. -Apply 를 붙이면 기존 파일을 덮어쓴다."
}

# player_hit.png은 대응하는 기존 파일이 없다. 지금 player_hit.anim이 어떤 시트를 쓰는지
# 확인한 뒤 별도로 붙여야 하므로 여기서는 건드리지 않는다.
$hit = Join-Path $Production "player_hit.png"
if (Test-Path $hit) {
    Write-Output ""
    Write-Output "참고: player_hit.png은 대응하는 기존 파일이 없어 설치하지 않았다."
}
