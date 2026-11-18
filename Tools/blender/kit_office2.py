"""More office props for ADDRESS (Blender headless):
  blender -b --factory-startup -P kit_office2.py -- <out_dir_models>
vending_machine, water_cooler, server_rack, whiteboard (wall), conference_table. Front -Y, origin bottom centre.
"""
import bpy, bmesh, math, sys, os, random
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp/models"
os.makedirs(OUT, exist_ok=True)
_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "kit_backrooms.py"), encoding="utf-8").read()
exec("\n".join(l for l in _src.split("def crt_monitor")[0].splitlines() if not l.startswith(("argv", "OUT_", "os.makedirs"))))
OUT_MODELS = OUT


def vending_machine():
    reset()
    body_m = mat("vend_red", (0.55, 0.06, 0.05), rough=0.35, metal=0.2)
    dark = mat("vend_dark", (0.03, 0.03, 0.035), rough=0.4)
    glass = mat("vend_glass", (0.05, 0.07, 0.08), rough=0.05)
    lit = mat("vend_light", (0.9, 0.95, 1.0), rough=0.3, emit=(0.9, 0.95, 1.0), emit_strength=2.5)
    steel = mat("vend_steel", (0.6, 0.6, 0.6), rough=0.3, metal=1.0)
    W, D, H = 0.9, 0.8, 1.85
    parts = [box("body", (W, D, H), (0, 0, H / 2), body_m, bevel=0.02, seg=3)]
    parts.append(box("window", (W * 0.62, 0.02, H * 0.62), (-W * 0.1, -D / 2 - 0.005, H * 0.6), glass, bevel=0.006))
    parts.append(box("lightstrip", (W * 0.62, 0.012, 0.05), (-W * 0.1, -D / 2 - 0.01, H * 0.93), lit))
    rng = random.Random(3)
    cols = [(0.8, 0.1, 0.1), (0.1, 0.3, 0.8), (0.9, 0.7, 0.1), (0.1, 0.6, 0.2), (0.9, 0.4, 0.05), (0.6, 0.6, 0.65)]
    for row in range(5):
        for c in range(6):
            col = cols[rng.randrange(len(cols))]
            m = mat(f"can{row}{c}", col, rough=0.3, metal=0.6)
            parts.append(cyl(f"can{row}{c}", 0.028, 0.11, (-W * 0.36 + c * 0.075, -D / 2 + 0.06, H * 0.36 + row * 0.2), m, 16))
        parts.append(box(f"shelf{row}", (W * 0.6, 0.12, 0.008), (-W * 0.1, -D / 2 + 0.06, H * 0.3 + row * 0.2), steel))
    parts.append(box("panel", (W * 0.22, 0.02, H * 0.5), (W * 0.33, -D / 2 - 0.005, H * 0.62), dark, bevel=0.004))
    for i in range(12):
        parts.append(box(f"btn{i}", (0.035, 0.012, 0.025), (W * 0.29 + (i % 3) * 0.045, -D / 2 - 0.016, H * 0.72 - (i // 3) * 0.04), steel, bevel=0.003))
    parts.append(box("slot", (0.06, 0.012, 0.012), (W * 0.33, -D / 2 - 0.015, H * 0.48), steel))
    parts.append(box("flap", (W * 0.5, 0.03, 0.14), (-W * 0.1, -D / 2 - 0.01, 0.22), dark, bevel=0.006))
    o = join(parts, "vending_machine"); origin_bottom_center(o); export(o, OUT_MODELS, "vending_machine")


def water_cooler():
    reset()
    white = mat("cool_white", (0.85, 0.85, 0.83), rough=0.4)
    blue = mat("cool_bottle", (0.35, 0.6, 0.85), rough=0.05)
    grey = mat("cool_grey", (0.3, 0.3, 0.32), rough=0.5)
    redt = mat("tap_red", (0.8, 0.1, 0.1), rough=0.4); bluet = mat("tap_blue", (0.1, 0.25, 0.8), rough=0.4)
    parts = [box("body", (0.32, 0.32, 0.95), (0, 0, 0.475), white, bevel=0.015, seg=3),
             cyl("bottle", 0.135, 0.42, (0, 0, 0.95 + 0.25), blue, 32, bevel=0.03),
             cyl("neck", 0.05, 0.06, (0, 0, 0.98), blue, 24),
             box("recess", (0.2, 0.06, 0.2), (0, -0.15, 0.62), grey, bevel=0.01),
             box("tray", (0.2, 0.08, 0.02), (0, -0.16, 0.52), grey, bevel=0.004),
             cyl("tapR", 0.015, 0.04, (0.05, -0.17, 0.7), redt, 12, rot=(math.pi / 2, 0, 0)),
             cyl("tapB", 0.015, 0.04, (-0.05, -0.17, 0.7), bluet, 12, rot=(math.pi / 2, 0, 0))]
    o = join(parts, "water_cooler"); origin_bottom_center(o); export(o, OUT_MODELS, "water_cooler")


def server_rack():
    reset()
    black = mat("rack_black", (0.04, 0.04, 0.045), rough=0.45, metal=0.5)
    unit = mat("rack_unit", (0.12, 0.12, 0.13), rough=0.4, metal=0.6)
    led_g = mat("led_green", (0.1, 1, 0.2), emit=(0.1, 1, 0.2), emit_strength=8)
    led_o = mat("led_orange", (1, 0.6, 0.1), emit=(1, 0.6, 0.1), emit_strength=8)
    W, D, H = 0.62, 1.0, 2.0
    parts = [box("frame", (W, D, H), (0, 0, H / 2), black, bevel=0.01)]
    rng = random.Random(7)
    z = 0.12
    while z < H - 0.15:
        u = rng.choice([0.045, 0.09, 0.09, 0.135])
        parts.append(box(f"u{len(parts)}", (W - 0.08, 0.02, u - 0.006), (0, -D / 2 - 0.004, z + u / 2), unit, bevel=0.002))
        for k in range(rng.randrange(1, 6)):
            parts.append(box(f"l{len(parts)}", (0.006, 0.006, 0.006), (-W / 2 + 0.08 + k * 0.02, -D / 2 - 0.016, z + u / 2), led_g if rng.random() < 0.8 else led_o))
        for k in range(rng.randrange(0, 8)):
            parts.append(box(f"v{len(parts)}", (0.02, 0.008, u * 0.5), (0.05 + k * 0.03, -D / 2 - 0.012, z + u / 2), black))
        z += u
    o = join(parts, "server_rack"); origin_bottom_center(o); export(o, OUT_MODELS, "server_rack")


def whiteboard():
    reset()
    white = mat("wb_white", (0.92, 0.93, 0.92), rough=0.5)
    alu = mat("wb_frame", (0.7, 0.7, 0.72), rough=0.3, metal=1.0)
    ink = mat("wb_ink", (0.1, 0.15, 0.5), rough=0.3); inkr = mat("wb_ink_r", (0.6, 0.08, 0.06), rough=0.3)
    W, H = 1.5, 1.0
    parts = [box("board", (W, 0.02, H), (0, 0.01, H / 2), white),
             box("ft", (W + 0.03, 0.03, 0.02), (0, 0.0, H + 0.005), alu), box("fb", (W + 0.03, 0.03, 0.02), (0, 0.0, -0.005), alu),
             box("fl", (0.02, 0.03, H), (-W / 2 - 0.005, 0, H / 2), alu), box("fr", (0.02, 0.03, H), (W / 2 + 0.005, 0, H / 2), alu),
             box("tray", (W * 0.4, 0.07, 0.015), (0, -0.03, 0.0), alu)]
    rng = random.Random(11)
    for line in range(6):   # faded scribbles: lines, arrows, boxes
        y = H * 0.85 - line * 0.12; x0 = -W * 0.4 + rng.random() * 0.2
        for s in range(rng.randrange(3, 9)):
            parts.append(box(f"s{line}{s}", (rng.uniform(0.03, 0.12), 0.002, 0.008), (x0 + s * 0.11, -0.001, y + rng.uniform(-0.01, 0.01)), ink if line % 3 else inkr))
    parts.append(cyl("marker", 0.008, 0.12, (0.1, -0.04, 0.015), ink, 12, rot=(0, math.pi / 2, 0)))
    o = join(parts, "whiteboard"); origin_bottom_center(o); export(o, OUT_MODELS, "whiteboard")


def conference_table():
    reset()
    wood = mat("conf_wood", (0.32, 0.2, 0.12), rough=0.35)
    steel = mat("conf_steel", (0.25, 0.25, 0.27), rough=0.35, metal=0.8)
    L, W, H = 3.2, 1.2, 0.75
    parts = [box("top", (L, W, 0.04), (0, 0, H - 0.02), wood, bevel=0.012, seg=3)]
    for x in (-L / 2 + 0.35, L / 2 - 0.35):
        parts.append(box(f"leg{x}", (0.08, W * 0.7, H - 0.04), (x, 0, (H - 0.04) / 2), steel, bevel=0.01))
        parts.append(box(f"foot{x}", (0.5, W * 0.75, 0.03), (x, 0, 0.015), steel, bevel=0.008))
    parts.append(box("beam", (L - 0.8, 0.06, 0.08), (0, 0, H - 0.12), steel))
    o = join(parts, "conference_table"); origin_bottom_center(o); export(o, OUT_MODELS, "conference_table")


if __name__ == "__main__":
    vending_machine(); water_cooler(); server_rack(); whiteboard(); conference_table()
    print("OFFICE2 DONE")
