<#
    RemoveChromaKeyGreen.ps1
    ------------------------------------------------------------------
    캐릭터 스프라이트 시트의 크로마키 초록 배경을 투명으로 바꾼다.

    왜 필요한가:
    생성한 시트 중 일부는 알파 채널 없이 배경이 형광 초록(#00FF00 계열)으로 채워져 나온다.
    ash-king-phase2-ultimate.png가 그렇다. 그대로 슬라이스하면 캐릭터마다 초록 사각형이
    같이 잘려서, 게임에서는 보스 뒤에 초록 판이 따라다닌다.

    PrepareBossRoomSprite.ps1과 따로 둔 이유:
    저쪽은 보스 방 배경 한 장 전용이다. 방 크기와 안쪽 바닥 범위를 재서 출력하는데,
    그건 콜라이더 배치에 쓰는 값이라 캐릭터 시트에는 뜻이 없다. 판정 규칙만 같고
    하는 일이 다르므로 파일을 나눴다.

    무엇을 하는가:
      1. 초록으로 판정된 픽셀의 알파를 0으로
      2. 남은 픽셀의 초록 번짐(spill)을 깎는다 — 안 하면 실루엣에 초록 실선이 남는다
      3. Apply 모드에서 원본을 Raw/PreChromaKey 에 백업하고 덮어쓴다

    <b>발 라인 정렬은 하지 않는다.</b> 그건 NormalizeBossSheets.ps1의 일이고,
    그 도구는 알파로 발을 찾으므로 반드시 이 도구를 먼저 돌려야 한다.

    사용법:
      미리보기: powershell -File Tools\RemoveChromaKeyGreen.ps1
      실제 적용: powershell -File Tools\RemoveChromaKeyGreen.ps1 -Apply
#>
param(
    # 붙이면 원본 PNG를 덮어쓴다. 없으면 측정과 미리보기만 한다.
    [switch]$Apply,

    # 처리할 시트. 여러 장을 넘길 수 있다.
    [string[]]$SheetPaths = @(
        "Assets\Project\Art\Characters\Boss\AshKing\ash-king-phase2-ultimate.png"
    ),

    # 초록 판정 기준. G가 R/B보다 이 배수 이상 크면 배경으로 본다.
    # 단순 색 일치로 안 하는 이유: 생성 이미지라 배경에 압축 노이즈가 섞여 있어서
    # #00FF00과 몇 단계 어긋난 픽셀이 테두리에 남는다.
    [double]$GreenRatio = 1.35,

    # 배경으로 인정할 최소 G값. 어두운 초록(캐릭터 그림자에 섞인 것)을 지키기 위한 하한이다.
    [int]$GreenFloor = 90,

    [string]$PreviewPath = "$env:TEMP\chroma_key_preview.png"
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

foreach ($relative in $SheetPaths) {
    $path = if ([System.IO.Path]::IsPathRooted($relative)) { $relative }
            else { [System.IO.Path]::GetFullPath((Join-Path $root $relative)) }

    if (-not (Test-Path $path)) {
        Write-Output "$relative : 파일 없음"
        continue
    }

    $name = [System.IO.Path]::GetFileName($path)

    $source = New-Object System.Drawing.Bitmap $path
    $width = $source.Width
    $height = $source.Height

    # 픽셀을 한 번에 읽는다. GetPixel을 39만 번 부르면 몇 분씩 걸린다.
    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $data = $source.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                             [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $stride = $data.Stride
    $source.UnlockBits($data)
    $source.Dispose()

    $keyed = 0
    $despilled = 0

    for ($y = 0; $y -lt $height; $y++) {
        $base = $y * $stride
        for ($x = 0; $x -lt $width; $x++) {
            $i = $base + $x * 4
            $b = [int]$bytes[$i]; $g = [int]$bytes[$i + 1]; $r = [int]$bytes[$i + 2]

            if ($g -gt $GreenFloor -and $g -gt ($r * $GreenRatio) -and $g -gt ($b * $GreenRatio)) {
                $bytes[$i + 3] = 0
                $keyed++
                continue
            }

            # 실루엣 가장자리에 남는 초록기를 깎는다. G를 R/B 최대치까지만 내리므로
            # 원래 초록기가 없던 픽셀은 값이 그대로다.
            $maxRB = [Math]::Max($r, $b)
            if ($g -gt $maxRB) {
                $bytes[$i + 1] = [byte]$maxRB
                $despilled++
            }

            # 배경이 아닌 픽셀은 확실히 불투명하게 만든다. 알파 채널이 아예 없던 시트는
            # 32bpp로 읽을 때 255로 오지만, 반투명이 섞여 들어온 경우 슬라이스 결과가 흐려진다.
            $bytes[$i + 3] = 255
        }
    }

    $total = $width * $height
    Write-Output "$name"
    Write-Output ("  크기      : {0}x{1}" -f $width, $height)
    Write-Output ("  초록 제거 : {0} 픽셀 ({1}%)" -f $keyed, [math]::Round($keyed * 100.0 / $total, 1))
    Write-Output ("  번짐 정리 : {0} 픽셀" -f $despilled)

    $result = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $outData = $result.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                                [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $outData.Scan0, $bytes.Length)
    $result.UnlockBits($outData)

    if (-not $Apply) {
        # 미리보기는 투명이 눈에 보이도록 자홍색 위에 겹쳐 그린다.
        $preview = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($preview)
        $graphics.Clear([System.Drawing.Color]::Magenta)
        $graphics.DrawImage($result, 0, 0)
        $graphics.Dispose()
        $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $preview.Dispose()

        Write-Output "  미리보기  : $PreviewPath (자홍색이 투명이 될 자리다)"
        Write-Output "  적용하려면 -Apply 를 붙여라."
    }
    else {
        $backupDir = Join-Path ([System.IO.Path]::GetDirectoryName($path)) "Raw\PreChromaKey"
        if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir | Out-Null }

        $backupPath = Join-Path $backupDir $name

        # 백업이 이미 있으면 덮어쓰지 않는다. 두 번째 실행이 <b>이미 처리된 그림</b>을
        # 원본 자리에 얹으면 진짜 원본을 되찾을 방법이 없어진다.
        if (Test-Path $backupPath) {
            Write-Output "  백업      : 이미 있음 → 그대로 둔다 ($backupPath)"
        }
        else {
            Copy-Item $path $backupPath
            Write-Output "  백업      : $backupPath"
        }

        $result.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "  적용      : 덮어썼다 → $path"
    }

    $result.Dispose()
}
