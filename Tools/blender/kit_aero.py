"""Frutiger Aero props: blender -b --factory-startup -P kit_aero.py -- <out_dir>
aero_tube (floor-to-ceiling glass water column with bubbles), aero_planter (round glossy planter with soil),
aero_bench (curved white bench), aero_orb (glass sphere on a chrome stem). Front = Blender -Y. Origin bottom centre.
"""
import bpy, sys, os, math, random
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kit_backrooms as K

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp/aero"
os.makedirs(OUT, exist_ok=True)
random.seed(4)


def glass(name, tint=(0.75, 0.95, 1.0)):
    m = K.mat(name, tint, rough=0.03)
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Alpha"].default_value = 0.25
    m.blend_method = "BLEND" if hasattr(m, "blend_method") else None
    return m


def finish_export(objs, name):
    o = K.join(objs, name)
    K.origin_bottom_center(o)
    K.export(o, OUT, name)


def tube():
    K.reset()
    chrome = K.mat("aero_chrome", (0.9, 0.92, 0.95), rough=0.06, metal=1.0)
    gl = glass("aero_glass")
    water = K.mat("aero_water", (0.25, 0.75, 0.95), rough=0.05)
    bub = K.mat("aero_bubble", (0.95, 1.0, 1.0), rough=0.02)
    H = 2.6
    parts = [K.cyl("base", 0.24, 0.12, (0, 0, 0.06), chrome, 64, bevel=0.01), K.cyl("cap", 0.24, 0.1, (0, 0, H - 0.05), chrome, 64, bevel=0.01)]
    g = K.cyl("glass", 0.2, H - 0.22, (0, 0, H / 2), gl, 64)
    parts.append(g)
    parts.append(K.cyl("water", 0.185, H - 0.5, (0, 0, (H - 0.5) / 2 + 0.12), water, 48))
    for i in range(26):
        z = random.uniform(0.25, H - 0.45); r = random.uniform(0.008, 0.028)
        a = random.uniform(0, 6.28); d = random.uniform(0, 0.13)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=r, location=(math.cos(a) * d, math.sin(a) * d, z))
        s = bpy.context.active_object; s.data.materials.append(bub); parts.append(s)
    finish_export(parts, "aero_tube")


def planter():
    K.reset()
    white = K.mat("aero_white", (0.95, 0.96, 0.97), rough=0.12, coat=0.6) if False else K.mat("aero_white", (0.95, 0.96, 0.97), rough=0.1)
    soil = K.mat("aero_soil", (0.12, 0.08, 0.05), rough=0.95)
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=0.42, depth=0.55, location=(0, 0, 0.275)); p = bpy.context.active_object
    p.data.materials.append(white)
    # tapered rounded pot: scale bottom ring
    import bmesh
    bm = bmesh.new(); bm.from_mesh(p.data)
    for v in bm.verts:
        if v.co.z < 0: v.co.x *= 0.78; v.co.y *= 0.78
    bm.to_mesh(p.data); bm.free()
    K.bevel = None
    md = p.modifiers.new("bev", "BEVEL"); md.width = 0.04; md.segments = 5; md.limit_method = "ANGLE"
    s = K.cyl("soil", 0.39, 0.02, (0, 0, 0.52), soil, 64)
    finish_export([p, s], "aero_planter")


def bench():
    K.reset()
    white = K.mat("aero_white", (0.95, 0.96, 0.97), rough=0.1)
    chrome = K.mat("aero_chrome", (0.9, 0.92, 0.95), rough=0.06, metal=1.0)
    parts = []
    # curved seat: segments along an arc
    R = 2.2; n = 14; span = math.radians(40)
    for i in range(n):
        a = -span / 2 + span * (i + 0.5) / n
        x, y = math.sin(a) * R, -math.cos(a) * R + R
        b = K.box(f"seg{i}", (2 * R * math.sin(span / n / 2) + 0.01, 0.42, 0.08), (x, y, 0.44), white, bevel=0.03, seg=4)
        b.rotation_euler = (0, 0, a); parts.append(b)
    for sx in (-0.55, 0.0, 0.55):
        parts.append(K.cyl(f"leg{sx}", 0.03, 0.4, (sx, 0.02 + (R - math.sqrt(R * R - sx * sx)), 0.2), chrome, 24))
    finish_export(parts, "aero_bench")


def orb():
    K.reset()
    chrome = K.mat("aero_chrome", (0.9, 0.92, 0.95), rough=0.06, metal=1.0)
    gl = glass("aero_glass_orb", (0.6, 0.9, 1.0))
    green = K.mat("aero_leaf", (0.25, 0.75, 0.25), rough=0.4)
    parts = [K.cyl("foot", 0.18, 0.03, (0, 0, 0.015), chrome, 48, bevel=0.008), K.cyl("stem", 0.015, 0.75, (0, 0, 0.4), chrome, 24)]
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.2, location=(0, 0, 0.98)); s = bpy.context.active_object; s.data.materials.append(gl); parts.append(s)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=0.09, location=(0, 0, 0.95)); m = bpy.context.active_object; m.data.materials.append(green); parts.append(m)
    finish_export(parts, "aero_orb")


if __name__ == "__main__":
    tube(); planter(); bench(); orb()
    print("AERO DONE")
