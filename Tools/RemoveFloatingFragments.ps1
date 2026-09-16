<#
    RemoveFloatingFragments.ps1
    ------------------------------------------------------------------
    몸에서 떨어져 허공에 떠 있는 작은 조각을 지운다.

    왜 필요한가:
    2026-09-10에 넣은 새 걷기·대기 시트에 몸에서 12~36px 떨어진 허공에 가는 세로 조각
    (3~50px)이 떠 있었다. 대기 E/W는 6프레임 전부, 걷기는 거의 모든 방향이다.
    반투명(알파 중앙값 37~123)이라 원본 빨강일 때는 붉은 실선으로, 재색칠 뒤에는 갈색 점으로
    보인다. 캐릭터가 가만히 서 있어도 앞에 뭔가가 떠다니는 것처럼 보인다.

    RemoveCrossFrameFragments.ps1에 덧붙이지 않은 이유:
    그 도구는 "칸 가장자리에 걸친 조각"(옆 칸에서 넘어온 참격)을 지운다. 이 조각들은 칸
    한가운데(x 54~200)에 있고 절반은 그 도구의 최소 크기(20px)보다 작아서 판정 자체가 다르다.
    그리고 그 도구는 칸 오른쪽 절반의 좌표를 잘못 푼다([int] 나눗셈이 내림이 아니라 반올림).
    2026-09-11에 발견해 보고만 하고 고치지 않았으므로, 그 코드 위에 새 판정을 얹지 않았다.

    판정 — 둘을 모두 만족할 때만 지운다:
      1. 8방향으로 이어진 덩어리가 MinPixels~MaxPixels 크기다 (실측 3~50px)
      2. 덩어리 밖의 그림이 반경 Radius(유클리드 거리) 안에 한 픽셀도 없다
    몸통은 1번에서 빠진다(수만 px). 망토 끝자락처럼 1~3px 틈으로 떨어진 조각은 바로 옆에
    망토가 있어서 2번에 걸려 살아남는다.
    2026-09-11 실측: 걷기 27개·대기 16개가 지워지고, 망토 옆 조각 11개(틈 2~3px)는 남는다.
    눈으로 한 칸씩 분류한 결과와 같다.

    8방향으로 묶는 이유:
    비스듬한 조각은 4방향으로 묶으면 여러 개로 쪼개진다. 쪼개진 조각끼리 서로 "옆에 그림이
    있다"고 보게 되어 영영 허공으로 판정되지 않는다.

    지울 때 둘레의 희미한 픽셀(알파 1~AlphaThreshold)도 같이 지운다:
    조각마다 둘레 2px 안에 알파 1~8짜리가 수십 개씩 붙어 있었다(걷기 703개, 대기 413개).
    화면에서는 안 보이지만 남겨두면 나중에 밝기를 올리거나 외곽선을 따는 도구가 그것을
    다시 조각으로 잡는다.

    스킬·이펙트 시트에는 쓰지 않는다:
    거기서는 허공에 흩어진 불티가 <b>의도한 그림</b>이다. 기본 대상이 걷기·대기뿐인 이유다.

    순서: RecolorToEmberPalette.ps1과는 순서가 상관없다(이 도구는 알파만 본다).

    사용법:
      미리보기(원본 안 건드림): powershell -File Tools\RemoveFloatingFragments.ps1
      실제 적용:                powershell -File Tools\RemoveFloatingFragments.ps1 -Apply
#>
param(
    # 붙이면 원본 PNG를 덮어쓴다. 없으면 측정과 미리보기만 한다.
    [switch]$Apply,

    # 처리할 시트. 여러 장을 넘길 수 있다. 이펙트가 섞인 시트는 넣지 않는다.
    [string[]]$SheetPaths = @(
        "Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_walk.png",
        "Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_idle.png"
    ),

    # 셀 한 변(px). 이 프로젝트의 플레이어 시트는 전부 256이다.
    [int]$Cell = 256,

    # 알파가 이 값보다 커야 "그림"으로 센다. 1~8은 화면에서 안 보이는 잔상이다.
    # RemoveCrossFrameFragments.ps1과 같은 기준이다.
    [int]$AlphaThreshold = 8,

    # 허공 판정 반경(px). 망토 옆 조각의 틈이 2~3px, 허공 조각은 12px 이상이었다.
    # 그 사이에서 망토 쪽으로 여유를 둔 값이다.
    [double]$Radius = 8,

    # 이보다 작은 덩어리는 건드리지 않는다. 1~2px 점은 화면에서 안 보인다.
    [int]$MinPixels = 3,

    # 이보다 큰 덩어리는 허공에 있어도 지우지 않는다. 실측 최대 50px.
    # 크게 떨어져 나간 조각은 의도한 그림일 수 있어서 사람이 보고 정해야 한다.
    [int]$MaxPixels = 80,

    # 미리보기를 쓸 폴더. Assets 안에 쓰면 유니티가 그것까지 임포트하므로 밖에 둔다.
    [string]$PreviewDir = $env:TEMP
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

# 8방향 시트의 세로 순서. 이미지 맨 윗줄부터 S, SW, W, NW, N, NE, E, SE다.
$directionNames = @("S", "SW", "W", "NW", "N", "NE", "E", "SE")

# 반경 안의 이웃을 셀 때 쓰는 제곱 거리. 루프마다 제곱하지 않으려고 미리 구한다.
$radiusSquared = $Radius * $Radius
$reach = [int][Math]::Ceiling($Radius)

foreach ($relative in $SheetPaths) {
    $path = if ([System.IO.Path]::IsPathRooted($relative)) { $relative }
            else { [System.IO.Path]::GetFullPath((Join-Path $root $relative)) }

    if (-not (Test-Path $path)) {
        Write-Output "$relative : 파일 없음"
        continue
    }

    $name = [System.IO.Path]::GetFileName($path)

    # 원본을 열어 바이트만 꺼내고 바로 닫는다. (RecolorToEmberPalette.ps1과 같은 방식)
    # new Bitmap(image)로 복사하면 GDI+가 반투명 픽셀을 미리곱한 알파로 한 번 거쳐
    # 지우지 않은 픽셀까지 1~4단계씩 바뀐다. 이 도구는 지운 픽셀 말고는 한 비트도 안 바꿔야 한다.
    $source = New-Object System.Drawing.Bitmap $path
    $width = $source.Width
    $height = $source.Height
    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $sourceData = $source.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                                   [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $sourceData.Stride
    $bytes = New-Object byte[] ($stride * $height)
    [System.Runtime.InteropServices.Marshal]::Copy($sourceData.Scan0, $bytes, 0, $bytes.Length)
    $source.UnlockBits($sourceData)
    $source.Dispose()   # 여기서 파일 핸들을 놓아야 나중에 같은 경로로 저장할 수 있다

    # 미리보기용 표시본. 지울 픽셀을 자홍색으로 칠해 어디가 지워지는지 한눈에 보이게 한다.
    $marked = $bytes.Clone()

    # 알파만 따로 꺼내 둔다. 덩어리 찾기와 이웃 검사가 알파만 읽는다.
    $count = $width * $height
    $alpha = New-Object byte[] $count
    for ($y = 0; $y -lt $height; $y++) {
        $rowBase = $y * $stride
        $pixBase = $y * $width
        for ($x = 0; $x -lt $width; $x++) { $alpha[$pixBase + $x] = $bytes[$rowBase + ($x * 4) + 3] }
    }

    $label = New-Object int[] $count      # 덩어리 번호. 0이면 아직 안 봤다
    $groupId = 0
    $stack = New-Object 'System.Collections.Generic.Stack[int]'
    $members = New-Object 'System.Collections.Generic.List[int]'
    $report = @()
    $erasedPixels = 0
    $faintPixels = 0

    $rows = [int][Math]::Floor($height / $Cell)
    $cols = [int][Math]::Floor($width / $Cell)

    for ($cellRow = 0; $cellRow -lt $rows; $cellRow++) {
        for ($cellCol = 0; $cellCol -lt $cols; $cellCol++) {
            $x0 = $cellCol * $Cell
            $y0 = $cellRow * $Cell
            $x1 = $x0 + $Cell
            $y1 = $y0 + $Cell

            for ($sy = $y0; $sy -lt $y1; $sy++) {
                for ($sx = $x0; $sx -lt $x1; $sx++) {
                    $start = ($sy * $width) + $sx
                    if ($label[$start] -ne 0 -or $alpha[$start] -le $AlphaThreshold) { continue }

                    # 8방향으로 이어진 덩어리를 모은다. 칸 밖으로는 안 나간다 — 옆 칸은 다른 프레임이다.
                    $groupId++
                    $members.Clear()
                    $label[$start] = $groupId
                    $stack.Push($start)
                    while ($stack.Count -gt 0) {
                        $q = $stack.Pop()
                        $members.Add($q)

                        # [int](q / width)는 반올림이라 쓰면 안 된다. 오른쪽 절반의 좌표가 음수로 풀린다.
                        $qy = [int][Math]::Floor($q / $width)
                        $qx = $q - ($qy * $width)
                        for ($dy = -1; $dy -le 1; $dy++) {
                            $ny = $qy + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -1; $dx -le 1; $dx++) {
                                $nx = $qx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                $n = ($ny * $width) + $nx
                                if ($label[$n] -ne 0 -or $alpha[$n] -le $AlphaThreshold) { continue }
                                $label[$n] = $groupId
                                $stack.Push($n)
                            }
                        }
                    }

                    # 1) 크기. 몸통은 여기서 빠진다.
                    $size = $members.Count
                    if ($size -lt $MinPixels -or $size -gt $MaxPixels) { continue }

                    # 2) 반경 안에 덩어리 밖의 그림이 있는가. 하나라도 있으면 몸에 딸린 조각으로 본다.
                    $isolated = $true
                    foreach ($m in $members) {
                        $my = [int][Math]::Floor($m / $width)
                        $mx = $m - ($my * $width)
                        for ($dy = -$reach; $dy -le $reach -and $isolated; $dy++) {
                            $ny = $my + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -$reach; $dx -le $reach; $dx++) {
                                $nx = $mx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                if ((($dx * $dx) + ($dy * $dy)) -gt $radiusSquared) { continue }
                                $n = ($ny * $width) + $nx
                                if ($alpha[$n] -le $AlphaThreshold -or $label[$n] -eq $groupId) { continue }
                                $isolated = $false
                                break
                            }
                        }
                        if (-not $isolated) { break }
                    }
                    if (-not $isolated) { continue }

                    # 허공 조각으로 확정. 조각과 둘레 2px 안의 희미한 픽셀을 완전히 비운다.
                    $sumX = 0.0
                    $sumY = 0.0
                    foreach ($m in $members) {
                        $my = [int][Math]::Floor($m / $width)
                        $mx = $m - ($my * $width)
                        $sumX += $mx - $x0
                        $sumY += $my - $y0

                        for ($dy = -2; $dy -le 2; $dy++) {
                            $ny = $my + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -2; $dx -le 2; $dx++) {
                                $nx = $mx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                $n = ($ny * $width) + $nx
                                $isMember = ($dx -eq 0 -and $dy -eq 0)
                                $isFaint = ($alpha[$n] -gt 0 -and $alpha[$n] -le $AlphaThreshold)
                                if (-not $isMember -and -not $isFaint) { continue }

                                $o = ($ny * $stride) + ($nx * 4)
                                if ($isFaint) { $faintPixels++ }
                                $bytes[$o] = 0; $bytes[$o + 1] = 0; $bytes[$o + 2] = 0; $bytes[$o + 3] = 0

                                # 표시본에는 자홍색으로. 희미한 픽셀도 칠해야 "같이 지워지는 범위"가 보인다.
                                $marked[$o] = 255; $marked[$o + 1] = 0; $marked[$o + 2] = 255; $marked[$o + 3] = 255
                                $alpha[$n] = 0
                            }
                        }
                    }
                    $erasedPixels += $size

                    $rowName = if ($rows -eq 8) { $directionNames[$cellRow] } else { "r$cellRow" }
                    $report += ("{0} f{1} {2}px (x{3},y{4})" -f $rowName, $cellCol, $size,
                                [int]($sumX / $size), [int]($sumY / $size))
                }
            }
        }
    }

    Write-Output "$name"
    Write-Output ("  크기      : {0}x{1}, 셀 {2}" -f $width, $height, $Cell)
    if ($report.Count -eq 0) {
        Write-Output "  허공 조각 : 없음"
        continue
    }
    Write-Output ("  허공 조각 : {0}개 {1}px (둘레 희미한 픽셀 {2}개 같이 비움)" -f $report.Count, $erasedPixels, $faintPixels)

    # 한 줄에 여섯 개씩. 조각마다 한 줄씩 찍으면 읽을 수가 없다.
    for ($i = 0; $i -lt $report.Count; $i += 6) {
        $last = [Math]::Min($i + 5, $report.Count - 1)
        Write-Output ("    " + ($report[$i..$last] -join "  "))
    }

    # 새 비트맵에 바이트를 그대로 붓는다. 그리기를 거치지 않으므로 나머지 픽셀이 원본과 같다.
    $result = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $resultData = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                                   [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $resultData.Scan0, $bytes.Length)
    $result.UnlockBits($resultData)

    if ($Apply) {
        # 다른 도구들과 같은 규칙 — 원본은 Raw\ 아래에 남긴다.
        $backupDir = Join-Path ([System.IO.Path]::GetDirectoryName($path)) "Raw\PreFloatingCleanup"
        if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }

        $backupPath = Join-Path $backupDir $name

        # 백업이 이미 있으면 덮어쓰지 않는다. 두 번째 실행이 <b>이미 처리된 그림</b>을
        # 원본 자리에 얹으면 진짜 원본을 되찾을 방법이 없어진다.
        if (Test-Path $backupPath) {
            Write-Output "  백업      : 이미 있음 → 그대로 둔다 ($backupPath)"
        } else {
            Copy-Item -LiteralPath $path -Destination $backupPath
            Write-Output "  백업      : $backupPath"
        }

        # 같은 경로에 덮어쓰므로 .meta와 GUID가 그대로다. 클립·프리팹 참조가 안 끊긴다.
        $result.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "  적용      : 덮어썼다 → $path"
    } else {
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($name)
        $previewPath = Join-Path $PreviewDir ($baseName + "-floating.png")
        $markedPath = Join-Path $PreviewDir ($baseName + "-floating-marked.png")
        $result.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)

        $markedBitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $markedData = $markedBitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                                             [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        [System.Runtime.InteropServices.Marshal]::Copy($marked, 0, $markedData.Scan0, $marked.Length)
        $markedBitmap.UnlockBits($markedData)
        $markedBitmap.Save($markedPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $markedBitmap.Dispose()

        Write-Output "  미리보기  : $previewPath"
        Write-Output "  표시본    : $markedPath (자홍색이 지워질 자리다)"
        Write-Output "  원본은 안 건드렸다. 확인 후 -Apply 를 붙여 다시 실행해라."
    }

    $result.Dispose()
}
