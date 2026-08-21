<#
    DefringeCharacterSprites.ps1
    ------------------------------------------------------------------
    캐릭터 스프라이트 외곽에 구워진 밝은 테두리(프린지)를 깎아낸다.

    왜 필요한가:
    플레이어 스프라이트를 재보니 가장자리 픽셀 평균 밝기가 92.1, 내부가 74.5였다.
    정상적인 캐릭터는 외곽이 내부보다 <b>어둡다</b>(윤곽선과 그림자가 있으니까).
    반대로 24% 더 밝다는 건, 밝은 배경에서 오려낼 때 배경색이 가장자리에 섞여 그대로
    픽셀에 박혔다는 뜻이다. 보스는 41.2 / 49.1로 정상이라 플레이어만 이 문제를 갖고 있다.

    게다가 플레이어는 반투명 픽셀이 0개다. 알파가 0 아니면 255뿐이라 부드럽게 가려지지도
    않고 흰 선이 그대로 드러난다. 어두운 던전 배경 위에서 플레이어만 유독 튀어 보이는 원인이다.

    무엇을 하는가:
      가장자리 픽셀(투명한 이웃이 있는 픽셀) 중 안쪽 이웃보다 뚜렷하게 밝은 것을
      안쪽 이웃의 평균색으로 끌어당긴다. 알파는 건드리지 않는다.

    안쪽 이웃 색으로 덮는 이유:
    단순히 어둡게 곱하면 색조가 같이 죽어서 옷이 회색이 된다. 바로 안쪽 픽셀은 그 부위의
    진짜 색이므로, 거기서 가져오면 색은 유지한 채 배경 섞임만 사라진다.

    사용법:
      미리보기: powershell -File Tools\DefringeCharacterSprites.ps1
      적용:     powershell -File Tools\DefringeCharacterSprites.ps1 -Apply
#>
param(
    [switch]$Apply,
    [string]$Folder = "",
    # 안쪽 이웃보다 이 배수 이상 밝으면 프린지로 본다. 1.08이면 8% 이상.
    [double]$BrightRatio = 1.08,
    # 몇 겹까지 깎을지. 프린지가 2픽셀이면 2가 필요하다.
    [int]$Passes = 2,
    [string]$PreviewPath = "$env:TEMP\defringe_preview.png"
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($Folder)) {
    $Folder = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Assets\Project\Art\Sprites\Player"))
}
$backupDir = Join-Path $Folder "Raw\PreDefringe"

function Get-Luminance([byte]$b, [byte]$g, [byte]$r) {
    return (0.2126 * $r) + (0.7152 * $g) + (0.0722 * $b)
}

$previewBefore = $null
$previewAfter = $null

foreach ($file in (Get-ChildItem $Folder -Filter *.png | Where-Object { $_.Name -notlike "*_raw*" } | Sort-Object Name)) {
    $bmp = New-Object System.Drawing.Bitmap $file.FullName
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    if ($previewBefore -eq $null) { $previewBefore = $bytes.Clone() }

    $fixed = 0

    for ($pass = 0; $pass -lt $Passes; $pass++) {
        # 이번 패스에서 바꿀 값을 따로 모은다. 제자리에서 고치면 이미 고쳐진 픽셀이
        # 다음 픽셀의 "안쪽 이웃"으로 쓰여서 수정이 안쪽으로 번진다.
        $updates = New-Object 'System.Collections.Generic.Dictionary[int,int[]]'

        for ($y = 1; $y -lt $h - 1; $y++) {
            for ($x = 1; $x -lt $w - 1; $x++) {
                $i = $y * $stride + $x * 4
                if ($bytes[$i + 3] -eq 0) { continue }

                # 가장자리인가 — 8이웃 중 투명이 있는가
                $isEdge = $false
                for ($dy = -1; $dy -le 1 -and -not $isEdge; $dy++) {
                    for ($dx = -1; $dx -le 1; $dx++) {
                        if ($bytes[($y + $dy) * $stride + ($x + $dx) * 4 + 3] -eq 0) { $isEdge = $true; break }
                    }
                }
                if (-not $isEdge) { continue }

                # 안쪽 이웃(불투명이면서 그 자신은 가장자리가 아닌 픽셀)의 평균색
                $sb = 0; $sg = 0; $sr = 0; $n = 0
                for ($dy = -1; $dy -le 1; $dy++) {
                    for ($dx = -1; $dx -le 1; $dx++) {
                        if ($dx -eq 0 -and $dy -eq 0) { continue }
                        $nx = $x + $dx; $ny = $y + $dy
                        $ni = $ny * $stride + $nx * 4
                        if ($bytes[$ni + 3] -eq 0) { continue }

                        $neighborIsEdge = $false
                        for ($ey = -1; $ey -le 1 -and -not $neighborIsEdge; $ey++) {
                            for ($ex = -1; $ex -le 1; $ex++) {
                                $ey2 = $ny + $ey; $ex2 = $nx + $ex
                                if ($ey2 -lt 0 -or $ey2 -ge $h -or $ex2 -lt 0 -or $ex2 -ge $w) { continue }
                                if ($bytes[$ey2 * $stride + $ex2 * 4 + 3] -eq 0) { $neighborIsEdge = $true; break }
                            }
                        }
                        if ($neighborIsEdge) { continue }

                        $sb += $bytes[$ni]; $sg += $bytes[$ni + 1]; $sr += $bytes[$ni + 2]; $n++
                    }
                }
                if ($n -eq 0) { continue }

                $ab = [int]($sb / $n); $ag = [int]($sg / $n); $ar = [int]($sr / $n)
                $lumEdge = Get-Luminance $bytes[$i] $bytes[$i + 1] $bytes[$i + 2]
                $lumInner = Get-Luminance $ab $ag $ar

                # 안쪽보다 뚜렷하게 밝을 때만 손댄다. 정상적인 밝은 부위(머리카락 하이라이트 등)를
                # 무조건 깎으면 캐릭터가 통째로 어두워진다.
                if ($lumEdge -le $lumInner * $BrightRatio) { continue }

                $updates[$i] = @($ab, $ag, $ar)
            }
        }

        foreach ($key in $updates.Keys) {
            $v = $updates[$key]
            $bytes[$key] = [byte]$v[0]; $bytes[$key + 1] = [byte]$v[1]; $bytes[$key + 2] = [byte]$v[2]
        }
        $fixed += $updates.Count
    }

    Write-Output ("{0,-46} 프린지 픽셀 {1}개 보정" -f $file.Name, $fixed)
    if ($previewAfter -eq $null) { $previewAfter = $bytes.Clone() }

    if ($Apply) {
        if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
        $backup = Join-Path $backupDir $file.Name
        if (-not (Test-Path $backup)) { Copy-Item $file.FullName $backup }

        $result = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $od = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $od.Scan0, $bytes.Length)
        $result.UnlockBits($od)
        $result.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
        $result.Dispose()
    }
}

Write-Output ""
if ($Apply) { Write-Output "적용 완료. 원본 백업: $backupDir" }
else { Write-Output "미리보기만 했다. -Apply 를 붙이면 원본을 덮어쓴다." }
