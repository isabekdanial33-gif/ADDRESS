"""ADDRESS player character v2: baggy yellow hazmat coverall, lofted hood with sealed face opening, full-face gas mask
that follows the face, twin knurled filters, gauntlet gloves, rubber boots with soles, fitted duct-tape seals, zipper
with storm flap. Base proportions: CC0 Blender Studio realistic male. Unity-Humanoid armature, FBX export.
  blender -b --factory-startup -P character_hazmat.py -- <base_meshes.blend> <out_dir> [preview_dir]
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
PREV = argv[2] if len(argv) > 2 else None
os.makedirs(OUT, exist_ok=True)
H = 1.78

def mat(name, color, rough, metal=0.0, coat=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if coat: b.inputs["Coat Weight"].default_value = coat; b.inputs["Coat Roughness"].default_value = 0.25
    return m

def sel(o):
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o

def apply_all(o):
    sel(o)
    for md in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)

def dup(o, name):
    n = o.copy(); n.data = o.data.copy(); n.name = name; bpy.context.collection.objects.link(n)
    for md in list(n.modifiers): n.modifiers.remove(md)
    return n

def keep(o, pred):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not pred(o.matrix_world @ v.co)], context="VERTS")
    bm.to_mesh(o.data); bm.free()

def smooth(o, it, lam=0.8):
    md = o.modifiers.new("lap", "LAPLACIANSMOOTH"); md.iterations = it; md.lambda_factor = lam; md.use_volume_preserve = True
    apply_all(o)

def plain_smooth(o, it, f=0.6):
    md = o.modifiers.new("sm", "SMOOTH"); md.iterations = it; md.factor = f; apply_all(o)

def disp(o, strength, tex=None, coords="LOCAL"):
    md = o.modifiers.new("d", "DISPLACE"); md.strength = strength; md.mid_level = 0 if tex is None else 0.5; md.direction = "NORMAL"
    if tex: md.texture = tex; md.texture_coords = coords
    apply_all(o)

def subsurf(o, lv=1):
    md = o.modifiers.new("s", "SUBSURF"); md.levels = lv; md.render_levels = lv; apply_all(o)

def shade(o, m):
    o.data.materials.clear(); o.data.materials.append(m)
    sel(o); bpy.ops.object.shade_smooth()

def prim_cyl(r, h, verts=32, loc=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc); return bpy.context.active_object

def orient(o, origin, axis):
    """Place object so its local +Z points along axis at origin."""
    o.rotation_euler = axis.to_track_quat("Z", "Y").to_euler(); o.location = origin

def join(objs, name):
    for o in objs: apply_all(o)
    sel(objs[0])
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    j = bpy.context.active_object; j.name = name; return j

def bevel(o, w, seg=2):
    md = o.modifiers.new("bev", "BEVEL"); md.width = w; md.segments = seg; md.limit_method = "ANGLE"

# ------------------------------------------------------------------ base body

bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(SRC, link=False) as (src, dst):
    dst.objects = ["GEO-body_male_realistic"]
body = dst.objects[0]; bpy.context.collection.objects.link(body); body.parent = None
for m in body.modifiers:
    if m.type == "MULTIRES": m.levels = 1
apply_all(body)
bb = [body.matrix_world @ Vector(c) for c in body.bound_box]
mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb))); mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
body.location -= Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z)); sel(body); bpy.ops.object.transform_apply(location=True)
s = H / (mx.z - mn.z); body.scale = (s, s, s); bpy.ops.object.transform_apply(scale=True)
hv = [v.co for v in body.data.vertices if v.co.z > H * 0.9]
if abs(min(v.y for v in hv)) < abs(max(v.y for v in hv)):
    body.rotation_euler = (0, 0, math.pi); bpy.ops.object.transform_apply(rotation=True)
V = [v.co.copy() for v in body.data.vertices]
tip = max(V, key=lambda v: v.x)
head_c = Vector((0, sum(v.y for v in V if v.z > 0.9 * H) / max(1, len([v for v in V if v.z > 0.9 * H])), 0.92 * H))
face_y = min(v.y for v in V if v.z > 0.86 * H and abs(v.x) < 0.03)
sh = Vector((0.18 * H / 1.78, 0.0, 0.815 * H)); arm = tip - sh; armd = arm.normalized()
L = {"hips": Vector((0, 0, 0.53 * H)), "spine": Vector((0, 0, 0.60 * H)), "chest": Vector((0, 0, 0.70 * H)), "neck": Vector((0, 0, 0.835 * H)),
     "head": Vector((0, 0, 0.875 * H)), "top": Vector((0, 0, H)), "shoulder": Vector((0.085 * H / 1.78, 0, 0.80 * H)), "upperarm": sh,
     "elbow": sh + arm * 0.40, "wrist": sh + arm * 0.74, "hand_end": sh + arm * 0.92, "upperleg": Vector((0.095 * H / 1.78, 0, 0.50 * H)),
     "knee": Vector((0.10 * H / 1.78, -0.01, 0.285 * H)), "ankle": Vector((0.11 * H / 1.78, 0.02, 0.045 * H)), "toe": Vector((0.12 * H / 1.78, -0.12, 0.01))}
mirror = lambda v, sx: Vector((v.x * sx, v.y, v.z))
along_arm = lambda p: (Vector((abs(p.x), p.y, p.z)) - L["wrist"]).dot(armd)
def arm_dist(p):
    q = Vector((abs(p.x), p.y, p.z)); t = max(0.0, min(1.2, (q - sh).dot(armd) / arm.length))
    return (sh + arm * t - q).length
is_hand = lambda p: along_arm(p) > -0.01 and arm_dist(p) < 0.09 and abs(p.x) > 0.2

def section(obj, center, axis, band=0.012, pct=None):
    """Radius of obj's vertices in a thin slab perpendicular to axis at center (mean, or a percentile to enclose it)."""
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    sl = [p for p in pts if abs((p - center).dot(axis)) < band and (p - center).length < 0.2]
    if abs(center.x) > 0.03: sl = [p for p in sl if p.x * center.x > 0]   # same side only (legs/arms come in pairs)
    if not sl: return center, 0.05
    c = sum(sl, Vector()) / len(sl)
    rs = sorted(((p - c) - axis * (p - c).dot(axis)).length for p in sl)
    r = rs[min(len(rs) - 1, int(len(rs) * pct))] if pct else sum(rs) / len(rs)
    return c, r

# ------------------------------------------------------------------ materials (linear colours)

suit_m = mat("suit_pvc", (0.80, 0.50, 0.010), 0.48)
rubber_m = mat("rubber_black", (0.018, 0.018, 0.018), 0.5)
glove_m = mat("glove_rubber", (0.025, 0.027, 0.025), 0.5)
tape_m = mat("duct_tape", (0.32, 0.32, 0.30), 0.55, metal=0.15)
lens_m = mat("lens_glass", (0.02, 0.025, 0.03), 0.04, metal=0.3)
metal_m = mat("mask_metal", (0.30, 0.31, 0.30), 0.32, metal=1.0)
filter_m = mat("filter_olive", (0.10, 0.12, 0.06), 0.45)
label_m = mat("filter_label", (0.55, 0.5, 0.35), 0.6)
zip_m = mat("zipper", (0.05, 0.05, 0.05), 0.35, metal=0.6)

folds = bpy.data.textures.new("folds", "CLOUDS"); folds.noise_scale = 0.2; folds.noise_depth = 0
crumple = bpy.data.textures.new("crumple", "DISTORTED_NOISE"); crumple.noise_scale = 0.035; crumple.distortion = 2.0

# ------------------------------------------------------------------ coverall

suit = dup(body, "suit")
keep(suit, lambda p: not is_hand(p) and p.z > 0.16 * H and p.z < 0.85 * H)
smooth(suit, 40, 1.0)                     # anatomy gone
plain_smooth(suit, 10, 0.7)
disp(suit, 0.045)                         # baggy volume
plain_smooth(suit, 6, 0.6)
subsurf(suit, 1)
disp(suit, 0.028, folds)                  # big soft folds
plain_smooth(suit, 3, 0.5)
disp(suit, 0.0018, crumple)               # faint crumple of thin PVC
dec = suit.modifiers.new("dec", "DECIMATE"); dec.ratio = 0.35; apply_all(suit)
# smoothing drags the open hem downward: clip it so it ends inside the boot shaft
keep(suit, lambda p: p.z > 0.175 * H)
# tuck the pant hems into the boots: squeeze the lowest 6 cm towards each leg's axis
for sx in (-1, 1):
    lv = [v for v in suit.data.vertices if v.co.x * sx > 0 and v.co.z < 0.215 * H]
    if not lv: continue
    cx = sum(v.co.x for v in lv) / len(lv); cy = sum(v.co.y for v in lv) / len(lv)
    for v in lv:
        k = 1.0 - 0.22 * min(1.0, (0.215 * H - v.co.z) / 0.06)
        v.co.x = cx + (v.co.x - cx) * k; v.co.y = cy + (v.co.y - cy) * k
shade(suit, suit_m)

# storm flap + zipper down the front
bvh = BVHTree.FromObject(suit, bpy.context.evaluated_depsgraph_get())
zparts = []
for i in range(46):
    z = 0.47 * H + i * (0.84 * H - 0.47 * H) / 46
    hit = bvh.ray_cast(Vector((0.0, -0.6, z)), Vector((0, 1, 0)))
    if hit[0] is None: continue
    p, n = hit[0], hit[1]
    bpy.ops.mesh.primitive_cube_add(size=1, location=p + n * 0.003); t = bpy.context.active_object
    t.scale = (0.011, 0.004, 0.006); t.rotation_euler = n.to_track_quat("-Y", "Z").to_euler(); t.data.materials.append(zip_m); zparts.append(t)
    bpy.ops.mesh.primitive_cube_add(size=1, location=p + n * 0.007 + Vector((0.016, 0, 0))); f = bpy.context.active_object
    f.scale = (0.03, 0.006, 0.0085); f.rotation_euler = n.to_track_quat("-Y", "Z").to_euler(); f.data.materials.append(suit_m); zparts.append(f)
zipper = join(zparts, "zipper")

# ------------------------------------------------------------------ hood (lofted rings, clean face cut, rubber seal)

bm = bmesh.new()
prof = [(0.80, 0.215, 0.17, 0.0), (0.83, 0.15, 0.135, 0.0), (0.86, 0.128, 0.142, 0.004), (0.90, 0.122, 0.148, 0.006),
        (0.94, 0.116, 0.142, 0.008), (0.975, 0.098, 0.12, 0.010), (1.0, 0.07, 0.085, 0.012), (1.015, 0.035, 0.04, 0.012)]
rings = []
N = 48
for z, rx, ry, yoff in prof:
    ring = []
    for k in range(N):
        a = 2 * math.pi * k / N
        ring.append(bm.verts.new((math.cos(a) * rx, head_c.y + yoff + math.sin(a) * ry, z * H)))
    rings.append(ring)
for r0, r1 in zip(rings, rings[1:]):
    for k in range(N): bm.faces.new((r0[k], r0[(k + 1) % N], r1[(k + 1) % N], r1[k]))
top = bm.verts.new((0, head_c.y + 0.012, 1.022 * H))
for k in range(N): bm.faces.new((rings[-1][k], rings[-1][(k + 1) % N], top))
me = bpy.data.meshes.new("hood"); bm.to_mesh(me); bm.free()
hood = bpy.data.objects.new("hood", me); bpy.context.collection.objects.link(hood)
subsurf(hood, 2)
disp(hood, 0.012, folds)
# face opening (elliptic boolean)
cut = prim_cyl(1, 0.4, 64); cut.scale = (0.078, 0.105, 1); cut.rotation_euler = (math.pi / 2, 0, 0); cut.location = (0, face_y - 0.05, 0.905 * H)
sel(cut); bpy.ops.object.transform_apply(rotation=True, scale=True)
bo = hood.modifiers.new("cut", "BOOLEAN"); bo.object = cut; bo.operation = "DIFFERENCE"; bo.solver = "EXACT"
so = hood.modifiers.new("sol", "SOLIDIFY"); so.thickness = 0.004
apply_all(hood); bpy.data.objects.remove(cut, do_unlink=True)
shade(hood, suit_m)
hb = BVHTree.FromObject(hood, bpy.context.evaluated_depsgraph_get())
seal_pts = []
for k in range(64):
    a = 2 * math.pi * k / 64
    x, z = math.cos(a) * 0.08, 0.905 * H + math.sin(a) * 0.107
    h = hb.ray_cast(Vector((x, -0.8, z)), Vector((0, 1, 0)))
    seal_pts.append(h[0] if h[0] is not None else Vector((x, face_y - 0.02, z)))
cd = bpy.data.curves.new("seal", "CURVE"); cd.dimensions = "3D"; spl = cd.splines.new("POLY"); spl.points.add(len(seal_pts) - 1)
for i, p in enumerate(seal_pts): spl.points[i].co = (p.x, p.y - 0.002, p.z, 1)
spl.use_cyclic_u = True; cd.bevel_depth = 0.006; cd.bevel_resolution = 3
seal = bpy.data.objects.new("seal", cd); bpy.context.collection.objects.link(seal)
sel(seal); bpy.ops.object.convert(target="MESH"); seal = bpy.context.active_object; seal.data.materials.append(rubber_m)
hood = join([hood, seal], "hood"); sel(hood); bpy.ops.object.shade_smooth()

# ------------------------------------------------------------------ gas mask that follows the face

face_src = dup(body, "face_src")
keep(face_src, lambda p: p.z > 0.82 * H)
smooth(face_src, 20, 1.0)
mask_c = Vector((0, face_y + 0.05, 0.905 * H))
bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=1, location=mask_c)
mask = bpy.context.active_object; mask.name = "mask_shell"
mask.scale = (0.088, 0.09, 0.118); sel(mask); bpy.ops.object.transform_apply(scale=True)
keep(mask, lambda p: p.y < mask_c.y - 0.01)
sw = mask.modifiers.new("sw", "SHRINKWRAP"); sw.target = face_src; sw.wrap_method = "PROJECT"; sw.use_negative_direction = True
sw.use_positive_direction = True; sw.offset = 0.022; sw.use_project_y = True
apply_all(mask)
plain_smooth(mask, 25, 0.8)
so = mask.modifiers.new("sol", "SOLIDIFY"); so.thickness = 0.007; so.offset = 1
apply_all(mask); bpy.data.objects.remove(face_src, do_unlink=True)
shade(mask, rubber_m)
mbvh = BVHTree.FromObject(mask, bpy.context.evaluated_depsgraph_get())
def on_mask(x, z):
    h = mbvh.ray_cast(Vector((x, -0.8, z)), Vector((0, 1, 0)))
    return (h[0], h[1]) if h[0] is not None else (Vector((x, face_y - 0.03, z)), Vector((0, -1, 0)))

mparts = [mask]
for sx in (-1, 1):
    p, n = on_mask(sx * 0.033, 0.928 * H)
    lens = prim_cyl(0.021, 0.006, 48); orient(lens, p + n * 0.002, -n); lens.data.materials.append(lens_m)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.0225, minor_radius=0.0038, major_segments=48, minor_segments=12)
    rim = bpy.context.active_object; orient(rim, p + n * 0.004, -n); rim.data.materials.append(metal_m)
    mparts += [lens, rim]
    # cheek filter: knurled olive canister on a threaded port
    p, n = on_mask(sx * 0.058, 0.872 * H)
    axis = (n + Vector((sx * 0.55, 0.05, -0.35))).normalized()
    port = prim_cyl(0.022, 0.018, 32); orient(port, p + axis * 0.008, axis); port.data.materials.append(rubber_m); bevel(port, 0.002)
    can = prim_cyl(0.041, 0.05, 48); orient(can, p + axis * 0.04, axis); can.data.materials.append(filter_m); bevel(can, 0.005, 3)
    band = prim_cyl(0.0415, 0.018, 48); orient(band, p + axis * 0.04, axis); band.data.materials.append(label_m)
    cap = prim_cyl(0.034, 0.006, 48); orient(cap, p + axis * 0.067, axis); cap.data.materials.append(metal_m); bevel(cap, 0.002)
    mparts += [port, can, band, cap]
    for k in range(20):  # knurling ridges round the rim
        a = 2 * math.pi * k / 20
        bpy.ops.mesh.primitive_cube_add(size=1); r = bpy.context.active_object
        r.scale = (0.004, 0.004, 0.012); r.data.materials.append(filter_m)
        q = axis.to_track_quat("Z", "Y")
        local = Vector((math.cos(a) * 0.042, math.sin(a) * 0.042, 0.016))
        r.location = p + axis * 0.04 + q @ local; r.rotation_euler = (q @ Quaternion((0, 0, 1), a)).to_euler() if False else q.to_euler()
        mparts.append(r)
    for k in range(7):  # intake holes on the cap face
        a = 2 * math.pi * k / 7
        q = axis.to_track_quat("Z", "Y")
        hole = prim_cyl(0.0045, 0.003, 12); hole.data.materials.append(rubber_m)
        hole.rotation_euler = q.to_euler(); hole.location = p + axis * 0.0705 + q @ Vector((math.cos(a) * 0.017, math.sin(a) * 0.017, 0))
        mparts.append(hole)
p, n = on_mask(0.0, 0.868 * H)
valve = prim_cyl(0.024, 0.026, 40); orient(valve, p + n * 0.012, -n); valve.data.materials.append(rubber_m); bevel(valve, 0.004, 2)
grille = prim_cyl(0.019, 0.004, 32); orient(grille, p + n * 0.026, -n); grille.data.materials.append(metal_m)
mparts += [valve, grille]
for k in range(5):
    bpy.ops.mesh.primitive_cube_add(size=1); slot = bpy.context.active_object
    slot.scale = (0.026, 0.0025, 0.002); slot.location = p + n * 0.0285 + Vector((0, 0, -0.012 + k * 0.006)); slot.data.materials.append(rubber_m)
    mparts.append(slot)
from mathutils import Quaternion
gasmask = join(mparts, "gas_mask")
sel(gasmask); bpy.ops.object.shade_smooth()
dec = gasmask.modifiers.new("dec", "DECIMATE"); dec.ratio = 0.4; apply_all(gasmask)

# ------------------------------------------------------------------ gloves with gauntlet cuffs

gloves = dup(body, "gloves")
keep(gloves, lambda p: along_arm(p) > -0.03 and arm_dist(p) < 0.09 and abs(p.x) > 0.2)
smooth(gloves, 3, 0.5); disp(gloves, 0.004)
cuffs = []
for sx in (-1, 1):
    w = mirror(L["wrist"], sx); d = mirror(armd, sx)
    c, r = section(suit, w - d * 0.05, d)
    cuff = prim_cyl(r + 0.006, 0.09, 40); orient(cuff, c - d * 0.01, d); cuff.data.materials.append(glove_m)
    # flare the cuff opening
    bm = bmesh.new(); bm.from_mesh(cuff.data)
    for v in bm.verts:
        if v.co.z > 0: v.co.x *= 1.06; v.co.y *= 1.06
    bm.to_mesh(cuff.data); bm.free()
    so = cuff.modifiers.new("sol", "SOLIDIFY"); so.thickness = 0.003
    cuffs.append(cuff)
gloves = join([gloves] + cuffs, "gloves"); shade(gloves, glove_m)

# ------------------------------------------------------------------ boots with soles

boot_parts = []
for sx in (-1, 1):
    ank = mirror(L["ankle"], sx)
    bm = bmesh.new(); rings = []
    # foot: ellipse rings along the foot axis, flat bottom; shaft: rings up to the boot top
    spec = [(-0.23, 0.10, 0.045, 0.06, 0.04), (-0.16, 0.06, 0.047, 0.055, 0.045), (-0.08, 0.02, 0.05, 0.05, 0.05), (0.0, 0.0, 0.052, 0.055, 0.06),
            (0.03, 0.02, 0.05, 0.06, 0.075)]
    for f, dy, rx, rz, zc in spec:  # f: offset along foot (negative = towards toe)
        ring = []
        cy = ank.y + f + 0.03
        for k in range(32):
            ang = 2 * math.pi * k / 32
            x = ank.x + math.cos(ang) * rx
            z = max(0.02, zc + math.sin(ang) * rz)
            ring.append(bm.verts.new((x, cy, z)))
        rings.append(ring)
    for z in (0.11, 0.155 * H, 0.19 * H, 0.22 * H, 0.245 * H):
        if z < 0.12:
            c, r = Vector((ank.x, ank.y + 0.025, z)), 0.058
        else:
            c, r = section(suit, Vector((ank.x, ank.y, max(z, 0.165 * H))), Vector((0, 0, 1)), 0.02, pct=0.97)
            c.z = z; r = r + 0.008               # boot shaft over the tucked-in pant leg
        ring = [bm.verts.new((c.x + math.cos(2 * math.pi * k / 32) * r, c.y + math.sin(2 * math.pi * k / 32) * r, c.z)) for k in range(32)]
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(32): bm.faces.new((r0[k], r0[(k + 1) % 32], r1[(k + 1) % 32], r1[k]))
    bm.faces.new(rings[0][::-1])
    me = bpy.data.meshes.new("boot"); bm.to_mesh(me); bm.free()
    boot = bpy.data.objects.new(f"boot{sx}", me); bpy.context.collection.objects.link(boot)
    subsurf(boot, 2); plain_smooth(boot, 4, 0.5)
    # keep the bottom flat on the sole
    for v in boot.data.vertices: v.co.z = max(v.co.z, 0.022)
    so = boot.modifiers.new("sol", "SOLIDIFY"); so.thickness = 0.004; apply_all(boot)
    xs = [v.co.x for v in boot.data.vertices if v.co.z < 0.03]; ys = [v.co.y for v in boot.data.vertices if v.co.z < 0.03]
    bpy.ops.mesh.primitive_cube_add(size=1, location=((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, 0.012)); sole = bpy.context.active_object
    sole.scale = (max(xs) - min(xs) + 0.012, max(ys) - min(ys) + 0.016, 0.024); bevel(sole, 0.008, 3); sole.data.materials.append(rubber_m)
    boot.data.materials.append(rubber_m)
    boot_parts += [boot, sole]
boots = join(boot_parts, "boots"); shade(boots, rubber_m)

# ------------------------------------------------------------------ fitted duct tape seals (over glove cuffs and boot tops)

tapes = []
for sx in (-1, 1):
    d = mirror(armd, sx)
    c, r = section(gloves, mirror(L["wrist"], sx) - d * 0.09, d, 0.01)
    bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=r + 0.004, depth=0.045); t = bpy.context.active_object
    orient(t, c, d); t.data.materials.append(tape_m); tapes.append(t)
    top_c, rb = section(boots, mirror(Vector((L["ankle"].x, L["ankle"].y + 0.025, 0.24 * H)), sx), Vector((0, 0, 1)), 0.02)
    bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=rb + 0.005, depth=0.05, location=top_c); t2 = bpy.context.active_object
    t2.data.materials.append(tape_m); tapes.append(t2)
for t in tapes:
    so = t.modifiers.new("sol", "SOLIDIFY"); so.thickness = 0.003
    bm = bmesh.new(); bm.from_mesh(t.data)
    caps = [f for f in bm.faces if abs(f.normal.z) > 0.9]; bmesh.ops.delete(bm, geom=caps, context="FACES"); bm.to_mesh(t.data); bm.free()
tape = join(tapes, "tape"); shade(tape, tape_m)

bpy.data.objects.remove(body, do_unlink=True)
parts = [suit, zipper, hood, gasmask, gloves, boots, tape]
for o in parts:
    tri = sum(len(p.vertices) - 2 for p in o.data.polygons)
    print("part", o.name, "tris", tri)

# ------------------------------------------------------------------ armature + skinning

bpy.ops.object.armature_add(location=(0, 0, 0)); rig = bpy.context.active_object; rig.name = "Armature"
bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones
for b in list(eb): eb.remove(b)
def bone(n, h, t, par=None):
    b = eb.new(n); b.head = h; b.tail = t
    if par: b.parent = eb[par]
bone("Hips", L["hips"], L["spine"]); bone("Spine", L["spine"], L["chest"], "Hips"); bone("Chest", L["chest"], L["neck"] - Vector((0, 0, 0.06)), "Spine")
bone("UpperChest", L["neck"] - Vector((0, 0, 0.06)), L["neck"], "Chest"); bone("Neck", L["neck"], L["head"], "UpperChest"); bone("Head", L["head"], L["top"], "Neck")
for side, sx in (("Left", 1), ("Right", -1)):
    M = lambda v: mirror(v, sx)
    bone(f"{side}Shoulder", M(L["shoulder"]), M(L["upperarm"]), "UpperChest"); bone(f"{side}UpperArm", M(L["upperarm"]), M(L["elbow"]), f"{side}Shoulder")
    bone(f"{side}LowerArm", M(L["elbow"]), M(L["wrist"]), f"{side}UpperArm"); bone(f"{side}Hand", M(L["wrist"]), M(L["hand_end"]), f"{side}LowerArm")
    bone(f"{side}UpperLeg", M(L["upperleg"]), M(L["knee"]), "Hips"); bone(f"{side}LowerLeg", M(L["knee"]), M(L["ankle"]), f"{side}UpperLeg")
    bone(f"{side}Foot", M(L["ankle"]), M(L["toe"]) + Vector((0, 0.05, 0)), f"{side}LowerLeg"); bone(f"{side}Toes", M(L["toe"]) + Vector((0, 0.05, 0)), M(L["toe"]) - Vector((0, 0.03, 0)), f"{side}Foot")
bpy.ops.object.mode_set(mode="OBJECT")
for o in parts:
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    if not any(len(v.groups) for v in o.data.vertices): sel(rig); bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")
gasmask.vertex_groups.clear(); g = gasmask.vertex_groups.new(name="Head"); g.add(list(range(len(gasmask.data.vertices))), 1.0, "REPLACE")

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "hazmat_player.fbx"), use_selection=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                         add_leaf_bones=False, bake_anim=False, object_types={"ARMATURE", "MESH"})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "hazmat_player.blend"))

if PREV:
    os.makedirs(PREV, exist_ok=True)
    scn = bpy.context.scene; scn.render.engine = "BLENDER_EEVEE"; scn.render.resolution_x = 900; scn.render.resolution_y = 1200
    scn.view_settings.view_transform = "Standard"; scn.view_settings.look = "None"
    w = bpy.data.worlds.new("w"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.36, 0.38, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.5
    bpy.ops.mesh.primitive_plane_add(size=8); bpy.context.active_object.data.materials.append(mat("floor", (0.25, 0.25, 0.25), 0.8))
    for nm, en, loc, rot in (("key", 700, (1.6, -2.2, 2.6), (55, 0, 35)), ("fill", 250, (-2.2, -1.5, 1.6), (70, 0, -55)), ("rim", 500, (-1.5, 2.0, 2.4), (-55, 0, -140))):
        l = bpy.data.objects.new(nm, bpy.data.lights.new(nm, "AREA")); scn.collection.objects.link(l)
        l.data.energy = en; l.data.size = 1.8; l.location = loc; l.rotation_euler = tuple(math.radians(a) for a in rot)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam; cam.data.lens = 60
    for n, p in {"front": (0, -4.2, 1.0), "three_quarter": (2.6, -3.2, 1.2), "side": (4.2, 0, 1.0), "back": (0, 4.2, 1.0)}.items():
        cam.location = p; cam.rotation_euler = (Vector((0, 0, 0.92)) - Vector(p)).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = os.path.join(PREV, f"hazmat_{n}.png"); bpy.ops.render.render(write_still=True)
    cam.data.lens = 85; cam.location = (0.42, -0.78, 1.66); cam.rotation_euler = (Vector((0, head_c.y, 1.6)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scn.render.filepath = os.path.join(PREV, "hazmat_mask_closeup.png"); bpy.ops.render.render(write_still=True)
    cam.location = (0.5, -0.9, 0.95); cam.rotation_euler = (Vector((0.3, 0, 0.8)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scn.render.filepath = os.path.join(PREV, "hazmat_glove_closeup.png"); bpy.ops.render.render(write_still=True)
print("HAZMAT DONE")
