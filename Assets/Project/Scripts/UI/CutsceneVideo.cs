using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 추가 생성(2026-09-19, 보스 궁극기 영상) — 영상 컷인을 화면 위에 재생한다.
///
/// 흐름: 위아래 검은 띠가 들어오고 뒤 화면이 어두워진다 → 영상 재생 → 영상의 마지막 흰 화면이 흐려지며 게임 화면으로 돌아온다
/// → 띠가 빠진다 → <see cref="Finished"/>. 보스 궁극기에서는 Finished를 받은 쪽이 방 전체 폭발을 터뜨린다.
///
/// <b>왜 VideoPlayer를 화면에 바로 그리지 않고 RenderTexture → RawImage로 보내나.</b> 카메라 근·원 평면에 그리는 방식은
/// UI 위에 띠를 얹거나 흰 화면을 서서히 걷어내는 연출을 할 수 없다. RawImage는 다른 UI처럼 투명도를 다루고,
/// 화면비가 16:9가 아닐 때도 AspectRatioFitter로 늘어나지 않게 맞출 수 있다.
///
/// <b>시간은 전부 실제 시간(unscaled)이다.</b> 궁극기 동안 게임은 PauseGate로 멈춰 있어서(timeScale 0) 게임 시간을 쓰면
/// 영상도 띠도 멈춘다. VideoPlayer의 시간 기준도 같은 이유로 UnscaledGameTime으로 둔다.
///
/// <b>미리 준비(Prepare)한다.</b> 영상 파일은 처음 여는 순간 디코더를 띄우느라 멈칫한다. 플레이 영상 분석에서 첫 사용 끊김이
/// 이미 잡힌 적이 있어서, 시작할 때 준비해 두고 재생 뒤에도 다음 재생을 위해 다시 준비한다.
/// </summary>
[DisallowMultipleComponent]
public class CutsceneVideo : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("재생할 VideoPlayer. Render Mode는 Render Texture, 대상은 아래 화면(RawImage)의 텍스처와 같아야 한다.")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Tooltip("영상을 보여줄 판. 텍스처 = VideoPlayer의 Target Texture.")]
    [SerializeField] private RawImage screen;

    [Tooltip("뒤 게임 화면을 어둡게 덮는 검은 판.")]
    [SerializeField] private Image dim;

    [Tooltip("위쪽 검은 띠(위에 붙어 있고 높이로 들어온다).")]
    [SerializeField] private RectTransform topBar;

    [Tooltip("아래쪽 검은 띠(아래에 붙어 있고 높이로 들어온다).")]
    [SerializeField] private RectTransform bottomBar;

    [Header("연출")]
    [Tooltip("띠 높이(화면 높이 대비). 영상 위아래를 조금 가린다 — 너무 두꺼우면 그림의 왕관이 잘린다.")]
    [SerializeField, Range(0f, 0.2f)] private float barHeight = 0.07f;

    [Tooltip("띠가 들어오고 나가는 시간(실제 초).")]
    [SerializeField, Min(0f)] private float barSeconds = 0.2f;

    [Tooltip("뒤 화면을 어둡게 하는 정도(0~1).")]
    [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.6f;

    [Tooltip("영상이 끝난 뒤 마지막 흰 화면이 흐려지며 게임으로 돌아오는 시간(실제 초).")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.25f;

    /// <summary>재생이 끝나고 띠까지 다 빠졌을 때(건너뛴 경우 포함) 한 번 울린다.</summary>
    public event Action Finished;

    /// <summary>지금 컷인이 돌고 있는가(띠가 들어오는 순간부터 다 빠질 때까지).</summary>
    public bool IsPlaying { get; private set; }

    private Coroutine routine;
    private bool videoEnded;

    private void Awake()
    {
        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = false;
            videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            videoPlayer.loopPointReached += OnVideoEnded;

            // 추가 생성(2026-09-19) — 해독 실패를 콘솔에 남긴다. 영상이 안 풀리면 화면은 검은색일 뿐 아무 말이 없어서,
            // 파일 형식 문제인지 연결 문제인지 가릴 수 없었다(첫 판 v1이 그랬다).
            videoPlayer.errorReceived += OnVideoError;
        }

        ApplyCover(0f);
        SetScreenAlpha(0f);
    }

    private void OnDestroy()
    {
        if (videoPlayer == null) return;
        videoPlayer.loopPointReached -= OnVideoEnded;
        videoPlayer.errorReceived -= OnVideoError;
    }

    /// <summary>
    /// 추가 생성(2026-09-19) — 영상을 열거나 풀지 못했을 때. 컷인이 멈춰 버리지 않게 끝난 것으로 치고 넘어간다 —
    /// 궁극기라면 영상 없이 바로 폭발로 이어지는 편이, 검은 화면에 갇히는 것보다 낫다.
    /// </summary>
    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[컷인 영상] 재생 오류: {message} ({(source.clip != null ? source.clip.name : "영상 없음")})", this);
        videoEnded = true;
    }

    private void Start()
    {
        if (videoPlayer != null) videoPlayer.Prepare();
    }

    /// <summary>컷인을 처음부터 재생한다. 이미 돌고 있으면 무시한다.</summary>
    public void Play()
    {
        if (IsPlaying || videoPlayer == null) return;
        routine = StartCoroutine(PlayRoutine());
    }

    /// <summary>
    /// 영상을 건너뛴다. 결과는 끝까지 본 것과 같다 — 흰 화면이 걷히고 Finished가 울린다.
    /// 첫 관람에도 건너뛸 수 있게 할지는 부르는 쪽(보스)이 정한다.
    /// </summary>
    public void Skip()
    {
        if (!IsPlaying) return;
        videoEnded = true;
    }

    private void OnVideoEnded(VideoPlayer source) => videoEnded = true;

    private IEnumerator PlayRoutine()
    {
        IsPlaying = true;
        videoEnded = false;

        // 1) 띠와 어둠이 들어온다.
        yield return Animate(barSeconds, u => ApplyCover(u));

        // 2) 준비가 덜 됐으면 기다린다. 보통은 Start에서 이미 끝나 있다.
        // 수정(2026-09-19) — 준비 중 오류가 나면(OnVideoError가 videoEnded를 켠다) 기다림을 멈춘다. 안 그러면 여기서 영영 돈다.
        if (!videoPlayer.isPrepared)
        {
            videoPlayer.Prepare();
            while (!videoPlayer.isPrepared && !videoEnded) yield return null;
        }

        // 지난 재생의 마지막 칸(흰 화면)이 한 프레임 보이지 않게 텍스처를 검게 비운 뒤 띄운다.
        if (!videoEnded)
        {
            // 추가 생성(2026-09-19) — 띄우기 직전에 화면 판 크기를 맞춘다. 이유는 FitScreen 참고.
            FitScreen();
            ClearTexture();
            videoPlayer.time = 0;
            videoPlayer.Play();
            SetScreenAlpha(1f);
        }

        // 3) 끝까지(또는 건너뛸 때까지) 기다린다.
        while (!videoEnded) yield return null;

        // 4) 마지막 흰 화면을 걷어내며 게임 화면으로 돌아오고, 띠가 빠진다.
        yield return Animate(fadeOutSeconds, u => SetScreenAlpha(1f - u));
        videoPlayer.Stop();
        yield return Animate(barSeconds, u => ApplyCover(1f - u));

        IsPlaying = false;
        routine = null;

        // 다음 재생도 끊기지 않게 다시 준비해 둔다.
        videoPlayer.Prepare();
        Finished?.Invoke();
    }

    /// <summary>실제 시간으로 0→1을 흘리며 매 프레임 apply를 부른다.</summary>
    private static IEnumerator Animate(float seconds, Action<float> apply)
    {
        if (seconds <= 0f)
        {
            apply(1f);
            yield break;
        }

        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            apply(Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }

        apply(1f);
    }

    /// <summary>띠 높이와 어둠을 u(0~1)만큼 적용한다.</summary>
    private void ApplyCover(float u)
    {
        float height = barHeight * u * CanvasHeight();
        if (topBar != null) topBar.sizeDelta = new Vector2(topBar.sizeDelta.x, height);
        if (bottomBar != null) bottomBar.sizeDelta = new Vector2(bottomBar.sizeDelta.x, height);

        if (dim != null)
        {
            Color c = dim.color;
            c.a = dimAlpha * u;
            dim.color = c;
        }
    }

    private void SetScreenAlpha(float alpha)
    {
        if (screen == null) return;
        screen.enabled = alpha > 0f;
        Color c = screen.color;
        c.a = alpha;
        screen.color = c;
    }

    /// <summary>
    /// 추가 생성(2026-09-19, 검은 화면) — 영상 화면 판을 캔버스 안에 16:9로 맞춘다(남는 쪽은 뒤의 어둠이 채운다).
    ///
    /// 처음에는 AspectRatioFitter(Fit In Parent)에 맡겼는데, 도구로 씬을 만들 때 판 크기가 0×0으로 저장되고 실행 중에도
    /// 바로잡히지 않아 <b>영상은 끝까지 도는데 그림이 하나도 안 보였다</b>(오류도 없었다). 재생하는 순간에는 캔버스 크기가
    /// 확실히 정해져 있으므로 그때 직접 계산한다. 가운데 기준으로 두고 크기만 넣는다.
    /// </summary>
    private void FitScreen()
    {
        if (screen == null) return;

        var canvasRect = transform as RectTransform;
        if (canvasRect == null) return;

        Vector2 size = canvasRect.rect.size;
        if (size.x <= 0f || size.y <= 0f) size = new Vector2(1920f, 1080f);

        const float aspect = 16f / 9f;
        Vector2 fitted = size.x / size.y > aspect
            ? new Vector2(size.y * aspect, size.y)
            : new Vector2(size.x, size.x / aspect);

        RectTransform rect = screen.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = fitted;
    }

    /// <summary>캔버스 기준 화면 높이. 띠 높이를 비율로 적으려고 쓴다.</summary>
    private float CanvasHeight()
    {
        var canvasRect = transform as RectTransform;
        return canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : 1080f;
    }

    private void ClearTexture()
    {
        var target = videoPlayer.targetTexture;
        if (target == null) return;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = previous;
    }
}
