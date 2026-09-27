using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 추가 생성(2026-09-27, 클리어 결과 반복 장면) — 월드에 깔린 배경 스프라이트 위에 반복 영상을 정확히 겹쳐 튼다.
///
/// 결과 화면 배경은 UI가 아니라 카메라가 그리는 SpriteRenderer다. 영상은 SpriteRenderer에 바로 그릴 수 없어서
/// (스프라이트의 텍스처는 바꿀 수 없다) 궁극기 컷인(CutsceneVideo)과 같은 길로 보낸다:
/// <b>VideoPlayer → RenderTexture → RawImage</b>. RawImage는 캔버스 맨 앞 자식으로 붙여 글자들 뒤에 깔린다.
///
/// <b>배경 스프라이트와 한 치도 어긋나지 않게 겹치는 방법.</b> RawImage를 화면 전체로 늘리지 않고, 배경 스프라이트가
/// 화면에서 차지하는 자리(Camera.WorldToViewportPoint로 구한 0~1 좌표)를 그대로 앵커로 쓴다. 배경은 화면보다 조금 커서
/// 가장자리가 잘려 보이는데(사망 배경과 같은 구도), 앵커도 화면 밖(0 미만·1 초과)까지 똑같이 나가므로 영상도 같은 만큼 잘린다.
/// 캔버스가 Screen Space - Overlay(화면 전체 = 뷰포트 0~1)일 때 성립한다. 결과 씬 캔버스가 그렇다.
///
/// <b>첫 장이 나오기 전의 빈 화면.</b> 영상은 디코더를 여는 동안 몇 프레임 늦게 나온다. 그사이 RenderTexture는 비어(검정)
/// 있어서 그대로 보이면 번쩍인다. 그래서 만들자마자 지금 배경 그림을 RenderTexture에 복사해 둔다(Graphics.Blit).
/// 영상의 첫 장이 바로 그 그림이라, 영상이 시작돼도 이음매가 보이지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage), typeof(VideoPlayer))]
public class BackgroundLoopVideo : MonoBehaviour
{
    private SpriteRenderer background;   // 겹칠 배경. 화면에서 차지하는 자리를 여기서 읽는다
    private RawImage screen;
    private VideoPlayer player;
    private RenderTexture renderTexture;
    private Vector2Int fittedScreenSize;  // 마지막으로 자리를 맞춘 화면 크기. 바뀌면 다시 맞춘다

    /// <summary>
    /// 배경 스프라이트 위에 반복 영상을 얹는다. 캔버스에 오브젝트를 새로 만들어 붙이고, 그 컴포넌트를 돌려준다.
    /// 씬에 미리 만들어 두지 않는 이유: 클리어일 때만 필요하고, 사망 화면에는 아무것도 남기지 않기 위해서다.
    /// </summary>
    /// <param name="target">겹칠 배경 스프라이트.</param>
    /// <param name="canvas">RawImage를 붙일 캔버스(Screen Space - Overlay).</param>
    /// <param name="clip">반복 재생할 영상.</param>
    public static BackgroundLoopVideo Attach(SpriteRenderer target, Canvas canvas, VideoClip clip)
    {
        if (target == null || canvas == null || clip == null)
        {
            Debug.LogWarning("[배경 반복 영상] 배경·캔버스·영상 중 빈 것이 있어 영상 없이 정지 그림만 쓴다.");
            return null;
        }

        var go = new GameObject("BackgroundLoopVideo", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        // 캔버스는 자식 순서대로 그린다. 맨 앞 자식이어야 제목·기록 글자 뒤에 깔린다.
        go.transform.SetAsFirstSibling();

        // RequireComponent 덕분에 RawImage(와 CanvasRenderer)·VideoPlayer가 같이 붙는다.
        var loop = go.AddComponent<BackgroundLoopVideo>();
        loop.Begin(target, clip);
        return loop;
    }

    /// <summary>RenderTexture를 만들고, 배경 그림으로 미리 채우고, 자리를 맞춘 뒤 재생을 시작한다.</summary>
    private void Begin(SpriteRenderer target, VideoClip clip)
    {
        background = target;
        screen = GetComponent<RawImage>();
        player = GetComponent<VideoPlayer>();
        screen.raycastTarget = false; // 배경이라 클릭을 받을 이유가 없다(버튼 클릭을 가로채지 않게)

        // sRGB RenderTexture인 이유: 이 프로젝트는 리니어 색 공간이다. sRGB로 저장된 영상을 sRGB가 아닌 텍스처에 담으면
        // 읽을 때 변환이 한 번 빠져 화면이 허옇게 뜬다. 궁극기 컷인의 RenderTexture(R8G8B8A8_SRGB)와 같은 설정이다.
        var descriptor = new RenderTextureDescriptor((int)clip.width, (int)clip.height, RenderTextureFormat.ARGB32, 0)
        {
            sRGB = true,
        };
        renderTexture = new RenderTexture(descriptor)
        {
            name = "BackgroundLoopVideo",
            wrapMode = TextureWrapMode.Clamp, // 가장자리에서 반대편 픽셀이 번져 나오지 않게
        };
        renderTexture.Create();

        // 첫 장이 나오기 전의 검은 화면 대신 지금 배경 그림을 넣어 둔다(클래스 주석 참고).
        if (background.sprite != null) Graphics.Blit(background.sprite.texture, renderTexture);

        screen.texture = renderTexture;
        FitToBackground();

        player.playOnAwake = false;
        player.source = VideoSource.VideoClip;
        player.clip = clip;
        player.isLooping = true;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = renderTexture;
        player.audioOutputMode = VideoAudioOutputMode.None; // 소리 없는 영상이다(오디오 트랙을 찾느라 시작이 늦어지지 않게)

        // 궁극기 컷인과 같은 두 설정 — 게임 시간(timeScale)과 무관하게 흐르고, 늦어도 장을 버리지 않는다.
        // 결과 화면은 판이 끝난 순간의 timeScale(히트스톱 0 등)을 물려받을 수 있다. 게임 시간으로 돌리면 영상이 멈춘다.
        player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        player.skipOnDrop = false;

        // Prepare를 따로 부르지 않아도 Play가 준비부터 한다. 준비되는 동안은 위에서 채운 배경 그림이 보인다.
        player.Play();
    }

    private void Update()
    {
        // 창 크기(가로세로 비율)가 바뀌면 배경이 화면에서 차지하는 자리도 바뀐다. 그때만 다시 맞춘다 —
        // 매 프레임 앵커를 쓰면 캔버스가 매 프레임 배치를 다시 계산한다.
        if (Screen.width != fittedScreenSize.x || Screen.height != fittedScreenSize.y) FitToBackground();
    }

    /// <summary>
    /// RawImage를 배경 스프라이트가 화면에서 차지하는 자리에 딱 맞춘다(클래스 주석 참고).
    /// 카메라를 못 찾으면 화면 전체를 덮는다 — 배경이 화면을 거의 꽉 채우는 지금 구도에서는 차이가 작다.
    /// </summary>
    private void FitToBackground()
    {
        fittedScreenSize = new Vector2Int(Screen.width, Screen.height);

        var rect = (RectTransform)transform;
        Camera cam = Camera.main;
        if (cam != null && background != null)
        {
            // Renderer.bounds — 유니티가 계산해 주는 월드 기준 테두리 상자. 뷰포트 좌표는 화면 왼쪽 아래 (0,0) ~ 오른쪽 위 (1,1)이고,
            // 앵커도 부모(= 화면 전체 캔버스)의 왼쪽 아래 (0,0) ~ 오른쪽 위 (1,1)이라 그대로 넣으면 된다.
            Bounds bounds = background.bounds;
            rect.anchorMin = cam.WorldToViewportPoint(bounds.min);
            rect.anchorMax = cam.WorldToViewportPoint(bounds.max);
        }
        else
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
        }

        // 앵커가 곧 크기다 — 여백(offset)은 0.
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        // RenderTexture는 GPU 메모리를 직접 잡는다. 씬이 바뀌어도 알아서 풀리지 않으므로(에셋이 아니라 코드로 만든 것) 직접 놓는다.
        if (player != null) player.Stop();
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
