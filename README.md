# 재의 길 (Path of Ash)

> 타고 남은 것은 길뿐이다.

`재의 길`은 영구 성장 없이 한 판의 아이템 시너지와 전투 숙련으로 진행하는 2D 탑다운 던전 슬래셔 로그라이크입니다.

## 핵심 방향

- 죽으면 언락과 영구 강화 없이 처음부터 다시 시작합니다.
- 반복 동기는 유물 조합과 짧고 명확한 전투 판단에서 만듭니다.
- 잿빛·숯색을 기본으로 사용하고 주황색 잉걸만 강조색으로 씁니다.
- 픽셀아트는 작은 화면에서도 실루엣과 공격 예비동작이 먼저 읽혀야 합니다.

## 현재 구현 상태

- 타이틀 → 게임 → 결과 → 재시작 런 흐름
- 플레이어 이동, 대시, 기본 공격과 Q/W/E/R 스킬
- 공용 체력·피격·넉백 시스템과 잿불 망령 상태 머신
- 방 전멸 → 보상 상자 → 문 개방 → 다음 방 진행
- 유물 14종, 보관함과 장착 슬롯 3개, 장착 유물만 능력치 적용
- 재의 왕 2페이즈 보스, 패턴 선택과 체력 50% 페이즈 전환
- 1·2페이즈 보스 애니메이션 컨트롤러와 6프레임 스프라이트 시트
- 체력·스태미나·스킬·유물 인벤토리 UI

세부 구현 상태와 다음 작업은 [프로젝트 컨텍스트](Docs/PROJECT_CONTEXT.md)를 기준으로 확인합니다.

## 조작

- 이동: 방향키
- 대시: `Shift`
- 기본 공격: `Ctrl`
- 스킬: `Q`, `W`, `E`, `R`
- 상호작용: `F`
- 인벤토리: `I` 또는 `Tab`

## 개발 환경

- Unity `6000.3.14f1`
- Universal Render Pipeline 2D Renderer
- New Input System
- 기준 해상도 `640×360`
- 캐릭터 기준 PPU `32`

## 주요 구조

- `Assets/Project/Scripts/Core` — 런 수명과 씬 흐름
- `Assets/Project/Scripts/Combat` — 체력, 피해, 히트박스
- `Assets/Project/Scripts/Skills` — ScriptableObject 기반 스킬
- `Assets/Project/Scripts/Dungeon` — 방 진행과 출구
- `Assets/Project/Scripts/Items` — 유물 데이터, 인스턴스, 인벤토리
- `Assets/Project/Scripts/Enemy` — 일반 적과 보스 상태 머신
- `Assets/Project/Scripts/UI` — HUD와 인벤토리 화면
- `Assets/Editor` — 임포트, 빌더, 슬라이스 자동화 도구

## 문서

- [PROJECT_CONTEXT.md](Docs/PROJECT_CONTEXT.md) — 현재 상태, 중요한 규칙, 다음 작업을 빠르게 파악하는 문서
- [DEVELOPMENT_LOG.md](Docs/DEVELOPMENT_LOG.md) — 날짜별 작업 과정과 설계 근거 전체 기록

README에는 프로젝트 소개와 현재 기능만 유지합니다. 긴 문제 해결 과정과 날짜별 기록은 개발 로그에 추가하고, 현재 사실이 바뀌면 프로젝트 컨텍스트를 갱신합니다.
