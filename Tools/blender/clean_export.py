# blender -b --python clean_export.py -- <in> <out.fbx> [keep_unskinned=0]
# Imports a downloaded creature, keeps only the armature(s) + meshes deformed by them, prints bone names, height,
# actions, and exports FBX (all actions as takes, embedded textures).
import bpy, sys, os, mathutils
args = sys.argv[sys.argv.index('--') + 1:]
src, out = args[0], args[1]
keep_unskinned = len(args) > 2 and args[2] == '1'
low = src.lower()
if low.endswith('.blend'):
    bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)
    if low.endswith(('.glb', '.gltf')): bpy.ops.import_scene.gltf(filepath=src)
    else: bpy.ops.import_scene.fbx(filepath=src)

def skinned(o):
    return o.type == 'MESH' and (any(m.type == 'ARMATURE' and m.object for m in o.modifiers) or (o.parent and o.parent.type == 'ARMATURE' and o.parent_type == 'ARMATURE'))
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
keep = set(arms)
for o in bpy.data.objects:
    if skinned(o) or (keep_unskinned and o.type == 'MESH'): keep.add(o)
# keep empties that are ancestors of kept objects (gltf/fbx roots carry transforms)
for o in list(keep):
    p = o.parent
    while p: keep.add(p); p = p.parent
for o in list(bpy.data.objects):
    if o not in keep: bpy.data.objects.remove(o, do_unlink=True)
for o in bpy.data.objects:
    try: o.hide_set(False); o.hide_viewport = False; o.hide_render = False
    except Exception: pass
bpy.context.view_layer.update()
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o, do_unlink=True)
# ATTACH=1: rigid parts (a head on an empty) ride on the nearest bone, so they follow the animation
if os.environ.get('ATTACH') == '1' and arms:
    a = arms[0]
    for o in list(bpy.data.objects):
        if o.type != 'MESH' or skinned(o): continue
        c = o.matrix_world @ (sum((mathutils.Vector(v) for v in o.bound_box), mathutils.Vector()) / 8)
        named = [b for b in a.data.bones if 'head' in b.name.lower() and 'end' not in b.name.lower()] or [b for b in a.data.bones if 'neck' in b.name.lower()]
        best = min(named or list(a.data.bones), key=lambda b: ((a.matrix_world @ b.head_local) - c).length)
        mw = o.matrix_world.copy(); o.parent = a; o.parent_type = 'BONE'; o.parent_bone = best.name
        bpy.context.view_layer.update(); o.matrix_world = mw
        print('ATTACHED', o.name, '->', best.name)
dec = float(os.environ.get('DECIMATE', '0') or 0)
if dec > 0:
    for o in bpy.data.objects:
        if o.type == 'MESH':
            m = o.modifiers.new('dec', 'DECIMATE'); m.ratio = dec
            bpy.context.view_layer.objects.active = o
            # decimate must run before the armature: move it to the top, then apply
            while o.modifiers.find('dec') > 0: bpy.ops.object.modifier_move_up(modifier='dec')
            bpy.ops.object.modifier_apply(modifier='dec')
import re
# STRIP=1: glTF importer appends _<n> to bone names (mixamorig:Hips_64) -> strip so Unity's humanoid auto-map works
if os.environ.get('STRIP') == '1':
    for a in arms:
        for b in a.data.bones:
            n = re.sub(r'(_end)?(_\d+)+$', '', b.name)
            if n != b.name and n not in a.data.bones: b.name = n
# KEEP_ACTIONS=<regex>: drop all other actions (keeps the fbx small)
ka = os.environ.get('KEEP_ACTIONS')
if ka:
    for act in list(bpy.data.actions):
        if not re.search(ka, act.name): bpy.data.actions.remove(act)
# APPLY=1: bake every parent/scale into the armature and meshes (rigs that come at 0.002 scale under cm-scaled empties)
if os.environ.get('APPLY') == '2':
    # meshes whose rest data sits far from the skeleton: bake the rest-pose deformation, then re-bind (vertex groups keep the weights)
    # the pose the model is displayed in (on Sketchfab) becomes the new rest pose
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for m in meshes:
        arm = next((md.object for md in m.modifiers if md.type == 'ARMATURE' and md.object), None)
        bpy.ops.object.select_all(action='DESELECT'); m.select_set(True); bpy.context.view_layer.objects.active = m
        for md in list(m.modifiers):
            if md.type == 'ARMATURE': bpy.ops.object.modifier_apply(modifier=md.name)
        mw = m.matrix_world.copy(); m.parent = None; m.matrix_world = mw
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        m['_arm'] = arm.name if arm else ''
    for a in arms:
        mw = a.matrix_world.copy(); a.parent = None; a.matrix_world = mw
        bpy.ops.object.select_all(action='DESELECT'); a.select_set(True); bpy.context.view_layer.objects.active = a
        bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode='OBJECT')
        a.animation_data_clear()
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for m in meshes:
        arm = bpy.data.objects.get(m.get('_arm', ''))
        if not arm: continue
        md = m.modifiers.new('Armature', 'ARMATURE'); md.object = arm
        m.parent = arm; m.matrix_parent_inverse = arm.matrix_world.inverted()
    for o in list(bpy.data.objects):
        if o.type == 'EMPTY' and not o.children: bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.update()
    for a in arms:
        hb = [b for b in a.data.bones if 'Hips' in b.name]; hd = [b for b in a.data.bones if b.name.endswith('Head')]
        if hb and hd: print('APPLIED hips', tuple(round(v, 2) for v in a.matrix_world @ hb[0].head_local), 'head', tuple(round(v, 2) for v in a.matrix_world @ hd[0].head_local))
if os.environ.get('APPLY') == '1':
    for o in list(bpy.data.objects):
        if o.type in ('ARMATURE', 'MESH') and o.parent and o.parent.type == 'EMPTY':
            mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in bpy.data.objects: o.select_set(o.type in ('ARMATURE', 'MESH'))
    if arms: bpy.context.view_layer.objects.active = arms[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for o in list(bpy.data.objects):
        if o.type == 'EMPTY' and not o.children: bpy.data.objects.remove(o, do_unlink=True)
    for a in arms:
        hb = [b for b in a.data.bones if 'Hips' in b.name]; hd = [b for b in a.data.bones if b.name.endswith('Head')]
        if hb and hd: print('APPLIED hips', tuple(round(v, 2) for v in a.matrix_world @ hb[0].head_local), 'head', tuple(round(v, 2) for v in a.matrix_world @ hd[0].head_local))
import mathutils
mn = mathutils.Vector((1e9,) * 3); mx = -mn
tris = 0
for o in bpy.data.objects:
    if o.type == 'MESH':
        tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
        for c in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(c); mn = mathutils.Vector(map(min, mn, w)); mx = mathutils.Vector(map(max, mx, w))
print('BOUNDS', tuple(round(v, 2) for v in mn), tuple(round(v, 2) for v in mx), 'TRIS', tris)
for a in arms: print('BONES', a.name, len(a.data.bones), [b.name for b in a.data.bones])
for a in bpy.data.actions: print('ACT', a.name, tuple(round(x) for x in a.frame_range))
# textures as real PNG files next to the fbx (Unity finds them by file name)
odir = os.path.dirname(os.path.abspath(out))
used = set()
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    for ms in o.material_slots:
        if ms.material and ms.material.use_nodes:
            for n in ms.material.node_tree.nodes:
                if n.type == 'TEX_IMAGE' and n.image: used.add(n.image)
stem = os.path.splitext(os.path.basename(out))[0]
for i, img in enumerate(used):
    try:
        if img.size[0] == 0: print('NOIMG', img.name); continue
        fn = os.path.join(odir, f'{stem}_{i}.png')
        img.filepath_raw = fn; img.file_format = 'PNG'; img.save()
        print('TEX', img.name, '->', os.path.basename(fn), img.size[:])
    except Exception as e: print('TEXFAIL', img.name, e)
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         path_mode='RELATIVE', embed_textures=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL')
print('EXPORTED', out, os.path.getsize(out))
