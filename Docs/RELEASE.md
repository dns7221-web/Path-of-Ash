# 재의 길 — 배포·PV 가이드

> 작성: 2026-10-05 · 대상 버전 1.0.0

빌드 → itch.io 배포 → PV 영상까지의 순서와 정해 둔 값입니다. 처음 한 번 따라 하고, 이후 패치는 **1. 빌드 → 3. 올리기**만 반복합니다.

## 0. 정해 둔 값

| 항목 | 값 | 이유 |
| --- | --- | --- |
| 회사 이름 | `Gaksultang` | 설정 저장 위치를 정합니다. **첫 배포 뒤에는 바꾸지 않습니다** |
| 제품 이름 | `재의 길` | 창 제목과 저장 위치에 쓰입니다 |
| 실행 파일 | `PathOfAsh.exe` | 한글 파일명은 일부 압축 프로그램·명령줄에서 깨집니다 |
| 버전 | `1.0.0` | 버그 수정 `1.0.1`, 기능 추가 `1.1.0` (Player Settings → Version) |
| 스크립팅 백엔드 | Mono (기본값) | 아래 "알아야 할 것" 참고 |
| 플랫폼 | Windows 64비트 | |

## 전체 순서

- [ ] 1. 아이콘 만들기 → `게임 아이콘 적용`
- [ ] 2. `Windows 빌드 (zip 포함)` → 다른 PC(노트북)에서 zip 풀어 실행해 보기
- [ ] 3. itch.io 페이지 만들기(Draft) → butler로 올리기 → 직접 받아서 실행해 보기
- [ ] 4. PV 녹화 → 편집 → YouTube 업로드
- [ ] 5. itch.io 페이지에 PV·스크린샷 넣고 Public 전환
- [ ] 6. README 상단에 GIF·itch.io 링크·PV 링크

## 1. 아이콘

소재: **재 위에 꽂힌 잉걸 검** (10-05 결정). **최종은 2.5D(입체감 있는 반픽셀풍)** 입니다(10-05). 회화풍은 3D 렌더처럼, 2D 셀 셰이딩은 맛이 살지 않아서 그 사이로 정했습니다 —
계단처럼 끊긴 픽셀 질감이 본편 픽셀아트와 이어집니다. 크기별 확인: 256·48·32px 합격, 16·24px는 "어두운 칸의 주황 불꽃"으로 읽힘(허용).
아래는 2D 셀 셰이딩 시도 때의 프롬프트로, 다시 뽑을 때 출발점으로 남겨 둡니다.
타이틀의 무덤 검, 보스 등장의 검을 꽂는 장면과 이어지고, 세로 막대 + 손잡이라 16px에서도 형태가 남습니다.

GPT 이미지 생성에 넣을 프롬프트(영문이 결과가 안정적입니다):

```text
Square game icon, 1024x1024, 1:1.
2D anime-style illustration for a Japanese-style subculture roguelike game. Hand-drawn look, NOT 3D.

Subject: a single longsword planted upright in a small mound of grey ash, centered.
The blade is bold, about one tenth of the image width, dark steel with glowing ember-orange
crack lines drawn as clean strokes, brightest where it enters the ash.
Simple wide crossguard and pommel, forming a clear cross silhouette like a grave marker.
A few diamond-shaped orange ember sparks float upward.

Rendering: cel shading with flat colors and only two or three tone steps, clean confident lineart,
a thick dark outline around the whole sword so it pops from the background.
Soft glow only around the orange cracks.
No 3D rendering, no bevels, no stone texture, no realistic lighting, no smoke, no photorealism, no depth of field.

Palette: charcoal black, ash grey, pale grey highlights, and ember orange (#FF6A2A) as the only accent color.
Background: flat dark charcoal (#141414) filling the whole square, with a soft orange glow behind the base of the sword.
Composition: the whole sword fits inside the frame with about 10% empty margin on every side.
One clear silhouette that stays readable when shrunk to 32x32 pixels.

No text, no letters, no logo, no border, no frame, no rounded corners, no characters, no hands,
no other swords, no scenery.
```

같은 대화에서 3D처럼 나온 결과를 고칠 때(구도는 유지):

```text
Keep this exact composition, but redraw it as a 2D anime-style illustration:
cel shading with flat colors, clean lineart, a thick dark outline around the sword.
Remove the 3D bevels, the stone texture and the realistic smoke.
Draw the ember cracks as clean glowing strokes.
```

결과가 어긋날 때 이어서 보낼 수정 문장:

| 증상 | 수정 문장 |
| --- | --- |
| 너무 복잡함 | `Simplify: remove small details, make the sword thicker, fewer embers. Keep one bold silhouette.` |
| 배경에 풍경이 들어감 | `Remove all scenery. Plain dark charcoal background only.` |
| 주황이 너무 많음 | `Keep orange only on the blade cracks and at the base. Everything else grey.` |
| 검이 잘림 | `Leave 10% empty margin on all sides. The whole sword must be inside the frame.` |
| 글자가 들어감 | `Remove all text and letters.` |
| 3D처럼 보임 | `Make it flat 2D: cel shading, clean lineart, no bevels, no texture, no realistic lighting.` |

- 게임 규칙(회색·숯색 바탕 + 주황 잉걸만 강조색)을 그대로 따릅니다.
- 배경을 투명이 아니라 어두운 색으로 꽉 채웁니다. 회색 실루엣이 투명 배경이면 어두운 작업 표시줄에서 사라집니다.
- **32×32로 줄여서 알아볼 수 있는지**가 기준입니다. 작업 표시줄과 탐색기에서는 그 크기로 보입니다.
- 결과물을 `Assets/Project/Art/UI/Icon/GameIcon.png`에 넣고 **Tools → 재의 길 → 배포 → 게임 아이콘 적용**.
- itch.io 표지 이미지(630×500)는 같은 그림을 넓게 다시 뽑거나 게임 스크린샷을 씁니다.

## 2. 빌드

**Tools → 재의 길 → 배포 → Windows 빌드 (zip 포함)** — 코드: [`AshReleaseBuilder.cs`](../Assets/Editor/AshReleaseBuilder.cs)

메뉴가 하는 일:

1. 검사 — 회사 이름이 `DefaultCompany`면 중단, 저장 안 한 씬이 있으면 물어봄, 효과음 팩·아이콘이 없으면 물어봄
2. `BuildPipeline.BuildPlayer`로 빌드 (StrictMode — 빌드 중 에러가 하나라도 있으면 실패 처리)
3. 배포하면 안 되는 폴더 삭제 (`PathOfAsh_BurstDebugInformation_DoNotShip`)
4. zip 만들기, 용량 보고서 쓰기, 탐색기 열기

결과(`Builds/Windows`, 커밋되지 않음):

```text
Builds/Windows/
├─ PathOfAsh_v1.0.0/              ← butler가 올리는 폴더
├─ PathOfAsh_v1.0.0_Windows.zip   ← 웹 업로드·지인 전달용
└─ PathOfAsh_v1.0.0_report.txt    ← 빌드 시간·용량·가장 큰 에셋 15개
```

**개발 빌드 (프로파일러 연결)** 메뉴는 실행하면 에디터 Profiler가 자동으로 붙습니다.
"씬 전환 멈칫함이 HDD 탓인지" 같은, 에디터에서는 잴 수 없는 것을 노트북(SSD)에서 잴 때 씁니다. **itch.io에 올리지 않습니다**(업로드 스크립트가 `_dev` 폴더를 건너뜁니다).

### 빌드 후 직접 확인 — 개발 PC가 아닌 곳에서

에디터에서 되던 것이 빌드에서 안 되는 경우가 있어서, zip을 **다른 폴더나 다른 PC에 풀어서** 확인합니다.

- [ ] 타이틀 → 튜토리얼 → 던전 → 보스 → 클리어 → 결과 → 재시작 한 바퀴
- [ ] 창 크기 2/3/4배·전체화면 전환 (에디터에서는 확인 불가 항목)
- [ ] 게임을 껐다 켜도 볼륨·키 설정이 유지되는지
- [ ] 효과음·배경음, 궁극기 컷인 영상, 결과 화면 반복 영상
- [ ] 한글이 네모로 나오지 않는지 (Dynamic 폰트)
- [ ] 문제가 있으면 로그: `%USERPROFILE%\AppData\LocalLow\Gaksultang\재의 길\Player.log`

## 3. itch.io 배포

### 페이지 만들기 (처음 한 번)

itch.io → 프로필 → **Upload new project**

| 항목 | 값 |
| --- | --- |
| Title | `재의 길 (Path of Ash)` |
| Project URL | `path-of-ash` |
| Short description | `영구 성장 없이, 유물 조합만으로 한 판을 완성하는 픽셀아트 던전 로그라이크` |
| Classification | Games |
| Kind of project | **Downloadable** |
| Release status | Released |
| Pricing | No payments |
| Uploads | 비워 둠 — butler가 채웁니다 |
| Genre / Tags | Action / `roguelike`, `pixel-art`, `top-down`, `dungeon-crawler`, `singleplayer`, `unity` |
| Languages | Korean |
| Community | **Comments** 켜기 — 플레이테스트 피드백 받는 곳 |
| Visibility | 처음엔 **Draft** → 직접 받아서 확인 후 Public |

설명란에 넣을 것: 한 줄 소개, 핵심 특징 3개, 조작 표, 권장 사양, 아래 SmartScreen 안내, GitHub 저장소 링크, 소리 크레딧(README의 표).

> **SmartScreen 안내 문구** — 서명하지 않은 exe라서 처음 실행할 때 "Windows의 PC 보호" 창이 뜰 수 있습니다. **추가 정보 → 실행**을 누르세요.

### 올리기 — butler

처음 한 번: [butler 설치](https://itch.io/docs/butler/installing.html) → PATH에 추가 → `butler login`

```powershell
# 미리보기 (실제로 올리지 않음)
powershell -File Tools\DeployItch.ps1 -Target 아이디/path-of-ash -DryRun

# 올리기
powershell -File Tools\DeployItch.ps1 -Target 아이디/path-of-ash
```

`아이디`는 itch.io 계정 이름입니다(`https://아이디.itch.io`). 스크립트는 가장 최근의 `PathOfAsh_v*` 폴더를 찾아
`windows` 채널에 올리고, 폴더 이름의 버전을 itch.io 버전 기록에 남깁니다. 코드: [`DeployItch.ps1`](../Tools/DeployItch.ps1)

패치할 때: Player Settings에서 Version을 `1.0.1`로 → 빌드 메뉴 → 같은 명령. 바뀐 파일만 올라갑니다.

## 4. PV 영상

Unity Recorder 5.1.7이 이미 설치되어 있습니다. 녹화는 에디터 Play 모드에서 합니다 — 디버그 도구(무적·열쇠 지급)를 쓸 수 있어서 빌드보다 찍기 쉽습니다.

### Recorder 설정

**Window → General → Recorder → Recorder Window → Add Recorder → Movie**

| 항목 | 값 | 이유 |
| --- | --- | --- |
| Recording Mode | Manual | 원하는 장면에서 시작·정지 |
| Playback | **Constant**, Target FPS **60** | 게임 시간이 녹화 프레임에 맞춰 흘러서 PC가 느려도 영상은 끊기지 않습니다. 소리를 같이 녹음하려면 Constant여야 합니다 |
| Cap FPS | 켬 | 실시간보다 빨리 돌지 않게 |
| Exit Play Mode | 켬 | 녹화 끝나면 Play 종료 |
| Capture | Game View | |
| Output Resolution | **1920×1080** | 게임 기본 해상도 640×360의 정확히 3배 — 픽셀이 번지지 않습니다 |
| Format | MP4 (H.264), Quality High | |
| Include Audio | 켬 | |
| Path | `Recordings/` (커밋되지 않음), 이름 `PV_<Take>` | 테이크 번호 자동 증가 |

Game 뷰 해상도도 1920×1080으로 맞춥니다. 설정은 Recorder 창의 프리셋 저장으로 남겨 두면 다음에 그대로 불러옵니다.
녹화 시작·정지 단축키는 **Edit → Shortcuts**에서 `Recorder`로 검색하면 나옵니다.

### 찍을 때 쓰는 도구 (에디터 전용, 빌드에는 없음)

| 키 | 기능 | PV에서 |
| --- | --- | --- |
| F6 | 플레이어 무적 | 보스 패턴을 맞아 가며 여러 번 찍기 |
| F3 | 보스 열쇠 지급 | 열쇠 모으는 과정 건너뛰고 보스 방 직행 |
| F1 | 조사 오버레이 | **녹화 전에 꺼져 있는지 확인** (기본은 꺼짐) |
| F7~F9 | 시간 배속 | 화면에 글자가 떠서 녹화에는 쓰지 않음 |

한 번에 길게 찍지 말고 **장면마다 10~30초짜리 테이크를 여러 개** 찍어 편집에서 고릅니다.

### 구성 — 90초

| 시간 | 장면 | 자막(짧게) |
| --- | --- | --- |
| 0:00–0:05 | 보스 2페이즈 재 폭발 → 네 모서리로 피함 (가장 강한 장면을 맨 앞에) | — |
| 0:05–0:10 | 타이틀 화면 | 재의 길 · Path of Ash |
| 0:10–0:25 | 던전 전투: 대시, 기본 공격, Q/W/E/R, 망령 돌진 예고선, 사수, 자폭병 | 예비동작을 읽고 피한다 |
| 0:25–0:40 | 상자 → 유물 장착 → **능력치 창(C)에서 숫자가 바뀌는 장면** | 영구 성장 없음 · 유물 조합이 전부 |
| 0:40–0:48 | 보스 열쇠 4개 → 부서진 문 열림 | |
| 0:48–1:05 | 보스 등장 연출 → 1페이즈 내려찍기·잿불 파도 | 재를 두른 자 |
| 1:05–1:20 | 70%에서 2페이즈 전환 연출 → 왕관 의식 → 궁극기 컷인 | |
| 1:20–1:27 | 클리어 연출 「손을 펴다」 | |
| 1:27–1:30 | 끝 화면: 제목 + itch.io 주소 + GitHub 주소 | |

itch.io용으로는 이 중 앞 30초만 잘라 한 개 더 만들어도 됩니다.

### 녹화 후 확인

- [ ] 궁극기 컷인 영상이 녹화본에서 빠르거나 느리지 않은지 — 컷인은 `UnscaledGameTime`으로 흐르고, Constant 녹화는 게임 시간을 고정합니다. 어긋나면 그 장면만 Playback을 Variable로 따로 찍습니다
- [ ] 소리와 화면이 맞는지 (타격음과 타격 프레임)

### 편집·업로드

- 편집: DaVinci Resolve(무료) — 타임라인 1920×1080, 60fps. 자막은 게임 폰트(NeoDunggeunmo)를 쓰면 통일감이 납니다
- 내보내기: H.264, 1080p60
- YouTube에 올리고 itch.io 페이지의 **Gameplay video or trailer** 칸과 README에 링크

README용 GIF — 녹화본에서 6초를 잘라 정확히 1/3(640×360)로 줄입니다. 정수배 축소에 `neighbor`라 픽셀이 번지지 않습니다.

```bash
ffmpeg -ss 00:00:12 -t 6 -i PV.mp4 -vf "fps=20,scale=640:360:flags=neighbor,split[a][b];[a]palettegen[p];[b][p]paletteuse" -loop 0 Docs/Images/gameplay.gif
```

## 알아야 할 것 — 면접에서 설명할 수 있게

**`BuildPipeline.BuildPlayer`와 `BuildReport`** — Build 버튼이 내부에서 부르는 것과 같은 API입니다. 반환되는 `BuildReport`에
결과·시간·에러 수·`packedAssets`(에셋별 빌드 용량)가 들어 있어서, 용량 보고서를 따로 계산하지 않고 읽기만 했습니다.

**`IPreprocessBuildWithReport`** — 모든 빌드 직전에 유니티가 부르는 콜백입니다. 회사 이름 검사를 메뉴가 아니라 여기에 둔 이유는,
Build Profiles 창에서 빌드해도 빠져나갈 수 없게 하려는 것입니다. `BuildFailedException`을 던지면 빌드가 취소됩니다.
"사람이 기억해야 하는 규칙을 파이프라인에 맡겼다"가 핵심입니다.

**회사 이름이 왜 중요한가** — `PlayerPrefs`는 Windows에서 레지스트리 `HKCU\Software\Gaksultang\재의 길`에,
`Application.persistentDataPath`와 `Player.log`는 `AppData\LocalLow\Gaksultang\재의 길`에 저장됩니다. 배포 뒤에 바꾸면 경로가 바뀌어
플레이어 설정이 사라진 것처럼 보입니다. 플레이테스터에게 버그를 받을 때는 `Player.log`를 보내 달라고 하면 됩니다.

**Mono와 IL2CPP** — Mono는 C#을 중간 언어(IL) 그대로 담아 실행 시 JIT 컴파일하고, IL2CPP는 빌드 때 C++로 바꿔 기계어로 컴파일합니다.
IL2CPP가 더 빠르고 역컴파일이 어렵지만 빌드가 느리고 Visual Studio C++ 도구가 필요합니다. 이 게임은 2D이고 코드가 GitHub에 공개되어 있어
역컴파일 방어가 의미 없으므로 Mono로 충분합니다. 성능 문제가 Profiler에서 CPU 스크립트 쪽으로 잡히면 그때 바꿉니다.

**`_DoNotShip` 폴더** — Burst 컴파일러의 디버그 정보입니다. 이 프로젝트는 Burst를 직접 쓰지 않지만 2D Animation 패키지가 끌고 들어옵니다.
크래시 분석용이라 배포본에는 넣지 않습니다.

**StrictMode** — 기본 빌드는 에러가 찍혀도 결과물이 나올 수 있습니다. 배포 빌드는 에러가 하나라도 있으면 실패로 처리해 "에러를 품은 빌드"가 나가지 않게 했습니다.

**버전 번호(Semantic Versioning)** — `주.부.수`. 버그만 고치면 수(1.0.1), 기능을 더하면 부(1.1.0). 버전의 주인은 Player Settings 한 곳이고,
빌드 폴더 이름과 itch.io 버전 기록은 거기서 읽어 옵니다(같은 값을 두 곳에 적지 않는 프로젝트 원칙).

**왜 CI 빌드가 아닌가** — GitHub Actions로 자동 빌드할 수도 있지만, 재배포 금지 효과음 팩이 저장소에 없어서 CI 빌드는 소리가 빠집니다.
라이선스 제약 때문에 로컬 빌드를 선택했고, 대신 로컬 빌드 순서를 메뉴 하나로 고정했습니다.

**Recorder의 Constant 재생** — 내부적으로 `Time.captureDeltaTime`을 고정해 매 프레임 게임 시간이 정확히 1/60초씩 흐르게 합니다.
실제 시간과 게임 시간을 떼어 놓는 것이라, PC가 느려도 영상은 매끈하고 대신 녹화 중 게임이 느리게 느껴질 수 있습니다.
