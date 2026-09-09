<#
    FixWalkCycleSheet.ps1
    ------------------------------------------------------------------
    걷기 6프레임 시트의 두 가지 문제를 후처리로 고친다. 그림을 다시 뽑지 않는다.

    왜 필요한가:
    생성으로 받은 걷기 시트는 규격은 완벽했지만(발 기준선 241, 높이 190, 셀 경계 침범 0)
    보고 있으면 미끄러지는 것처럼 보였다. 원인을 재보니 둘이었다.

      1. 상체가 6프레임 전부 <b>픽셀 단위로 완전히 동일</b>했다.
         정체성을 지키려고 상체를 공유시킨 후처리의 결과다. 정체성은 100% 보장되지만
         걷는 동안 몸이 한 프레임도 안 움직인다. 필요한 것은 "형태 동일"이지 "픽셀 동일"이 아니다.

      2. 다리의 좌우 배분이 대칭이 아니었다.
         왼/오른쪽 다리 픽셀 수의 차이가 f0..f5에서 +647, +282, -107, -364, <b>+567</b>, -79 로
         f4에서 되돌아간다. 대칭이면 -282여야 할 자리다. 그래서 절뚝이는 것처럼 읽혔다.

    무엇을 하는가:

      [미러] 뒤쪽 세 프레임의 <b>다리만</b> 앞쪽 세 프레임의 좌우 반전으로 바꾼다.
             f3←f0, f4←f1, f5←f2. 완전한 2박자 보행이 된다.

             왜 다리만인가: 망토 밑단이 비대칭이라 통째로 뒤집으면 망토가 반대로 뒤집힌다.
             그래서 망토가 끝나는 행(LegSplitRow) 아래만 건드린다. 그 행은 색으로 재서 정했다 —
             y210부터 갈색(부츠)이 오르고 회색(망토)이 떨어진다.

             왜 셀 한가운데(128)가 아니라 몸통 중심으로 뒤집는가: 캐릭터가 셀 정중앙에 있지
             않다(중심 x≈122). 128 기준으로 뒤집으면 다리가 몸통에서 12px 어긋나 붙는다.

      [바운스] 프레임마다 상체를 1~2px 위로 올린다. 걸을 때의 상하 흔들림이다.

             왜 전체가 아니라 상체만인가: 전체를 올리면 발바닥이 241에서 벗어난다.
             이 프로젝트는 스프라이트 피벗이 <b>셀 한가운데로 고정</b>(alignment: 0 = Center)이라
             캐릭터가 셀 안 어디에 그려지느냐가 곧 화면에서 서는 높이다. 발은 못 움직인다.

             이음매 처리: 올리면 허리 바로 위에 빈 줄이 생긴다. 허리 행을 그만큼 복제해 메운다.
             그 자리는 망토가 덮고 있어 한두 줄 눌린 것은 눈에 안 띈다.

    사용법:
      미리보기(원본 안 건드림): powershell -File Tools\FixWalkCycleSheet.ps1
      실제 적용:                powershell -File Tools\FixWalkCycleSheet.ps1 -Apply
#>
param(
    # 붙이면 원본을 덮어쓴다. 없으면 옆에 -fixed.png 미리보기만 만든다.
    [switch]$Apply,

    [string]$SheetPath = "_ArtReview\20260909-north-walk6-v1\player_walk_n_6frames_1536x256.png",

    # 셀 한 변(px). 이 프로젝트의 플레이어 시트는 전부 256이다.
    [int]$Cell = 256,

    # 이 행부터 아래가 "다리"다. 색으로 재서 정한 값 — y210부터 갈색이 오르고 회색이 떨어진다.
    # 조금 아래로 잡은 이유: 경계에 망토 자락이 걸치면 반전 때 자락이 뒤집혀 티가 난다.
    [int]$LegSplitRow = 212,

    # 이 행보다 위가 "상체"다. 바운스는 여기까지만 적용한다.
    [int]$WaistRow = 168,

    # 프레임별 상체를 위로 올릴 픽셀 수(f0..f5).
    #
    # 접지 픽셀이 최대인 프레임(재보면 f1과 f4)에서 0으로 둔다 — 발이 닿는 순간 몸이 가장
    # 낮아져야 무게가 실린다. 그 사이 두 프레임은 2 → 1 로 올라갔다 내려온다.
    #
    # <b>주기가 3이어야 한다.</b> (f1,f2,f3)=(0,2,1)과 (f4,f5,f0)=(0,2,1)이 같은 모양이라
    # 두 걸음이 똑같이 보인다. 주기가 안 맞으면 다리는 대칭인데 몸만 절뚝인다.
    [string]$BobPattern = "1,0,2,1,0,2",

    # 다리를 뒤집을 때의 좌우 대칭축(px). 0이면 아래에서 자동으로 잰다.
    [int]$MirrorAxis = 0
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sheet = if ([System.IO.Path]::IsPathRooted($SheetPath)) { $SheetPath } else { Join-Path $root $SheetPath }

if (-not (Test-Path $sheet)) {
    Write-Error "시트를 못 찾았다: $sheet"
    return
}

# Bitmap::FromFile은 파일 핸들을 붙들고 있어서 같은 경로로 Save하면 GDI+ 오류가 난다.
# 복사본을 떠서 원본 핸들을 즉시 놓는다. (다른 도구들과 같은 방식)
$tmp = [System.Drawing.Image]::FromFile($sheet)
$bmp = New-Object System.Drawing.Bitmap $tmp
$tmp.Dispose()

$w = $bmp.Width
$h = $bmp.Height
$frames = [int]($w / $Cell)

Write-Host "시트: $w x $h, 프레임 $frames개, 셀 $Cell"

if ($frames -lt 6) {
    Write-Error "프레임이 6개 미만이다($frames). 이 도구는 6프레임 걷기 시트를 전제로 한다."
    $bmp.Dispose()
    return
}

# 전체 픽셀을 한 번에 꺼낸다. 픽셀 단위 GetPixel/SetPixel은 이 크기에서 수십 초가 걸린다.
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$bytes = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

# 픽셀 하나의 시작 바이트 위치. 포맷은 BGRA 순서다.
function Get-Offset([int]$x, [int]$y) { return ($y * $stride) + ($x * 4) }

# 이 픽셀이 주황 잉걸(=검)인가.
#
# 실측한 칼날 색이 (255,181,4) (243,97,0) (200,46,11) 처럼 R이 압도적이다.
# 회색·숯색은 R G B가 서로 가까워서 이 검사에 안 걸린다.
function Test-Ember([byte[]]$Buf, [int]$Offset) {
    if ($Buf[$Offset + 3] -le 16) { return $false }   # 투명
    $b = [int]$Buf[$Offset + 0]
    $g = [int]$Buf[$Offset + 1]
    $r = [int]$Buf[$Offset + 2]
    return ($r -gt ($g + 35)) -and ($r -gt ($b + 55)) -and ($r -gt 110)
}

# --- 좌우 대칭축 구하기 ---
#
# <b>상체의 bbox 중심을 쓰면 안 된다.</b> 등에 멘 검과 흘러내린 머리카락이 한쪽으로만
# 뻗어 있어서 bbox 한가운데가 몸의 축과 다르다. 이 시트에서는 상체 bbox 중심이 134인데
# 실제 다리 축은 119였다 — 그걸로 뒤집으면 다리가 30px 어긋나 붙는다.
#
# 대신 <b>모든 프레임의 다리 무게중심을 평균</b>낸다. 걷기는 한 주기 동안 왼발과 오른발이
# 같은 만큼 나가므로, 평균이 곧 몸의 축이다. 그림이 아니라 동작에서 축을 얻는 방식이라
# 다른 방향·다른 캐릭터에도 그대로 통한다.
if ($MirrorAxis -gt 0) {
    $bodyCenter = $MirrorAxis
    Write-Host "대칭축: $bodyCenter (사람이 지정한 값)"
} else {
    $sum = 0.0
    $count = 0
    for ($i = 0; $i -lt 6; $i++) {
        for ($y = $LegSplitRow; $y -lt $Cell; $y++) {
            for ($x = 0; $x -lt $Cell; $x++) {
                $off = Get-Offset (($i * $Cell) + $x) $y

                # 잉걸(=검)은 빼고 센다. 검은 한쪽으로만 뻗어 있어서 같이 세면 축이
                # 그쪽으로 끌려간다. 실제로 이 시트에서 119여야 할 축이 110 근처로 밀렸다.
                if ($bytes[$off + 3] -gt 16 -and -not (Test-Ember $bytes $off)) {
                    $sum += $x
                    $count++
                }
            }
        }
    }

    if ($count -eq 0) {
        Write-Error "다리 영역(y$LegSplitRow 아래)에 픽셀이 없다. LegSplitRow를 확인해라."
        $bmp.UnlockBits($data); $bmp.Dispose(); return
    }

    $bodyCenter = [int][math]::Round($sum / $count)
    Write-Host "대칭축: $bodyCenter (다리 무게중심 $count px의 평균, 셀 한가운데는 $($Cell/2))"
}

# --- 1) 다리 미러: f3<-f0, f4<-f1, f5<-f2 ---
# 읽는 곳(앞 세 프레임)과 쓰는 곳(뒤 세 프레임)이 겹치지 않아 임시 버퍼가 필요 없다.
$mirrored = 0
for ($i = 3; $i -lt 6; $i++) {
    $srcFrame = $i - 3
    for ($y = $LegSplitRow; $y -lt $Cell; $y++) {
        for ($x = 0; $x -lt $Cell; $x++) {
            $mx = (2 * $bodyCenter) - $x
            $dst = Get-Offset (($i * $Cell) + $x) $y

            if ($mx -lt 0 -or $mx -ge $Cell) {
                # 반전하면 셀 밖에서 오는 자리다. 투명으로 비운다.
                $bytes[$dst + 0] = 0; $bytes[$dst + 1] = 0
                $bytes[$dst + 2] = 0; $bytes[$dst + 3] = 0
                continue
            }

            # <b>검은 뒤집으면 안 된다.</b> 검은 상체의 일부이고 상체는 6프레임이 같은 그림인데,
            # 칼끝이 y212 아래까지 뻗어 있어 다리 영역에 걸친다. 그대로 뒤집으면 칼끝이
            # 몸 반대편으로 날아가 <b>허공에 뜬 주황 조각</b>이 된다(실제로 그렇게 나왔다).
            #
            # 색으로 가른다. 이 프로젝트는 팔레트가 "회색·숯색 바탕에 주황 잉걸만 강조"라
            # 주황이면 잉걸, 곧 검이다. 다리와 망토는 회색 계열이라 절대 안 걸린다.
            $dstIsEmber = (Test-Ember $bytes $dst)
            if ($dstIsEmber) { continue }   # 원래 있던 검은 그대로 둔다

            $src = Get-Offset (($srcFrame * $Cell) + $mx) $y

            if (Test-Ember $bytes $src) {
                # 뒤집혀 온 것이 검이면 칠하지 않고 비운다. 그 자리의 진짜 내용(검에 가려져
                # 있던 다리)은 원본에 없어서 복원할 수 없다. 몇 픽셀 비는 것이 허공에 뜬
                # 주황 조각보다 낫다 — 후자는 한눈에 고장으로 보인다.
                $bytes[$dst + 0] = 0; $bytes[$dst + 1] = 0
                $bytes[$dst + 2] = 0; $bytes[$dst + 3] = 0
                continue
            }

            $bytes[$dst + 0] = $bytes[$src + 0]
            $bytes[$dst + 1] = $bytes[$src + 1]
            $bytes[$dst + 2] = $bytes[$src + 2]
            $bytes[$dst + 3] = $bytes[$src + 3]
        }
    }
    $mirrored++
}
Write-Host "다리 미러: 프레임 $mirrored개 (f3<-f0, f4<-f1, f5<-f2), 기준선 y$LegSplitRow 아래"

# --- 2) 상체 바운스 ---
# 위에서부터 훑으면서 dst[y] = src[y + bob] 로 당긴다. 아래 행은 아직 안 고쳐진 상태라
# 임시 버퍼 없이 제자리에서 안전하다. 허리 행에서 멈춰 그 행을 복제해 이음매를 메운다.
$bobs = $BobPattern.Split(",") | ForEach-Object { [int]$_.Trim() }
if ($bobs.Count -lt $frames) {
    Write-Error "BobPattern의 값이 프레임 수보다 적다."
    $bmp.UnlockBits($data); $bmp.Dispose(); return
}

for ($i = 0; $i -lt 6; $i++) {
    $bob = $bobs[$i]
    if ($bob -le 0) { continue }

    for ($y = 0; $y -lt $WaistRow; $y++) {
        $from = [math]::Min($y + $bob, $WaistRow - 1)
        for ($x = 0; $x -lt $Cell; $x++) {
            $dst = Get-Offset (($i * $Cell) + $x) $y
            $src = Get-Offset (($i * $Cell) + $x) $from
            $bytes[$dst + 0] = $bytes[$src + 0]
            $bytes[$dst + 1] = $bytes[$src + 1]
            $bytes[$dst + 2] = $bytes[$src + 2]
            $bytes[$dst + 3] = $bytes[$src + 3]
        }
    }
}
Write-Host "상체 바운스: $BobPattern (위로 올린 픽셀 수, 허리 y$WaistRow 위만)"

[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
$bmp.UnlockBits($data)

if ($Apply) {
    # 다른 도구들과 같은 규칙 — 원본은 Raw\ 아래에 남긴다.
    $dir = Split-Path -Parent $sheet
    $backupDir = Join-Path $dir "Raw\PreWalkFix"
    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Force -Path $backupDir | Out-Null }

    $backup = Join-Path $backupDir ([System.IO.Path]::GetFileName($sheet))
    if (-not (Test-Path $backup)) {
        Copy-Item -LiteralPath $sheet -Destination $backup
        Write-Host "원본 백업: $backup"
    } else {
        Write-Host "백업이 이미 있어 덮어쓰지 않았다: $backup"
    }

    $bmp.Save($sheet, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "적용 완료: $sheet"
} else {
    $preview = [System.IO.Path]::ChangeExtension($sheet, $null) + "-fixed.png"
    $bmp.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "미리보기: $preview"
    Write-Host "원본은 안 건드렸다. 확인 후 -Apply 를 붙여 다시 실행해라."
}

$bmp.Dispose()
