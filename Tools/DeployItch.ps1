<#
    DeployItch.ps1
    ------------------------------------------------------------------
    가장 최근 배포용 빌드 폴더(Builds\Windows\PathOfAsh_v*)를 itch.io에 올린다. (2026-10-05 추가 생성)

    왜 butler인가:
    itch.io 공식 업로드 도구다. 웹에서 zip을 손으로 올리는 것과 달리
      1. 바뀐 파일만 올린다(패치). 1.0.1을 올릴 때 수백 MB를 다시 보내지 않는다
      2. --userversion으로 버전 기록이 페이지에 남는다 — "플레이테스트 후 패치했다"는 근거
      3. itch 앱으로 받은 사람은 자동 업데이트된다
      4. 채널 이름에 windows가 들어가면 itch가 Windows 다운로드로 자동 표시한다

    빌드는 유니티 메뉴(Tools → 재의 길 → 배포 → Windows 빌드)로 먼저 만든다.
    이 스크립트는 빌드하지 않고 올리기만 한다 — 빌드와 업로드를 나누면 올리기 전에 빌드를 직접 실행해 볼 수 있다.

    처음 한 번:
      1. butler 설치: https://itch.io/docs/butler/installing.html (압축 풀고 PATH에 추가)
      2. butler login   (브라우저가 열리고 itch.io 계정으로 승인)

    사용법:
      미리보기(올리지 않음): powershell -File Tools\DeployItch.ps1 -Target 아이디/path-of-ash -DryRun
      올리기:               powershell -File Tools\DeployItch.ps1 -Target 아이디/path-of-ash
#>
param(
    # itch.io 계정/게임 주소. 예: gaksultang/path-of-ash  (게임 페이지 주소 https://계정.itch.io/게임 에서 따온다)
    [Parameter(Mandatory = $true)]
    [string]$Target,

    # 채널. 이름에 windows가 들어가야 itch가 Windows용으로 자동 분류한다.
    [string]$Channel = "windows",

    # 붙이면 실제로 올리지 않고 무엇을 올릴지만 보여준다.
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# 이 스크립트는 Tools 폴더에 있으므로 프로젝트 루트는 그 부모다.
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $projectRoot "Builds\Windows"

if (-not (Test-Path $buildRoot)) {
    throw "빌드 폴더가 없습니다: $buildRoot`n유니티에서 Tools → 재의 길 → 배포 → Windows 빌드 (zip 포함)를 먼저 실행하세요."
}

# 가장 최근 배포용 빌드. 개발 빌드(_dev)는 프로파일러가 붙는 빌드라 절대 올리지 않는다.
$folder = Get-ChildItem $buildRoot -Directory -Filter "PathOfAsh_v*" |
    Where-Object { $_.Name -notlike "*_dev" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $folder) {
    throw "배포용 빌드 폴더(PathOfAsh_v*)가 없습니다. 유니티에서 먼저 빌드하세요."
}

# 폴더 이름의 버전(PathOfAsh_v1.0.0 → 1.0.0)을 itch.io 버전 기록에 그대로 쓴다. 버전의 주인은 Player Settings 한 곳이다.
$version = $folder.Name -replace '^PathOfAsh_v', ''

if (-not (Get-Command butler -ErrorAction SilentlyContinue)) {
    Write-Host "butler가 설치되어 있지 않거나 PATH에 없습니다." -ForegroundColor Red
    Write-Host "설치: https://itch.io/docs/butler/installing.html  → 설치 후 'butler login' 한 번"
    exit 1
}

Write-Host "올릴 폴더 : $($folder.FullName)"
Write-Host "대상      : ${Target}:${Channel}"
Write-Host "버전      : $version"

# $args는 PowerShell 예약 변수라 다른 이름을 쓴다.
$butlerArgs = @("push", $folder.FullName, "${Target}:${Channel}", "--userversion", $version)
if ($DryRun) { $butlerArgs += "--dry-run" }

& butler @butlerArgs
if ($LASTEXITCODE -ne 0) { throw "butler push 실패 (종료 코드 $LASTEXITCODE)" }

if (-not $DryRun) {
    # 업로드 후 itch 서버가 패치를 처리하는 데 몇 분 걸린다. status로 처리 상태를 확인한다.
    & butler status "${Target}:${Channel}"
    Write-Host "완료. 게임 페이지에서 다운로드가 보이는지, 받은 zip이 실행되는지 직접 확인하세요." -ForegroundColor Green
}
