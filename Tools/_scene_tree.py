import re

p = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Assets/Scenes/SampleScene.scene"
lines = open(p, encoding="utf-8").read().splitlines()

# map transform fileID -> gameObject fileID and -> father fileID
tr = {}
go_of_tr = {}
father_of_tr = {}
name_of_go = {}
cur_go = None
for l in lines:
    m = re.match(r"^--- !u!1 &(\d+)", l)
    if m:
        cur_go = m.group(1)
        continue
    m2 = re.match(r"^  m_Name: (.+)$", l)
    if m2 and cur_go:
        name_of_go[cur_go] = m2.group(1).strip()
        cur_go = None

i = 0
while i < len(lines):
    if lines[i].startswith("--- !u!224 &") or lines[i].startswith("--- !u!4 &"):
        tr_id = lines[i].split("&")[1].strip()
        go = father = None
        j = i + 1
        while j < len(lines) and not lines[j].startswith("--- "):
            b = lines[j]
            if b.startswith("  m_GameObject:"):
                mm = re.search(r"fileID: (\d+)", b)
                go = mm.group(1) if mm else None
            if b.startswith("  m_Father:"):
                mm = re.search(r"fileID: (\d+)", b)
                father = mm.group(1) if mm else None
            j += 1
        go_of_tr[tr_id] = go
        father_of_tr[tr_id] = father
        i = j
        continue
    i += 1

children = {}
for tr_id, fa in father_of_tr.items():
    children.setdefault(fa, []).append(tr_id)

def walk(tr_id, depth=0):
    go = go_of_tr.get(tr_id)
    n = name_of_go.get(go, "?")
    print("  " * depth + "- " + n)
    for c in children.get(tr_id, []):
        walk(c, depth + 1)

for tr_id, fa in father_of_tr.items():
    if fa in (None, "0") or fa not in father_of_tr:
        walk(tr_id)