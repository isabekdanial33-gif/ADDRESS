"""Derived textures for aesthetics (tileable). Run: python -I make_textures.py
BR_Wallpaper_Color.png : mono-yellow Backrooms wallpaper - detail/luminance from ambientCG Wallpaper001A,
                         faint repeating motif columns, low-frequency tileable discoloration.
BR_Carpet_Color.png    : damp beige/olive carpet from Carpet016 with tileable darker damp patches.
"""
import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Game", "Assets", "ThirdParty", "AmbientCG")
OUT = os.path.join(ROOT, "Game", "Assets", "ADDRESS", "Art", "Textures")
os.makedirs(OUT, exist_ok=True)
N = 2048


def tile_noise(n, freqs, seed):
    """Tileable low-frequency noise as a sum of periodic sines (integer frequencies keep it seamless)."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n] / n * 2 * math.pi
    acc = np.zeros((n, n))
    for f in freqs:
        for _ in range(3):
            fx, fy = rng.integers(-f, f + 1), rng.integers(-f, f + 1)
            if fx == 0 and fy == 0:
                continue
            ph = rng.uniform(0, 2 * math.pi)
            acc += np.sin(fx * x + fy * y + ph) / f
    acc -= acc.min(); acc /= max(1e-6, acc.max())
    return acc


def find(id_, key):
    d = os.path.join(SRC, id_)
    for f in os.listdir(d):
        if key in f:
            return os.path.join(d, f)
    raise FileNotFoundError(id_ + key)


def wallpaper():
    base = Image.open(find("Wallpaper001A", "_Color")).convert("L").resize((N, N), Image.LANCZOS)
    lum = np.asarray(base, dtype=np.float32) / 255.0
    lum = lum / max(1e-3, lum.mean())                       # detail around 1.0
    lum = 1.0 + (lum - 1.0) * 1.4
    yellow = np.array([0.80, 0.70, 0.36])                   # mono-yellow, slightly dirty
    img = np.ones((N, N, 3)) * yellow
    # motif: columns of small rhombus/chevron marks, low contrast, offset every other row
    motif = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(motif)
    step_x, step_y = 128, 96
    for j in range(N // step_y + 1):
        for i in range(N // step_x + 1):
            cx = i * step_x + (step_x // 2 if j % 2 else 0)
            cy = j * step_y
            for ox in (-N, 0, N):
                x0 = cx + ox
                d.polygon([(x0, cy - 22), (x0 + 13, cy), (x0, cy + 22), (x0 - 13, cy)], fill=255)
                d.line([(x0 - 26, cy + 34), (x0, cy + 22), (x0 + 26, cy + 34)], fill=200, width=4)
    motif = motif.filter(ImageFilter.GaussianBlur(1.6))
    m = np.asarray(motif, dtype=np.float32)[..., None] / 255.0
    img = img * (1 - 0.085 * m)                              # motif slightly darker
    # tileable discoloration (water/age) and slight hue drift
    stain = tile_noise(N, [4, 6, 9], 7)[..., None]
    img = img * (0.95 + 0.06 * stain)
    img[..., 2] *= 0.92 + 0.10 * tile_noise(N, [3, 4], 11)
    img = img * lum[..., None]
    out = Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8))
    out.save(os.path.join(OUT, "BR_Wallpaper_Color.png"))


def carpet():
    base = Image.open(find("Carpet016", "_Color")).convert("RGB").resize((N, N), Image.LANCZOS)
    a = np.asarray(base, dtype=np.float32) / 255.0
    lum = a.mean(axis=2, keepdims=True); lum = lum / max(1e-3, lum.mean())
    tint = np.array([0.50, 0.42, 0.27])                      # damp olive-brown
    img = tint * (1.0 + (lum - 1.0) * 1.2)
    damp = tile_noise(N, [2, 3, 4, 6], 3)[..., None]
    damp = np.clip((damp - 0.62) * 2.2, 0, 1)
    img = img * (1 - 0.13 * damp)
    Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8)).save(os.path.join(OUT, "BR_Carpet_Color.png"))


if __name__ == "__main__":
    wallpaper(); carpet()
    print("textures ->", OUT)
