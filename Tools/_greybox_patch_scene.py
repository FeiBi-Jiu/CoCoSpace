import re, io

SCENE = "Assets/Scenes/SampleScene.scene"
txt = open(SCENE, encoding="utf-8").read()

# ---- helpers -------------------------------------------------------------
def blocks(text):
    """yield (header, body) for each '--- !u!<class> &<id>' block"""
    for m in re.finditer(r"--- !u!(\d+) &(-?\d+)\n", text):
        start = m.start()
        nxt = text.find("\n--- !u!", m.end())
        end = len(text) if nxt < 0 else nxt
        yield m.group(1), m.group(2), start, end


def find_go(text, name):
    for cls, fid, s, e in blocks(text):
        if cls == "1" and re.search(r"m_Name: " + re.escape(name) + r"\s*$", text[s:e], re.M):
            return fid, s, e
    return None, None, None


def comp_type(text, fid):
    for cls, cid, s, e in blocks(text):
        if cid == fid:
            return cls
    return None


def remove_component(text, go_name, type_name):
    gid, gs, ge = find_go(text, go_name)
    if gid is None:
        raise SystemExit("GO not found: " + go_name)
    go_block = text[gs:ge]
    target_ids = []
    for c in re.findall(r"- component: \{fileID: (-?\d+)\}", go_block):
        for cls, cid, s, e in blocks(text):
            if cid == c and re.match(r"\s*" + type_name + r":", text[s:e].split("\n", 1)[1]):
                target_ids.append((cid, s, e))
                break
    if not target_ids:
        print("  skip (no %s on %s)" % (type_name, go_name))
        return text
    for cid, s, e in sorted(target_ids, key=lambda x: -x[1]):
        line = "- component: {fileID: " + cid + "}"
        text = text[:s] + text[e:].lstrip("\n")
        text = text.replace("  " + line + "\n", "", 1)
    print("  removed %s from %s" % (type_name, go_name))
    return text


REMOVE = [(n, "BoxCollider") for n in
          ("GreyboxBody", "GreyboxNose", "Road_W", "Road_E", "Road_N", "Road_S")]
REMOVE += [(n, "SphereCollider") for n in ("GreyboxHead",)]
REMOVE += [(n, "CapsuleCollider") for n in
           ("Node_01_Neutral", "Node_02_Fire", "Node_03_Water", "Node_04_Thunder", "Node_05_Poison")]

for name, comp in REMOVE:
    txt = remove_component(txt, name, comp)

# ---- disable Player SpriteRenderer (sprite asset missing) ----------------
m = re.search(r"--- !u!212 &1583200067\nSpriteRenderer:\n(?:.*\n)*?  m_Enabled: 1", txt)
if m:
    txt = txt[:m.start()] + m.group(0)[:-1] + "0" + txt[m.end():]
    print("  SpriteRenderer disabled")
else:
    print("  SpriteRenderer block not found / already disabled")

# ---- camera ortho size so the whole greybox map is visible ---------------
if "orthographic size: 5" in txt:
    txt = txt.replace("orthographic size: 5", "orthographic size: 9", 1)
    print("  camera orthographic size -> 9")

open(SCENE, "w", encoding="utf-8").write(txt)
print("scene patched")