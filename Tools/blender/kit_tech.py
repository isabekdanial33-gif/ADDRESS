"""Computer kit for ADDRESS (Blender 5.x headless):
  blender -b --factory-startup -P kit_tech.py -- <out_dir_models>
pc_tower (beige 2000s system unit), ball_mouse, cable_coil, desk_lamp-free. Same conventions as kit_backrooms.py:
metres, origin bottom centre, FRONT faces Blender -Y.
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_MODELS = argv[0] if len(argv) > 0 else "/tmp/models"
os.makedirs(OUT_MODELS, exist_ok=True)
# shared helpers (reset, mat, box, cyl, join, origin_bottom_center, export, boolean_cut)
_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "kit_backrooms.py"), encoding="utf-8").read()
exec(_src.split("def crt_monitor")[0].split("argv = sys.argv")[0])
exec("\n".join(l for l in _src.split("def crt_monitor")[0].splitlines() if not l.startswith(("argv", "OUT_", "os.makedirs"))))


def pc_tower():
    reset()
    beige = mat("pc_plastic", (0.73, 0.69, 0.59), rough=0.48)
    beige2 = mat("pc_bay", (0.66, 0.62, 0.53), rough=0.5)
    dark = mat("pc_dark", (0.08, 0.08, 0.08), rough=0.6)
    metal = mat("pc_metal", (0.42, 0.42, 0.43), rough=0.65, metal=0.3)
    led = mat("pc_led", (0.1, 0.9, 0.2), rough=0.3, emit=(0.1, 1.0, 0.25), emit_strength=6)
    W, H, D = 0.19, 0.42, 0.45
    parts = [box("case", (W, D, H), (0, 0, H / 2), metal, bevel=0.004)]
    front = box("front", (W + 0.006, 0.03, H + 0.004), (0, -D / 2 - 0.012, H / 2), beige, bevel=0.01, seg=3); parts.append(front)
    # two 5.25" bays + one 3.5" floppy
    for i in range(2):
        parts.append(box(f"bay{i}", (0.148, 0.006, 0.042), (0, -D / 2 - 0.029, H - 0.05 - i * 0.05), beige2, bevel=0.002))
        parts.append(box(f"bayline{i}", (0.12, 0.002, 0.002), (0, -D / 2 - 0.0325, H - 0.05 - i * 0.05), dark))
    parts.append(box("floppy", (0.1, 0.006, 0.028), (0, -D / 2 - 0.029, H - 0.17), beige2, bevel=0.002))
    parts.append(box("floppyslot", (0.075, 0.003, 0.004), (0, -D / 2 - 0.0325, H - 0.167), dark))
    parts.append(cyl("power", 0.014, 0.01, (0, -D / 2 - 0.029, 0.15), beige2, 32, bevel=0.002, rot=(math.pi / 2, 0, 0)))
    parts.append(cyl("led", 0.003, 0.006, (0.04, -D / 2 - 0.029, 0.15), led, 12, rot=(math.pi / 2, 0, 0)))
    for i in range(6):   # front intake slots
        parts.append(box(f"slot{i}", (0.12, 0.004, 0.005), (0, -D / 2 - 0.0285, 0.035 + i * 0.012), dark))
    # rear: port plate + PSU fan
    parts.append(box("ports", (0.045, 0.004, 0.16), (-0.04, D / 2 + 0.002, H - 0.17), dark))
    parts.append(cyl("fan", 0.04, 0.004, (0.03, D / 2 + 0.002, H - 0.06), dark, 32, rot=(math.pi / 2, 0, 0)))
    for x in (-0.07, 0.07):
        for y in (-0.15, 0.15):
            parts.append(cyl(f"foot{x}{y}", 0.012, 0.01, (x, y, 0.005), dark, 16))
    for p in parts: p.location.z += 0.0
    o = join(parts, "pc_tower"); origin_bottom_center(o); export(o, OUT_MODELS, "pc_tower")


def ball_mouse():
    reset()
    beige = mat("mouse_plastic", (0.76, 0.72, 0.63), rough=0.45)
    dark = mat("mouse_dark", (0.12, 0.12, 0.12), rough=0.6)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.5, location=(0, 0, 0))
    b = bpy.context.active_object; b.name = "mouse_body"
    b.scale = (0.062, 0.112, 0.07); bpy.ops.object.transform_apply(scale=True)
    bm = bmesh.new(); bm.from_mesh(b.data)
    for v in bm.verts:
        if v.co.z < 0: v.co.z *= 0.12
        else: v.co.z *= 0.95 + 0.25 * (-v.co.y / 0.056) * 0.3   # higher at the back
    bm.to_mesh(b.data); bm.free()
    b.location.z = 0.004; b.data.materials.append(beige)
    for poly in b.data.polygons: poly.use_smooth = True
    # button grooves cut into the shell (between the two buttons, and behind them)
    c1 = box("c1", (0.0016, 0.05, 0.06), (0, -0.032, 0.062)); boolean_cut(b, c1)
    c2 = box("c2", (0.07, 0.0016, 0.06), (0, -0.006, 0.063)); boolean_cut(b, c2)
    tail = cyl("tail", 0.0025, 0.025, (0, -0.065, 0.008), dark, 12, rot=(math.pi / 2, 0, 0))
    o = join([b, tail], "ball_mouse"); origin_bottom_center(o); export(o, OUT_MODELS, "ball_mouse")


def cable_coil():
    reset()
    black = mat("cable_rubber", (0.03, 0.03, 0.03), rough=0.55)
    metal = mat("cable_plug", (0.6, 0.6, 0.62), rough=0.3, metal=0.9)
    blue = mat("cable_head", (0.12, 0.16, 0.42), rough=0.45)
    cu = bpy.data.curves.new("coil", "CURVE"); cu.dimensions = "3D"; cu.bevel_depth = 0.0045; cu.bevel_resolution = 3
    sp = cu.splines.new("POLY"); pts = []
    loops, R = 3, 0.075
    for i in range(loops * 36 + 1):
        a = i / 36 * math.pi * 2; r = R + 0.004 * math.sin(i * 0.7)
        pts.append((math.cos(a) * r, math.sin(a) * r * 0.92, 0.006 + i * 0.00012 + 0.004 * math.sin(a * 2 + i)))
    pts += [(R + 0.02, 0.0, 0.008), (R + 0.07, -0.02, 0.006), (R + 0.11, -0.03, 0.006)]
    sp.points.add(len(pts) - 1)
    for p, c in zip(sp.points, pts): p.co = (*c, 1)
    ob = bpy.data.objects.new("coil", cu); bpy.context.collection.objects.link(ob); ob.data.materials.append(black)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True); bpy.ops.object.convert(target="MESH")
    head = box("head", (0.035, 0.02, 0.012), (R + 0.125, -0.03, 0.007), blue, bevel=0.003)
    pins = box("pins", (0.016, 0.012, 0.007), (R + 0.149, -0.03, 0.007), metal, bevel=0.001)
    o = join([ob, head, pins], "cable_coil"); origin_bottom_center(o); export(o, OUT_MODELS, "cable_coil")


if __name__ == "__main__":
    pc_tower(); ball_mouse(); cable_coil()
    print("TECH KIT DONE")
