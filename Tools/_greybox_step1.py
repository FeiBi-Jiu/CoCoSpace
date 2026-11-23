import sys
sys.path.insert(0, "Tools")
from _greybox_mcp import init, run, call, show

init()

# 1) remove duplicated EventSystem root (scene has two -> Unity spams warnings)
run("manage_gameobject", {"action": "delete", "target": "27580", "search_method": "by_id"})

MATS = [
    ("GreyboxGround", [0.16, 0.19, 0.24, 1]),
    ("GreyboxRoad", [0.30, 0.33, 0.38, 1]),
    ("NodeNeutral", [0.72, 0.72, 0.75, 1]),
    ("NodeFire", [0.88, 0.26, 0.20, 1]),
    ("NodeWater", [0.22, 0.48, 0.92, 1]),
    ("NodeThunder", [0.95, 0.84, 0.20, 1]),
    ("NodePoison", [0.30, 0.78, 0.32, 1]),
    ("PropCommon", [0.60, 0.60, 0.62, 1]),
    ("PropCrate", [0.95, 0.60, 0.15, 1]),
    ("PlayerBody", [0.25, 0.85, 0.95, 1]),
    ("MuzzleFlash", [1.0, 0.92, 0.35, 1]),
    ("HitFx", [1.0, 0.55, 0.15, 1]),
    ("BulletGreybox", [1.0, 0.90, 0.35, 1]),
    ("GhostBody", [0.70, 0.85, 1.0, 1]),
]

for name, color in MATS:
    run("manage_material", {
        "action": "create",
        "material_path": f"Assets/Materials/{name}.mat",
        "shader": "Unlit/Color",
        "color": color,
    })