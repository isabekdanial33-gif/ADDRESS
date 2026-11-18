"""Source of truth for Data/profiles.json (semantic placement rules per model). Run: python -I gen_profiles.py"""
import json, os

P = []


def add(id, **k):
    d = {"id": id}
    d.update(k)
    P.append(d)


OFF = ["backrooms", "hotel", "school", "mall", "oldweb"]
ALL = []
SURF = dict(support="Surface", rotation="Free", massClass="Small")

# heavy floor furniture
add("metal_office_desk", group="desk", rotation="FaceWall", massClass="Heavy", mass=60, clearanceFront=0.75, styles=OFF, rooms=["office"])
add("SchoolDesk_01", group="desk", rotation="Step90", massClass="Medium", mass=12, clearanceFront=0.55, styles=OFF + ["dreamcore"], rooms=["office", "school"])
add("drawer_cabinet", group="cabinet", rotation="FaceWall", massClass="Heavy", mass=45, clearanceFront=0.55, styles=OFF, rooms=["office", "storage"], openable=True)
add("Shelf_01", group="shelf", rotation="FaceWall", massClass="Heavy", mass=25, clearanceFront=0.5, styles=OFF, rooms=["storage", "office"])
add("wooden_bookshelf_worn", group="shelf", rotation="FaceWall", massClass="Heavy", mass=40, clearanceFront=0.5, styles=OFF + ["dreamcore"], rooms=["storage", "office", "lounge"], moods=["neutral", "dark"])
add("vintage_cabinet_01", group="cabinet", rotation="FaceWall", massClass="Heavy", mass=90, clearanceFront=0.6, styles=["hotel", "dreamcore", "backrooms"], rooms=["lounge"], moods=["neutral", "dark"])
add("industrial_storage_cart", group="cart", rotation="Step90", massClass="Heavy", mass=40, clearanceFront=0.4, styles=["backrooms", "poolrooms"], rooms=["storage", "utility"])
add("side_table_01", group="smalltable", rotation="Step90", massClass="Medium", mass=8, styles=OFF + ["frutiger", "dreamcore"], rooms=["lounge", "office"])
add("modern_coffee_table_01", group="coffeetable", rotation="Step90", massClass="Medium", mass=15, styles=["frutiger", "hotel", "mall", "backrooms"], rooms=["lounge"])
add("Sofa_01", group="sofa", rotation="FaceWall", massClass="Heavy", mass=50, clearanceFront=0.7, styles=["hotel", "dreamcore", "backrooms"], rooms=["lounge"])
add("ArmChair_01", group="armchair", rotation="FaceAnchor", massClass="Heavy", mass=25, clearanceFront=0.5, styles=["hotel", "dreamcore", "backrooms"], rooms=["lounge"])
# chairs
add("SchoolChair_01", group="chair", rotation="FaceAnchor", massClass="Medium", mass=5, fallenChance=0.08, fallenEuler=[-90, 0, 0], styles=OFF + ["dreamcore"], rooms=["office", "school"])
add("modern_arm_chair_01", group="chair", rotation="FaceAnchor", massClass="Medium", mass=9, fallenChance=0.05, fallenEuler=[-90, 0, 0], styles=["frutiger", "hotel", "backrooms", "mall"], rooms=["office", "lounge"])
add("plastic_monobloc_chair_01", group="chair", rotation="FaceAnchor", massClass="Medium", mass=3, fallenChance=0.12, fallenEuler=[0, 0, 90], styles=["poolrooms", "backrooms", "school"], rooms=["pool", "office", "storage"])
add("metal_stool_01", group="stool", rotation="Free", massClass="Medium", mass=4, fallenChance=0.1, fallenEuler=[0, 0, 90], styles=["backrooms", "poolrooms", "oldweb"], rooms=["office", "storage", "pool"])
# floor small
add("cardboard_box_01", group="box", rotation="Free", massClass="Medium", mass=4, canStack=True, openable=True, styles=ALL, rooms=["storage", "office", "utility", "corridor"])
add("plastic_crate_01", group="crate", rotation="Free", massClass="Medium", mass=2, canStack=True, styles=["backrooms", "poolrooms", "school"], rooms=["storage", "utility", "pool"])
add("plastic_container", group="container", rotation="Step90", massClass="Medium", mass=3, canStack=True, openable=True, styles=["backrooms", "poolrooms"], rooms=["storage", "utility"])
add("trashbag", group="trash", rotation="Free", massClass="Medium", mass=3, maxTilt=12, styles=["backrooms", "mall", "school"], rooms=["corridor", "office", "utility"], moods=["neutral", "dark"])
add("WetFloorSign_01", group="sign", rotation="Free", massClass="Small", mass=1, fallenChance=0.15, fallenEuler=[-90, 0, 0], styles=["poolrooms", "backrooms", "mall", "frutiger"], rooms=["pool", "corridor", "office"])
add("korean_fire_extinguisher_01", group="extinguisher", rotation="FaceWall", massClass="Medium", mass=6, styles=["backrooms", "poolrooms", "mall", "school", "hotel"], rooms=["corridor", "office", "utility", "pool"])
add("potted_plant_01", group="plant", rotation="Free", massClass="Medium", mass=8, styles=["frutiger", "backrooms", "mall", "hotel", "poolrooms"], rooms=["office", "lounge", "corridor", "pool"])
add("potted_plant_02", group="plant", rotation="Free", massClass="Medium", mass=6, styles=["frutiger", "mall", "hotel", "poolrooms"], rooms=["lounge", "pool", "office"])
add("projector_screen", group="screen", rotation="FaceWall", massClass="Medium", mass=7, clearanceFront=1.0, styles=["school", "backrooms", "oldweb"], rooms=["office", "school"])
add("rubber_boots", split=True, group="boots", rotation="Free", massClass="Small", mass=1.5, styles=["backrooms", "poolrooms"], rooms=["utility", "storage", "pool"])
add("vintage_suitcase", split=True, group="suitcase", rotation="Free", massClass="Medium", mass=4, styles=["hotel", "dreamcore", "backrooms"], rooms=["storage", "lounge", "corridor"])
add("life_jacket", disabled=True, group="lifejacket", styles=["poolrooms"])
# surface items
add("classic_laptop", group="computer", support="Surface", rotation="FaceAnchor", massClass="Small", mass=3, styles=OFF + ["frutiger"], rooms=["office", "school"])
add("Television_01", group="tv", support="Surface", rotation="FaceAnchor", massClass="Medium", mass=10, styles=["backrooms", "hotel", "oldweb", "vhs", "dreamcore"], rooms=["office", "lounge"])
add("desk_lamp_arm_01", disabled=True, group="desklamp", mass=1.5, styles=OFF, rooms=["office"], **SURF)
add("alarm_clock_01", group="clock", mass=0.4, styles=["hotel", "dreamcore", "school", "backrooms"], rooms=["office", "lounge"], **SURF)
add("binder_notebook", split=True, group="paper", mass=0.4, styles=OFF + ["frutiger"], rooms=["office", "school", "storage"], **SURF)
add("office_notepads", split=True, group="paper", mass=0.1, styles=OFF + ["frutiger"], rooms=["office", "school"], **SURF)
add("clipboard", group="paper", mass=0.3, styles=OFF + ["poolrooms"], rooms=["office", "pool", "school"], **SURF)
add("stationery_supplies", split=True, group="stationery", mass=0.05, styles=OFF, rooms=["office", "school"], **SURF)
add("vintage_stapler", group="stationery", mass=0.4, styles=OFF, rooms=["office", "school"], **SURF)
add("plastic_thermos", group="drink", mass=0.6, styles=["backrooms", "poolrooms", "school"], rooms=["office", "pool", "utility"], **SURF)
add("rubber_duck_toy", group="toy", mass=0.2, styles=["poolrooms", "dreamcore", "frutiger"], rooms=["pool", "lounge"], moods=["light", "neutral"], **SURF)
add("Camera_01", group="camera", mass=0.6, styles=["backrooms", "hotel", "dreamcore"], rooms=["office", "lounge"], **SURF)
add("jug_01", group="drink", mass=0.8, styles=["hotel", "dreamcore", "backrooms"], rooms=["lounge", "office"], **SURF)
add("vintage_electric_kettle", group="kettle", mass=1.2, styles=["hotel", "dreamcore", "backrooms"], rooms=["office", "lounge", "utility"], **SURF)
# wall
add("wall_clock", group="wallclock", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=1.7, wallMax=2.1, styles=ALL, rooms=["office", "corridor", "school", "lounge", "pool"])
add("fire_alarm", group="alarm", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=1.25, wallMax=1.45, styles=["backrooms", "mall", "school", "hotel", "poolrooms"], rooms=["corridor", "office", "pool"])
add("fancy_picture_frame_01", group="picture", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=1.3, wallMax=1.6, styles=["hotel", "dreamcore", "backrooms"], rooms=["lounge", "corridor"])
add("security_camera_01", group="seccam", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=2.1, wallMax=2.35, styles=["backrooms", "mall", "poolrooms", "school"], rooms=["corridor", "pool", "office"], moods=["neutral", "dark"])
# ceiling
add("ceiling_fan", group="fan", support="Ceiling", rotation="Free", massClass="Fixed", styles=["hotel", "dreamcore", "backrooms"], rooms=["lounge", "office"])
add("modern_ceiling_lamp_01", group="pendant", support="Ceiling", rotation="Free", massClass="Fixed", styles=["frutiger", "hotel", "mall"], rooms=["lounge", "office"])
add("lightbulb_led", disabled=True, group="bulb")
add("old_gas_mask", disabled=True, group="reference")

for d in P:
    if "moods" not in d:
        d["moods"] = ["neutral", "light"]
    if isinstance(d.get("fallenEuler"), list):
        x, y, z = d["fallenEuler"]
        d["fallenEuler"] = {"x": x, "y": y, "z": z}

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Game", "Assets", "ADDRESS", "Data", "profiles.json")
os.makedirs(os.path.dirname(out), exist_ok=True)
json.dump({"profiles": P}, open(out, "w"), indent=1)
print(len(P), "profiles ->", out)

# Blender-made props (Tools/blender/kit_backrooms.py)
add("crt_monitor", source="blender", group="computer", support="Surface", rotation="FaceAnchor", massClass="Medium", mass=12, styles=OFF + ["vhs"], rooms=["office", "school"])
add("keyboard", source="blender", group="keyboard", support="Surface", rotation="FaceAnchor", massClass="Small", mass=1, styles=OFF + ["vhs", "frutiger"], rooms=["office", "school", "storage"])
add("mug_coffee", source="blender", group="drink", support="Surface", massClass="Small", mass=0.35, styles=ALL, rooms=["office", "lounge", "utility"])
add("mug_empty", source="blender", group="drink", support="Surface", massClass="Small", mass=0.3, styles=ALL, rooms=["office", "lounge", "utility", "storage"])
add("wall_outlet", source="blender", group="outlet", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=0.25, wallMax=0.32, styles=ALL, rooms=[])
add("light_switch", source="blender", group="switch", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=1.02, wallMax=1.08, styles=ALL, rooms=[])
add("vent_grille", source="blender", group="vent", support="Wall", rotation="FaceWall", massClass="Fixed", wallMin=2.2, wallMax=2.4, styles=ALL, rooms=[])
# computer kit (Tools/blender/kit_tech.py)
add("pc_tower", source="blender", group="pctower", rotation="FaceAnchor", massClass="Medium", mass=9, styles=OFF + ["vhs"], rooms=[], surfaces="none")
add("ball_mouse", source="blender", group="mouse", support="Surface", rotation="Free", massClass="Small", mass=0.15, styles=OFF + ["vhs", "frutiger"], rooms=["storage"])
add("cable_coil", source="blender", group="cable", support="Surface", rotation="Free", massClass="Small", mass=0.3, styles=ALL, rooms=["storage", "utility", "office"])
# openable props (Tools/blender/kit_open.py) - moving parts animated by Interact/Openable
add("open_box", source="blender", group="box", rotation="Free", massClass="Medium", mass=3, canStack=True, openable=True, styles=ALL, rooms=["storage", "office", "utility", "corridor"], surfaces="none")
add("storage_tub", source="blender", group="container", rotation="Step90", massClass="Medium", mass=3, openable=True, styles=["backrooms", "poolrooms", "school"], rooms=["storage", "utility"], surfaces="none")
add("trash_bag_tied", source="blender", group="trash", rotation="Free", massClass="Small", mass=2, openable=True, styles=["backrooms", "mall", "school"], rooms=["corridor", "office", "utility"], moods=["neutral", "dark"], surfaces="none")
add("file_cabinet", source="blender", group="cabinet", rotation="FaceWall", massClass="Heavy", mass=55, clearanceFront=0.7, openable=True, styles=OFF, rooms=["office", "storage"])
# Frutiger Aero props (Tools/blender/kit_aero.py)
add("aero_tube", source="blender", group="tube", rotation="Free", massClass="Fixed", mass=80, styles=["frutiger"], rooms=["atrium", "lounge"], moods=["light", "neutral"], surfaces="none")
add("aero_planter", source="blender", group="planter", rotation="Free", massClass="Heavy", mass=60, styles=["frutiger"], rooms=["atrium", "lounge"], moods=["light", "neutral"], surfaces="none")
add("aero_bench", source="blender", group="bench", rotation="FaceWall", massClass="Heavy", mass=40, clearanceFront=0.6, styles=["frutiger"], rooms=["atrium", "lounge"], moods=["light", "neutral"])
add("aero_orb", source="blender", group="orb", rotation="Free", massClass="Medium", mass=6, styles=["frutiger"], rooms=["atrium", "lounge"], moods=["light", "neutral"], surfaces="none")
json.dump({"profiles": P}, open(out, "w"), indent=1)

# per-part overrides for split sets (checked visually on the part thumbnails)
for pid in ["office_notepads_b", "office_notepads_c", "office_notepads_d", "office_notepads_f", "office_notepads_g",
            "office_notepads_h", "office_notepads_j", "stationery_supplies_a", "stationery_supplies_b", "stationery_supplies_g"]:
    P.append({"id": pid, "disabled": True, "group": "disabled", "moods": ["neutral"]})
json.dump({"profiles": P}, open(out, "w"), indent=1)
print("with part overrides:", len(P))
