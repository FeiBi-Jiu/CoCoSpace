import sys, time, re
sys.path.insert(0, "Tools")
from _greybox_mcp import init, call, run

init()

for i in range(6):
    p = call("manage_editor", {"action": "telemetry_ping"})
    if '"success":true' in p.replace(" ", ""):
        print("ping ok")
        break
    time.sleep(5)

run("manage_components", {"action": "set_property", "target": "Player", "searchMethod": "by_name",
                          "componentType": "SpriteRenderer", "properties": {"enabled": False}})

run("manage_components", {"action": "set_property", "target": "Player", "searchMethod": "by_name",
                          "componentType": "FirePrediction",
                          "properties": {
                              "muzzlePrefab": {"path": "Assets/Prefabs/MuzzleFlash.prefab"},
                              "hitPrefab": {"path": "Assets/Prefabs/HitFx.prefab"},
                          }})

run("manage_components", {"action": "set_property", "target": "Main Camera", "searchMethod": "by_name",
                          "componentType": "Camera", "properties": {"orthographicSize": 9}})

run("manage_scene", {"action": "save"})
time.sleep(3)

SCENE = "Assets/Scenes/SampleScene.scene"
txt = open(SCENE, encoding="utf-8").read()
print("ortho size lines:", re.findall(r"orthographic size: [0-9.]+", txt))
print("muzzlePrefab lines:", re.findall(r"muzzlePrefab: \{[^}]*\}", txt))
print("hitPrefab lines:", re.findall(r"hitPrefab: \{[^}]*\}", txt))
m = re.search(r"--- !u!212 &1583200067\nSpriteRenderer:\n(?:.*\n)*?  m_Enabled: (\d)", txt)
print("player SpriteRenderer m_Enabled:", m.group(1) if m else "?")