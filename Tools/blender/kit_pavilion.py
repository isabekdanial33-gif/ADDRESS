"""Frutiger Aero pavilion exit: white glossy arch, glass barrel vault, aqua tiles, glowing far end.
  blender -b --factory-startup -P kit_pavilion.py -- <out_dir>
Unity: entrance faces local +Z (Blender -Y); the far (back) end sits at y=0.
"""
import bpy, math, sys, os
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp"
_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "kit_backrooms.py"), encoding="utf-8").read()
exec("\n".join(l for l in _src.split("def crt_monitor")[0].splitlines() if not l.startswith(("argv", "OUT_", "os.makedirs"))))
reset()
white = mat("pav_white", (0.95, 0.97, 1.0), rough=0.12)
tile = mat("pav_tile", (0.55, 0.85, 0.92), rough=0.15)
glass = mat("pav_glass", (0.7, 0.9, 1.0), rough=0.05)
glow = mat("pav_glow", (0.85, 1.0, 1.0), rough=0.3, emit=(0.75, 1.0, 1.0), emit_strength=6)
aqua = mat("pav_aqua", (0.1, 0.65, 0.8), rough=0.2, emit=(0.1, 0.7, 0.9), emit_strength=1.5)
D, R = 3.4, 1.25
parts = [box("floor", (2 * R + 0.3, D, 0.06), (0, -D / 2, 0.03), tile, bevel=0.01)]
# glass barrel vault
bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=R, depth=D, end_fill_type="NOTHING", location=(0, -D / 2, 0.06), rotation=(math.pi / 2, 0, 0))
v = bpy.context.active_object; v.name = "vault"; v.data.materials.append(glass)
cut = box("cut", (3 * R, D + 1, R), (0, -D / 2, 0.06 - R / 2)); boolean_cut(v, cut)
md = v.modifiers.new("solid", "SOLIDIFY"); md.thickness = 0.03; bpy.context.view_layer.objects.active = v; bpy.ops.object.modifier_apply(modifier="solid")
parts.append(v)
# white ribs along the vault + the entrance arch
for i, y in enumerate((-0.02, -D * 0.33, -D * 0.66, -D + 0.02)):
    bpy.ops.mesh.primitive_torus_add(major_radius=R + 0.02, minor_radius=0.07 if i == 3 else 0.04, major_segments=48, minor_segments=12, location=(0, y, 0.06), rotation=(math.pi / 2, 0, 0))
    t = bpy.context.active_object; t.name = f"rib{i}"; t.data.materials.append(white)
    c2 = box(f"c{i}", (3 * R, 0.5, R), (0, y, 0.06 - R / 2)); boolean_cut(t, c2); parts.append(t)
for sx in (-1, 1):
    parts.append(cyl(f"base{sx}", 0.12, 0.12, (sx * (R + 0.02), -D + 0.02, 0.06), white, 24, bevel=0.02))
    parts.append(box(f"curb{sx}", (0.12, D, 0.1), (sx * (R + 0.02), -D / 2, 0.08), white, bevel=0.03))
parts.append(box("aqua_strip", (2 * R, 0.04, 0.02), (0, -D + 0.05, 0.075), aqua))
parts.append(box("step", (2 * R + 0.6, 0.35, 0.05), (0, -D - 0.17, 0.025), white, bevel=0.015))

o = join(parts, "pavilion")
o.location = (0, 0, 0); bpy.context.scene.cursor.location = (0, 0, 0)
bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
# put the far end exactly at y=0
import mathutils
o.location = (0, 0, 0); bpy.ops.object.transform_apply(location=True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "exit_aero_pavilion.glb"), use_selection=True, export_apply=True, export_format="GLB", export_yup=True)
print("PAVILION DONE")
