<#
    RemoveCrossFrameFragments.ps1
    ------------------------------------------------------------------
    프레임 칸 경계를 넘어와 옆 칸에 남은 이펙트 조각을 지우는 도구.

    왜 필요한가:
    플레이어 시트는 6프레임을 한 장에 이어 그린 형태인데, 참격 이펙트가 칸을 넘어간다.
    그래서 예를 들어 sword_slam NW f4(가만히 선 프레임) 칸에 앞 프레임 참격이
    1181px짜리로 걸쳐 있다. 게임에서는 <b>가만히 있는데 검 궤적이 스쳐 보이는</b> 것으로 나온다.

    이건 수습이지 정석이 아니다:
    제대로 된 해결은 (1) 셀을 키워 다시 뽑거나 (2) 이펙트를 캐릭터 시트에서 빼내
    별도 스프라이트로 생성하는 것이다. 이 프로젝트도 Q 스킬은 이미 (2)를 하고 있다
    (SlamImpact/SlamBurst 프리팹). 지금 시트를 다시 뽑을 형편이 아니라서 지우는 것이고,
    <b>참격 꼬리가 칸 경계에서 뭉툭하게 끊기는 대가</b>를 안다.

    무엇을 지우나 — 두 조건을 <b>모두</b> 만족할 때만 지운다:
      1. 본체(칸에서 가장 큰 덩어리)와 <b>떨어져 있다</b>
      2. 칸 가장자리에서 EdgeMargin 안에 걸쳐 있다
    화면 가운데에 정상적으로 흩어진 불티는 2번에 안 걸려서 살아남는다.

    연결 요소로 판정하는 이유:
    처음엔 "빈 열로 갈라진 덩어리"만 지우려 했는데, 실제로 재보니 조각 24개 중 11개가
    본체와 가로 범위가 겹치거나 사이에 다른 조각이 껴 있었다. 열 단위로는 못 가른다.

    사용법:
      미리보기(에셋 변경 없음): powershell -File Tools\RemoveCrossFrameFragments.ps1
      실제 적용:                powershell -File Tools\RemoveCrossFrameFragments.ps1 -Apply
#>
param(
    # 붙이면 실제 PNG를 수정한다. 없으면 측정 + 미리보기 이미지만 만든다.
    [switch]$Apply,

    # 칸 가장자리에서 이 픽셀 안에 걸쳐 있으면 "넘어온 것"으로 의심한다.
    # 0으로 두면 정확히 경계에 닿은 것만 잡는데, 실제로는 1~2px 떠 있는 조각이 많아서 여유를 준다.
    [int]$EdgeMargin = 12,

    # 칸에서 가장 큰 덩어리의 이 비율 이상이면 조각으로 보지 않는다.
    #
    # 왜 "칸 전체의 절반"이 아니라 "가장 큰 덩어리 대비"인가:
    # 처음엔 칸 전체 픽셀의 50% 이상을 본체로 봤는데, 캐릭터와 참격 이펙트가 비슷한 크기인
    # 프레임에서는 <b>둘 다 50%를 못 넘어 양쪽 다 지워졌다.</b> 미리보기에서 11000px짜리를
    # 조각이라고 잡아낸 게 그 경우다. 가장 큰 덩어리를 기준으로 삼으면 이 함정이 없다.
    [double]$MaxFragmentRatio = 0.30,

    # 이 픽셀 수 미만인 덩어리는 그냥 둔다.
    #
    # 왜 필요한가: 처음엔 크기 조건 없이 돌렸더니 1픽셀짜리 잔티를 수천 개 지웠다.
    # 부드러운 가장자리에서 알파가 살짝 남은 점들인데, 화면에서 보이지도 않는 것을 지우면
    # 얻는 것 없이 원본만 건드리게 된다. 실제로 거슬리는 조각은 수십~1000px 단위다.
    [int]$MinFragmentPixels = 20,

    # 위아래 경계(= 다른 방향 행)도 볼지. 세로로 넘치면 옆 방향 그림이 섞인다.
    [bool]$CheckVertical = $true,

    # 미리보기 PNG 저장 경로
    [string]$PreviewPath = "$env:TEMP\fragment_cleanup_preview.png",

    # 훑을 시트 폴더. 비우면 플레이어 8방향 시트 폴더를 쓴다.
    #
    # 왜 열어둔가: 원래 이 도구는 플레이어 폴더에 못박혀 있었다. 그런데 같은 증상이
    # <b>보스 전환 시트에서도</b> 나왔다 — 갑옷 붕괴 클립 뒤쪽 세 프레임에 옆 칸에서
    # 넘어온 조각이 있었다. 판정 조건(본체 대비 비율, 최소 픽셀 수)은 여러 번 헛발질하며
    # 잡은 것이라 <b>복사본을 만들면 그 함정을 다시 밟는다.</b> 대상만 열어준다.
    [string]$SheetDir = "",

    # 행 이름. 8방향 시트는 방향 이름이지만, 한 줄짜리 시트는 이름이 없다.
    # 보고와 미리보기 라벨에만 쓴다.
    [string[]]$RowNames = @('S','SW','W','NW','N','NE','E','SE'),

    # 훑을 파일 이름 패턴. 폴더에 셀 크기가 다른 시트가 섞여 있으면 좁혀서 쓴다.
    [string]$FilePattern = "*.png",

    # 본체와 <b>가로 범위가 전혀 안 겹치는</b> 조각도 지운다.
    #
    # 왜 따로 뒀나: 기본 판정은 "칸 가장자리 EdgeMargin 안"이다. 그런데 보스 전환 알 시트는
    # 다음 프레임 알의 왼쪽 끄트머리가 <b>칸 가장자리가 아니라 한참 안쪽(x 183~209)에</b>
    # 잘려 남아 있었다. 가장자리만 보면 영영 안 잡힌다.
    #
    # 그렇다고 EdgeMargin을 60까지 키우면 <b>정상적으로 흩어진 재까지</b> 사정권에 들어온다.
    # 대신 "본체 가로 범위 밖에 통째로 벗어났는가"를 본다. 옆 칸에서 넘어온 그림은 본체와
    # 좌우로 떨어져 있고, 본체 주변에 흩어진 파편은 대개 본체의 가로 범위 안에 있다.
    #
    # 기본은 꺼둔다. 플레이어 시트에서 이미 확인된 동작을 바꾸지 않기 위해서다.
    [switch]$OutsideBody
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($SheetDir)) {
    $SheetDir = Join-Path $PSScriptRoot "..\Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir"
}
$SheetDir  = [System.IO.Path]::GetFullPath($SheetDir)
$BackupDir = Join-Path $SheetDir "Raw\PreFragmentCleanup"
$Cell = 256
$Dirs = $RowNames

# 행 이름이 모자라면 번호로 부른다. 한 줄짜리 시트에 8방향 이름을 붙이면
# 보고가 "S f3"으로 나와서 <b>있지도 않은 방향</b>을 가리킨다.
function Get-RowLabel {
    param([int]$Index)
    if ($Index -lt $Dirs.Count) { return $Dirs[$Index] }
    return "r$Index"
}

# 파일을 잠그지 않고 비트맵을 연다.
# Bitmap::FromFile은 핸들을 붙들고 있어서 같은 경로로 Save하면 GDI+ 오류가 난다.
function Open-Unlocked {
    param([string]$Path)
    $tmp = [System.Drawing.Image]::FromFile($Path)
    $bmp = New-Object System.Drawing.Bitmap $tmp
    $tmp.Dispose()
    return $bmp
}

# ------------------------------------------------------------------
# 본 처리
# ------------------------------------------------------------------
Write-Output "대상 폴더 : $SheetDir"
if ($Apply) { Write-Output "모드      : 적용(PNG 수정)" } else { Write-Output "모드      : 미리보기만" }
Write-Output "판정      : 본체와 분리 + 가장자리 $EdgeMargin px 안"
Write-Output ""

$previewRows = @()
$totalErased = 0
$totalCells = 0

foreach ($file in (Get-ChildItem $SheetDir -Filter $FilePattern -File | Sort-Object Name)) {
    $bmp = Open-Unlocked -Path $file.FullName
    $cols = [int]($bmp.Width / $Cell)
    $rows = [int]($bmp.Height / $Cell)
    if ($rows -lt 1 -or $cols -lt 1) { $bmp.Dispose(); continue }

    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $bytes = New-Object byte[] ($stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

    $sheetReport = @()
    $touchedRows = @{}

    for ($r = 0; $r -lt $rows; $r++) {
        for ($c = 0; $c -lt $cols; $c++) {
            $ox = $c * $Cell
            $oy = $r * $Cell

            # 1) 칸 전체 불투명 픽셀 수와, 가장자리에 걸친 픽셀이 있는지 본다.
            $total = 0
            $hasEdge = $false
            for ($y = 0; $y -lt $Cell; $y++) {
                $base = ($oy + $y) * $stride + $ox * 4
                for ($x = 0; $x -lt $Cell; $x++) {
                    if ($bytes[$base + $x * 4 + 3] -le 8) { continue }
                    $total++
                    if ($x -lt $EdgeMargin -or $x -ge ($Cell - $EdgeMargin)) { $hasEdge = $true }
                    elseif ($CheckVertical -and ($y -lt $EdgeMargin -or $y -ge ($Cell - $EdgeMargin))) { $hasEdge = $true }
                }
            }

            # 가장자리에 아무것도 없으면 넘어온 조각도 없다. 대부분의 칸이 여기서 빠진다.
            #
            # 추가 생성 — OutsideBody를 켜면 가장자리 조건으로 칸을 거르지 않는다.
            # 알 시트의 조각은 칸 <b>안쪽</b>(x 183~209)에 있어서, 여기서 걸러버리면
            # 덩어리를 세어보기도 전에 칸이 통째로 넘어간다.
            if ($total -eq 0) { continue }
            if (-not $hasEdge -and -not $OutsideBody) { continue }

            # 칸 하나에서 지운 것을 모아 한 줄로 보고한다. 조각마다 한 줄씩 찍으면 읽을 수가 없다.
            # 반드시 칸마다 0으로 되돌린다 — 안 그러면 PowerShell은 앞 칸 값을 그대로 이어써서
            # 보고 숫자가 칸을 넘어 계속 불어난다.
            $cellPieces = 0
            $cellPixels = 0

            # 2) 칸 안의 모든 덩어리를 찾는다.
            #    가장자리 것만 훑으면 "가장 큰 덩어리"를 알 수 없어서 무엇이 본체인지 못 정한다.
            $visited = New-Object bool[] ($Cell * $Cell)
            $comps = New-Object System.Collections.Generic.List[object]

            for ($sy = 0; $sy -lt $Cell; $sy++) {
                for ($sx = 0; $sx -lt $Cell; $sx++) {
                    $si = $sy * $Cell + $sx
                    if ($visited[$si]) { continue }
                    if ($bytes[($oy + $sy) * $stride + ($ox + $sx) * 4 + 3] -le 8) { continue }

                    # 덩어리 채우기. 스택에 인덱스만 담아 재귀 없이 돈다.
                    $stack = New-Object System.Collections.Generic.Stack[int]
                    $stack.Push($si)
                    $visited[$si] = $true
                    $comp = New-Object System.Collections.Generic.List[int]
                    $touchesEdge = $false

                    while ($stack.Count -gt 0) {
                        $i = $stack.Pop()
                        $comp.Add($i)
                        $iy = [int]($i / $Cell)
                        $ix = $i - $iy * $Cell

                        if ($ix -lt $EdgeMargin -or $ix -ge ($Cell - $EdgeMargin)) { $touchesEdge = $true }
                        elseif ($CheckVertical -and ($iy -lt $EdgeMargin -or $iy -ge ($Cell - $EdgeMargin))) { $touchesEdge = $true }

                        foreach ($d in @(@(1,0), @(-1,0), @(0,1), @(0,-1))) {
                            $nx = $ix + $d[0]
                            $ny = $iy + $d[1]
                            if ($nx -lt 0 -or $nx -ge $Cell -or $ny -lt 0 -or $ny -ge $Cell) { continue }
                            $ni = $ny * $Cell + $nx
                            if ($visited[$ni]) { continue }
                            if ($bytes[($oy + $ny) * $stride + ($ox + $nx) * 4 + 3] -le 8) { continue }
                            $visited[$ni] = $true
                            $stack.Push($ni)
                        }
                    }

                    # 추가 생성 — 가로 범위. OutsideBody 판정에 쓴다.
                    $minX = $Cell; $maxX = -1
                    foreach ($i in $comp) {
                        $iy = [int]($i / $Cell)
                        $ix = $i - $iy * $Cell
                        if ($ix -lt $minX) { $minX = $ix }
                        if ($ix -gt $maxX) { $maxX = $ix }
                    }

                    $comps.Add(@{ Pixels = $comp; Size = $comp.Count; Edge = $touchesEdge; MinX = $minX; MaxX = $maxX })
                }
            }

            # 3) 가장 큰 덩어리를 찾는다. 이건 무슨 일이 있어도 안 지운다.
            $maxSize = 0
            $body = $null
            foreach ($cp in $comps) { if ($cp.Size -gt $maxSize) { $maxSize = $cp.Size; $body = $cp } }
            $fragLimit = $maxSize * $MaxFragmentRatio

            # 4) 가장자리에 걸쳐 있고 충분히 작은 덩어리만 지운다.
            foreach ($cp in $comps) {
                if ($cp.Size -ge $fragLimit) { continue }
                if ($cp.Size -lt $MinFragmentPixels) { continue }

                # 추가 생성 — 본체와 가로로 전혀 안 겹치면 넘어온 그림으로 본다.
                #
                # 알 시트의 조각은 칸 가장자리가 아니라 x 183~209(칸 폭의 71~82%)에 있었다.
                # 가장자리만 보는 기본 판정으로는 영영 안 잡힌다. 그렇다고 EdgeMargin을
                # 그만큼 키우면 정상적으로 흩어진 재까지 사정권에 들어온다.
                $outside = $false
                if ($OutsideBody -and $null -ne $body) {
                    if ($cp.MaxX -lt $body.MinX -or $cp.MinX -gt $body.MaxX) { $outside = $true }
                }

                if (-not $cp.Edge -and -not $outside) { continue }

                foreach ($i in $cp.Pixels) {
                    $iy = [int]($i / $Cell)
                    $ix = $i - $iy * $Cell
                    $bytes[($oy + $iy) * $stride + ($ox + $ix) * 4 + 3] = 0
                }

                $cellPieces++
                $cellPixels += $cp.Size
                $totalErased += $cp.Size
                $touchedRows[$r] = $true
            }

            if ($cellPieces -gt 0) {
                $sheetReport += ("{0} f{1}: {2}개 {3}px" -f (Get-RowLabel $r), $c, $cellPieces, $cellPixels)
                $totalCells++
            }
        }
    }

    if ($sheetReport.Count -eq 0) {
        Write-Output "$($file.Name) : 지울 조각 없음"
        $bmp.UnlockBits($data)
        $bmp.Dispose()
        continue
    }

    Write-Output "$($file.Name)"
    Write-Output ("   " + ($sheetReport -join "  "))

    # 수정 전 행 이미지를 먼저 떠둔다(아직 bytes를 비트맵에 되돌리기 전).
    foreach ($r in $touchedRows.Keys) {
        $srcRect = New-Object System.Drawing.Rectangle 0, ($r * $Cell), ($cols * $Cell), $Cell
        $before = $bmp.Clone($srcRect, $bmp.PixelFormat)
        $previewRows += @{ Label = "$($file.Name)  $(Get-RowLabel $r)"; Before = $before; Row = $r; Cols = $cols; File = $file.FullName }
    }

    # 지운 결과를 비트맵에 되돌린다.
    [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    $bmp.UnlockBits($data)

    # 수정 후 행 이미지를 뜬다.
    foreach ($row in $previewRows) {
        if ($row.File -ne $file.FullName) { continue }
        if ($row.ContainsKey('After')) { continue }
        $srcRect = New-Object System.Drawing.Rectangle 0, ($row.Row * $Cell), ($row.Cols * $Cell), $Cell
        $row['After'] = $bmp.Clone($srcRect, $bmp.PixelFormat)
    }

    if ($Apply) {
        if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }
        $backup = Join-Path $BackupDir $file.Name
        # 백업은 최초 1회만. 두 번 돌려도 원본이 덮이지 않게 한다.
        if (-not (Test-Path $backup)) {
            Copy-Item $file.FullName $backup
            Write-Output "   원본 백업 -> Raw\PreFragmentCleanup\$($file.Name)"
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
# 미리보기: 행마다 [수정 전 / 수정 후]
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
        if ($row.ContainsKey('After')) { $g.DrawImage($row.After, 0, $y) }
        $g.DrawString("$($row.Label)  AFTER", $font, $black, 6, ($y + 6))
        $y += $Cell

        $row.Before.Dispose()
        if ($row.ContainsKey('After')) { $row.After.Dispose() }
    }

    $g.Dispose()
    $canvas.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Write-Output "미리보기 저장 : $PreviewPath"
}

Write-Output ""
Write-Output "지운 조각 : $totalCells 개 / $totalErased px"

if (-not $Apply) {
    Write-Output ""
    Write-Output "실제로 쓰려면 -Apply 를 붙여서 다시 실행해라."
}
