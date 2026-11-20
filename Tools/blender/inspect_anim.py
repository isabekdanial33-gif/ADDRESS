"""Inspect downloaded creatures: actions, rig, size; render 3 frames of each action side by side.
blender -b --factory-startup -P inspect_anim.py -- <out_dir> <glb...>"""
import bpy, sys, os, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
out = argv[0]; files = argv[1:]
os.makedirs(out, exist_ok=True)
scn = bpy.context.scene
scn.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
scn.render.resolution_x = 360; scn.render.resolution_y = 360
scn.view_settings.view_transform = "AgX"
def bounds():
    dg = bpy.context.evaluated_depsgraph_get()
    mn = Vector((1e9,)*3); mx = -mn
    for o in bpy.data.objects:
        if o.type != "MESH" or o.hide_render: continue
        e = o.evaluated_get(dg); m = e.to_mesh()
        for v in list(m.vertices)[::7]:
            w = o.matrix_world @ v.co; mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
        e.to_mesh_clear()
    return mn, mx
for f in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scn = bpy.context.scene
    scn.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    scn.render.resolution_x = 320; scn.render.resolution_y = 320; scn.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("w"); scn.world = world; world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.57, 0.6, 1)
    bpy.ops.import_scene.gltf(filepath=f)
    name = os.path.splitext(os.path.basename(f))[0][:24]
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    skinned = [o for o in meshes if any(m.type == "ARMATURE" for m in o.modifiers)]
    tris = sum(len(o.data.polygons) for o in meshes)
    acts = [(a.name, int(a.frame_range[0]), int(a.frame_range[1])) for a in bpy.data.actions]
    bones = len(arms[0].data.bones) if arms else 0
    print(f"INFO {name}: meshes {len(meshes)} skinned {len(skinned)} polys {tris} armatures {len(arms)} bones {bones}")
    for a in acts: print(f"  ACT {a}")
    mn, mx = bounds(); print(f"  SIZE {tuple(round(x,2) for x in (mx-mn))}")
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scn.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(15), math.radians(30)); sun.data.energy = 3.5
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    shots = [(None, 0)]
    for (an, a0, a1) in acts[:6]:
        shots += [(an, a0 + (a1 - a0) // 3), (an, a0 + 2 * (a1 - a0) // 3)]
    for i, (an, fr) in enumerate(shots):
        if an and arms:
            arms[0].animation_data_create(); arms[0].animation_data.action = bpy.data.actions[an]
            try:
                slots = bpy.data.actions[an].slots
                if len(slots): arms[0].animation_data.action_slot = slots[0]
            except Exception: pass
        scn.frame_set(fr)
        mn, mx = bounds(); ctr = (mn + mx) / 2; r = max((mx - mn).length, 0.05)
        cam.location = ctr + Vector((r * 0.7, -r * 1.2, r * 0.35))
        cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = os.path.join(out, f"{name}_{i:02d}.png")
        bpy.ops.render.render(write_still=True)
print("DONE")
