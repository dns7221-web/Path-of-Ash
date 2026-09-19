"""보스 궁극기 영상 만들기 — 생성 그림을 이어 5초짜리 컷인 영상(mp4)으로 만든다.

사용: python Tools/make_boss_ultimate_video.py [--preview]
입력: Assets/Project/Art/BossUltimateVideo/keyframes/*.png (첫 판 8장)
      Assets/Project/Art/BossUltimateVideo/generated_v2/*.png (사이 그림 9장 + 효과 3장)
      모두 1672x941(16:9). 프롬프트는 같은 폴더의 boss_ultimate_v1/v2.prompt.txt
출력: Assets/Project/Art/BossUltimateVideo/boss_ultimate_v3.mp4 (1920x1080, 30fps, H.264, 소리 없음)

v2까지는 그림 한 장이 곧 컷 하나였다. 컷마다 카메라가 달라서 움직임이 아니라 "장면 넘김"으로 보였다.
v3는 같은 카메라로 자세만 바꾼 사이 그림을 받아, 컷 하나 안에서 그림을 바꿔 가며 움직인다:
  - 컷 = 카메라 하나. 컷 안에서 그림이 바뀌어도 확대·초점·흔들림은 끊기지 않고 이어진다
  - 효과 그림(검은 배경)은 컷 그림 위에 빛으로 더한다(screen). 커지며 사라지게 해서 폭발·충격파가 실제로 퍼진다
  - 빛 번짐: 그림의 밝은 곳을 흐리게 번져 더한다. 칼을 들고 멈춘 동안 점점 세져 힘이 모이는 것처럼 보인다
  - 올라가는 불씨, 번쩍임, 흔들림, 끝의 흰 화면은 전과 같다
04·05(칼 쥐기·들기)는 카메라가 따로 놀아 끊겨 보이던 주범이라 06 기준의 연속 동작(06a→06b→06→06c)으로 바꿨다.
게임은 마지막 흰 화면(#FFF2C0)에서 방 전체 폭발로 넘어간다 — 그래서 영상은 흰색으로 끝난다.
타이밍은 아래 SHOTS 표만 고치면 된다. --preview는 확인용 모음 그림만 만든다.
"""
import math
import pathlib
import random
import subprocess
import sys
from dataclasses import dataclass, field

import imageio_ffmpeg
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
FOLDER = ROOT / "Assets" / "Project" / "Art" / "BossUltimateVideo"
OUTPUT = FOLDER / "boss_ultimate_v3.mp4"
PREVIEW = FOLDER / "boss_ultimate_v3_preview.png"

W, H, FPS = 1920, 1080, 30
DURATION = 5.0
WHITE = (255, 242, 192)     # 흰 불 #FFF2C0 — 게임의 폭발 첫 색과 같다
EMBERS = [(255, 176, 0), (240, 96, 0), (255, 242, 192), (138, 42, 16)]


@dataclass
class Pose:
    """컷 안의 그림 한 장. at부터 다음 그림이 들어올 때까지 보인다."""
    image: str                  # FOLDER 기준 경로
    at: float                   # 들어오는 시각(초)
    fade: float = 0.0           # 앞 그림에서 섞이며 들어오는 시간(0 = 바로 바뀜 — 동작은 끊어 바꾸는 편이 또렷하다)
    glow: tuple = (0.0, 0.0)    # 빛 번짐 세기, 이 그림이 보이는 동안 시작→끝


@dataclass
class Fx:
    """검은 배경 효과 그림을 컷 그림 위에 빛으로 더한다. 좌표는 모두 그림 안의 비율(0~1)."""
    image: str
    t0: float
    t1: float
    at: tuple                   # 컷 그림에서 효과 기준점이 올 자리
    pivot: tuple                # 효과 그림 안의 기준점(폭발 중심, 고리 중심, 궤적 끝)
    scale: tuple                # 효과 폭 ÷ 컷 그림 폭, 시작→끝(빠르게 커지다 멈춘다)
    alpha: tuple = (1.0, 0.0)   # 세기 시작→끝(처음엔 버티다 끝에서 빨리 꺼진다)
    rotate: float = 0.0         # 기준점을 중심으로 돌리는 각도(도, 반시계)


@dataclass
class Shot:
    """카메라 하나. 확대·초점·흔들림은 컷 전체에 걸쳐 이어진다."""
    t0: float
    t1: float
    zoom: tuple                 # 확대 시작→끝(1.0 = 그림 전체)
    focus: tuple                # 초점 시작(x,y)→끝(x,y), 그림 안의 비율
    poses: list
    shake: tuple = (0, 0)       # 흔들림 세기(px) 시작→끝
    punch: bool = False         # True = 처음에 빠르게 움직이고 끝에서 멈춘다(충격에 밀려나는 카메라)
    fx: list = field(default_factory=list)
    note: str = ""


K, G = "keyframes/", "generated_v2/"

SHOTS = [
    Shot(0.00, 1.05, zoom=(1.00, 1.10), focus=((0.50, 0.52), (0.50, 0.50)),
         poses=[Pose(G + "01a_crown_before.png", 0.00),
                Pose(K + "01_crown.png", 0.36, fade=0.14),
                Pose(G + "01b_crown_complete.png", 0.72, glow=(0.7, 0.2))],
         note="왕관 — 꺼져 있던 왕관에 조각이 날아와 붙으며 번쩍"),
    Shot(1.05, 1.65, zoom=(1.08, 1.13), focus=((0.50, 0.62), (0.50, 0.38)),
         poses=[Pose(K + "02_queen_wide.png", 1.05)],
         note="보스 전신 — 아래에서 얼굴로"),
    Shot(1.65, 2.40, zoom=(1.00, 1.08), focus=((0.52, 0.42), (0.53, 0.38)),
         poses=[Pose(G + "03a_eyes_closed.png", 1.65),
                Pose(G + "03b_eyes_half_open.png", 1.97),
                Pose(K + "03_eye_open.png", 2.08, glow=(0.5, 0.15))],
         note="얼굴 — 감은 눈 → 반쯤 → 뜬다"),
    Shot(2.40, 3.50, zoom=(1.02, 1.10), focus=((0.50, 0.50), (0.50, 0.44)), shake=(0, 5),
         poses=[Pose(G + "06a_sword_low.png", 2.40),
                Pose(G + "06b_sword_lifting.png", 2.74),
                Pose(K + "06_raise_top.png", 2.88, glow=(0.0, 0.8)),
                Pose(G + "06c_sword_slam_smear.png", 3.38, glow=(0.4, 0.4))],
         note="대검 — 낮게 쥔 칼을 들어 머리 위에서 힘을 모으고(흔들림·빛이 차오른다), 내려친다(잔상)"),
    Shot(3.50, 3.62, zoom=(1.18, 1.18), focus=((0.50, 0.60), (0.50, 0.60)),
         poses=[Pose(G + "07a_first_contact.png", 3.50)],
         # 궤적 그림은 끝이 왼쪽을 향한다 — 45도 돌려 오른쪽 위(06에서 칼끝이 있던 쪽)에서 내려와 닿은 모양으로
         fx=[Fx(G + "FX3_sword_trail.png", 3.50, 3.62, at=(0.45, 0.83), pivot=(0.016, 0.80),
                scale=(0.65, 0.65), alpha=(0.9, 0.0), rotate=45)],
         note="칼끝이 바닥에 닿는 한순간 — 칼이 지나온 궤적이 꺼진다"),
    Shot(3.62, 4.05, zoom=(1.18, 1.06), focus=((0.50, 0.60), (0.50, 0.55)), shake=(18, 0), punch=True,
         poses=[Pose(K + "07_impact.png", 3.62, glow=(0.6, 0.2))],
         fx=[Fx(G + "FX1_explosion.png", 3.62, 4.00, at=(0.46, 0.86), pivot=(0.50, 0.64),
                scale=(0.45, 1.50), alpha=(1.0, 0.0))],
         note="폭발 — 불꽃이 퍼지고 카메라가 뒤로 밀린다(07a와 같은 카메라에서 이어진다)"),
    Shot(4.05, 4.70, zoom=(1.10, 1.02), focus=((0.50, 0.55), (0.50, 0.50)), shake=(10, 0),
         poses=[Pose(K + "08_impact_wide.png", 4.05),
                Pose(G + "08b_white_fire.png", 4.40, fade=0.06)],
         fx=[Fx(G + "FX2_shockwave_ring.png", 4.05, 4.42, at=(0.47, 0.80), pivot=(0.50, 0.52),
                scale=(0.35, 2.00), alpha=(0.9, 0.0))],
         note="충격파 — 고리가 퍼지고 흰 불에 삼켜진다"),
]

# (시각, 세기, 사라지는 시간) — 흰 번쩍임
FLASHES = [(0.72, 0.60, 0.14), (2.08, 0.15, 0.15), (3.38, 0.30, 0.08),
           (3.50, 0.50, 0.07), (3.62, 0.95, 0.12), (4.05, 0.45, 0.10)]
WHITE_OUT = (4.48, 4.70)    # 이 구간에서 흰색으로 차오르고, 그 뒤는 끝까지 흰 화면
# (시각, 개수, 화면 위치 비율) — 불씨가 사방으로 한 번 터진다
BURSTS = [(0.72, 40, (0.50, 0.55)), (3.62, 90, (0.47, 0.80))]


def ease(t):
    """부드러운 시작·끝(smoothstep)."""
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def ease_out(t):
    """빠르게 시작해 천천히 멈춘다."""
    t = max(0.0, min(1.0, t))
    return 1 - (1 - t) ** 3


_images = {}
_glows = {}
_effects = {}


def load(path):
    if path not in _images:
        full = FOLDER / path
        if not full.exists():
            sys.exit(f"그림이 없다: {full}")
        _images[path] = Image.open(full).convert("RGB")
    return _images[path]


def glow_of(path):
    """밝은 곳만 남겨 흐리게 번진 빛. 작게 줄여 흐린 뒤 키운다 — 빠르고, 더 넓게 번진다."""
    if path not in _glows:
        image = load(path)
        small = image.resize((image.width // 4, image.height // 4), Image.Resampling.BILINEAR)
        bright = np.clip((np.asarray(small, dtype=np.float32) - 150.0) * 1.8, 0, 255).astype(np.uint8)
        blurred = Image.fromarray(bright).filter(ImageFilter.GaussianBlur(6))
        _glows[path] = blurred.resize(image.size, Image.Resampling.BICUBIC)
    return _glows[path]


def effect_of(fx):
    """효과 그림, 그 안의 기준점(px), 원래 폭.

    검은 바탕의 잡티를 걷어내 화면 전체가 뿌옇게 밝아지지 않게 한다. rotate가 있으면 기준점을 한가운데 둔
    넉넉한 판에 옮긴 뒤 돌린다 — 그대로 돌리면 기준점에서 먼 쪽(궤적 꼬리)이 판 밖으로 잘린다.
    """
    key = (fx.image, fx.pivot, fx.rotate)
    if key not in _effects:
        a = np.asarray(load(fx.image), dtype=np.float32)
        image = Image.fromarray(np.clip((a - 18.0) * (255.0 / 237.0), 0, 255).astype(np.uint8))
        width = image.width
        px, py = fx.pivot[0] * image.width, fx.pivot[1] * image.height
        if fx.rotate:
            r = int(math.ceil(max(math.hypot(x - px, y - py) for x in (0, image.width) for y in (0, image.height))))
            pad = Image.new("RGB", (2 * r, 2 * r))
            pad.paste(image, (int(r - px), int(r - py)))
            image = pad.rotate(fx.rotate, resample=Image.Resampling.BICUBIC)
            px = py = r
        _effects[key] = (image, px, py, width)
    return _effects[key]


def to_array(image):
    return np.asarray(image, dtype=np.float32) / 255.0


def screen(base, layer, strength):
    """base 위에 layer를 strength만큼 빛으로 더한다(둘 다 0~1 배열)."""
    return 1.0 - (1.0 - base) * (1.0 - layer * strength)


def apply_fx(base, fx, u):
    """효과 그림을 크기·세기에 맞춰 컷 그림 크기의 검은 판에 놓고 빛으로 더한다."""
    alpha = fx.alpha[0] + (fx.alpha[1] - fx.alpha[0]) * u * u
    if alpha <= 0.01:
        return base
    h, w = base.shape[:2]
    effect, px, py, width = effect_of(fx)
    s = fx.scale[0] + (fx.scale[1] - fx.scale[0]) * ease_out(u)
    k = w * s / width           # 효과 그림 1px → 컷 그림 몇 px
    size = (max(1, int(effect.width * k)), max(1, int(effect.height * k)))
    layer = Image.new("RGB", (w, h))
    layer.paste(effect.resize(size, Image.Resampling.BICUBIC),
                (int(fx.at[0] * w - px * k), int(fx.at[1] * h - py * k)))
    return screen(base, to_array(layer), alpha)


def shot_image(shot, t):
    """t 시각의 컷 그림 — 카메라로 자르기 전, 그림 크기 그대로."""
    k = max(i for i, pose in enumerate(shot.poses) if pose.at <= t)
    pose = shot.poses[k]
    end = shot.poses[k + 1].at if k + 1 < len(shot.poses) else shot.t1
    u = (t - pose.at) / max(1e-6, end - pose.at)

    base = to_array(load(pose.image))
    if pose.fade > 0 and k > 0 and t < pose.at + pose.fade:
        mix = ease((t - pose.at) / pose.fade)
        base = to_array(load(shot.poses[k - 1].image)) * (1 - mix) + base * mix

    glow = pose.glow[0] + (pose.glow[1] - pose.glow[0]) * u
    if glow > 0:
        base = screen(base, to_array(glow_of(pose.image)), glow)

    for fx in shot.fx:
        if fx.t0 <= t < fx.t1:
            base = apply_fx(base, fx, (t - fx.t0) / (fx.t1 - fx.t0))
    return Image.fromarray(np.clip(base * 255 + 0.5, 0, 255).astype(np.uint8))


def camera_frame(image, e, zoom, focus, offset):
    """그림에서 확대·초점대로 16:9 상자를 잘라 1920x1080으로 늘린다. e = 진행도(곡선 적용 뒤)."""
    iw, ih = image.size
    z = zoom[0] + (zoom[1] - zoom[0]) * e
    fx = focus[0][0] + (focus[1][0] - focus[0][0]) * e
    fy = focus[0][1] + (focus[1][1] - focus[0][1]) * e
    cw = iw / z
    ch = cw * H / W
    if ch > ih:
        ch = ih
        cw = ch * W / H
    cx = min(max(fx * iw + offset[0], cw / 2), iw - cw / 2)
    cy = min(max(fy * ih + offset[1], ch / 2), ih - ch / 2)
    box = (cx - cw / 2, cy - ch / 2, cx + cw / 2, cy + ch / 2)
    return image.resize((W, H), Image.Resampling.BICUBIC, box=box)


def vignette():
    """가장자리를 조금 어둡게 — 시선을 가운데로 모은다."""
    small = Image.new("L", (192, 108), 0)
    px = small.load()
    for y in range(108):
        for x in range(192):
            dx, dy = (x - 95.5) / 96, (y - 53.5) / 54
            d = min(1.0, math.sqrt(dx * dx + dy * dy))
            px[x, y] = int(255 * (1.0 - 0.28 * d ** 2.2))
    return small.resize((W, H), Image.Resampling.BICUBIC)


class Ember:
    def __init__(self, rng, burst_at=None):
        self.x = rng.uniform(0, W)
        self.y = rng.uniform(H * 0.4, H + 40)
        self.vx = rng.uniform(-30, 30)
        self.vy = -rng.uniform(60, 160)
        self.size = rng.choice([4, 4, 6, 6, 8])
        if burst_at is not None:
            a = rng.uniform(0, math.tau)
            s = rng.uniform(400, 1100)
            self.vx, self.vy = math.cos(a) * s, math.sin(a) * s * 0.7
            self.x = burst_at[0] * W + rng.uniform(-80, 80)
            self.y = burst_at[1] * H + rng.uniform(-40, 40)
            self.size = rng.choice([6, 8, 10])
        self.color = rng.choice(EMBERS)
        self.life = rng.uniform(0.6, 1.4)
        self.age = 0.0
        self.phase = rng.uniform(0, math.tau)

    def step(self, dt):
        self.age += dt
        self.x += self.vx * dt + math.sin(self.age * 5 + self.phase) * 20 * dt
        self.y += self.vy * dt
        self.vx *= 0.97
        self.vy *= 0.985

    def alive(self):
        return self.age < self.life and -20 < self.x < W + 20 and -20 < self.y < H + 60


def render_frame(t, embers, rng, vig):
    # 흰 화면 구간
    if t >= WHITE_OUT[1]:
        return Image.new("RGB", (W, H), WHITE)

    shot = next((s for s in SHOTS if s.t0 <= t < s.t1), None)
    if shot is None:
        frame = Image.new("RGB", (W, H), (0, 0, 0))
    else:
        u = (t - shot.t0) / (shot.t1 - shot.t0)
        a0, a1 = shot.shake
        amp = a1 + (a0 - a1) * (1 - u) ** 1.5
        offset = (rng.uniform(-amp, amp), rng.uniform(-amp, amp))
        frame = camera_frame(shot_image(shot, t), ease_out(u) if shot.punch else ease(u),
                             shot.zoom, shot.focus, offset)

    frame = Image.composite(frame, Image.new("RGB", (W, H), (0, 0, 0)), vig)

    # 불씨
    draw = ImageDraw.Draw(frame, "RGBA")
    for e in embers:
        a = 1.0 - e.age / e.life
        a *= 0.65 + 0.35 * math.sin(e.age * 30 + e.phase)
        if a <= 0.05:
            continue
        s = e.size
        draw.rectangle([e.x, e.y, e.x + s - 1, e.y + s - 1], fill=e.color + (int(255 * a),))

    # 번쩍임 + 흰색으로 차오르기
    white = 0.0
    for at, strength, fade in FLASHES:
        if at <= t < at + fade:
            white = max(white, strength * (1 - (t - at) / fade))
    if t >= WHITE_OUT[0]:
        white = max(white, ease((t - WHITE_OUT[0]) / (WHITE_OUT[1] - WHITE_OUT[0])))
    if white > 0:
        frame = Image.blend(frame, Image.new("RGB", (W, H), WHITE), min(1.0, white))
    return frame


def main():
    preview = "--preview" in sys.argv
    vig = vignette()
    rng = random.Random(7)
    embers = []
    frames_total = int(round(DURATION * FPS))
    dt = 1 / FPS

    writer = None
    if not preview:
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
        # 수정(2026-09-19) — 유니티(윈도우 Media Foundation) 호환 설정으로 바꿨다.
        # 처음 판(High 프로필, 색 정보 없음)은 유니티가 "Color primaries 0 is unknown"과 "Unexpected timestamp values
        # (baseline이 아닌 H.264)"를 띄우고 화면이 검게 나왔다. 그래서:
        #   - Baseline 프로필 + B프레임 없음: Media Foundation이 시간값을 어긋나게 읽지 않는다(유니티 권장 형식)
        #   - BT.709 색 정보를 넣고 RGB→YUV 변환도 BT.709로: 색 공간을 추측하지 않게 한다
        #   - 1초마다 키프레임: 처음으로 되감을 때 바로 첫 칸을 찾는다
        cmd = [ffmpeg, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
               "-r", str(FPS), "-i", "-", "-an",
               "-vf", "scale=out_color_matrix=bt709:out_range=tv",
               "-c:v", "libx264", "-profile:v", "baseline", "-level", "4.0", "-bf", "0", "-g", str(FPS),
               "-pix_fmt", "yuv420p", "-crf", "19", "-preset", "slow",
               # 색 정보를 H.264 본문(VUI)에 직접 적는다 — 위 -color_* 옵션만으로는 primaries·transfer가 unknown으로 남았다.
               "-x264-params", "colorprim=bt709:transfer=bt709:colormatrix=bt709",
               "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
               "-movflags", "+faststart", str(OUTPUT)]
        writer = subprocess.Popen(cmd, stdin=subprocess.PIPE)

    # 확인용 모음 그림에 넣을 순간 — 컷마다 그림이 바뀌는 곳
    picks = {int(round(s * FPS)) for s in (0.2, 0.5, 0.85, 1.4, 1.8, 2.2, 2.6, 3.3, 3.42, 3.55, 3.75, 4.2)}
    thumbs = []
    for i in range(frames_total):
        t = i * dt
        # 불씨 뿌리기: 평소엔 조금, 왕관이 붙는 순간과 내려찍는 순간엔 사방으로 한 번 터진다
        for _ in range(rng.randint(0, 2)):
            embers.append(Ember(rng))
        for at, count, where in BURSTS:
            if abs(t - at) < dt / 2:
                embers.extend(Ember(rng, burst_at=where) for _ in range(count))
        frame = render_frame(t, embers, rng, vig)
        for e in embers:
            e.step(dt)
        embers[:] = [e for e in embers if e.alive()]

        if writer is not None:
            writer.stdin.write(frame.tobytes())
        if i in picks:
            thumb = frame.copy()
            thumb.thumbnail((480, 270))
            thumbs.append((t, thumb))

    if writer is not None:
        writer.stdin.close()
        writer.wait()

    cols, rows = 4, (len(thumbs) + 3) // 4
    sheet = Image.new("RGB", (cols * 488, rows * 300), (40, 20, 20))
    d = ImageDraw.Draw(sheet)
    for k, (t, thumb) in enumerate(thumbs):
        x, y = (k % cols) * 488, (k // cols) * 300
        sheet.paste(thumb, (x, y + 26))
        d.text((x + 6, y + 6), f"{t:.2f}s", fill=(255, 176, 0))
    sheet.save(PREVIEW)
    print(("미리보기만: " if preview else f"영상: {OUTPUT} ({OUTPUT.stat().st_size // 1024}KB), ") + f"확인용: {PREVIEW}")


if __name__ == "__main__":
    main()
