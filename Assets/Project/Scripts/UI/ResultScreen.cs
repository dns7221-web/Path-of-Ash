using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;    // 추가 생성(2026-09-27) — 글자 받침(RawImage)
using UnityEngine.Video; // 추가 생성(2026-09-27) — 클리어 반복 영상(VideoClip)

/// <summary>
/// 결과 화면. RunResultData에 기록된 값을 읽어 표시하고, 재시작/타이틀로 보낸다.
///
/// 이 컴포넌트는 값을 계산하지 않는다. 계산은 RunManager가 끝냈고 여기서는 읽어서 보여주기만
/// 한다. 화면이 값을 만들기 시작하면 나중에 "리절트에 뜨는 숫자와 실제 기록이 다르다"는
/// 문제가 생기고, 그때 어느 쪽이 맞는지 판단할 근거가 없어진다.
///
/// 참조는 비어 있어도 동작한다. UI를 만들기 전에도 키보드로 흐름을 확인할 수 있어야 하기
/// 때문이다.
/// </summary>
public class ResultScreen : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("RunManager가 결과를 기록해둔 에셋. 같은 에셋을 연결해야 한다.")]
    [SerializeField] private RunResultData result;

    [Header("표시 (없어도 동작한다)")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text survivedText;
    [SerializeField] private TMP_Text killText;

    // 추가 생성 — 방을 무한 반복하는 구조라 "몇 번째 방까지 갔는가"가 곧 이번 판의 성적이다.
    // 생존 시간만으로는 구석에서 버틴 판과 계속 전진한 판이 구분되지 않는다.
    [Tooltip("도달한 방 수. 비워두면 표시하지 않는다.")]
    [SerializeField] private TMP_Text roomText;

    // 추가 생성: 죽었을 때와 탈출했을 때 배경을 바꿔 끼우기 위한 자리.
    //
    // 씬을 둘로 나누지 않은 이유: 결과 화면의 구조(텍스트 위치, 키 입력, 흐름)가 완전히
    // 같아서 씬을 복제하면 한쪽만 고치는 실수가 생긴다. 바뀌는 건 배경 한 장과 제목 문구뿐이라
    // 이쪽이 훨씬 싸다.
    // 수정(2026-09-27) — 제목의 "(클리어 이미지는 나중에 채운다)"를 뺐다. 클리어 그림(GameClear_FinalRest_v1)과 반복 영상이 들어왔다.
    [Header("배경")]
    [Tooltip("배경을 그리는 SpriteRenderer. 비워두면 배경 교체를 하지 않는다.")]
    [SerializeField] private SpriteRenderer background;
    [Tooltip("죽어서 끝났을 때의 배경.")]
    [SerializeField] private Sprite deathBackground;
    [Tooltip("클리어했을 때의 배경. 아직 없으면 비워둔다 — 그러면 사망 배경을 그대로 쓴다.")]
    [SerializeField] private Sprite clearedBackground;

    // 추가 생성(2026-09-27, 클리어 반복 장면) — 클리어 배경 위에서 반복 재생할 영상(숨쉬기·바람·풀린 끈, 3.6초).
    // 정지 그림(clearedBackground)과 같은 그림 한 장으로 만든 영상이라(Tools/MakeClearLoopVideo.py), 영상이 늦게 시작하거나
    // 비어 있어도 정지 그림이 그대로 보인다 — 영상은 "있으면 살아 움직이는" 덧붙임이다.
    [Tooltip("클리어했을 때 배경 위에서 반복 재생할 영상. 비우면 정지 그림(클리어 배경)만 쓴다.")]
    [SerializeField] private VideoClip clearedLoop;

    // 추가 생성(2026-09-27) — 글자 받침. 클리어 그림(노을 하늘)은 밝아서, 어두운 사망 배경에 맞춘 밝은 글자가 묻힌다.
    // 글꼴이 TMP Bitmap 셰이더라 외곽선·그림자를 켤 수 없어서, 글자가 놓인 왼쪽·아래만 어둡게 받친다. 캐릭터가 있는 오른쪽은 그대로다.
    [Header("클리어 글자 받침 — 밝은 클리어 그림 위에서 글자가 묻히지 않게")]
    [Tooltip("왼쪽(제목·기록 쪽)을 어둡게 하는 정도. 0이면 끈다. 눈에 보이는 어두워짐 기준이다(색 공간 보정은 코드가 한다).")]
    [SerializeField, Range(0f, 1f)] private float clearedScrimLeft = 0.62f;
    [Tooltip("아래(조작 안내 쪽)를 어둡게 하는 정도. 0이면 끈다.")]
    [SerializeField, Range(0f, 1f)] private float clearedScrimBottom = 0.55f;

    // 추가 생성(2026-09-27) — 글자 받침용으로 코드에서 만든 텍스처. 에셋이 아니라서 직접 지운다(OnDestroy).
    private Texture2D scrimTexture;

    // 추가 생성 — 아래쪽 안내 문구("R 다시 내려간다  ESC 타이틀").
    //
    // 키를 글자로 적어두면 플레이어가 재시작 키를 바꿨을 때 이 문구만 옛 키를 말한다.
    // 실제 바인딩에서 읽어 채운다.
    [Tooltip("아래쪽 조작 안내. 비워두면 이름으로 찾는다(PressKeyText).")]
    [SerializeField] private TMP_Text pressKeyText;

    [Header("키 입력 — UI 버튼이 없어도 흐름을 확인할 수 있게")]

    // 수정(입력 중앙화): 재시작 액션도 InputBindings로 옮겼다. 설정의 조작 탭에서
    // '결과 화면 - 다시 시작'으로 바꿀 수 있다. 스킬 4와 같은 R이지만 쓰이는 화면이 달라
    // 겹침 검사에서 서로를 지우지 않는다.
    //
    // 재시작만 옮기고 타이틀(ESC)은 그대로 둔 이유: ESC는 <b>바꿀 수 없는 키</b>다. 이 게임에서 ESC는 화면을
    // 닫고 빠져나오는 키라, 그걸 다른 데로 옮길 수 있게 하면 창에서 못 나오는 상태를
    // 플레이어가 스스로 만들 수 있다. 바꿀 수 없는 키를 리바인딩 목록에 올리는 것은
    // 고를 수 없는 선택지를 보여주는 것과 같아서, 아예 성격이 다른 입력으로 남긴다.
    [Tooltip("타이틀로 나가는 키. 설정에서 바꾸지 않는 고정 키다.")]
    [SerializeField] private Key titleKey = Key.Escape;

    private void Awake()
    {
        // 인스펙터에 안 꽂혀 있어도 찾는다. 이 문구는 씬에 이미 놓여 있고, 새로 만든
        // 필드라 연결이 비어 있을 수밖에 없다. 이름으로 찾는 것은 타이틀 연출 도구와 같은 방식이다.
        if (pressKeyText == null)
        {
            var found = GameObject.Find("PressKeyText");
            if (found != null) pressKeyText = found.GetComponent<TMP_Text>();
        }
    }

    private void OnEnable()
    {
        // 설정에서 재시작 키를 바꾸면 안내 문구도 따라가야 한다.
        InputBindings.BindingsChanged += RefreshHint;
        RefreshHint();
    }

    private void OnDisable()
    {
        InputBindings.BindingsChanged -= RefreshHint;
    }

    /// <summary>아래쪽 조작 안내를 지금 바인딩으로 다시 쓴다.</summary>
    private void RefreshHint()
    {
        if (pressKeyText == null) return;

        // ESC는 바꿀 수 없는 고정 키라 글자로 적어도 어긋나지 않는다.
        pressKeyText.text = ControlHintLabel.Fill("{Restart} 다시 내려간다   ESC 타이틀", true);
    }

    private void Start()
    {
        Refresh();
    }

    private void Update()
    {
        if (InputBindings.RestartAction.WasPressedThisFrame())
        {
            OnRestart();
            return;
        }

        // 타이틀은 고정 키라 예전처럼 키보드를 직접 읽는다.
        // 키보드가 없는 환경(패드만 연결)에서 Keyboard.current가 null일 수 있어 먼저 확인한다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[titleKey].wasPressedThisFrame) OnTitle();
    }

    /// <summary>결과 값을 화면에 반영한다.</summary>
    private void Refresh()
    {
        if (result == null)
        {
            Debug.LogError("[ResultScreen] RunResultData가 비어 있다. 인스펙터에서 에셋을 연결해라.", this);
            return;
        }

        if (titleText != null)
        {
            // 추가 생성(2026-09-24, 결과 화면 줄바꿈) — "재가 되었다"가 "재가 되었" / "다"로 잘렸다. 제목 상자 폭보다 글자가 조금 넓어서
            // 마지막 한 글자만 다음 줄로 넘어갔다. 제목은 늘 한 줄이어야 하므로 줄바꿈을 끄고, 넘치는 만큼은 상자 밖으로 그린다
            // (가운데 정렬이라 양쪽으로 고르게 넘친다). 상자 폭을 씬에서 맞추는 대신 코드로 박는 이유: 문구가 바뀌어도 다시 안 깨진다.
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            titleText.overflowMode = TextOverflowModes.Overflow;

            titleText.text = result.Cleared ? "탈출했다" : "재가 되었다";
        }

        if (survivedText != null)
            survivedText.text = $"생존 {result.FormatSurvivedTime()}";

        if (killText != null)
            killText.text = $"처치 {result.KillCount}";

        // 추가 생성 — 도달한 방 수. 텍스트를 안 붙여뒀으면 조용히 넘어간다.
        if (roomText != null)
            roomText.text = $"{result.RoomsEntered}번째 방";

        ApplyBackground();

        // UI가 아직 없을 때도 값이 넘어왔는지 확인할 수 있게 남긴다. UI가 붙으면 지운다.
        Debug.Log($"[결과] 생존 {result.FormatSurvivedTime()} / 처치 {result.KillCount} / " +
                  $"{result.RoomsEntered}번째 방 / {(result.Cleared ? "클리어" : "사망")}");
    }

    /// <summary>
    /// 추가 생성: 결말에 맞는 배경으로 바꾼다.
    /// 클리어 배경이 아직 없으면 사망 배경을 그대로 쓰므로, 이미지가 한 장뿐인 지금도 문제없다.
    ///
    /// 수정(2026-09-27, 클리어 반복 장면) — 클리어면 배경을 바꾼 뒤 글자 받침을 깔고, 그 뒤에 반복 영상을 얹는다.
    /// </summary>
    private void ApplyBackground()
    {
        if (background == null) return;

        Sprite target = result.Cleared && clearedBackground != null
            ? clearedBackground
            : deathBackground;

        // 인스펙터에서 이미 배경을 넣어둔 상태일 수 있으니, 지정된 게 없으면 건드리지 않는다.
        if (target != null) background.sprite = target;

        // 추가 생성(2026-09-27) — 여기부터는 클리어일 때만. 사망 배경은 어두워서 받침도 영상도 필요 없다.
        if (!result.Cleared) return;

        // 받침과 영상은 제목 글자가 들어 있는 캔버스에 붙인다. 제목을 안 꽂았으면 씬의 캔버스를 찾는다.
        Canvas canvas = titleText != null ? titleText.canvas : FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // 붙이는 순서가 곧 그리는 순서다. 둘 다 캔버스의 첫 번째 자식으로 들어가므로 나중에 붙인 영상이 첫 번째(가장 먼저 = 가장 뒤)가 되고,
        // 받침은 두 번째, 원래 있던 글자들은 그 뒤(= 위)에 그려진다: 영상 → 받침 → 글자.
        AddClearedScrim(canvas);
        if (clearedLoop != null) BackgroundLoopVideo.Attach(background, canvas, clearedLoop);
    }

    /// <summary>
    /// 추가 생성(2026-09-27) — 글자 받침: 화면 왼쪽과 아래를 부드럽게 어둡게 하는 반투명 층을 캔버스에 깐다.
    ///
    /// 그라데이션 그림 파일을 따로 두지 않고 64x36 텍스처를 코드로 만든다. 작게 만들어도 화면에 늘릴 때 이중선형 필터가
    /// 부드럽게 이어 주고, 세기를 인스펙터 숫자로 바로 바꿔 볼 수 있다.
    /// </summary>
    private void AddClearedScrim(Canvas canvas)
    {
        if (clearedScrimLeft <= 0f && clearedScrimBottom <= 0f) return;

        const int width = 64;
        const int height = 36;
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            // 텍스처는 아래 줄부터 채운다 — v는 아래 0, 위 1.
            float v = (y + 0.5f) / height;

            // 아래: 바닥이 가장 어둡고 화면 높이 24% 지점에서 사라진다(조작 안내 한 줄을 받칠 만큼).
            float bottom = clearedScrimBottom * (1f - Mathf.SmoothStep(0f, 1f, v / 0.24f));

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;

                // 왼쪽: 화면 폭 30%까지는 가장 어둡고(제목·기록이 놓인 곳) 60%에서 사라진다(캐릭터는 그 오른쪽에 있다).
                float left = clearedScrimLeft * (1f - Mathf.SmoothStep(0f, 1f, (u - 0.30f) / 0.30f));

                // 두 층 겹치기: 하나만 있어도 그만큼 어둡고, 둘이 겹친 왼쪽 아래 모서리는 조금 더 어둡다.
                float darken = 1f - (1f - left) * (1f - bottom);
                pixels[y * width + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(ToBlendAlpha(darken) * 255f));
            }
        }

        scrimTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "ClearedScrim",
            wrapMode = TextureWrapMode.Clamp,   // 가장자리에서 반대편 값이 번져 나오지 않게
            filterMode = FilterMode.Bilinear,   // 작은 텍스처를 늘릴 때 계단 없이 부드럽게
        };
        scrimTexture.SetPixels32(pixels);
        scrimTexture.Apply(false, true); // 다 만들었으면 CPU 쪽 사본을 버린다(다시 고칠 일이 없다)

        var go = new GameObject("ClearedScrim", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsFirstSibling(); // 글자들보다 먼저(= 뒤에) 그린다

        // 화면 전체를 덮는다(앵커 0~1, 여백 0).
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<RawImage>();
        image.texture = scrimTexture;
        image.raycastTarget = false; // 받침이 버튼 클릭을 가로채지 않게
    }

    /// <summary>
    /// 추가 생성(2026-09-27) — "눈에 보이는 어두워짐"(0~1)을 섞기용 알파로 바꾼다.
    ///
    /// 리니어 색 공간에서는 반투명 섞기가 빛의 양(리니어 값)으로 일어난다. 그래서 검정을 알파 0.6으로 덮어도 화면 값(sRGB)은
    /// 0.4배가 아니라 약 0.66배(0.4^(1/2.2))밖에 안 어두워진다 — 포토샵에서 본 것보다 옅게 보인다.
    /// 보이는 대로 d만큼 어둡게 하려면 알파를 1 - (1 - d)^2.2로 올린다. 감마 색 공간이면 섞기가 화면 값으로 일어나므로 그대로 쓴다.
    /// </summary>
    private static float ToBlendAlpha(float darken)
    {
        return QualitySettings.activeColorSpace == ColorSpace.Linear
            ? 1f - Mathf.Pow(1f - darken, 2.2f)
            : darken;
    }

    private void OnDestroy()
    {
        // 추가 생성(2026-09-27) — 코드로 만든 텍스처는 에셋이 아니라서 씬이 바뀔 때 저절로 지워지지 않는다. 직접 지운다.
        if (scrimTexture != null) Destroy(scrimTexture);
    }

    /// <summary>재시작. UI 버튼의 OnClick에 연결한다.</summary>
    public void OnRestart()
    {
        GameFlow.StartNewRun();
    }

    /// <summary>타이틀로. UI 버튼의 OnClick에 연결한다.</summary>
    public void OnTitle()
    {
        GameFlow.LoadTitle();
    }
}
