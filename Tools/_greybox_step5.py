import sys
sys.path.insert(0, "Tools")
from _greybox_mcp import init, run

init()

RETRY = [
    ("manage_gameobject", {"action": "modify", "target": "Node_01_Neutral", "search_method": "by_name",
                           "position": [0, 0.03, 0], "scale": [6, 0.02, 6]}),
    ("manage_gameobject", {"action": "modify", "target": "Node_02_Fire", "search_method": "by_name",
                           "position": [-8, 0.03, 0], "scale": [6, 0.02, 6]}),
    ("manage_gameobject", {"action": "modify", "target": "Road_E", "search_method": "by_name",
                           "position": [4.5, 0.02, 0], "scale": [9, 0.02, 1.4]}),
    ("manage_gameobject", {"action": "modify", "target": "Road_S", "search_method": "by_name",
                           "position": [0, 0.02, -4.5], "scale": [1.4, 0.02, 9]}),
    ("manage_components", {"action": "set_property", "target": "Player", "searchMethod": "by_name",
                           "componentType": "SpriteRenderer", "properties": {"enabled": False}}),
    ("manage_scene", {"action": "save"}),
]

for tool, args in RETRY:
    run(tool, args)