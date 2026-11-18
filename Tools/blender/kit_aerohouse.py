"""Frutiger Aero house: white rounded pavilion-house, wide glass windows, flat overhanging roof, open doorway.
  blender -b --factory-startup -P kit_aerohouse.py -- <out_dir>
Origin bottom centre, the doorway faces Blender -Y (Unity +Z). Inside the back wall (Blender +Y) the exit is placed.
"""
import bpy, math, sys, os
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp"
_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "kit_backrooms.py"), encoding="utf-8").read()
exec("\n".join(l for l in _src.split("def crt_monitor")[0].splitlines() if not l.startswith(("argv", "OUT_", "os.makedirs"))))
reset()
white = mat("house_white", (0.96, 0.97, 0.98), rough=0.25)
glass = mat("house_glass", (0.6, 0.85, 1.0), rough=0.05)
aqua = mat("house_aqua", (0.15, 0.7, 0.85), rough=0.2, emit=(0.1, 0.6, 0.8), emit_strength=0.8)
floor_m = mat("house_floor", (0.85, 0.93, 0.97), rough=0.1)
W, D, H, t = 7.0, 6.0, 3.0, 0.2
parts = [box("plinth", (W + 0.6, D + 0.6, 0.25), (0, 0, 0.125), white, bevel=0.08, seg=4),
         box("floor", (W - 0.2, D - 0.2, 0.02), (0, 0, 0.26), floor_m)]
# walls with window and door openings (cut with booleans)
walls = [box("wb", (W, t, H), (0, D / 2 - t / 2, 0.25 + H / 2), white, bevel=0.04),
         box("wf", (W, t, H), (0, -D / 2 + t / 2, 0.25 + H / 2), white, bevel=0.04),
         box("wl", (t, D, H), (-W / 2 + t / 2, 0, 0.25 + H / 2), white, bevel=0.04),
         box("wr", (t, D, H), (W / 2 - t / 2, 0, 0.25 + H / 2), white, bevel=0.04)]
for w in walls:
    for m in list(w.modifiers): bpy.context.view_layer.objects.active = w; bpy.ops.object.modifier_apply(modifier=m.name)
boolean_cut(walls[1], box("door", (1.5, 1, 2.3), (0, -D / 2, 0.25 + 1.15)))
for x in (-2.3, 2.3): boolean_cut(walls[1], box("wfw", (1.6, 1, 1.4), (x, -D / 2, 0.25 + 1.6)))
for y in (-1.2, 1.2):
    boolean_cut(walls[2], box("wlw", (1, 1.8, 1.5), (-W / 2, y, 0.25 + 1.6)))
    boolean_cut(walls[3], box("wrw", (1, 1.8, 1.5), (W / 2, y, 0.25 + 1.6)))
parts += walls
# glass panes in the window openings
for x in (-2.3, 2.3): parts.append(box("g", (1.6, 0.03, 1.4), (x, -D / 2 + t / 2, 0.25 + 1.6), glass))
for y in (-1.2, 1.2):
    parts.append(box("g", (0.03, 1.8, 1.5), (-W / 2 + t / 2, y, 0.25 + 1.6), glass))
    parts.append(box("g", (0.03, 1.8, 1.5), (W / 2 - t / 2, y, 0.25 + 1.6), glass))
# flat roof with a generous rounded overhang + aqua fascia line
parts.append(box("roof", (W + 1.6, D + 1.6, 0.22), (0, 0, 0.25 + H + 0.11), white, bevel=0.1, seg=5))
parts.append(box("fascia", (W + 1.62, D + 1.62, 0.04), (0, 0, 0.25 + H + 0.02), aqua, bevel=0.02))
# rounded column at the porch corners
for x in (-W / 2 - 0.55, W / 2 + 0.55): parts.append(cyl("col", 0.12, H, (x, -D / 2 - 0.55, 0.25 + H / 2), white, 24, bevel=0.03))
o = join(parts, "aero_house")
bpy.context.scene.cursor.location = (0, 0, 0); bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "aero_house.glb"), use_selection=True, export_apply=True, export_format="GLB", export_yup=True)
print("HOUSE DONE")
