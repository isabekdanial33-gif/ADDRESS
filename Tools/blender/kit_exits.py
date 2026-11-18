"""Exit models (Backrooms) for ADDRESS. blender -b --factory-startup -P kit_exits.py -- <out_dir>
Front faces Blender -Y (Unity +Z = into the room). Origin bottom centre, back face at Blender +Y = wall plane.
Moving parts are separate named objects inside one GLB (door leaf, elevator doors) so Unity can animate them.
"""
import bpy, sys, os, math
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import kit_backrooms as K   # reuse helpers (mat, box, cyl, join, ...)

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp/exits"
os.makedirs(OUT, exist_ok=True)


def export_all(name):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, name + ".glb"), use_selection=True, export_apply=True, export_format="GLB", export_yup=True)
    print("exported", name)


def finish(o, name):
    o.name = name
    for md in list(o.modifiers):
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=md.name)
    K.smooth(o)
    return o


def handleless_door():
    """A real-looking panelled office door. The handle is a separate object: Unity shows it on some doors and hides
    it on others (a door with no handle is the oddity you notice). The leaf pivots on its hinge line."""
    K.reset()
    leaf_m = K.mat("door_leaf", (0.80, 0.70, 0.36), rough=0.55)
    trim_m = K.mat("door_trim", (0.74, 0.64, 0.33), rough=0.5)
    seam_m = K.mat("door_seam", (0.02, 0.018, 0.012), rough=0.9)
    hinge_m = K.mat("door_hinge", (0.55, 0.52, 0.45), rough=0.3, metal=0.85)
    steel = K.mat("door_steel", (0.70, 0.70, 0.68), rough=0.25, metal=1.0)
    W, H, D = 0.86, 2.02, 0.04
    seam = [K.box("s1", (W + 0.012, 0.03, 0.006), (0, 0.0, H + 0.003), seam_m),
            K.box("s2", (0.006, 0.03, H + 0.006), (-W / 2 - 0.003, 0.0, H / 2), seam_m),
            K.box("s3", (0.006, 0.03, H + 0.006), (W / 2 + 0.003, 0.0, H / 2), seam_m)]
    finish(K.join(seam, "seam"), "seam")
    parts = [K.box("leaf", (W, D, H), (0, -0.004, H / 2), leaf_m, bevel=0.003)]
    # four recessed panels framed by mouldings (front face at y = -0.024)
    fy = -0.004 - D / 2
    panels = [(-0.19, 1.43, 0.30, 0.95), (0.19, 1.43, 0.30, 0.95), (-0.19, 0.48, 0.30, 0.68), (0.19, 0.48, 0.30, 0.68)]
    for i, (cx, cz, pw, ph) in enumerate(panels):
        t = 0.018
        parts += [K.box(f"m{i}a", (pw + t, 0.008, t), (cx, fy - 0.003, cz + ph / 2), trim_m, bevel=0.003),
                  K.box(f"m{i}b", (pw + t, 0.008, t), (cx, fy - 0.003, cz - ph / 2), trim_m, bevel=0.003),
                  K.box(f"m{i}c", (t, 0.008, ph + t), (cx - pw / 2, fy - 0.003, cz), trim_m, bevel=0.003),
                  K.box(f"m{i}d", (t, 0.008, ph + t), (cx + pw / 2, fy - 0.003, cz), trim_m, bevel=0.003)]
    parts.append(K.box("kick", (W - 0.06, 0.003, 0.2), (0, fy - 0.0015, 0.12), steel, bevel=0.001))
    leaf = K.join(parts, "leaf"); finish(leaf, "leaf")
    bpy.ops.object.select_all(action="DESELECT"); leaf.select_set(True); bpy.context.view_layer.objects.active = leaf
    bpy.context.scene.cursor.location = (-W / 2, -0.004, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    # lever handle on the latch side (front), with a round rose
    hx = W / 2 - 0.075
    rose = K.cyl("rose", 0.028, 0.008, (hx, fy - 0.004, 1.02), steel, 32, bevel=0.002, rot=(math.pi / 2, 0, 0))
    neck = K.cyl("neck", 0.009, 0.05, (hx, fy - 0.03, 1.02), steel, 16, rot=(math.pi / 2, 0, 0))
    lever = K.box("lever", (0.13, 0.018, 0.018), (hx - 0.055, fy - 0.055, 1.02), steel, bevel=0.007, seg=3)
    plate = K.box("lockplate", (0.022, 0.006, 0.12), (hx, fy - 0.003, 0.9), steel, bevel=0.002)
    handle = K.join([rose, neck, lever, plate], "door_handle"); finish(handle, "door_handle")
    hinges = [K.cyl(f"h{i}", 0.008, 0.09, (-W / 2 - 0.004, -0.024, z), hinge_m, 16, bevel=0.002) for i, z in enumerate((0.25, 1.0, 1.75))]
    finish(K.join(hinges, "hinges"), "hinges")
    export_all("exit_door_handleless")


def elevator():
    K.reset()
    steel = K.mat("elev_steel", (0.62, 0.62, 0.60), rough=0.28, metal=1.0)
    frame_m = K.mat("elev_frame", (0.42, 0.41, 0.39), rough=0.35, metal=0.9)
    dark = K.mat("elev_dark", (0.015, 0.015, 0.015), rough=0.9)
    button = K.mat("elev_button", (1.0, 0.75, 0.35), rough=0.3, emit=(1.0, 0.6, 0.2), emit_strength=4.0)
    disp = K.mat("elev_display", (0.05, 0.02, 0.01), rough=0.1, emit=(1.0, 0.25, 0.05), emit_strength=2.0)
    W, H = 1.0, 2.1
    # surround frame (jambs + head) proud of the wall
    parts = [K.box("jl", (0.12, 0.06, H + 0.12), (-W / 2 - 0.06, -0.03, (H + 0.12) / 2), frame_m, bevel=0.006),
             K.box("jr", (0.12, 0.06, H + 0.12), (W / 2 + 0.06, -0.03, (H + 0.12) / 2), frame_m, bevel=0.006),
             K.box("hd", (W + 0.24, 0.06, 0.12), (0, -0.03, H + 0.06), frame_m, bevel=0.006),
             K.box("sill", (W + 0.24, 0.10, 0.012), (0, -0.05, 0.006), frame_m, bevel=0.002),
             K.box("void", (W, 0.02, H), (0, 0.01, H / 2), dark)]
    finish(K.join(parts, "frame"), "frame")
    for side in (-1, 1):
        d = K.box(f"door_{'l' if side < 0 else 'r'}", (W / 2 - 0.004, 0.025, H), (side * W / 4, -0.02, H / 2), steel, bevel=0.003)
        edge = K.box("edge", (0.012, 0.03, H - 0.02), (side * 0.006, -0.02, H / 2), dark, bevel=0.003)      # rubber safety edge
        groove = K.box("groove", (W / 2 - 0.06, 0.004, 0.004), (side * W / 4, -0.034, 1.0), frame_m)          # brushed band
        finish(K.join([d, edge, groove], f"door_{'l' if side < 0 else 'r'}"), f"door_{'l' if side < 0 else 'r'}")
    # call panel: brushed plate, up/down buttons with chrome rings, engraved lines
    px = W / 2 + 0.26
    panel = K.box("panel", (0.10, 0.012, 0.26), (px, -0.006, 1.06), steel, bevel=0.005, seg=3)
    ring1 = K.cyl("r1", 0.022, 0.006, (px, -0.014, 1.11), frame_m, 32, bevel=0.002, rot=(math.pi / 2, 0, 0))
    ring2 = K.cyl("r2", 0.022, 0.006, (px, -0.014, 1.01), frame_m, 32, bevel=0.002, rot=(math.pi / 2, 0, 0))
    btn = K.cyl("btn", 0.016, 0.01, (px, -0.019, 1.11), button, 24, rot=(math.pi / 2, 0, 0))
    btn2 = K.cyl("btn2", 0.016, 0.01, (px, -0.019, 1.01), frame_m, 24, rot=(math.pi / 2, 0, 0))
    braille = [K.cyl(f"b{i}", 0.002, 0.003, (px + 0.032, -0.014, 1.11 + (i - 1) * 0.008), frame_m, 8, rot=(math.pi / 2, 0, 0)) for i in range(3)]
    finish(K.join([panel, ring1, ring2, btn, btn2] + braille, "call_panel"), "call_panel")
    ind = K.box("indicator", (0.22, 0.015, 0.07), (0, -0.065, H + 0.2), disp, bevel=0.003)
    finish(ind, "indicator")
    export_all("exit_elevator")


def vent_hatch():
    K.reset()
    metal = K.mat("hatch_metal", (0.72, 0.71, 0.66), rough=0.45, metal=0.6)
    dark = K.mat("hatch_dark", (0.01, 0.01, 0.01), rough=1.0)
    W, H = 0.62, 0.46
    frame = K.box("frame", (W, 0.014, H), (0, -0.007, H / 2), metal, bevel=0.003)
    cut = K.box("cut", (W - 0.05, 0.05, H - 0.05), (0, 0, H / 2)); K.boolean_cut(frame, cut)
    fins = []
    for i in range(10):
        fins.append(K.box(f"f{i}", (W - 0.05, 0.016, 0.004), (0, -0.006, 0.035 + i * 0.041), metal, bevel=0.0008))
    screws = [K.cyl(f"sc{i}", 0.006, 0.006, (sx * (W / 2 - 0.012), -0.016, sz), metal, 12, rot=(math.pi / 2, 0, 0))
              for i, (sx, sz) in enumerate(((-1, 0.012), (1, 0.012), (-1, H - 0.012), (1, H - 0.012)))]
    grille = K.join([frame] + fins + screws, "grille")
    finish(grille, "grille")
    back = K.box("duct", (W - 0.05, 0.02, H - 0.05), (0, 0.012, H / 2), dark)
    finish(back, "duct")
    export_all("exit_vent_hatch")


if __name__ == "__main__":
    handleless_door(); elevator(); vent_hatch()
    print("EXITS DONE")
