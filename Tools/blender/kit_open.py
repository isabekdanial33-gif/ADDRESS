"""Openable props for ADDRESS (moving parts are separate named objects; origins on their hinges):
  blender -b --factory-startup -P kit_open.py -- <out_dir_models>
open_box (4 flaps), storage_tub (lid), trash_bag_tied (knot), file_cabinet (4 drawers).
Front faces Blender -Y (Unity +Z), origin of the whole model bottom centre.
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp/models"
os.makedirs(OUT, exist_ok=True)
_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "kit_backrooms.py"), encoding="utf-8").read()
exec("\n".join(l for l in _src.split("def crt_monitor")[0].splitlines() if not l.startswith(("argv", "OUT_", "os.makedirs"))))


def pivot(o, p):
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.context.scene.cursor.location = p; bpy.ops.object.origin_set(type="ORIGIN_CURSOR")


def apply_mods(o):
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)


def export_scene(name):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), use_selection=True, export_apply=True, export_format="GLB", export_yup=True)
    print("exported", name)


def open_box():
    reset()
    card = mat("cardboard", (0.55, 0.40, 0.24), rough=0.85)
    card_in = mat("cardboard_inside", (0.45, 0.33, 0.2), rough=0.9)
    tape = mat("tape_brown", (0.62, 0.45, 0.25), rough=0.35)
    W, D, H, t = 0.52, 0.40, 0.34, 0.006
    parts = [box("bottom", (W, D, t), (0, 0, t / 2), card),
             box("wf", (W, t, H), (0, -D / 2 + t / 2, H / 2), card), box("wb", (W, t, H), (0, D / 2 - t / 2, H / 2), card),
             box("wl", (t, D, H), (-W / 2 + t / 2, 0, H / 2), card), box("wr", (t, D, H), (W / 2 - t / 2, 0, H / 2), card),
             box("tape", (0.05, D + 0.004, 0.002), (0, 0, 0.0035), tape)]
    body = join(parts, "body"); apply_mods(body)
    # flaps lie closed on top; each pivots on its hinge edge
    for n, size, loc, hinge in (("flap_f", (W, D / 2, t), (0, -D / 4, H), (0, -D / 2, H)), ("flap_b", (W, D / 2, t), (0, D / 4, H + t), (0, D / 2, H + t)),
                                ("flap_l", (W / 2, D, t), (-W / 4, 0, H + 2 * t), (-W / 2, 0, H + 2 * t)), ("flap_r", (W / 2, D, t), (W / 4, 0, H + 3 * t), (W / 2, 0, H + 3 * t))):
        f = box(n, size, loc, card); apply_mods(f); pivot(f, hinge)
    tp = box("flaptape", (0.05, D * 0.9, 0.002), (0, 0, H + 4 * t + 0.001), tape); tp.name = "flaptape"
    export_scene("open_box")


def storage_tub():
    reset()
    plastic = mat("tub_plastic", (0.55, 0.62, 0.70), rough=0.35, alpha=1.0)
    lidm = mat("tub_lid", (0.12, 0.30, 0.55), rough=0.3)
    W, D, H, t = 0.60, 0.40, 0.30, 0.008
    parts = [box("bottom", (W - 0.04, D - 0.04, t), (0, 0, t / 2), plastic, bevel=0.004),
             box("wf", (W, t, H), (0, -D / 2 + t / 2, H / 2), plastic, bevel=0.003), box("wb", (W, t, H), (0, D / 2 - t / 2, H / 2), plastic, bevel=0.003),
             box("wl", (t, D, H), (-W / 2 + t / 2, 0, H / 2), plastic, bevel=0.003), box("wr", (t, D, H), (W / 2 - t / 2, 0, H / 2), plastic, bevel=0.003),
             box("rim", (W + 0.02, D + 0.02, 0.012), (0, 0, H - 0.006), plastic, bevel=0.004)]
    body = join(parts, "body"); apply_mods(body)
    # hollow the rim: cut the inside
    cut = box("cut", (W - 0.02, D - 0.02, 0.05), (0, 0, H)); boolean_cut(body, cut)
    lid = box("lid", (W + 0.03, D + 0.03, 0.025), (0, 0, H + 0.0125), lidm, bevel=0.008, seg=3); apply_mods(lid)
    handles = [box(f"h{i}", (0.12, 0.02, 0.012), (0, s * (D / 2 + 0.01), H + 0.01), lidm, bevel=0.004) for i, s in enumerate((-1, 1))]
    for h in handles: apply_mods(h)
    lid = join([lid] + handles, "lid"); pivot(lid, (0, D / 2 + 0.015, H))
    export_scene("storage_tub")


def trash_bag_tied():
    reset()
    black = mat("bag_plastic", (0.02, 0.02, 0.022), rough=0.25)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=24, radius=0.28, location=(0, 0, 0.26))
    bag = bpy.context.active_object; bag.name = "bag"
    bm = bmesh.new(); bm.from_mesh(bag.data)
    import random; random.seed(4)
    for v in bm.verts:
        z = v.co.z
        if z < -0.18: v.co.z = -0.18 - (z + 0.18) * 0.15            # flat-ish bottom
        if z > 0.12: v.co.x *= 0.6 + 0.4 * (0.28 - z) / 0.16; v.co.y *= 0.6 + 0.4 * (0.28 - z) / 0.16   # gathered neck
        v.co += Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))) * 0.012   # wrinkles
    bm.to_mesh(bag.data); bm.free()
    for p in bag.data.polygons: p.use_smooth = True
    bag.data.materials.append(black)
    md = bag.modifiers.new("disp", "DISPLACE"); tex = bpy.data.textures.new("wr", "CLOUDS"); tex.noise_scale = 0.08; md.texture = tex; md.strength = 0.02
    apply_mods(bag); pivot(bag, (0, 0, 0))
    knot = cyl("knot", 0.035, 0.06, (0, 0, 0.56), black, 16, bevel=0.01); apply_mods(knot)
    tail = cyl("tail", 0.02, 0.07, (0, 0, 0.615), black, 12, bevel=0.008); apply_mods(tail)
    tail.scale = (1.4, 0.6, 1); bpy.context.view_layer.objects.active = tail; bpy.ops.object.transform_apply(scale=True)
    ears = [tail]
    knot = join([knot] + ears, "knot"); pivot(knot, (0, 0, 0.53))
    export_scene("trash_bag_tied")


def file_cabinet():
    reset()
    grey = mat("cab_grey", (0.42, 0.44, 0.45), rough=0.4, metal=0.6)
    dark = mat("cab_dark", (0.05, 0.05, 0.05), rough=0.8)
    chrome = mat("cab_handle", (0.75, 0.75, 0.74), rough=0.2, metal=1.0)
    label = mat("cab_label", (0.9, 0.88, 0.8), rough=0.6)
    W, D, H, n = 0.47, 0.62, 1.32, 4
    t = 0.012
    parts = [box("back", (W, t, H), (0, D / 2 - t / 2, H / 2), grey), box("l", (t, D, H), (-W / 2 + t / 2, 0, H / 2), grey), box("r", (t, D, H), (W / 2 - t / 2, 0, H / 2), grey),
             box("top", (W, D, t), (0, 0, H - t / 2), grey, bevel=0.004), box("base", (W, D, 0.05), (0, 0, 0.025), dark)]
    dh = (H - 0.06) / n
    for i in range(n + 1): parts.append(box(f"rail{i}", (W, 0.02, 0.012), (0, -D / 2 + 0.01, 0.05 + i * dh), grey))
    body = join(parts, "body"); apply_mods(body)
    for i in range(n):
        z0 = 0.05 + i * dh
        front = box("front", (W - 0.03, 0.02, dh - 0.014), (0, -D / 2 + 0.01, z0 + dh / 2), grey, bevel=0.004)
        tray = [box("tb", (W - 0.06, D - 0.06, 0.008), (0, 0.02, z0 + 0.02), grey), box("tl", (0.008, D - 0.06, dh * 0.6), (-W / 2 + 0.035, 0.02, z0 + dh * 0.32), grey),
                box("tr", (0.008, D - 0.06, dh * 0.6), (W / 2 - 0.035, 0.02, z0 + dh * 0.32), grey), box("tbk", (W - 0.06, 0.008, dh * 0.6), (0, D / 2 - 0.04, z0 + dh * 0.32), grey)]
        handle = box("handle", (0.14, 0.03, 0.018), (0, -D / 2 - 0.012, z0 + dh * 0.62), chrome, bevel=0.006)
        lab = box("label", (0.08, 0.004, 0.03), (0, -D / 2 - 0.001, z0 + dh * 0.82), label)
        dr = join([front, handle, lab] + tray, f"drawer_{i}"); apply_mods(dr); pivot(dr, (0, -D / 2, z0))
    export_scene("file_cabinet")


if __name__ == "__main__":
    open_box(); storage_tub(); trash_bag_tied(); file_cabinet()
    print("OPEN KIT DONE")
