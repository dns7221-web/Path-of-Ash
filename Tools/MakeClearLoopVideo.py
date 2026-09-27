# -*- coding: utf-8 -*-
"""
클리어 결과 화면 반복 영상 만들기 (2026-09-27)

고화질 정지 그림 한 장(GameClear_FinalRest_v1.png)에 숨쉬기·바람·풀린 끈의 흔들림을 입혀
3.6초짜리 반복 영상(GameClear_FinalRest_Loop.mp4)을 만든다. 결과 화면(ResultScreen)이 클리어일 때 튼다.

[왜 이렇게 만드나]
- 그림 생성기로 뽑은 8장(GameClear_FinalRest_Loop8_v1_raw.png)은 장마다 얼굴·검·계단 테두리까지 조금씩 달라서
  이어 붙이면 화면 전체가 떨린다. 한 장도 591x333으로 작아 결과 화면에 채우면 흐려진다.
- 그래서 원본 한 장은 그대로 두고, "움직일 곳의 픽셀을 조금씩 옮기는" 변형(warp)만 한다.
  움직이지 않는 곳(얼굴·검·계단·하늘)은 원본 픽셀 그대로라 떨림이 원리적으로 없다.

[어떻게 옮기나 — 역방향 매핑]
결과 그림의 각 픽셀 (x, y)가 "원본의 (x - dx, y - dy)에서 색을 가져온다"(cv2.remap).
정방향(원본 픽셀을 밀어 보내기)으로 하면 도착 자리가 겹치거나 비어 구멍이 생기지만, 역방향은 모든 픽셀이 반드시
어딘가에서 색을 받아 오므로 빈틈이 없다. dx, dy는 "영역 마스크 x 방향 x 시간 곡선"을 더해 만든다.

[망토는 따로 오려서 움직인다]
찢어진 망토 끝 뒤에는 산 능선이 있다. 그림 전체를 휘면 능선도 같이 출렁인다. 그래서 망토만 색으로 오려(배경 = 하늘·산·돌)
따로 옮기고, 망토가 비켜난 자리는 주변 색으로 메운 배경(cv2.inpaint)이 드러나게 한다(층 나누기).

[반복의 이음매]
모든 시간 곡선이 0초와 3.6초에서 0이고 기울기도 0이다(smoothstep). 그래서 마지막 장 다음에 첫 장이 와도 튀지 않는다.
이음매는 "쉼" 구간에 떨어지므로, 재생기가 반복할 때 한두 장 멈칫해도 눈에 띄지 않는다.

[필요한 것]  Python 3 + pip install numpy opencv-python imageio-ffmpeg
[실행]       python Tools/MakeClearLoopVideo.py   (저장소 맨 위 폴더에서)
             움직임 세기·시간을 바꾸려면 아래 "조정값"만 고치고 다시 돌린다. 결과 영상은 유니티가 알아서 다시 읽는다.
"""
import os
import subprocess

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # 저장소 맨 위(Tools의 부모)
RESULT_DIR = os.path.join(ROOT, "Assets", "Project", "Art", "Result")
SRC = os.path.join(RESULT_DIR, "GameClear_FinalRest_v1.png")
OUT = os.path.join(RESULT_DIR, "GameClear_FinalRest_Loop.mp4")

# ── 조정값 ──────────────────────────────────────────────────────────────
LOOP_SECONDS, FPS = 3.6, 30          # 한 바퀴 길이와 초당 장 수(108장)
HAIR_GUST = (-12.0, -4.5)            # 바람이 셀 때 머리카락 끝이 가는 거리(픽셀, 왼쪽·위)
HAIR_IDLE = 2.0                      # 바람이 없을 때도 머리카락 끝이 살짝 흔들리는 폭(픽셀)
CAPE_GUST = (-9.0, -3.6)             # 망토 끝이 가는 거리(픽셀)
CORD_SWING = (-6.0, -0.6)            # 끈 끝이 흔들리는 폭(픽셀)
BREATH_LIFT = 2.5                    # 들숨 때 어깨·가슴이 올라가는 높이(픽셀)


def smoothstep(x):
    """0~1 사이를 천천히 출발해 천천히 멈추게 잇는다. 양 끝의 기울기가 0이라 이어 붙여도 꺾이지 않는다."""
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def soft_polygon(points, blur, height, width):
    """다각형 마스크(안 1, 밖 0)를 만들고 가장자리를 흐린다. 경계가 딱 끊기면 옮긴 곳과 안 옮긴 곳 사이에 금이 보인다."""
    mask = np.zeros((height, width), np.float32)
    cv2.fillPoly(mask, [np.array(points, np.int32)], 1.0)
    return cv2.GaussianBlur(mask, (0, 0), blur)


# ── 시간 곡선: 기획의 한 바퀴(쉼 → 들숨 → 바람 → 날숨·망토가 늦게 가라앉음 → 쉼) ──
def breath(t):
    """숨. 쉼 0.35초 → 들숨 1.1초 → 멈춤 0.3초 → 날숨 1.7초(내쉬는 쪽을 더 길게 — 편안해 보인다) → 쉼."""
    if t < 0.35:
        return 0.0
    if t < 1.45:
        return float(smoothstep((t - 0.35) / 1.1))
    if t < 1.75:
        return 1.0
    return float(1 - smoothstep((t - 1.75) / 1.7))


def gust(t, start, peak, end):
    """바람 한 줄기. start~peak 동안 차오르고 peak~end 동안 가라앉는다."""
    return float(smoothstep((t - start) / (peak - start)) * (1 - smoothstep((t - peak) / (end - peak))))


def cord_swing(t):
    """풀린 끈. 바람에 밀린 뒤 진자처럼 흔들리며 잦아든다. 한 바퀴 끝(3.5초)까지 반드시 0으로 돌아오게 창을 씌운다."""
    if t < 1.5:
        return 0.0
    if t < 1.95:
        return float(smoothstep((t - 1.5) / 0.45))
    d = t - 1.95
    return float(np.exp(-2.2 * d) * np.cos(2 * np.pi * d / 0.9) * (1 - smoothstep((t - 3.1) / 0.4)))


class ClearLoop:
    """정지 그림 한 장과 영역 마스크를 들고, 시각 t의 한 장을 만들어 준다."""

    def __init__(self, source_path):
        image = cv2.imread(source_path)
        if image is None:
            raise FileNotFoundError(source_path)

        # H.264는 가로·세로가 짝수여야 하고, 유니티 BC7 압축은 4의 배수여야 한다 → 세로를 4의 배수로 자른다(941 → 940).
        self.image = image[: image.shape[0] // 4 * 4, : image.shape[1] // 4 * 4]
        h, w = self.image.shape[:2]
        self.ys, self.xs = np.mgrid[0:h, 0:w].astype(np.float32)
        xs, ys = self.xs, self.ys

        # ── 영역 마스크(원본 픽셀 좌표) — 1은 조정값만큼, 0은 전혀 안 움직인다 ──
        # 머리카락: 뿌리(머리 쪽) 0 → 끝 1
        self.hair = soft_polygon([(1100, 140), (1000, 125), (900, 135), (820, 148), (760, 185), (700, 228), (640, 285),
                                  (625, 350), (655, 425), (720, 455), (820, 432), (920, 400), (1000, 360), (1080, 318),
                                  (1125, 250)], 18, h, w) * smoothstep((1080 - xs) / 420)
        # 망토: 몸 쪽 0 → 찢어진 끝 1. 검은 빼고, 머리카락이 앞을 덮는 윗단(y 400 위)도 뺀다 — 층 순서가 꼬이지 않게
        cape_outline = [(935, 355), (850, 375), (760, 398), (650, 428), (555, 468), (540, 522), (598, 562), (648, 602),
                        (700, 632), (780, 622), (860, 590), (930, 540), (965, 450)]
        sword = soft_polygon([(870, 585), (890, 632), (700, 745), (495, 765), (470, 700), (640, 640), (830, 585)], 8, h, w)
        self.cape = (soft_polygon(cape_outline, 14, h, w) * smoothstep((935 - xs) / 360) * (1 - sword)
                     * smoothstep((ys - 400) / 60))
        # 끈: 쥔 손 0 → 늘어진 끝 1
        self.cord = soft_polygon([(1448, 628), (1512, 618), (1566, 700), (1602, 778), (1604, 900), (1496, 904),
                                  (1438, 780), (1436, 680)], 6, h, w) * smoothstep((ys - 640) / 200)
        # 숨: 어깨·가슴 1 → 허리 아래 0(무릎 위에 둔 손은 가만히)
        self.body = soft_polygon([(950, 110), (1250, 55), (1460, 150), (1570, 330), (1570, 470), (1400, 520), (1100, 540),
                                  (975, 500), (925, 350)], 30, h, w) * (1 - smoothstep((ys - 420) / 120))

        # ── 망토 오리기: 배경(하늘·산·돌)을 색으로 찾고, 망토 영역 안에서 배경이 아닌 것은 전부 망토로 본다 ──
        # 망토를 색으로 직접 찾으면 회갈색 부분이 반투명으로 잡혀 뒤의 메운 하늘이 비쳐 보였다.
        hsv = cv2.cvtColor(self.image, cv2.COLOR_BGR2HSV).astype(np.float32)
        hue, sat, val = hsv[..., 0], hsv[..., 1], hsv[..., 2]
        sky = np.clip((val - 175) / 15, 0, 1) * np.clip((115 - sat) / 15, 0, 1)            # 밝고 채도 낮음
        stone = np.clip((45 - sat) / 10, 0, 1) * np.clip((val - 85) / 15, 0, 1)            # 회색(돌·먼 산)
        mountain = (((hue >= 95) & (hue <= 175)).astype(np.float32)                         # 보랏빛 산
                    * np.clip((115 - sat) / 15, 0, 1) * np.clip((val - 75) / 15, 0, 1))
        cape_alpha = 1 - np.clip(np.maximum(np.maximum(sky, stone), mountain), 0, 1)
        cape_alpha = cv2.GaussianBlur(cv2.morphologyEx(cape_alpha, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8)), (0, 0), 0.7)
        cape_alpha *= soft_polygon(cape_outline, 2, h, w) > 0.5
        silver_hair = np.clip((val - 165) / 15, 0, 1) * np.clip((70 - sat) / 15, 0, 1)       # 은발은 망토 층이 아니다
        cape_alpha *= 1 - silver_hair
        count, labels, stats, _ = cv2.connectedComponentsWithStats((cape_alpha > 0.5).astype(np.uint8))
        for i in range(1, count):                                                          # 떨어진 작은 점은 버린다
            if stats[i, cv2.CC_STAT_AREA] < 30:
                cape_alpha[labels == i] = 0
        self.cape_alpha = cape_alpha.astype(np.float32)

        # 망토가 비켜난 자리에 보일 배경: 망토가 있던 곳만 주변 색으로 메우고, 그 밖은 원본 그대로 둔다
        hole = (cv2.dilate((cape_alpha > 0.2).astype(np.uint8), np.ones((7, 7), np.uint8)) > 0).astype(np.uint8)
        filled = cv2.inpaint(self.image, hole, 6, cv2.INPAINT_TELEA)
        use = cv2.GaussianBlur(cv2.dilate((cape_alpha > 0.05).astype(np.float32), np.ones((5, 5), np.uint8)), (0, 0), 1.0)[..., None]
        self.backdrop = (self.image.astype(np.float32) * (1 - use) + filled.astype(np.float32) * use).astype(np.uint8)

    def _warp(self, source, dx, dy, interpolation=cv2.INTER_CUBIC):
        """역방향 매핑: 결과의 (x, y)가 원본의 (x - dx, y - dy)에서 색을 가져온다."""
        return cv2.remap(source, (self.xs - dx).astype(np.float32), (self.ys - dy).astype(np.float32),
                         interpolation, borderMode=cv2.BORDER_REFLECT)

    def frame(self, t):
        """시각 t(초)의 한 장(BGR)을 만든다."""
        hair_gust = gust(t, 1.35, 1.95, 3.2)
        cape_gust = gust(t, 1.6, 2.3, 3.5)                  # 망토는 머리카락보다 늦게 오고 늦게 가라앉는다(무거우니까)
        idle = np.sin(2 * np.pi * t / LOOP_SECONDS)          # 한 바퀴에 한 번 도는 아주 작은 흔들림 — 완전히 멈춘 순간이 없게
        swing = cord_swing(t)

        # 머리카락·끈·숨은 그림 전체를 한 번에 휜다(뒤가 하늘·어두운 망토처럼 매끈해서 같이 휘어도 티가 안 난다)
        dx = self.hair * (HAIR_GUST[0] * hair_gust - HAIR_IDLE * idle) + self.cord * (CORD_SWING[0] * swing)
        dy = self.hair * (HAIR_GUST[1] * hair_gust) + self.body * (-BREATH_LIFT * breath(t)) + self.cord * (CORD_SWING[1] * swing)
        base = self._warp(self.image, dx, dy).astype(np.float32)
        if cape_gust <= 0.001:
            return base.astype(np.uint8)

        # 망토: 오린 망토만 옮겨, 메운 배경(머리카락·숨과 똑같이 휜 것) 위에 얹는다
        cdx, cdy = self.cape * (CAPE_GUST[0] * cape_gust), self.cape * (CAPE_GUST[1] * cape_gust)
        cape_rgb = self._warp(self.image, cdx, cdy).astype(np.float32)
        alpha = self._warp(self.cape_alpha, cdx, cdy, cv2.INTER_LINEAR)[..., None]
        layered = cape_rgb * alpha + self._warp(self.backdrop, dx, dy).astype(np.float32) * (1 - alpha)
        use_layer = np.clip(self.cape * 6, 0, 1)[..., None]  # 망토가 움직이는 곳에서만 층 결과를 쓴다
        return np.clip(base * (1 - use_layer) + layered * use_layer, 0, 255).astype(np.uint8)


def find_ffmpeg():
    """imageio-ffmpeg에 들어 있는 ffmpeg를 쓴다. 없으면 PATH의 ffmpeg."""
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except ImportError:
        return "ffmpeg"


def main():
    loop = ClearLoop(SRC)
    h, w = loop.image.shape[:2]
    count = int(round(LOOP_SECONDS * FPS))

    # 인코딩 설정은 이미 게임에서 잘 도는 궁극기 컷인 영상(boss_ultimate_v3.mp4)과 맞췄다:
    # H.264 Constrained Baseline, yuv420p, bt709. 색 변환 행렬도 bt709로 해야 표시와 어긋나지 않는다
    # (ffmpeg 기본은 bt601이라 태그만 709로 달면 붉은 끈 같은 색이 살짝 틀어진다).
    # -g 30: 1초마다 키 프레임 — 반복할 때 처음으로 되감는 비용이 작아진다.
    command = [find_ffmpeg(), "-y", "-loglevel", "error",
               "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{w}x{h}", "-r", str(FPS), "-i", "-",
               "-vf", "scale=out_color_matrix=bt709:out_range=tv",
               "-c:v", "libx264", "-profile:v", "baseline", "-preset", "slow", "-crf", "18", "-g", str(FPS),
               "-pix_fmt", "yuv420p", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
               "-color_range", "tv", "-movflags", "+faststart", "-an", OUT]
    encoder = subprocess.Popen(command, stdin=subprocess.PIPE)
    for i in range(count):
        encoder.stdin.write(loop.frame(i / FPS).tobytes())
    encoder.stdin.close()
    if encoder.wait() != 0:
        raise RuntimeError("ffmpeg 인코딩 실패")

    # 이음매 확인: 첫 장과 "마지막 장 다음 장(= 한 바퀴 뒤)"이 같아야 한다
    seam = float(np.abs(loop.frame(0).astype(int) - loop.frame(count / FPS).astype(int)).mean())
    print(f"{count}장 {w}x{h} {FPS}fps → {OUT} ({os.path.getsize(OUT) / 1024:.0f}KB), 이음매 차이 {seam:.3f}")


if __name__ == "__main__":
    main()
