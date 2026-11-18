"""Render a 3/4 preview of each GLB: blender -b --factory-startup -P preview_glb.py -- <out_dir> <glb1> <glb2> ..."""
import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
out = argv[0]; files = argv[1:]
os.makedirs(out, exist_ok=True)
scn = bpy.context.scene
scn.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
scn.render.resolution_x = 640; scn.render.resolution_y = 480
scn.view_settings.view_transform = "AgX"
world = bpy.data.worlds.new("w"); scn.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.57, 0.6, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
for f in files:
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.import_scene.gltf(filepath=f)
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    mn = Vector((1e9, 1e9, 1e9)); mx = -mn
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c); mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    ctr = (mn + mx) / 2; r = max((mx - mn).length, 0.05)
    bpy.ops.mesh.primitive_plane_add(size=r * 8, location=(ctr.x, ctr.y, mn.z))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    cam.location = ctr + Vector((r * 0.9, -r * 1.3, r * 0.75))
    cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scn.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(15), math.radians(30)); sun.data.energy = 3.5
    scn.render.filepath = os.path.join(out, os.path.splitext(os.path.basename(f))[0] + ".png")
    bpy.ops.render.render(write_still=True)
print("PREVIEWS DONE")
