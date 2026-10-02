# 재의 왕 등장 연출 시트 (2026-10-02)

내장 `image_gen`으로 `ash-king-idle.png`를 외형과 정면 자세 참고 이미지로 첨부해 생성했다.
`*.prompt.txt`에는 실제 사용한 프롬프트를, `*-grid.png`에는 생성된 투명 PNG 원본을 저장했다.
원본은 1536×1024, 512×512 셀의 3열×2행이다. 알파를 색으로 지우거나 그림을 다시 그리지 않았다.
최종 채택본은 `ash-king-intro-awaken-v3-grid.png`, `ash-king-intro-plant-v3-grid.png`이다.
첫 생성본과 plant v2는 비교 및 재작업을 위해 남겼다. `ash-king-idle-reference-2x.png`는
idle 첫 셀을 최근접 보간으로 정확히 2배 확대한 외형 참고 컷이며, 최종 재생성 때 함께 첨부했다.

게임용 결과는 상위 `AshKing` 폴더의 `ash-king-intro-awaken.png`와
`ash-king-intro-plant.png`이며, 둘 다 기존과 같은 1536×256, 256×256 셀 6장이다.

## 재현

프로젝트 루트의 PowerShell에서 실행한다.

```powershell
& .\Tools\PrepareBossWalkSheet.ps1 `
  -SourcePath 'Assets\Project\Art\Characters\Boss\AshKing\Raw\Intro20261002\ash-king-intro-awaken-v3-grid.png' `
  -OutputPath 'Assets\Project\Art\Characters\Boss\AshKing\ash-king-intro-awaken.png' `
  -ReferenceFrame 6 -TargetHeight 200 -HeadCenterX 113.5 -GroundLine 216 `
  -EndpointReferencePath 'Assets\Project\Art\Characters\Boss\AshKing\ash-king-idle.png'

& .\Tools\PrepareBossWalkSheet.ps1 `
  -SourcePath 'Assets\Project\Art\Characters\Boss\AshKing\Raw\Intro20261002\ash-king-intro-plant-v3-grid.png' `
  -OutputPath 'Assets\Project\Art\Characters\Boss\AshKing\ash-king-intro-plant.png' `
  -ReferenceFrame 6 -TargetHeight 200 -HeadCenterX 113.5 -GroundLine 216 `
  -EndpointReferencePath 'Assets\Project\Art\Characters\Boss\AshKing\ash-king-idle.png' `
  -EndpointFrames 1,6
```

`ReferenceFrame`은 1부터 시작한다. 서 있는 6번 프레임의 키와 머리 중심으로 공통 배율과
x 오프셋을 계산하며, 프레임별로 바닥선만 정렬한다. 기존 idle 첫 셀을 실측한 키는 200px,
바닥은 y=216, 머리 중심은 x=113.5이다. 무릎 꿇은 프레임을 개별 확대하지 않는다.

생성 도구가 idle을 픽셀 단위로 복제하지는 못하므로 패킹 마지막 단계에서 awaken의 6번,
plant의 1·6번을 실제 idle 첫 셀로 교체한다. 이 단계는 원본을 수정하지 않는 셀 복사이며,
idle 진입·복귀 시 동일한 픽셀과 알파를 사용하게 한다.

## 검수 기록

- 정면 갑옷·왕관·화면 왼쪽 수직 대검 외형과 일어서는 동작/제자리 칼 찍기 동작을 확인했다.
- awaken의 공통 배율은 0.4842615012, 공통 x 오프셋은 13px이다.
- plant의 공통 배율은 0.5128205128, 공통 x 오프셋은 1px이다.
- 출력 크기와 투명 알파를 확인했고, 연결 프레임 세 곳 모두 idle과 다른 픽셀 수가 0이었다.
- 원본의 칼·망토 모양에는 생성 그림 특유의 프레임 차이가 있다. 중간 자세를 idle과 완전히
  같은 픽셀로 만들었다는 뜻은 아니며, 최종 재생 속도와 조명·재 효과는 Unity에서 확인한다.
- 시트에는 이름·재 입자·충격파·왕관 화염을 합성하지 않았다. 이 효과는 런타임에서 더한다.
- `ReferenceFrame`을 생략한 기존 걷기 시트를 이전 스크립트와 새 스크립트로 각각 재패킹해
  SHA256 `87B7E4019F5982C0F5C36280AAC1D2C29CA10EDFB9098A8E8AD012B3F4F1FBB5` 일치를 확인했다.
- 최초 결과의 넓은 어깨와 중앙으로 밀린 칼은 두 차례 수정 생성으로 줄였다. 생성 과정의
  발·갑옷 세부 위치 차이는 중간 프레임에 일부 남아 있으므로 수작업 리깅과 같은 완전한
  픽셀 구조 보존으로 보지는 않는다. 끝 자세 연결 세 곳의 픽셀 일치는 별도로 보장한다.
- awaken v3에서 첫 프레임의 눈과 검·갑옷·망토 주황 균열을 껐다. 첫 프레임의 차가운
  휴면 상태와 두 번째 프레임의 주황 점화가 구별되도록 내장 image_gen으로 국소 수정했다.

생성 원본 위치:

- awaken: `C:\Users\도현승\.codex\generated_images\01a0fbf3-179c-7013-8400-64df0e416292\exec-95d4f702-9dff-4043-9798-47c187d5f487.png`
- plant: `C:\Users\도현승\.codex\generated_images\01a0fbf3-179c-7013-8400-64df0e416292\exec-922a6437-2bd7-4235-8036-4fabec48e07a.png`
- awaken v2 (체형 수정): `C:\Users\도현승\.codex\generated_images\01a0fbf3-179c-7013-8400-64df0e416292\exec-73da9587-49c0-4b97-99e3-cfb8f5c41246.png`
- plant v2 (비채택): `C:\Users\도현승\.codex\generated_images\01a0fbf3-179c-7013-8400-64df0e416292\exec-6d0a3da2-a2b6-4a9c-9204-27b9f649b85b.png`
- 최종 plant v3: `C:\Users\도현승\.codex\generated_images\01a0fbf3-179c-7013-8400-64df0e416292\exec-40529e7f-3aad-4afe-bd4a-14ff9c279087.png`
- 최종 awaken v3 (휴면 점화 전): `C:\Users\도현승\.codex\generated_images\01a0fbf3-179c-7013-8400-64df0e416292\exec-7d68c3c8-db8f-4a85-a43d-5d0964237396.png`
