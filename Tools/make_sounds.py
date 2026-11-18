"""Synthesised sound set for ADDRESS (no external assets): python -I make_sounds.py
All files 44.1 kHz mono 16-bit WAV in Game/Assets/ADDRESS/Audio. Loops are made seamless by crossfading.
"""
import os, wave
import numpy as np

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Game", "Assets", "ADDRESS", "Audio")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)


def save(name, x, peak=0.8):
    x = np.asarray(x, dtype=np.float64)
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print("wrote", name, f"{len(x) / SR:.2f}s")


def t(sec): return np.arange(int(sec * SR)) / SR


def lowpass(x, fc):
    # one-pole low-pass applied twice (gentle 12 dB/oct)
    a = np.exp(-2 * np.pi * fc / SR)
    for _ in range(2):
        y = np.empty_like(x); acc = 0.0
        for i in range(len(x)):
            acc = (1 - a) * x[i] + a * acc; y[i] = acc
        x = y
    return x


def highpass(x, fc): return x - lowpass(x, fc)


def bandpass(x, lo, hi): return lowpass(highpass(x, lo), hi)


def loopify(x, fade=0.5):
    n = int(fade * SR); head, tail = x[:n], x[-n:]
    ramp = np.linspace(0, 1, n)
    out = x[n:].copy(); out[-n:] = tail * (1 - ramp) + head * ramp
    return out


def fluorescent_hum():
    tt = t(8.5)
    base = sum(np.sin(2 * np.pi * 120 * k * tt + rng.uniform(0, 6)) / (k ** 1.3) for k in range(1, 12))
    # ballast buzz: clipped 120 Hz with slow amplitude wander
    buzz = np.clip(np.sin(2 * np.pi * 120 * tt) * 3, -1, 1) * 0.15
    wander = 0.85 + 0.15 * np.sin(2 * np.pi * 0.13 * tt) + 0.05 * np.sin(2 * np.pi * 0.71 * tt)
    hiss = bandpass(rng.normal(0, 1, len(tt)), 2500, 7000) * 0.05
    x = (base * 0.6 + buzz) * wander + hiss
    save("hum_fluorescent", loopify(x), 0.55)


def room_tone():
    tt = t(10.5)
    x = lowpass(rng.normal(0, 1, len(tt)), 220) * 1.0 + np.sin(2 * np.pi * 50 * tt) * 0.02
    save("room_tone", loopify(x), 0.4)


def footsteps_carpet():
    for i in range(6):
        n = int(0.32 * SR); x = np.zeros(n)
        thud = lowpass(rng.normal(0, 1, n), 180 + rng.uniform(-30, 40)) * np.exp(-t(0.32) * 26)
        scuff = bandpass(rng.normal(0, 1, n), 900, 3500) * np.exp(-t(0.32) * 18) * 0.18
        x = thud * 2.2 + scuff
        x[: int(0.004 * SR)] *= np.linspace(0, 1, int(0.004 * SR))
        save(f"step_carpet_{i}", x, 0.7 + rng.uniform(-0.1, 0.05))


def breathing_mask():
    # one full cycle: inhale through filter (bright hiss), valve click, exhale (lower, muffled), pause
    seq = []
    inh = bandpass(rng.normal(0, 1, int(1.5 * SR)), 1200, 6500)
    env = np.sin(np.linspace(0, np.pi, len(inh))) ** 1.5
    seq.append(inh * env * 0.7)
    click = np.zeros(int(0.06 * SR)); click[:300] = rng.normal(0, 1, 300) * np.exp(-np.arange(300) / 40); seq.append(bandpass(click, 1500, 5000) * 0.8)
    seq.append(np.zeros(int(0.15 * SR)))
    exh = bandpass(rng.normal(0, 1, int(1.7 * SR)), 300, 2200)
    env = np.sin(np.linspace(0, np.pi, len(exh))) ** 1.2
    flutter = 1 + 0.25 * np.sin(2 * np.pi * 23 * t(1.7))    # exhale valve flutter
    seq.append(exh * env * flutter * 0.85)
    seq.append(np.zeros(int(1.0 * SR)))
    save("breath_mask", np.concatenate(seq), 0.45)


def door_creak():
    tt = t(1.4)
    f = 180 + 60 * np.sin(2 * np.pi * 0.8 * tt) + rng.normal(0, 5, len(tt)).cumsum() / 400
    phase = 2 * np.pi * np.cumsum(f) / SR
    x = np.sign(np.sin(phase)) * 0.3 + np.sin(phase * 2) * 0.3
    x = bandpass(x, 400, 3000) * np.exp(-tt * 1.2) * (1 + 0.6 * rng.random(len(tt)))
    save("door_open", x, 0.5)


def elevator():
    tt = t(3.2)
    chime = (np.sin(2 * np.pi * 880 * tt) + 0.5 * np.sin(2 * np.pi * 1760 * tt)) * np.exp(-tt * 3) * (tt < 1.2)
    motor = bandpass(rng.normal(0, 1, len(tt)), 80, 400) * np.clip((tt - 1.0) * 2, 0, 1) * np.clip((3.2 - tt) * 2, 0, 1) * 0.6
    slide = bandpass(rng.normal(0, 1, len(tt)), 600, 2500) * np.clip(np.sin(np.pi * (tt - 1.2) / 1.6), 0, 1) * (tt > 1.2) * 0.25
    save("elevator_open", chime * 0.6 + motor + slide, 0.6)


def vent():
    tt = t(0.9)
    x = bandpass(rng.normal(0, 1, len(tt)), 2000, 8000) * np.exp(-tt * 6) * 0.5
    clank = sum(np.sin(2 * np.pi * f * tt) * np.exp(-tt * 9) for f in (430, 1130, 2210)) * 0.4
    save("vent_open", x + clank, 0.6)


def shutter():
    tt = t(0.35)
    x = np.zeros(len(tt))
    for k, off in enumerate((0.0, 0.11)):
        i = int(off * SR); n = int(0.05 * SR)
        x[i:i + n] += bandpass(rng.normal(0, 1, n), 1500, 9000) * np.exp(-np.arange(n) / (0.008 * SR)) * (1.0 if k == 0 else 0.6)
    save("camera_shutter", x, 0.7)


def travel_whoosh():
    tt = t(2.0)
    x = lowpass(rng.normal(0, 1, len(tt)), 300) * np.sin(np.pi * tt / 2.0) ** 2 + np.sin(2 * np.pi * 40 * tt) * 0.2 * np.sin(np.pi * tt / 2.0)
    save("travel", x, 0.6)


if __name__ == "__main__":
    fluorescent_hum(); room_tone(); footsteps_carpet(); breathing_mask(); door_creak(); elevator(); vent(); shutter(); travel_whoosh()
