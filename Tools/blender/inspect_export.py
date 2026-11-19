# blender -b --python inspect_export.py -- <in.(blend|fbx)> <out.fbx>
# Prints armatures, meshes (tri counts), actions; exports everything as FBX with all actions baked as takes.
import bpy, sys, os
args = sys.argv[sys.argv.index('--') + 1:]
src, out = args[0], args[1]
if src.lower().endswith(('.glb','.gltf')):
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=src)
elif src.lower().endswith('.blend'):
    bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
tris = 0
for o in bpy.data.objects:
    info = o.type
    if o.type == 'MESH':
        me = o.data; t = sum(len(p.vertices) - 2 for p in me.polygons); tris += t
        info += f' tris={t} mods={[m.type for m in o.modifiers]} mats={[m.name for m in me.materials if m]}'
    if o.type == 'ARMATURE':
        info += f' bones={len(o.data.bones)} first={[b.name for b in o.data.bones][:12]}'
    print('OBJ', o.name, info, 'dims', tuple(round(d, 2) for d in o.dimensions), 'hidden', o.hide_get() if o.name in bpy.context.view_layer.objects else 'n/a')
print('TRIS', tris)
for a in bpy.data.actions:
    print('ACT', a.name, tuple(round(x) for x in a.frame_range))
for i in bpy.data.images:
    print('IMG', i.name, i.filepath, i.size[:])
# make everything visible & exportable
for o in bpy.data.objects:
    try: o.hide_set(False); o.hide_viewport = False
    except Exception: pass
bpy.ops.file.pack_all() if False else None
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         path_mode='COPY', embed_textures=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL')
print('EXPORTED', out, os.path.getsize(out))
