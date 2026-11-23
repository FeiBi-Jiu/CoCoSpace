import sys
sys.path.insert(0, "Tools")
from _greybox_mcp import init, run

init()

MAT = "Assets/Materials/"


def box(name, primitive, pos, scale, mat, parent=None, remove=("BoxCollider",)):
    args = {
        "action": "create",
        "name": name,
        "primitive_type": primitive,
        "position": pos,
        "scale": scale,
    }
    if parent:
        args["parent"] = parent
    if remove:
        args["components_to_remove"] = list(remove)
    if not run("manage_gameobject", args):
        return
    run("manage_material", {
        "action": "assign_material_to_renderer",
        "target": name,
        "material_path": MAT + mat + ".mat",
        "mode": "shared",
        "slot": 0,
    })


# --- ground ---------------------------------------------------------------
box("GreyboxGround", "Plane", [0, 0, 0], [6, 1, 6], "GreyboxGround", remove=())

# --- map container --------------------------------------------------------
run("manage_gameobject", {"action": "create", "name": "GreyboxMap", "position": [0, 0, 0]})

# --- capture nodes (discs) -----------------------------------------------
NODES = [
    ("Node_01_Neutral", [0, 0.03, 0], "NodeNeutral"),
    ("Node_02_Fire", [-12, 0.03, 0], "NodeFire"),
    ("Node_03_Water", [12, 0.03, 0], "NodeWater"),
    ("Node_04_Thunder", [0, 0.03, 12], "NodeThunder"),
    ("Node_05_Poison", [0, 0.03, -12], "NodePoison"),
]
for name, pos, mat in NODES:
    box(name, "Cylinder", pos, [5, 0.02, 5], mat, parent="GreyboxMap", remove=("CapsuleCollider",))

# --- roads ---------------------------------------------------------------
ROADS = [
    ("Road_W", [-6, 0.02, 0], [12, 0.02, 1.6]),
    ("Road_E", [6, 0.02, 0], [12, 0.02, 1.6]),
    ("Road_N", [0, 0.02, 6], [1.6, 0.02, 12]),
    ("Road_S", [0, 0.02, -6], [1.6, 0.02, 12]),
]
for name, pos, scale in ROADS:
    box(name, "Cube", pos, scale, "GreyboxRoad", parent="GreyboxMap")

# --- props ---------------------------------------------------------------
PROPS = [
    ("Prop_01", [-4, 0.4, 3], "PropCrate"),
    ("Prop_02", [4, 0.4, 3], "PropCommon"),
    ("Prop_03", [-4, 0.4, -3], "PropCommon"),
    ("Prop_04", [4, 0.4, -3], "PropCrate"),
    ("Prop_05", [10, 0.4, 6], "PropCommon"),
    ("Prop_06", [-10, 0.4, 6], "PropCrate"),
    ("Prop_07", [0, 0.4, 9], "PropCommon"),
    ("Prop_08", [-6, 0.4, -9], "PropCrate"),
]
for name, pos, mat in PROPS:
    box(name, "Cube", pos, [0.8, 0.8, 0.8], mat, parent="GreyboxMap")

# --- player greybox body -------------------------------------------------
box("GreyboxBody", "Cube", [0, 1.0, 0], [0.8, 1.4, 0.8], "PlayerBody", parent="Player")
box("GreyboxHead", "Sphere", [0, 1.95, 0], [0.55, 0.55, 0.55], "PlayerBody", parent="Player",
    remove=("SphereCollider",))
box("GreyboxNose", "Cube", [0, 1.15, 0.45], [0.3, 0.3, 0.3], "MuzzleFlash", parent="Player")

# --- fx prefabs ----------------------------------------------------------
box("MuzzleFlashSrc", "Sphere", [0, 0, 0], [0.3, 0.3, 0.3], "MuzzleFlash", remove=("SphereCollider",))
run("manage_gameobject", {
    "action": "modify",
    "target": "MuzzleFlashSrc",
    "save_as_prefab": True,
    "prefab_path": "Assets/Prefabs/MuzzleFlash.prefab",
})

box("HitFxSrc", "Sphere", [0, 0, 0], [0.25, 0.25, 0.25], "HitFx", remove=("SphereCollider",))
run("manage_gameobject", {
    "action": "modify",
    "target": "HitFxSrc",
    "save_as_prefab": True,
    "prefab_path": "Assets/Prefabs/HitFx.prefab",
})

# remove the temporary scene instances (prefab assets are kept)
run("manage_gameobject", {"action": "delete", "target": "MuzzleFlashSrc"})
run("manage_gameobject", {"action": "delete", "target": "HitFxSrc"})