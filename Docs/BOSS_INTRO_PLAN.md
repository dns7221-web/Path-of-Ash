# 보스 등장 연출 「잿빛 석상, 깨어나는 왕」 — 작업 지시서

> 작성: 2026-10-02 · 대상: 이 저장소에서 작업하는 코딩 에이전트(이미지 생성 포함)
> 상태: 기획 확정, 구현 전. **「결정 대기」 표시 항목은 기본값으로 진행하고 사용자에게 확인받는다.**

## 0. 한 줄 요약

지금 보스는 방에 들어오면 일반 몬스터처럼 그냥 생성된다. 이것을 **방 중앙에 재를 뒤집어쓰고 무릎 꿇은 석상 → 깨어나 일어섬 → 이름 카드 → 전투 시작**으로 바꾼다. 길이는 약 4.5초, 스킵 가능.

## 1. 반드시 알고 시작할 사실 (틀리기 쉬운 것)

> 경로 표기: `Scripts/` = `Assets/Project/Scripts/`, `Art/` = `Assets/Project/Art/`

| 사실 | 근거 파일 | 틀리면 생기는 일 |
|---|---|---|
| 1페이즈 보스는 **정면**을 보는 육중한 갑옷 기사다. 대검을 **화면 왼쪽 바닥에 칼끝을 꽂아 세우고** 손잡이를 쥐고 서 있다 | `Art/Characters/Boss/AshKing/ash-king-idle.png` | 걷기 시트(옆모습)를 기준으로 그리면 idle과 이어지지 않는다 |
| 1페이즈 이름은 일부러 **`???`**, 2페이즈에 본명 **「재의 길」**이 드러난다 | `Scripts/UI/BossHealthBar.cs` (`phase1Name`, `phase2Name`) | 이름 카드에 본명을 쓰면 2페이즈 반전이 깨진다 |
| 2페이즈는 갑옷 껍질이 깨지고 **흰 머리의 진짜 모습**이 나온다 | `ash-king-phase2-idle.png`, `BossTransitionSequence.cs` | 등장 연출에서 진짜 모습을 암시하면 안 된다 |
| Game 씬은 **Cinemachine을 쓰지 않는다.** 방 하나 = 고정 카메라 하나 | `Scripts/Combat/CameraShake.cs` 상단 주석 | CinemachineBrain을 넣으면 카메라 구도의 주인이 바뀐다. **넣지 않는다** |
| `Time.timeScale`은 `PauseGate` 한 곳에서만 바꾼다 | `Scripts/UI/PauseGate.cs` | 연출 중에도 시간은 흐르게 두고 **입력만 잠근다** |
| 보스 방 배경에는 왕좌가 없고, 위쪽 가운데는 **출구 아치**, 중앙은 비어 있다 | `Art/Environment/BossRooms/ash-king-boss-room-orthographic-open-1920x1080-v1.png` | 왕좌를 놓으면 출구를 막는다. 보스는 **중앙**에 둔다 |

**이야기 구조**: 재 껍질(등장) → 갑옷 껍질(1페이즈) → 진짜 모습(2페이즈). 등장 연출은 두 번째 겹까지만 보여 준다.

## 2. 콘티 (약 4.5초)

| 시간 | 화면 | 소리 | 담당 |
|---|---|---|---|
| 0.0 | 문이 쾅 닫힘, 화면 흔들림, 플레이어 조작 잠금 | 던전 음악 끄고 정적 | 코드 |
| 0.3 | 방이 어두워지고 석상만 희미하게 보임 (석상은 방에 들어온 순간부터 이미 보임) | — | Light2D |
| 0.8 | 투구 눈구멍에 주황 불이 켜짐 | 낮은 울림 | 시트 ① 1→2 |
| 1.0~2.0 | 고개 들고 칼 짚고 일어섬, 재가 몸에서 떨어져 흩날림, 잿빛이 원래 색으로 돌아옴 | — | 시트 ① + 파티클 + 셰이더 |
| 2.4 | 칼을 살짝 뽑아 들었다가 바닥에 쿵 다시 꽂음, 왕관 끝에서 불꽃이 솟음 | 쿵 | 시트 ② + 파티클 + 흔들림 |
| 2.8 | 이름 카드 `???` 표시, 체력바가 0에서 가득 차오름 | **보스 음악 시작** | UI |
| 4.0 | 조작 해제, 보스 AI 시작 | — | 코드 |

## 3. 이미지 생성 (2장)

### 공통 규격 (기존 보스 시트와 같음)
- 도구: 내장 image_gen, `transparent_background=true`
- 출력: 1536×1024 투명 PNG, **3열×2행**, 셀 512×512, 왼쪽→오른쪽, 위→아래 순서
- 참고 이미지: **`ash-king-idle.png`를 반드시 첨부**한다 (외형 + 마지막 프레임 자세 기준)
- 저장 위치: `Assets/Project/Art/Characters/Boss/AshKing/Raw/Intro20261002/`
  - 원본 PNG와 **사용한 프롬프트를 `*.prompt.txt`로 함께 저장**한다 (기존 `WalkCycle20260929` 폴더와 같은 방식)

### 시트 ① 깨어나기 `ash-king-intro-awaken`

```
Generation tool: built-in image_gen (transparent_background=true)
Attach: ash-king-idle.png (appearance reference AND the exact pose of the last frame)

Use case: identity-preserve. Create a NEW production "awakening" intro animation sprite sheet for the EXACT armored boss in the attached reference.
Preserve the dark iron Ash King design: jagged tall black crown helmet, slit orange eyes, layered massive black/gray shoulder armor, restrained orange ember cracks, full armored boots, black ragged cape, huge straight thick chipped gray greatsword with orange cracks. Preserve the detailed pixel-textured hand-painted game rendering and body proportions. Do not turn him into a skeleton or redesign him.

PRIMARY MOTION: a dormant king kneeling on one knee behind his planted greatsword slowly rises to his full standing idle pose. Heavy and deliberate, like a statue coming back to life.
Output 1536x1024 transparent PNG. EXACTLY 6 sprites in a REGULAR 3 columns x 2 rows contact sheet, 512x512 cells, sequential frames left-to-right/top-to-bottom. One whole body per cell.
SAME SCALE in every frame; kneeling frames are shorter only because he kneels, NOT because he is drawn smaller. Floor contact y=445 in every frame. Crown top y=65 in frames 5-6.
VIEW: FRONT view matching the idle reference exactly (same camera, same slight turn). NOT a side view.
SWORD: the greatsword stays planted point-down vertically in the floor on his right side (screen LEFT) in ALL frames, same position x=150, blade tip on the floor. It never leaves the floor.

Frame 1 TOP LEFT: DORMANT. Kneeling on his left knee (screen right), right foot planted, BOTH hands stacked on the sword hilt, head bowed until the crown nearly touches his hands. Eyes DARK, ember cracks DARK, cape pooled on the floor.
Frame 2 TOP CENTER: IDENTICAL body to frame 1. ONLY the eye slits ignite thin orange and the cracks glow faintly.
Frame 3 TOP RIGHT: head lifts to stare at the viewer, shoulders rise, still kneeling.
Frame 4 BOTTOM LEFT: pushing up on the hilt, left knee leaving the floor, torso half raised.
Frame 5 BOTTOM CENTER: nearly upright, left hand releasing the hilt and dropping to his side.
Frame 6 BOTTOM RIGHT: EXACT copy of the reference idle frame 1. Must be interchangeable with the idle sprite.

Cape follows with modest cloth lag. No ash particles, no smoke, no floor, no shadow, no text, no frame numbers, no effects (all effects are added in-engine). Leave empty cell margins; don't crop cape, sword or crown. Truly transparent background, preserve alpha. Sprites only.
```

### 시트 ② 칼 다시 꽂기 `ash-king-intro-plant`

```
Generation tool: built-in image_gen (transparent_background=true)
Attach: ash-king-idle.png (appearance reference AND the pose of frames 1 and 6)

Use case: identity-preserve. Create a NEW production "declaration" intro animation sprite sheet for the EXACT armored boss in the attached reference.
Preserve the dark iron Ash King design: jagged tall black crown helmet, slit orange eyes, layered massive black/gray shoulder armor, restrained orange ember cracks, full armored boots, black ragged cape, huge straight thick chipped gray greatsword with orange cracks. Preserve the detailed pixel-textured hand-painted game rendering and body proportions. Do not redesign him.

PRIMARY MOTION: from idle he lifts the planted greatsword slightly with both hands and drives it back down into the floor, a heavy small declaration gesture, NOT an attack. No step forward, no lunge.
Output 1536x1024 transparent PNG. EXACTLY 6 sprites in a REGULAR 3 columns x 2 rows contact sheet, 512x512 cells, sequential frames left-to-right/top-to-bottom. Same scale in every frame. Floor contact y=445. Crown top y=65.
VIEW: FRONT view matching the idle reference exactly. Sword stays on his right side (screen LEFT), vertical, x=150.

Frame 1 TOP LEFT: EXACT copy of the reference idle frame 1.
Frame 2 TOP CENTER: both hands on the hilt, sword lifted slightly so the tip just leaves the floor.
Frame 3 TOP RIGHT: sword raised higher, tip at knee height, shoulders tensed.
Frame 4 BOTTOM LEFT: sword driven back down into the floor, knees slightly bent, head pushed forward.
Frame 5 BOTTOM CENTER: HOLD of frame 4, only the cape settles.
Frame 6 BOTTOM RIGHT: EXACT copy of the reference idle frame 1.

Sword rigid, constant length and shape. No shockwave, sparks, crown fire, smoke, shadow, floor, text or frame numbers (added in-engine). Leave empty cell margins; don't crop cape, sword or crown. Truly transparent background, preserve alpha. Sprites only.
```

> 기존 공격 모션 `ash-king-slam`과 일부러 구분한다. 앞으로 나서는 동작도 충격파도 없이 **작게** 만들어야 플레이어가 공격 예고로 착각하지 않는다.

### 검수 기준 (통과 못 하면 수정 프롬프트로 다시 뽑기)
1. 여섯 프레임 모두 **같은 배율**인가 (무릎 꿇은 프레임만 작게 그려지지 않았나)
2. 칼 위치(x≈150)가 프레임마다 **고정**인가
3. 마지막 프레임이 idle 첫 프레임과 **겹쳐 보면 같은가**
4. 정면 시점인가 (옆모습으로 돌아가지 않았나)

## 4. 시트 패킹 — 도구 수정 필요

기존 `Tools/PrepareBossWalkSheet.ps1`은 이 시트에 **그대로 쓰면 안 된다.** 이 스크립트는 두 가지를 전제로 한다.
- 여섯 장 키의 **중앙값**으로 배율을 구한다 → 무릎 꿇은 프레임이 섞이면 배율이 커져 보스가 거대해진다
- 프레임마다 **머리 중심 x**를 맞춘다 → 고개를 숙였다 드는 동작에서 몸이 좌우로 튄다

**할 일**: 스크립트에 `-ReferenceFrame` 같은 옵션을 추가한다. **서 있는 마지막 프레임(6번) 하나로 배율과 x 오프셋을 한 번만 구하고 여섯 장 모두에 같은 값을 적용**한다. 프레임마다 맞추는 것은 바닥선(`GroundLine 216`)뿐이다.
출력은 기존과 같은 규격(1536×256, 256×256 셀 6장)으로 `Assets/Project/Art/Characters/Boss/AshKing/`에 저장한다.

## 5. 유니티 구현

### 코드 규칙 (이 저장소 공통)
- 주석은 한국어로 **왜 이렇게 했는지**까지 적는다. 새로 만든 부분에는 `추가 생성(2026-10-02)` 표시를 단다
- 유니티 내장 기능을 우선 쓴다
- 연출 길이는 **Timeline 에셋 한 곳에만** 적는다. 코드에 같은 초 단위 숫자를 다시 적지 않는다 (과거에 0.75 대 0.875 불일치 사고가 있었다)

### 재사용할 기존 코드

| 할 일 | 재사용 | 위치 |
|---|---|---|
| 시간표 재생 + 시그널 수신 | `BossTransitionSequence` 패턴 (`PlayableDirector` + `INotificationReceiver`) | `Scripts/Enemy/BossTransitionSequence.cs` |
| 플레이어 조작 잠금·무적 | `PlayerController.BeginScripted(face, trigger)` / `EndScripted()` | `Scripts/Player/PlayerController.cs` |
| 보스 무적 | `Health.IsInvulnerableExternally` | `Scripts/Combat/Health.cs` |
| 화면 흔들림 | `CameraShake.Shake(strength, seconds)` | `Scripts/Combat/CameraShake.cs` |
| 음악 | `SoundPlayer.StopMusic()`, `SoundPlayer.PlayMusic(MusicId.Boss)` | `Scripts/Audio/SoundPlayer.cs` |
| 체력바 | `BossHealthBar.Bind(health, phase2Ratio)` | `Scripts/UI/BossHealthBar.cs` |
| 연출 참고 예시 | 클리어 연출 | `Scripts/Dungeon/ClearCutscene.cs` |

### 바꿀 것

**1. `BossEncounter.BeginEncounter()`를 세 단계로 나눈다** (`Scripts/Enemy/BossEncounter.cs`)
지금은 이 함수 하나가 보스 생성, 체력바 연결, 음악 전환을 한 번에 한다.
- `SpawnBoss()`: 방 중앙에 보스를 생성한다. **AI 정지 + `IsInvulnerableExternally = true`**, 시트 ① 첫 프레임 자세로 둔다
- `PlayIntro()`: 연출을 재생한다
- `StartFight()`: 지금 `BeginEncounter`의 체력바·시전 바·왕관 의식 연결과 보스 음악을 여기로 옮기고, 무적 해제, AI를 시작한다

**2. `EnemyBoss`에 `Intro` 상태를 추가한다** (`Scripts/Enemy/EnemyBoss.cs`, `private enum State`)
Intro 상태에서는 추적·공격을 하지 않는다. Timeline이 재생 중에는 Animator Controller를 덮어쓰기 때문에, 그림이 멈춰 있어도 **AI는 따로 막아야 한다.**

**3. 새 컴포넌트 `BossIntroSequence`** (`Scripts/Enemy/BossIntroSequence.cs`)
- `BossTransitionSequence`와 같은 구조로 만든다: `PlayableDirector` 재생, 시그널 수신, 끝나면 이벤트 발생
- **스킵**: 입력이 들어오면 Timeline 끝으로 건너뛴다
- **중요 — 스킵하면 시그널이 사라진다.** `director.time`을 끝으로 옮기면 그 사이 Signal은 울리지 않는다. 그래서 전투 시작은 **시그널에 맡기지 않고** 정상 종료와 스킵 모두에서 코드가 직접 `Finish()`를 부른다. bool 하나로 **한 번만 실행**되게 막는다 (멱등 처리). 이 저장소는 시그널 유실로 "체력바가 안 차고 이름이 ???로 남는" 사고를 이미 한 번 겪었다

**4. Timeline 에셋 `BossIntro.playable`과 에디터 빌더**
기존 `Assets/Editor/AshBossTransitionTimelineBuilder.cs`처럼 **에디터 메뉴로 다시 만들 수 있는 빌더**를 만든다. 트랙 구성:
- Animation Track: 보스 (시트 ① → 시트 ②)
- Animation Track: 잿빛 셰이더 값 `_Ash` 1→0
- Animation Track: Light2D 밝기 (방 어두워짐, 보스 조명)
- Activation Track: 재 파티클, 왕관 불꽃 파티클, 이름 카드
- Signal Track: `DoorSlam`, `EyesIgnite`, `SwordPlant`, `NameCard`

**5. 잿빛 셰이더 (Shader Graph, Sprite Lit)**
`SpriteRenderer.color`는 **곱하기**라서 어두워지기만 하고 밝은 잿빛은 만들 수 없다. Shader Graph로 `_Ash`(0~1) 속성을 만들어 채도를 빼고 밝기를 올린다. Timeline 애니메이션이 머티리얼 값을 바꾸면 유니티가 내부적으로 MaterialPropertyBlock을 쓰므로 머티리얼 복사본이 생기지 않는다.

**6. 파티클 (ParticleSystem)**
- 재 떨어짐: 회색 작은 사각 입자, 아래로 떨어지다 흩어짐
- 잉걸: 주황 입자가 위로 떠오름
- 왕관 불꽃: 시트 ② 4번 프레임에 맞춰 한 번 터짐
- 기존 `Assets/Editor/AshBossParticleBuilder.cs` 방식대로 빌더로 만든다

**7. 이름 카드 UI**
- 글자는 **TextMeshPro**로 쓴다 (그림 속 글자는 흐려지고 수정하기 어렵다)
- 표시: `???` (`BossHealthBar.phase1Name`과 같은 값을 읽는다. 문자열을 두 곳에 적지 않는다)

### 결정 대기 (기본값으로 진행)

| 항목 | 기본값 | 다른 선택지 |
|---|---|---|
| 스킵 방식 | **처음 한 번은 전체, 다음부터 짧은 버전(약 1.5초) + 아무 키로 스킵**. 본 적 있는지는 `PlayerPrefs`에 저장 (설정 값이라 "영구 강화 없음" 원칙에 걸리지 않음) | 항상 전체 + 키 스킵만 |
| 이름 카드 부제 | **「재를 두른 자」** (정체를 밝히지 않는 별칭) | 부제 없음 |
| 2페이즈 이름 카드 | **이번 작업에서는 하지 않는다.** 이후 `BossTransitionSequence`의 `revealedSignal` 순간에 같은 UI로 「재의 길」을 띄우는 것을 후속 작업으로 둔다 | 이번에 같이 |

## 6. 작업 순서

그림 생성과 코드 작업은 **동시에** 할 수 있다.
1. 코드: `BossEncounter` 3단계 분리, `EnemyBoss.Intro` 상태, `BossIntroSequence` (그림 없이 기존 idle로 먼저 동작 확인)
2. 그림: 시트 ①② 생성 → 검수 → 패킹 도구 수정 → 패킹
3. Timeline 빌더 + 셰이더 + 파티클 + 이름 카드
4. 확인 항목 (아래)
5. `Docs/PROJECT_CONTEXT.md`와 `Docs/DEVELOPMENT_LOG.md`에 기록

## 7. 완료 확인 항목

- [ ] 보스 방에 들어가면 석상이 보이고, 문이 닫히고, 연출이 끝까지 재생된다
- [ ] 연출 중 플레이어가 움직이지 못하고 피해도 받지 않는다
- [ ] 연출 중 보스를 때려도 체력이 줄지 않는다
- [ ] 연출 중 보스가 패턴을 쓰지 않는다
- [ ] 스킵해도 체력바 연결·보스 음악·AI 시작이 **전부** 일어난다
- [ ] 연출이 끝나는 순간 보스 몸이 튀지 않는다 (마지막 프레임 = idle)
- [ ] 이름 카드에 `???`가 나오고, 2페이즈 전환 뒤 체력바 이름이 「재의 길」로 바뀐다 (기존 동작 유지)
- [ ] 연출 중 ESC나 인벤토리를 열었을 때 깨지지 않는다
- [ ] 두 번째 판부터 짧은 버전이 나온다
- [ ] 기존 EditMode 테스트(`Assets/Tests/EditMode`)가 모두 통과한다
