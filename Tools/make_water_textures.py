"""Water normal map (tileable) + caustics flipbook (16 frames, tileable) for Poolrooms. python -I make_water_textures.py"""
import os, math
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Game", "Assets", "ADDRESS", "Art", "Textures")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(3)


def periodic_waves(n, waves, phase_t=0.0):
    y, x = np.mgrid[0:n, 0:n] / n * 2 * math.pi
    h = np.zeros((n, n))
    for (kx, ky, amp, ph, speed) in waves:
        h += amp * np.sin(kx * x + ky * y + ph + speed * phase_t)
    return h


def water_normal(n=1024):
    waves = []
    for _ in range(40):
        k = rng.integers(2, 18); ang = rng.uniform(0, 2 * math.pi)
        kx, ky = int(round(k * math.cos(ang))), int(round(k * math.sin(ang)))
        if kx == 0 and ky == 0: continue
        waves.append((kx, ky, 1.0 / (k ** 1.2), rng.uniform(0, 6.28), 0))
    h = periodic_waves(n, waves)
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * n / 40; gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * n / 40
    nrm = np.dstack([-gx, -gy, np.ones_like(h)]); nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
    Image.fromarray(((nrm * 0.5 + 0.5) * 255).astype(np.uint8)).save(os.path.join(OUT, "water_normal.png"))


def caustics(n=256, frames=16):
    """Bright light network = thin edges between Worley cells (F2-F1), points orbit so the loop is seamless and tileable."""
    pts = rng.uniform(0, 1, (22, 2)); rad = rng.uniform(0.02, 0.06, 22); ph = rng.uniform(0, 2 * math.pi, 22)
    yy, xx = np.mgrid[0:n, 0:n] / n
    sheet = Image.new("L", (n * 4, n * 4))
    for f in range(frames):
        a = 2 * math.pi * f / frames
        p = pts + np.stack([rad * np.cos(a + ph), rad * np.sin(a * 1 + ph)], 1)
        d1 = np.full((n, n), 9.0); d2 = np.full((n, n), 9.0)
        for (px, py) in p:
            for ox in (-1, 0, 1):
                for oy in (-1, 0, 1):
                    d = np.hypot(xx - (px + ox), yy - (py + oy))
                    d2 = np.where(d < d1, d1, np.minimum(d2, d)); d1 = np.minimum(d1, d)
        edge = np.clip(1 - (d2 - d1) * 14, 0, 1) ** 3.5
        img = Image.fromarray((edge * 255).astype(np.uint8))
        sheet.paste(img, ((f % 4) * n, (f // 4) * n))
        img.save(os.path.join(OUT, f"caustic_{f:02d}.png"))
    sheet.save(os.path.join(OUT, "caustics_flipbook.png"))


def pool_tile_tint():
    # aqua tint version of the white tile colour map for pool basins
    src = os.path.join(os.path.dirname(OUT), "..", "..", "ThirdParty", "AmbientCG", "Tiles107")
    src = os.path.normpath(src)
    col = [f for f in os.listdir(src) if "_Color" in f][0]
    a = np.asarray(Image.open(os.path.join(src, col)).convert("RGB"), dtype=np.float32) / 255
    tint = np.array([0.55, 0.86, 0.92])
    Image.fromarray((np.clip(a * tint, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "pool_tile_aqua.png"))


if __name__ == "__main__":
    water_normal(); caustics(); pool_tile_tint()
    print("water textures ->", OUT)
