<#
    RecolorToEmberPalette.ps1
    ------------------------------------------------------------------
    새 플레이어 시트의 빨강을 프로젝트 팔레트(회색·숯색 바탕 + 주황 잉걸 강조)로 옮긴다.
    그림을 다시 뽑지 않는다.

    왜 필요한가:
    2026-09-10에 넣은 새 걷기·대기 시트는 불투명 픽셀의 56%가 채도 높은 진홍색이었다.
    옛 시트는 빨강 1~2%, 주황 10%, 나머지는 순수 회색이다. 회색·피부·머리카락까지 붉게
    떠 있어서(R-B 평균 +27, 옛 시트는 0) "살짝 붉은" 것이 아니라 팔레트 자체가 달랐다.
    "게임플레이 아트는 회색·숯색 바탕에 주황 잉걸만 강조색" 규칙과 정면으로 어긋난다.

    왜 다시 뽑지 않고 색만 바꾸는가:
    - 걷기 보정(FixWalkCycleSheet.ps1)까지 끝낸 프레임을 그대로 쓴다.
    - 같은 규칙을 앞으로 뽑을 시트에 걸면 모든 시트의 색이 똑같이 맞는다.
      생성은 뽑을 때마다 색이 조금씩 달라서, 10장을 같은 팔레트로 받을 방법이 없다.

    무엇을 하는가 (2026-09-11 시안 B):
      [천]   빨강 계열(색상 330~20도, 채도 > 0.25)은 채도를 0.35배로 줄이고 색상을 +12도
             주황 쪽으로 돌린다. 망토·옷이 숯색에 가까운 짙은 갈회색이 된다.
      [잉걸] 빨강 중 밝고 진한 픽셀(명도 > 0.62, 채도 > 0.45)은 색상만 24도(주황)로 바꾼다.
             칼날과 망토 가장자리가 옛 시트의 주황 잉걸이 된다. 명도·채도는 그대로라 빛이 산다.
      [무채] 나머지(머리카락·피부·금속)는 채도를 0.4배로 줄인다. 분홍빛 머리가 흰머리가 된다.
      [눈]   눈은 건드리지 않는다. 디자인의 붉은 눈이라 같이 바꾸면 주황 눈이 된다.

    눈을 찾는 방법:
      색만으로는 못 가른다. 눈, 칼날 잉걸, 후드 안감이 모두 "밝고 진한 빨강"이다.
      그래서 크기와 자리와 주변으로 가른다. 진한 빨강(채도·명도 0.6 이상) 덩어리 중에서
        - 작다 (2~20px, 평균 명도 0.74 이상)
        - 머리 윗줄(한 줄에 불투명 픽셀이 20개 이상인 첫 줄)에서 18~48행 아래에 있다 (얼굴 높이)
        - 둘레의 12% 이상이 밝은 무채색(피부·머리카락)이다
        - 2px 안에 어두운 픽셀(명도 0.3 미만)이 있다 (속눈썹·눈동자 테두리)
      인 것만 눈으로 본다. 후드 안감은 평균 명도가 0.66~0.73이라 첫 조건에서 걸러진다.
      2026-09-11 실측: 걷기·대기 96칸 전부에서 앞·대각선 2개, 옆 1개, 뒤 0개로
      프레임마다 일정하게 나왔다. 프레임마다 들쭉날쭉하면 눈 색이 깜빡이므로 그게 기준이다.

      수정(2026-09-11, 공격 시트) — 네 가지를 바꿨다. 걷기·대기 96칸 결과는 수정 전후로 한 픽셀도
      다르지 않고, 공격 5방향 1~5프레임에서 눈 개수가 전부 맞는다(앞·대각선 2, 옆 1, 뒤 0).
        1. 기준 줄: 맨 윗줄 → 머리 윗줄. 칼을 머리 위로 치켜든 프레임은 맨 윗줄이 칼끝이나 칼 쥔
           손이라(S 1프레임은 0행) 눈이 찾는 높이 밖으로 밀려났다. 칼과 손은 한 줄에 20px이 안 되고
           후드는 넘는다. 그 프레임만 눈이 주황이 되어 깜빡이는 것을 막는다
        2. 높이: 18~34 → 18~48. 웅크리거나 앞으로 숙인 프레임은 머리 윗줄과 눈 사이가 벌어진다
        3. 둘레 밝은 비율: 18% → 12%. S 1프레임 왼눈이 후드 그림자에 둘러싸여 14%였다.
           10%로 내리면 E 3프레임에서 오인이 생겨 12%로 잡았다
        4. 어두운 이웃 조건 추가. 높이를 넓히자 수평으로 뻗은 칼날 끝(x≈210)이 눈으로 잡혔다.
           눈 옆에는 속눈썹이 있고(8~25%) 칼날 끝 잉걸은 밝은 강철 위라 0%다

    두 번 걸면 안 된다:
      색 변환은 누적된다. 두 번 돌리면 채도가 0.35 x 0.35로 빠진다. 그래서 진한 빨강 비율이
      MinRedShare보다 낮으면 이미 처리된 시트로 보고 건너뛴다. 새로 뽑은 시트는 걸린다.

    순서:
      RemoveChromaKeyGreen.ps1 <b>뒤에</b> 돌린다. 이 도구는 초록을 무채색으로 보고 채도를 빼기
      때문에, 먼저 돌리면 초록 배경이 회녹색이 되어 크로마키가 안 먹는다.
      발 라인 정렬·걷기 보정과는 순서가 상관없다(모양을 안 건드린다).

    사용법:
      미리보기(원본 안 건드림): powershell -File Tools\RecolorToEmberPalette.ps1
      실제 적용:                powershell -File Tools\RecolorToEmberPalette.ps1 -Apply
      다른 시트:                powershell -File Tools\RecolorToEmberPalette.ps1 -SheetPaths "Assets\...\player_attack.png"
#>
param(
    # 붙이면 원본 PNG를 덮어쓴다. 없으면 측정과 미리보기만 한다.
    [switch]$Apply,

    # 이미 처리된 것처럼 보여도(빨강이 적어도) 강제로 처리한다. 빨강이 원래 적은 시트용.
    [switch]$Force,

    # 처리할 시트. 여러 장을 넘길 수 있다.
    [string[]]$SheetPaths = @(
        "Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_walk.png",
        "Assets\Project\Art\Sprites\Player\Topdown35\Production8Dir\player_idle.png"
    ),

    # 셀 한 변(px). 이 프로젝트의 플레이어 시트는 전부 256이다.
    [int]$Cell = 256,

    # [천] 빨강의 채도 배율. 0.35면 진홍 망토가 숯색에 가까운 갈회색이 된다.
    [double]$ClothSaturation = 0.35,

    # [천] 빨강의 색상을 주황 쪽으로 돌리는 각도. 남은 색기가 피가 아니라 잉걸로 읽히게 한다.
    [double]$ClothHueShift = 12,

    # [잉걸] 밝은 빨강이 옮겨갈 색상. 옛 시트의 칼날 균열이 22~30도라 그 가운데로 잡았다.
    [double]$GlowHue = 24,

    # [무채] 빨강이 아닌 픽셀의 채도 배율. 머리카락·피부에 낀 분홍기를 뺀다.
    [double]$NeutralSaturation = 0.40,

    # 눈을 찾을 높이. 캐릭터 맨 윗줄에서 몇 행 아래인지(시작,끝).
    # 수정(2026-09-11): 18,34 → 18,48. 칼을 머리 위로 치켜든 공격 프레임은 맨 윗줄이 칼 쥔
    # 손이라 눈이 39행 아래에 있었다. 넓힌 만큼 생기는 오인은 아래의 어두운 이웃 조건이 거른다.
    [string]$EyeBand = "18,48",

    # 불투명 픽셀 중 진한 빨강이 이 비율보다 적으면 이미 처리된 시트로 보고 건너뛴다.
    # 처리 전 56%, 처리 후 0.2%(눈만 남음)라 그 사이 어디든 되지만 여유 있게 잡았다.
    [double]$MinRedShare = 0.15,

    # 미리보기를 쓸 폴더. Assets 안에 쓰면 유니티가 그것까지 임포트하므로 밖에 둔다.
    [string]$PreviewDir = $env:TEMP
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

# 8방향 시트의 세로 순서. 이미지 맨 윗줄부터 S, SW, W, NW, N, NE, E, SE다.
# (AshPlayerDirectionalAnimationBuilder의 DirectionNames와 같은 순서)
$directionNames = @("S", "SW", "W", "NW", "N", "NE", "E", "SE")

$bandParts = $EyeBand.Split(",")
$eyeBandTop = [int]$bandParts[0].Trim()
$eyeBandBottom = [int]$bandParts[1].Trim()

foreach ($relative in $SheetPaths) {
    $path = if ([System.IO.Path]::IsPathRooted($relative)) { $relative }
            else { [System.IO.Path]::GetFullPath((Join-Path $root $relative)) }

    if (-not (Test-Path $path)) {
        Write-Output "$relative : 파일 없음"
        continue
    }

    $name = [System.IO.Path]::GetFileName($path)

    # 원본을 열어 바이트만 꺼내고 바로 닫는다. (RemoveChromaKeyGreen.ps1과 같은 방식)
    #
    # FromFile로 연 뒤 new Bitmap(image)로 복사하는 방식을 쓰지 않는 이유: GDI+가 그 복사를
    # "그리기"로 처리하면서 반투명 픽셀이 미리곱한 알파를 한 번 거쳐 RGB가 1~4단계씩 어긋난다.
    # 2026-09-11에 파이썬 기준 구현과 대조해 가장자리 950여 픽셀에서 확인했다.
    # 바이트만 옮기면 바꾸려는 색 말고는 원본과 한 비트도 안 달라진다.
    $source = New-Object System.Drawing.Bitmap $path
    $width = $source.Width
    $height = $source.Height

    # 픽셀을 한 번에 꺼낸다. GetPixel/SetPixel을 수백만 번 부르면 몇 분씩 걸린다.
    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $sourceData = $source.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                                   [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $sourceData.Stride
    $bytes = New-Object byte[] ($stride * $height)
    [System.Runtime.InteropServices.Marshal]::Copy($sourceData.Scan0, $bytes, 0, $bytes.Length)
    $source.UnlockBits($sourceData)
    $source.Dispose()   # 여기서 파일 핸들을 놓아야 나중에 같은 경로로 저장할 수 있다

    $count = $width * $height
    $hue = New-Object double[] $count     # 색상 0~360도
    $sat = New-Object double[] $count     # 채도 0~1
    $val = New-Object double[] $count     # 명도 0~1
    $alpha = New-Object byte[] $count

    # --- 1) 모든 픽셀의 색상·채도·명도를 한 번만 계산해 둔다 ---
    # 눈 찾기와 색 바꾸기가 같은 값을 두 번씩 읽으므로 미리 풀어두는 편이 빠르다.
    # 비율 통계(처리 전)도 여기서 같이 센다. 기준은 "채도 > 0.35, 명도 > 0.2"인 유채색 중
    # 색상 330~12도를 빨강, 12~45도를 주황으로 본다.
    $opaqueCount = 0
    $redBefore = 0
    $orangeBefore = 0
    for ($y = 0; $y -lt $height; $y++) {
        $rowBase = $y * $stride
        $pixBase = $y * $width
        for ($x = 0; $x -lt $width; $x++) {
            $o = $rowBase + ($x * 4)
            $a = $bytes[$o + 3]
            if ($a -eq 0) { continue }

            $p = $pixBase + $x
            $alpha[$p] = $a

            # 포맷이 BGRA 순서다.
            $b = $bytes[$o] / 255.0
            $g = $bytes[$o + 1] / 255.0
            $r = $bytes[$o + 2] / 255.0

            $max = [Math]::Max($r, [Math]::Max($g, $b))
            $min = [Math]::Min($r, [Math]::Min($g, $b))
            $d = $max - $min

            $hh = 0.0
            if ($d -gt 0) {
                if ($max -eq $r)     { $hh = 60.0 * (($g - $b) / $d) }
                elseif ($max -eq $g) { $hh = 60.0 * (($b - $r) / $d) + 120.0 }
                else                 { $hh = 60.0 * (($r - $g) / $d) + 240.0 }
                if ($hh -lt 0) { $hh += 360.0 }
            }
            $ss = 0.0
            if ($max -gt 0) { $ss = $d / $max }

            $hue[$p] = $hh
            $sat[$p] = $ss
            $val[$p] = $max

            if ($a -gt 16) {
                $opaqueCount++
                if ($ss -gt 0.35 -and $max -gt 0.20) {
                    if ($hh -ge 330 -or $hh -lt 12) { $redBefore++ }
                    elseif ($hh -lt 45) { $orangeBefore++ }
                }
            }
        }
    }

    if ($opaqueCount -eq 0) {
        Write-Output "$name : 불투명 픽셀이 없다. 건너뛴다."
        continue
    }

    $redShare = $redBefore / $opaqueCount
    Write-Output "$name"
    Write-Output ("  크기      : {0}x{1}, 셀 {2}" -f $width, $height, $Cell)
    Write-Output ("  처리 전   : 빨강 {0:P1} / 주황 {1:P1}" -f $redShare, ($orangeBefore / $opaqueCount))

    # 두 번 걸면 채도가 누적으로 빠진다. 빨강이 거의 없으면 이미 처리된 시트다.
    if ($redShare -lt $MinRedShare -and -not $Force) {
        Write-Output ("  건너뜀    : 빨강이 {0:P1}뿐이라 이미 처리된 시트로 본다. 그래도 하려면 -Force." -f $redShare)
        continue
    }

    # --- 2) 눈 찾기: 셀마다 따로 ---
    # 셀 단위인 이유: "캐릭터 맨 윗줄"이 셀마다 다르다(걷기 바운스로 1~2px씩 오르내린다).
    $protect = New-Object bool[] $count   # 참이면 색을 안 바꾼다
    $label = New-Object int[] $count      # 진한 빨강 덩어리 번호. 0이면 아직 안 봤다
    $ringMark = New-Object int[] $count   # 둘레를 셀 때 같은 픽셀을 두 번 세지 않으려는 표시
    $componentId = 0
    $stack = New-Object 'System.Collections.Generic.Stack[int]'
    $members = New-Object 'System.Collections.Generic.List[int]'

    $rows = [int][Math]::Floor($height / $Cell)
    $cols = [int][Math]::Floor($width / $Cell)

    # 칸별로 지킨 눈 픽셀 수. 인덱스는 (세로 칸 * 가로 칸 수 + 가로 칸)이다.
    $eyeTable = New-Object int[] ($rows * $cols)

    for ($cellRow = 0; $cellRow -lt $rows; $cellRow++) {
        for ($cellCol = 0; $cellCol -lt $cols; $cellCol++) {
            $x0 = $cellCol * $Cell
            $y0 = $cellRow * $Cell
            $x1 = $x0 + $Cell
            $y1 = $y0 + $Cell

            # 머리 윗줄. 반투명 가장자리(알파 16 이하)는 빼고 잰다.
            #
            # 수정(2026-09-11): "불투명 픽셀이 하나라도 있는 첫 줄" → "20개 이상인 첫 줄".
            # 칼을 머리 위로 치켜든 공격 프레임은 첫 줄이 칼끝이나 칼 쥔 손이라 눈을 찾는 높이가
            # 통째로 위로 밀렸다. 칼날과 손은 한 줄에 20px이 안 되고 후드는 넘는다.
            $top = -1
            for ($yy = $y0; $yy -lt $y1 -and $top -lt 0; $yy++) {
                $pixBase = $yy * $width
                $rowCount = 0
                for ($xx = $x0; $xx -lt $x1; $xx++) {
                    if ($alpha[$pixBase + $xx] -gt 16) { $rowCount++ }
                }
                if ($rowCount -ge 20) { $top = $yy }
            }
            if ($top -lt 0) { continue }   # 빈 칸(또는 머리가 없는 칸)

            # 덩어리를 찾기 시작할 행 범위. 눈은 20px 이하라 얼굴 높이에서 20행만 벗어나도
            # 평균 높이가 그 안에 들 수 없다. 그 밖에서 시작하는 덩어리는 볼 필요가 없다.
            $scanTop = [Math]::Max($y0, $top + $eyeBandTop - 20)
            $scanBottom = [Math]::Min($y1, $top + $eyeBandBottom + 21)

            for ($sy = $scanTop; $sy -lt $scanBottom; $sy++) {
                for ($sx = $x0; $sx -lt $x1; $sx++) {
                    $start = ($sy * $width) + $sx
                    if ($label[$start] -ne 0) { continue }

                    # 진한 빨강(눈 후보): 불투명, 색상 340~15도, 채도·명도 0.6 이상
                    $sh = $hue[$start]
                    if ($alpha[$start] -le 16 -or $sat[$start] -lt 0.60 -or $val[$start] -lt 0.60) { continue }
                    if ($sh -lt 340 -and $sh -ge 15) { continue }

                    # 8방향으로 이어진 덩어리를 모은다. 칸 밖으로는 안 나간다(옆 프레임 침범 방지).
                    $componentId++
                    $members.Clear()
                    $label[$start] = $componentId
                    $stack.Push($start)
                    while ($stack.Count -gt 0) {
                        $q = $stack.Pop()
                        $members.Add($q)
                        $qy = [int][Math]::Floor($q / $width)
                        $qx = $q - ($qy * $width)
                        for ($dy = -1; $dy -le 1; $dy++) {
                            $ny = $qy + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -1; $dx -le 1; $dx++) {
                                $nx = $qx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                $n = ($ny * $width) + $nx
                                if ($label[$n] -ne 0 -or $alpha[$n] -le 16) { continue }
                                if ($sat[$n] -lt 0.60 -or $val[$n] -lt 0.60) { continue }
                                $nh = $hue[$n]
                                if ($nh -lt 340 -and $nh -ge 15) { continue }
                                $label[$n] = $componentId
                                $stack.Push($n)
                            }
                        }
                    }

                    # 크기: 눈은 2~20px. 칼날 잉걸과 망토는 길게 이어져 훨씬 크다.
                    $size = $members.Count
                    if ($size -lt 2 -or $size -gt 20) { continue }

                    $sumV = 0.0
                    $sumY = 0.0
                    foreach ($m in $members) {
                        $sumV += $val[$m]
                        $sumY += [Math]::Floor($m / $width)
                    }

                    # 밝기: 눈동자는 하이라이트가 있어 평균 명도가 0.77 이상이다.
                    if (($sumV / $size) -lt 0.74) { continue }

                    # 자리: 캐릭터 맨 윗줄 기준 얼굴 높이에 있어야 한다.
                    $relativeRow = ($sumY / $size) - $top
                    if ($relativeRow -lt $eyeBandTop -or $relativeRow -gt $eyeBandBottom) { continue }

                    # 주변: 둘레(8방향 이웃 중 진한 빨강이 아닌 불투명 픽셀)의 몇 %가
                    # 밝은 무채색(채도 0.35 이하, 명도 0.8 이상 = 피부·머리카락)인가.
                    # 눈은 위가 속눈썹(어둡다)이고 아래가 피부라 20~50%, 후드 안감은 0~14%다.
                    # 수정(2026-09-11): 0.18 → 0.12. 후드 그림자에 둘러싸인 눈(S 공격 1프레임)이 14%였다.
                    # 안감은 평균 명도 조건(0.74)에서 이미 걸러지므로 여기를 낮춰도 안감이 안 잡힌다.
                    $ringTotal = 0
                    $ringPale = 0
                    foreach ($m in $members) {
                        $my = [int][Math]::Floor($m / $width)
                        $mx = $m - ($my * $width)
                        for ($dy = -1; $dy -le 1; $dy++) {
                            $ny = $my + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -1; $dx -le 1; $dx++) {
                                $nx = $mx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                $n = ($ny * $width) + $nx
                                if ($label[$n] -eq $componentId -or $ringMark[$n] -eq $componentId) { continue }
                                if ($alpha[$n] -le 16) { continue }
                                $nh = $hue[$n]
                                $isCore = ($sat[$n] -ge 0.60) -and ($val[$n] -ge 0.60) -and (($nh -ge 340) -or ($nh -lt 15))
                                if ($isCore) { continue }
                                $ringMark[$n] = $componentId
                                $ringTotal++
                                if ($sat[$n] -le 0.35 -and $val[$n] -ge 0.80) { $ringPale++ }
                            }
                        }
                    }
                    if ($ringTotal -eq 0 -or ($ringPale / $ringTotal) -lt 0.12) { continue }

                    # 추가 생성 — 2px 안에 어두운 픽셀(명도 0.3 미만)이 하나라도 있어야 한다.
                    # 눈은 바로 옆에 속눈썹·눈동자 테두리가 있다(실측 8~25%). 수평으로 뻗은 칼날 끝의
                    # 잉걸은 밝은 강철 위라 하나도 없다(0%). 찾는 높이를 48행까지 넓히면서 칼날 끝이
                    # 눈으로 잡히던 것을 이 조건이 거른다.
                    $hasDark = $false
                    foreach ($m in $members) {
                        $my = [int][Math]::Floor($m / $width)
                        $mx = $m - ($my * $width)
                        for ($dy = -2; $dy -le 2 -and -not $hasDark; $dy++) {
                            $ny = $my + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -2; $dx -le 2; $dx++) {
                                $nx = $mx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                $n = ($ny * $width) + $nx
                                if ($label[$n] -eq $componentId -or $alpha[$n] -le 16) { continue }
                                if ($val[$n] -lt 0.30) { $hasDark = $true; break }
                            }
                        }
                        if ($hasDark) { break }
                    }
                    if (-not $hasDark) { continue }

                    # 눈으로 확정. 눈동자 가장자리의 덜 진한 빨강(채도 0.35 이상)까지 1px 넓혀
                    # 지킨다. 안 그러면 가장자리만 갈색이 되어 눈이 작고 탁해 보인다.
                    $tableIndex = ($cellRow * $cols) + $cellCol
                    foreach ($m in $members) {
                        if (-not $protect[$m]) { $protect[$m] = $true; $eyeTable[$tableIndex]++ }
                        $my = [int][Math]::Floor($m / $width)
                        $mx = $m - ($my * $width)
                        for ($dy = -1; $dy -le 1; $dy++) {
                            $ny = $my + $dy
                            if ($ny -lt $y0 -or $ny -ge $y1) { continue }
                            for ($dx = -1; $dx -le 1; $dx++) {
                                $nx = $mx + $dx
                                if ($nx -lt $x0 -or $nx -ge $x1) { continue }
                                $n = ($ny * $width) + $nx
                                if ($protect[$n] -or $alpha[$n] -le 16 -or $sat[$n] -lt 0.35) { continue }
                                $nh = $hue[$n]
                                if ($nh -lt 340 -and $nh -ge 15) { continue }
                                $protect[$n] = $true
                                $eyeTable[$tableIndex]++
                            }
                        }
                    }
                }
            }
        }
    }

    # --- 3) 색 바꾸기 ---
    $redAfter = 0
    $orangeAfter = 0
    $changed = 0
    for ($y = 0; $y -lt $height; $y++) {
        $rowBase = $y * $stride
        $pixBase = $y * $width
        for ($x = 0; $x -lt $width; $x++) {
            $p = $pixBase + $x
            $a = $alpha[$p]
            if ($a -eq 0) { continue }

            $hh = $hue[$p]
            $ss = $sat[$p]
            $vv = $val[$p]

            if (-not $protect[$p]) {
                $isRed = (($hh -ge 330) -or ($hh -lt 20)) -and ($ss -gt 0.25)
                if ($isRed) {
                    if ($vv -gt 0.62 -and $ss -gt 0.45) {
                        # [잉걸] 색상만 주황으로. 명도·채도를 두면 빛나는 느낌이 그대로 남는다.
                        $hh = $GlowHue
                    } else {
                        # [천] 색기를 빼고 주황 쪽으로 살짝 돌린다.
                        $hh = ($hh + $ClothHueShift) % 360.0
                        $ss = $ss * $ClothSaturation
                    }
                } else {
                    # [무채] 머리카락·피부·금속의 분홍기를 뺀다.
                    $ss = $ss * $NeutralSaturation
                }

                # 색상·채도·명도 -> RGB
                $c = $vv * $ss
                $hp = $hh / 60.0
                $sector = [Math]::Floor($hp)
                $f = $hp - (2.0 * [Math]::Floor($hp / 2.0))   # hp를 2로 나눈 나머지
                $xc = $c * (1.0 - [Math]::Abs($f - 1.0))
                $m0 = $vv - $c
                if     ($sector -lt 1) { $r1 = $c;   $g1 = $xc;  $b1 = 0.0 }
                elseif ($sector -lt 2) { $r1 = $xc;  $g1 = $c;   $b1 = 0.0 }
                elseif ($sector -lt 3) { $r1 = 0.0;  $g1 = $c;   $b1 = $xc }
                elseif ($sector -lt 4) { $r1 = 0.0;  $g1 = $xc;  $b1 = $c }
                elseif ($sector -lt 5) { $r1 = $xc;  $g1 = 0.0;  $b1 = $c }
                else                   { $r1 = $c;   $g1 = 0.0;  $b1 = $xc }

                $o = $rowBase + ($x * 4)
                $bytes[$o]     = [byte][Math]::Min(255, [Math]::Max(0, [Math]::Round(($b1 + $m0) * 255.0)))
                $bytes[$o + 1] = [byte][Math]::Min(255, [Math]::Max(0, [Math]::Round(($g1 + $m0) * 255.0)))
                $bytes[$o + 2] = [byte][Math]::Min(255, [Math]::Max(0, [Math]::Round(($r1 + $m0) * 255.0)))
                $changed++
            }

            if ($a -gt 16 -and $ss -gt 0.35 -and $vv -gt 0.20) {
                if ($hh -ge 330 -or $hh -lt 12) { $redAfter++ }
                elseif ($hh -lt 45) { $orangeAfter++ }
            }
        }
    }

    # 새 비트맵에 바이트를 그대로 붓는다. 그리기를 거치지 않으므로 알파가 원본과 같다.
    $result = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $resultData = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                                   [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $resultData.Scan0, $bytes.Length)
    $result.UnlockBits($resultData)

    Write-Output ("  처리 후   : 빨강 {0:P1} / 주황 {1:P1}  (바꾼 픽셀 {2})" -f ($redAfter / $opaqueCount), ($orangeAfter / $opaqueCount), $changed)

    # 눈 보호 표. 가로가 프레임, 세로가 방향이다. 앞·대각선·옆에서 프레임마다 값이 비슷해야 하고,
    # 뒤(NW N NE)는 0이 정상이다. 앞을 보는 칸에 0이 섞이면 그 프레임만 눈이 주황이 되어 깜빡인다.
    Write-Output "  눈 보호   : 칸별로 지킨 픽셀 수 (가로 = 프레임)"
    for ($cellRow = 0; $cellRow -lt $rows; $cellRow++) {
        $rowName = if ($rows -eq 8) { $directionNames[$cellRow] } else { "row$cellRow" }
        $cellsText = @()
        for ($cellCol = 0; $cellCol -lt $cols; $cellCol++) { $cellsText += ("{0,3}" -f $eyeTable[($cellRow * $cols) + $cellCol]) }
        Write-Output ("    {0,-5}: {1}" -f $rowName, ($cellsText -join " "))
    }

    if ($Apply) {
        # 다른 도구들과 같은 규칙 — 원본은 Raw\ 아래에 남긴다.
        $backupDir = Join-Path ([System.IO.Path]::GetDirectoryName($path)) "Raw\PreRecolor"
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
        $previewPath = Join-Path $PreviewDir ([System.IO.Path]::GetFileNameWithoutExtension($name) + "-recolor.png")
        $result.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "  미리보기  : $previewPath"
        Write-Output "  원본은 안 건드렸다. 확인 후 -Apply 를 붙여 다시 실행해라."
    }

    $result.Dispose()
}
