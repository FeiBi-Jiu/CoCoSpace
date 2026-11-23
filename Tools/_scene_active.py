import re

p = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Assets/Scenes/SampleScene.scene"
lines = open(p, encoding="utf-8").read().splitlines()

i = 0
while i < len(lines):
    if lines[i].startswith("--- !u!1 &"):
        name = None
        active = None
        j = i + 1
        while j < len(lines) and not lines[j].startswith("--- "):
            b = lines[j]
            if b.startswith("  m_Name:"):
                name = b.split(":", 1)[1].strip()
            if b.startswith("  m_IsActive:"):
                active = b.split(":", 1)[1].strip()
            j += 1
        if name in ("Canvas", "ShopUI", "ResultPanel", "BackpackUI", "Boss1View", "GreyboxMap", "Player", "Main Camera", "EventSystem", "GreyboxGround"):
            print(name, "active =", active, "at line", i + 1)
        i = j
        continue
    i += 1