"""Backrooms kit + office props for ADDRESS (Blender 5.x, run headless):
  blender -b --factory-startup -P kit_backrooms.py -- <out_dir_models> <out_dir_arch>
Conventions: metres, origin at bottom centre, model FRONT faces Blender -Y (=> Unity +Z after glTF import).
Every piece uses bevels (no razor-sharp CG edges) and real sub-parts (keys, louvres, sockets).
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_MODELS = argv[0] if len(argv) > 0 else "/tmp/models"
OUT_ARCH = argv[1] if len(argv) > 1 else "/tmp/arch"
os.makedirs(OUT_MODELS, exist_ok=True); os.makedirs(OUT_ARCH, exist_ok=True)


def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)
    for m in list(bpy.data.materials): bpy.data.materials.remove(m)


def mat(name, color, rough=0.5, metal=0.0, emit=None, emit_strength=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = emit_strength
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
    return m


def box(name, size, loc=(0, 0, 0), material=None, bevel=0.0, seg=2):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object; o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    if bevel > 0:
        md = o.modifiers.new("bevel", "BEVEL"); md.width = bevel; md.segments = seg; md.limit_method = "ANGLE"
        md.harden_normals = True
    if material: o.data.materials.append(material)
    return o


def cyl(name, r, h, loc=(0, 0, 0), material=None, verts=32, bevel=0.0, rot=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc)
    o = bpy.context.active_object; o.name = name
    if rot: o.rotation_euler = rot; bpy.ops.object.transform_apply(rotation=True)
    if bevel > 0:
        md = o.modifiers.new("bevel", "BEVEL"); md.width = bevel; md.segments = 2; md.limit_method = "ANGLE"
    if material: o.data.materials.append(material)
    return o


def smooth(o, angle=35):
    o.data.shade_smooth() if hasattr(o.data, "shade_smooth") else None
    try:
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.shade_auto_smooth(angle=math.radians(angle))
    except Exception:
        pass


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        for md in o.modifiers:
            pass
    bpy.context.view_layer.objects.active = objs[0]
    for o in objs:
        bpy.context.view_layer.objects.active = o
        for md in list(o.modifiers):
            bpy.ops.object.modifier_apply(modifier=md.name)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    j = bpy.context.active_object; j.name = name
    smooth(j)
    return j


def origin_bottom_center(o):
    bpy.context.view_layer.objects.active = o
    bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
    minz = min(v.z for v in bb); cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2; cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
    bpy.context.scene.cursor.location = (cx, cy, minz)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    o.location = (0, 0, 0)


def export(o, folder, name):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True); bpy.context.view_layer.objects.active = o
    path = os.path.join(folder, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_apply=True, export_format="GLB", export_yup=True)
    print("exported", path)


def boolean_cut(target, cutter):
    md = target.modifiers.new("bool", "BOOLEAN"); md.operation = "DIFFERENCE"; md.object = cutter; md.solver = "EXACT"
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


# ------------------------------------------------------------------ props

def crt_monitor():
    reset()
    beige = mat("crt_plastic", (0.74, 0.70, 0.60), rough=0.45)
    dark = mat("crt_screen", (0.02, 0.025, 0.025), rough=0.08)
    grey = mat("crt_dark_plastic", (0.18, 0.17, 0.16), rough=0.5)
    # tapered shell: front 0.40 x 0.37, back 0.27 x 0.25, depth 0.40
    bm = bmesh.new()
    def ring(w, h, y, z0):
        return [bm.verts.new((sx * w / 2, y, z0 + sz * h / 2 + h / 2)) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    f = ring(0.40, 0.37, -0.20, 0.07); b = ring(0.27, 0.25, 0.20, 0.10)
    bm.faces.new(f[::-1]); bm.faces.new(b)
    for i in range(4): bm.faces.new((f[i], f[(i + 1) % 4], b[(i + 1) % 4], b[i]))
    me = bpy.data.meshes.new("crt_shell"); bm.to_mesh(me); bm.free()
    shell = bpy.data.objects.new("crt_shell", me); bpy.context.collection.objects.link(shell)
    shell.data.materials.append(beige)
    md = shell.modifiers.new("bevel", "BEVEL"); md.width = 0.018; md.segments = 4; md.limit_method = "ANGLE"
    # recessed screen opening
    cut = box("cut", (0.33, 0.06, 0.28), (0, -0.205, 0.07 + 0.185))
    boolean_cut(shell, cut)
    screen = box("screen", (0.335, 0.03, 0.285), (0, -0.19, 0.07 + 0.185), dark, bevel=0.01, seg=4)
    # slight convex glass
    sd = screen.modifiers.new("cast", "CAST"); sd.factor = 0.15; sd.cast_type = "SPHERE"
    # button strip + power button
    btn = box("btn", (0.02, 0.012, 0.012), (0.15, -0.205, 0.085), grey, bevel=0.003)
    # stand: swivel base
    base = cyl("base", 0.13, 0.025, (0, 0.0, 0.0125), grey, 48, bevel=0.006)
    neck = cyl("neck", 0.06, 0.05, (0, 0.0, 0.045), grey, 32, bevel=0.005)
    # rear vents
    vents = []
    for i in range(7):
        v = box(f"vent{i}", (0.18, 0.004, 0.008), (0, 0.195, 0.16 + i * 0.022), grey, bevel=0.001)
        vents.append(v)
    o = join([shell, screen, btn, base, neck] + vents, "crt_monitor")
    origin_bottom_center(o)
    export(o, OUT_MODELS, "crt_monitor")


def keyboard():
    reset()
    beige = mat("kb_plastic", (0.72, 0.69, 0.60), rough=0.5)
    keyc = mat("kb_keys", (0.80, 0.78, 0.71), rough=0.42)
    dark = mat("kb_keys_dark", (0.45, 0.43, 0.39), rough=0.45)
    # wedge-shaped body 0.46 x 0.17, 0.022 front -> 0.035 back
    bm = bmesh.new()
    pts = [(-0.23, -0.085, 0), (0.23, -0.085, 0), (0.23, 0.085, 0), (-0.23, 0.085, 0),
           (-0.23, -0.085, 0.020), (0.23, -0.085, 0.020), (0.23, 0.085, 0.034), (-0.23, 0.085, 0.034)]
    v = [bm.verts.new(p) for p in pts]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)): bm.faces.new([v[i] for i in f])
    me = bpy.data.meshes.new("kb_body"); bm.to_mesh(me); bm.free()
    body = bpy.data.objects.new("kb_body", me); bpy.context.collection.objects.link(body); body.data.materials.append(beige)
    md = body.modifiers.new("bevel", "BEVEL"); md.width = 0.006; md.segments = 3; md.limit_method = "ANGLE"
    parts = [body]
    rows = [(14, 0.0), (14, 0.0), (13, 0.009), (12, 0.013), (11, 0.02)]
    u = 0.0185
    for r, (n, off) in enumerate(rows):
        y = -0.055 + r * 0.021
        z = 0.020 + (y + 0.085) / 0.17 * 0.014 + 0.004
        for i in range(n):
            x = -0.165 + off + i * u
            k = box(f"k{r}_{i}", (0.016, 0.016, 0.009), (x, y, z), keyc if i not in (0, n - 1) else dark, bevel=0.0025, seg=2)
            parts.append(k)
    space = box("space", (0.11, 0.016, 0.009), (-0.03, -0.076, 0.024), keyc, bevel=0.0025)
    parts.append(space)
    # numpad block
    for r in range(5):
        for c in range(4):
            y = -0.055 + r * 0.021; z = 0.020 + (y + 0.085) / 0.17 * 0.014 + 0.004
            parts.append(box(f"n{r}_{c}", (0.016, 0.016, 0.009), (0.135 + c * u, y, z), keyc, bevel=0.0025))
    o = join(parts, "keyboard")
    origin_bottom_center(o)
    export(o, OUT_MODELS, "keyboard")


def mug(variant, color):
    reset()
    cer = mat(f"mug_{variant}", color, rough=0.18)
    coffee = mat("coffee", (0.05, 0.025, 0.012), rough=0.05)
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.041, depth=0.095, location=(0, 0, 0.0475))
    body = bpy.context.active_object; body.data.materials.append(cer)
    # hollow: remove top face then solidify
    bm = bmesh.new(); bm.from_mesh(body.data)
    top = max(bm.faces, key=lambda f: f.calc_center_median().z)
    bmesh.ops.delete(bm, geom=[top], context="FACES"); bm.to_mesh(body.data); bm.free()
    so = body.modifiers.new("solid", "SOLIDIFY"); so.thickness = 0.005; so.offset = -1
    bv = body.modifiers.new("bevel", "BEVEL"); bv.width = 0.002; bv.segments = 2
    bpy.ops.mesh.primitive_torus_add(major_radius=0.026, minor_radius=0.007, major_segments=32, minor_segments=12, location=(0.045, 0, 0.05), rotation=(math.pi / 2, 0, 0))
    handle = bpy.context.active_object; handle.data.materials.append(cer)
    # trim the half of the torus inside the mug
    cutter = box("cut", (0.06, 0.06, 0.12), (0.01, 0, 0.05)); boolean_cut(handle, cutter)
    parts = [body, handle]
    if variant != "empty":
        parts.append(cyl("coffee", 0.0355, 0.003, (0, 0, 0.07), coffee, 48))
    o = join(parts, f"mug_{variant}")
    origin_bottom_center(o)
    export(o, OUT_MODELS, f"mug_{variant}")


def wall_plate(name, kind):
    reset()
    plastic = mat("plate", (0.88, 0.86, 0.80), rough=0.35)
    dark = mat("plate_dark", (0.10, 0.10, 0.10), rough=0.6)
    plate = box("plate", (0.072, 0.008, 0.115), (0, 0, 0.0575), plastic, bevel=0.003, seg=3)
    parts = [plate]
    if kind == "outlet":
        for z in (0.035, 0.08):
            sock = box("sock", (0.034, 0.006, 0.03), (0, -0.004, z), plastic, bevel=0.004, seg=3)
            parts.append(sock)
            for dx in (-0.006, 0.006):
                h = box("hole", (0.0025, 0.004, 0.009), (dx, -0.0075, z), dark)
                parts.append(h)
    else:
        rocker = box("rocker", (0.018, 0.009, 0.034), (0, -0.006, 0.0575), plastic, bevel=0.003, seg=3)
        rocker.rotation_euler = (math.radians(8), 0, 0)
        parts.append(rocker)
    o = join(parts, name)
    origin_bottom_center(o)
    # wall items: back face must be at local -Z in Unity => Blender +Y; plate is centred on y=0 -> move back face to +Y? keep centred (profile uses bounds)
    export(o, OUT_MODELS, name)


def vent_grille():
    reset()
    metal = mat("vent_metal", (0.80, 0.79, 0.74), rough=0.4, metal=0.6)
    frame = box("frame", (0.30, 0.012, 0.20), (0, 0, 0.10), metal, bevel=0.003)
    cut = box("cut", (0.27, 0.05, 0.17), (0, 0, 0.10)); boolean_cut(frame, cut)
    parts = [frame]
    for i in range(9):
        # horizontal fins, slightly drooping front edge (stamped return-air grille)
        s = box(f"slat{i}", (0.27, 0.014, 0.003), (0, -0.001, 0.024 + i * 0.019), metal, bevel=0.0008)
        parts.append(s)
        lip = box(f"lip{i}", (0.27, 0.002, 0.008), (0, -0.0075, 0.021 + i * 0.019), metal)
        parts.append(lip)
    back = box("back", (0.27, 0.002, 0.17), (0, 0.005, 0.10), mat("vent_dark", (0.03, 0.03, 0.03), rough=0.9))
    parts.append(back)
    o = join(parts, "vent_grille")
    origin_bottom_center(o)
    export(o, OUT_MODELS, "vent_grille")


# ------------------------------------------------------------------ architecture

def troffer():
    reset()
    metal = mat("troffer_metal", (0.86, 0.86, 0.84), rough=0.35, metal=0.2)
    diffuser = mat("troffer_diffuser", (0.95, 0.95, 0.93), rough=0.3, emit=(1.0, 0.96, 0.86), emit_strength=6.0)
    housing = box("housing", (1.22, 0.62, 0.09), (0, 0, 0.045), metal, bevel=0.006, seg=2)
    cut = box("cut", (1.16, 0.56, 0.08), (0, 0, 0.0)); boolean_cut(housing, cut)
    panel = box("panel", (1.16, 0.56, 0.006), (0, 0, 0.075), diffuser)
    parts = [housing, panel]
    # parabolic louvres: cross blades
    for i in range(1, 8):
        parts.append(box(f"lx{i}", (0.004, 0.56, 0.035), (-0.58 + i * 0.145, 0, 0.05), metal, bevel=0.0015))
    for j in range(1, 4):
        parts.append(box(f"ly{j}", (1.16, 0.004, 0.035), (0, -0.28 + j * 0.14, 0.05), metal, bevel=0.0015))
    o = join(parts, "troffer")
    origin_bottom_center(o)  # bottom = the face that sits flush with the ceiling plane minus depth
    export(o, OUT_ARCH, "troffer")


def baseboard():
    reset()
    paint = mat("baseboard", (0.86, 0.80, 0.62), rough=0.4)
    # ogee-like profile in the YZ plane, extruded along X (1 m)
    prof = [(0, 0), (0.016, 0), (0.016, 0.07), (0.013, 0.078), (0.009, 0.084), (0.006, 0.094), (0.004, 0.10), (0, 0.10)]
    bm = bmesh.new()
    a = [bm.verts.new((-0.5, -y, z)) for y, z in prof]
    b = [bm.verts.new((0.5, -y, z)) for y, z in prof]
    for i in range(len(prof) - 1): bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    bm.faces.new(a[::-1]); bm.faces.new(b)
    me = bpy.data.meshes.new("baseboard"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("baseboard", me); bpy.context.collection.objects.link(o); o.data.materials.append(paint)
    smooth(o, 50)
    export(o, OUT_ARCH, "baseboard_1m")


def door_frame(w=1.1, h=2.15):
    reset()
    paint = mat("frame", (0.82, 0.76, 0.58), rough=0.4)
    t, d = 0.07, 0.19   # casing width, jamb depth (wall 0.15 + 2 cm proud each side)
    parts = []
    for sx in (-1, 1):
        parts.append(box("jamb", (0.02, d, h), (sx * (w / 2 + 0.01), 0, h / 2), paint, bevel=0.002))
        for sy in (-1, 1):
            parts.append(box("casing", (t, 0.015, h + t), (sx * (w / 2 + t / 2), sy * (d / 2 + 0.0075), (h + t) / 2), paint, bevel=0.004, seg=3))
    parts.append(box("head", (w + 0.04, d, 0.02), (0, 0, h + 0.01), paint, bevel=0.002))
    for sy in (-1, 1):
        parts.append(box("headcasing", (w + 2 * t, 0.015, t), (0, sy * (d / 2 + 0.0075), h + t / 2), paint, bevel=0.004, seg=3))
    o = join(parts, "door_frame")
    origin_bottom_center(o)
    export(o, OUT_ARCH, "door_frame")


if __name__ == "__main__":
    if len(argv) > 2:
        globals()[argv[2]](); sys.exit(0)
    crt_monitor(); keyboard()
    mug("coffee", (0.92, 0.91, 0.88)); mug("empty", (0.55, 0.12, 0.10))
    wall_plate("wall_outlet", "outlet"); wall_plate("light_switch", "switch")
    vent_grille()
    troffer(); baseboard(); door_frame()
    print("KIT DONE")
