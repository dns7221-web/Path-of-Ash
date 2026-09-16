<#
    AssembleDirectionalSheet.ps1
    ------------------------------------------------------------------
    방향별 6프레임 한 줄 5장(S, SE, E, NE, N)을 8방향 시트(6열 x 8행, 1536x2048)로 조립해 설치한다.

    왜 필요한가:
    생성은 5방향만 한다. W·SW·NW는 E·SE·NE를 좌우 반전해서 만든다(걷기와 같은 방식).
    시트의 행 순서가 S, SW, W, NW, N, NE, E, SE라서 손으로 붙이면 행을 틀리기 쉽다.

    무엇을 하는가:
      1. 행마다 한 줄을 넣는다. W·SW·NW는 셀마다 좌우 반전한다
      2. 행마다 마지막 프레임을 대기 시트의 같은 행 첫 프레임으로 바꾼다.
         생성기가 그린 "대기로 돌아온 자세"는 실제 대기와 머리가 10px쯤 어긋나서, 공격이 끝나고
         대기로 넘어가는 순간 캐릭터가 툭 튄다. 같은 그림을 넣으면 이음새가 없다
         (추가 생성) -KeepLastFrame이면 이 교체를 건너뛴다. 사망처럼 대기로 돌아가지 않는 모션용이다
      3. -Apply면 기존 시트를 Raw\<BackupName>에 백업하고 같은 경로에 덮어쓴다

    시트 크기와 격자(256 셀, 6x8)가 기존과 같아서 .meta의 조각·피벗과 클립의 스프라이트 참조가
    그대로 산다. 8방향 애니메이션 빌더를 다시 돌릴 필요가 없다.

    한 줄은 NormalizeGeneratedStrip.ps1 → RecolorToEmberPalette.ps1 순서로 만든 것을 넣는다.

    사용법:
      미리보기: powershell -File Tools\AssembleDirectionalSheet.ps1
      설치:     powershell -File Tools\AssembleDirectionalSheet.ps1 -Apply
      사망처럼 마지막 프레임에서 멈추는 모션(추가 생성): 위 명령에 -KeepLastFrame 을 붙인다
#>
param(
    # 붙이면 기존 시트를 덮어쓴다. 없으면 %TEMP%에 미리보기만 쓴다.
    [switch]$Apply,

    # 방향별 한 줄이 있는 폴더와 파일 이름 틀. {0} 자리에 S, SE, E, NE, N이 들어간다.
    [string]$StripDir = "_ArtReview\20260911-attack-v1",
    [string]$StripPattern = "player_attack_{0}_6frames_1536x256.png",

    # 덮어쓸 시트와 마지막 프레임을 가져올 대기 시트.
    [string]$TargetPath = "Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_attack.png",
    [string]$IdleSheet = "Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_idle.png",

    # 백업 폴더 이름(시트 옆 Raw\ 아래).
    [string]$BackupName = "PreAttackV2",

    # 추가 생성 — 붙이면 마지막 프레임을 대기 그림으로 바꾸지 않고 한 줄의 것을 그대로 쓴다.
    #
    # 왜 필요한가: 대기 교체는 공격·피격처럼 "끝나면 대기로 돌아가는" 모션의 이음새 규칙이다.
    # 사망은 마지막 프레임(잿더미)에서 멈추는 모션이다. Die 상태에는 출구 전환이 없어서
    # 클립의 마지막 프레임이 그대로 화면에 남는데, 여기서 대기 그림을 넣으면
    # 죽은 캐릭터가 마지막에 벌떡 서서 그 자세로 굳는다.
    [switch]$KeepLastFrame,

    [int]$Cell = 256,
    [int]$Frames = 6
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

# 시트 행 순서(AshPlayerDirectionalAnimationBuilder의 DirectionNames와 같다)와 행마다 쓸 원본·반전 여부.
$rows = @(
    @{ Name = "S";  Source = "S";  Mirror = $false },
    @{ Name = "SW"; Source = "SE"; Mirror = $true  },
    @{ Name = "W";  Source = "E";  Mirror = $true  },
    @{ Name = "NW"; Source = "NE"; Mirror = $true  },
    @{ Name = "N";  Source = "N";  Mirror = $false },
    @{ Name = "NE"; Source = "NE"; Mirror = $false },
    @{ Name = "E";  Source = "E";  Mirror = $false },
    @{ Name = "SE"; Source = "SE"; Mirror = $false }
)

function Resolve-ProjectPath([string]$p) {
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    return [System.IO.Path]::GetFullPath((Join-Path $root $p))
}

# PNG를 BGRA 바이트로 꺼낸다. 바이트만 옮기고 바로 닫는다(반투명 픽셀 값이 틀어지지 않게).
function Read-Pixels([string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $path
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $result = @{ Width = $bmp.Width; Height = $bmp.Height; Stride = $data.Stride; Bytes = $bytes }
    $bmp.UnlockBits($data)
    $bmp.Dispose()
    return $result
}

$outW = $Cell * $Frames
$outH = $Cell * $rows.Count
$outStride = $outW * 4
$out = New-Object byte[] ($outStride * $outH)
# 수정(사망 모션) — 대기 교체를 안 할 때는 대기 시트를 읽지 않는다. 쓰지도 않을 파일을 요구하지 않게 한다.
$idle = if ($KeepLastFrame) { $null } else { Read-Pixels (Resolve-ProjectPath $IdleSheet) }

$strips = @{}
foreach ($d in @("S", "SE", "E", "NE", "N")) {
    $p = Resolve-ProjectPath (Join-Path $StripDir ($StripPattern -f $d))
    if (-not (Test-Path $p)) { Write-Error "한 줄이 없다: $p"; return }
    $strips[$d] = Read-Pixels $p
    if ($strips[$d].Width -ne $outW -or $strips[$d].Height -ne $Cell) {
        Write-Error ("{0} 크기가 {1}x{2}다. {3}x{4}여야 한다." -f $d, $strips[$d].Width, $strips[$d].Height, $outW, $Cell); return
    }
}

for ($r = 0; $r -lt $rows.Count; $r++) {
    $row = $rows[$r]
    $src = $strips[$row.Source]
    for ($f = 0; $f -lt $Frames; $f++) {
        for ($y = 0; $y -lt $Cell; $y++) {
            $to = ((($r * $Cell) + $y) * $outStride) + ($f * $Cell * 4)
            # 수정(사망 모션) — -KeepLastFrame이면 이 분기를 건너뛰어, 마지막 프레임도 아래의
            # 일반 경로(반전 포함)로 한 줄에서 가져온다.
            if ($f -eq $Frames - 1 -and -not $KeepLastFrame) {
                # 마지막 프레임은 대기 시트의 같은 행 첫 프레임을 그대로 쓴다.
                $so = ((($r * $Cell) + $y) * $idle.Stride)
                [System.Array]::Copy($idle.Bytes, $so, $out, $to, $Cell * 4)
                continue
            }
            $so = ($y * $src.Stride) + ($f * $Cell * 4)
            if (-not $row.Mirror) {
                [System.Array]::Copy($src.Bytes, $so, $out, $to, $Cell * 4)
                continue
            }
            # 셀 안에서 좌우를 뒤집는다. 셀 밖 픽셀을 끌어오지 않도록 셀 폭 안에서만 옮긴다.
            for ($x = 0; $x -lt $Cell; $x++) {
                $s = $so + (($Cell - 1 - $x) * 4)
                $t = $to + ($x * 4)
                $out[$t] = $src.Bytes[$s]; $out[$t + 1] = $src.Bytes[$s + 1]
                $out[$t + 2] = $src.Bytes[$s + 2]; $out[$t + 3] = $src.Bytes[$s + 3]
            }
        }
    }
    $how = if ($row.Mirror) { "$($row.Source) 좌우 반전" } else { $row.Source }
    # 수정(사망 모션) — 마지막 프레임을 어디서 가져왔는지 보고에 그대로 적는다.
    $last = if ($KeepLastFrame) { "마지막 프레임 유지" } else { "마지막 프레임 ← 대기 $($row.Name)" }
    Write-Output ("  {0,-2} ← {1}, {2}" -f $row.Name, $how, $last)
}

$result = New-Object System.Drawing.Bitmap $outW, $outH, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$rect = New-Object System.Drawing.Rectangle 0, 0, $outW, $outH
$resultData = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                               [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
[System.Runtime.InteropServices.Marshal]::Copy($out, 0, $resultData.Scan0, $out.Length)
$result.UnlockBits($resultData)

$target = Resolve-ProjectPath $TargetPath
if ($Apply) {
    $backupDir = Join-Path ([System.IO.Path]::GetDirectoryName($target)) "Raw\$BackupName"
    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }
    $backup = Join-Path $backupDir ([System.IO.Path]::GetFileName($target))
    # 백업은 처음 한 번만. 두 번째 실행이 이미 조립한 시트로 원본을 덮지 않게 한다.
    if (Test-Path $backup) { Write-Output "백업      : 이미 있음 → 그대로 둔다 ($backup)" }
    else { Copy-Item -LiteralPath $target -Destination $backup; Write-Output "백업      : $backup" }
    # 같은 경로에 덮어쓰므로 .meta와 GUID가 그대로다.
    $result.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "설치      : $target"
} else {
    $preview = Join-Path $env:TEMP ([System.IO.Path]::GetFileNameWithoutExtension($target) + "-assembled.png")
    $result.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "미리보기  : $preview (설치하려면 -Apply)"
}
$result.Dispose()
