import re

p = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Assets/Scenes/SampleScene.scene"
lines = open(p, encoding="utf-8").read().splitlines()

names = {}
cur_go = None
for l in lines:
    m = re.match(r"^--- !u!1 &(\d+)", l)
    if m:
        cur_go = m.group(1)
        continue
    m2 = re.match(r"^  m_Name: (.+)$", l)
    if m2 and cur_go:
        names[cur_go] = m2.group(1).strip()
        cur_go = None

trs = {}
i = 0
while i < len(lines):
    l = lines[i]
    if l.startswith("--- !u!4 &"):
        go = None
        pos = scl = father = None
        j = i + 1
        while j < len(lines) and not lines[j].startswith("--- "):
            b = lines[j]
            if b.startswith("  m_GameObject:"):
                go = re.search(r"fileID: (\d+)", b).group(1)
            if b.startswith("  m_LocalPosition:"):
                pos = b.split(":", 1)[1].strip()
            if b.startswith("  m_LocalScale:"):
                scl = b.split(":", 1)[1].strip()
            if b.startswith("  m_Father:"):
                father = b.split(":", 1)[1].strip()
            j += 1
        if go and go in names:
            trs[names[go]] = (pos, scl, father)
        i = j
        continue
    i += 1

for n in sorted(trs):
    if any(k in n for k in ("Node", "Road", "Prop", "Greybox", "Player", "Altar", "Element", "MapRoot", "BulletPool")):
        print(n, "|", trs[n][0], "|", trs[n][1], "|", trs[n][2])