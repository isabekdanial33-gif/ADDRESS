"""First-person arms for ADDRESS, cut out of the hazmat character (suit sleeve below the elbow + glove + cuff tape).
  blender -b hazmat_player.blend -P fp_arms.py -- <out_dir>
Each arm is a rigid mesh: origin at the elbow, forearm along Blender -Y (Unity +Z), palm facing the body midline.
"""
import bpy, bmesh, sys, os
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp"
rig = bpy.data.objects["Armature"]
bones = rig.data.bones


def wpos(b, tail=False):
    return rig.matrix_world @ (bones[b].tail_local if tail else bones[b].head_local)


def cut(src_name, side):
    src = bpy.data.objects[src_name]
    keep = {f"{side}LowerArm", f"{side}Hand"}
    gi = {g.index for g in src.vertex_groups if g.name in keep}
    me = src.data.copy(); ob = bpy.data.objects.new(src_name + "_" + side, me); bpy.context.collection.objects.link(ob)
    ob.matrix_world = src.matrix_world.copy()
    bm = bmesh.new(); bm.from_mesh(me)
    dl = bm.verts.layers.deform.active
    kill = []
    for v in bm.verts:
        w = sum(wt for g, wt in v[dl].items() if g in gi) if dl else 0
        if w < 0.5: kill.append(v)
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(me); bm.free()
    ob.vertex_groups.clear(); ob.modifiers.clear(); ob.parent = None
    return ob if len(me.vertices) > 0 else None


for side in ("Left", "Right"):
    parts = [p for p in (cut("suit", side), cut("gloves", side), cut("tape", side)) if p]
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join(); arm = bpy.context.active_object; arm.name = f"fp_arm_{side}"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    elbow, wrist, tip = wpos(f"{side}LowerArm"), wpos(f"{side}Hand"), wpos(f"{side}Hand", True)
    axis = (wrist - elbow).normalized()
    # palm faces the body midline in the rest pose
    inward = Vector((-elbow.x, 0, 0)).normalized()
    side_v = (inward - axis * inward.dot(axis)).normalized()
    up = axis.cross(side_v).normalized()
    # build basis: forearm -> -Y, palm(inward) -> +X, up -> +Z
    R = Matrix(((side_v.x, side_v.y, side_v.z), (-axis.x, -axis.y, -axis.z), (up.x, up.y, up.z)))
    M = Matrix.Translation((0, 0, 0)) @ R.to_4x4() @ Matrix.Translation(-elbow)
    arm.data.transform(M)
    arm.matrix_world = Matrix.Identity(4)
    print(side, "verts", len(arm.data.vertices), "forearm len", (wrist - elbow).length, "hand len", (tip - wrist).length)
    for o in bpy.data.objects: o.select_set(False)
    arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, f"fp_arm_{side}.glb"), use_selection=True, export_format="GLB", export_yup=True, export_apply=True)
print("FP ARMS DONE")
