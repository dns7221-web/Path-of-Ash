# 재의 왕 1페이즈 걷기 (2026-09-29)

`ash-king-walk-grid.png`는 내장 ImageGen으로 다시 생성한 투명 배경 3×2 원본이다.
기존 1페이즈 갑옷과 왕관을 참고하고 발 디딤·회수 자세를 새로 만들었다.
생성 및 수정 프롬프트는 `ash-king-walk.prompt.txt`에 기록했다.

프로젝트 루트의 PowerShell에서 다음 명령으로 게임용 시트를 다시 패킹한다.

```powershell
& .\Tools\PrepareBossWalkSheet.ps1 `
  -SourcePath 'Assets\Project\Art\Characters\Boss\AshKing\Raw\WalkCycle20260929\ash-king-walk-grid.png' `
  -OutputPath 'Assets\Project\Art\Characters\Boss\AshKing\ash-king-walk.png'
```

출력은 1536×256, 256×256 셀 6장이다. 생성된 알파를 보존하고 동일 배율로 줄여
기존 왕관 중심과 발 기준선(y=216)을 맞춘다. 기존 PNG의 `.meta`는 교체하지 않는다.
옛 원본을 사용하던 `AshSpriteSheetNormalizer`의 걷기 항목은 제외했다.

`ashking_walk.anim`은 8fps, 0.75초 루프다. 마지막 중복 키를 제거했고,
`AshPlayerAnimationBuilder`도 1페이즈 걷기를 재생성할 때 같은 길이를 유지한다.
다른 애니메이션의 길이는 바꾸지 않았다.
