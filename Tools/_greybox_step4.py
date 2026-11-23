import sys, json
sys.path.insert(0, "Tools")
from _greybox_mcp import init, run

init()

# ---------- 1. 角色灰盒对齐 CharacterController（height 0.88, center 0）----------
run("manage_gameobject", {"action": "modify", "target": "GreyboxBody",
                          "search_method": "by_name", "position": [0, 0, 0],
                          "scale": [0.7, 0.88, 0.7]})
run("manage_gameobject", {"action": "modify", "target": "GreyboxHead",
                          "search_method": "by_name", "position": [0, 0.52, 0],
                          "scale": [0.35, 0.35, 0.35]})
run("manage_gameobject", {"action": "modify", "target": "GreyboxNose",
                          "search_method": "by_name", "position": [0, 0.05, 0.38],
                          "scale": [0.18, 0.18, 0.18]})

# 去掉角色灰盒上的多余碰撞体（避免与 CharacterController 打架）
for go, comp in (("GreyboxBody", "BoxCollider"),
                 ("GreyboxHead", "SphereCollider"),
                 ("GreyboxNose", "BoxCollider")):
    run("manage_components", {"action": "remove", "target": go,
                              "searchMethod": "by_name", "componentType": comp})

# 角色 SpriteRenderer 引用的是不存在的贴图，关闭避免报错（改用灰盒方块显示）
run("manage_components", {"action": "set_property", "target": "Player",
                          "searchMethod": "by_name",
                          "componentType": "SpriteRenderer",
                          "properties": {"enabled": False}})

# ---------- 2. 地图灰盒：去掉装饰物碰撞体，只保留地面与道具 ----------
DECOR = [("Node_01_Neutral", "CapsuleCollider"), ("Node_02_Fire", "CapsuleCollider"),
         ("Node_03_Water", "CapsuleCollider"), ("Node_04_Thunder", "CapsuleCollider"),
         ("Node_05_Poison", "CapsuleCollider"),
         ("Road_W", "BoxCollider"), ("Road_E", "BoxCollider"),
         ("Road_N", "BoxCollider"), ("Road_S", "BoxCollider")]
for go, comp in DECOR:
    run("manage_components", {"action": "remove", "target": go,
                              "searchMethod": "by_name", "componentType": comp})

# 据点收进相机视野（正交相机 size 9）
NODES = [("Node_01_Neutral", [0, 0.03, 0]),
         ("Node_02_Fire", [-8, 0.03, 0]),
         ("Node_03_Water", [8, 0.03, 0]),
         ("Node_04_Thunder", [0, 0.03, 8]),
         ("Node_05_Poison", [0, 0.03, -8])]
for name, pos in NODES:
    run("manage_gameobject", {"action": "modify", "target": name, "search_method": "by_name",
                              "position": pos, "scale": [6, 0.02, 6]})

ROADS = [("Road_W", [-4.5, 0.02, 0], [9, 0.02, 1.4]),
         ("Road_E", [4.5, 0.02, 0], [9, 0.02, 1.4]),
         ("Road_N", [0, 0.02, 4.5], [1.4, 0.02, 9]),
         ("Road_S", [0, 0.02, -4.5], [1.4, 0.02, 9])]
for name, pos, scale in ROADS:
    run("manage_gameobject", {"action": "modify", "target": name, "search_method": "by_name",
                              "position": pos, "scale": scale})

# ---------- 3. 射击：把灰盒特效预制体挂回 FirePrediction ----------
run("manage_components", {"action": "set_property", "target": "Player",
                          "searchMethod": "by_name", "componentType": "FirePrediction",
                          "properties": {
                              "muzzlePrefab": {"path": "Assets/Prefabs/MuzzleFlash.prefab"},
                              "hitPrefab": {"path": "Assets/Prefabs/HitFx.prefab"},
                          }})

# ---------- 4. 相机：正交尺寸覆盖整张灰盒地图 ----------
run("manage_components", {"action": "set_property", "target": "Main Camera",
                          "searchMethod": "by_name", "componentType": "Camera",
                          "properties": {"orthographicSize": 9}})

# ---------- 5. 保存场景 ----------
run("manage_scene", {"action": "save"})