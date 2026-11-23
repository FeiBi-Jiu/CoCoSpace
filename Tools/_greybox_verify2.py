import sys, os, time, json
sys.path.insert(0, "Tools")
import _greybox_mcp as m

def tree(node, depth=0, out=None):
    out = out if out is not None else []
    comps = ",".join(node.get("componentTypes", []))
    out.append("%s- %s [%s]%s" % ("  " * depth, node.get("name"), comps,
                                  "" if not node.get("childCount") else " (%d children)" % node["childCount"]))
    for c in node.get("children", []) or []:
        tree(c, depth + 1, out)
    return out

m.init()
print("play:", m.call("manage_editor", {"action": "play"}, retries=2)[:100], flush=True)
time.sleep(9)

print("console:", flush=True)
print(m.call("read_console", {"action": "get", "types": ["error", "warning"], "count": 25, "format": "plain"}, retries=2)[:2200], flush=True)

h = m.call("manage_scene", {"action": "get_hierarchy", "max_depth": 3, "max_children_per_node": 80}, retries=2)
try:
    d = json.loads(h)
    payload = d.get("data", d)
    items = payload.get("items", payload if isinstance(payload, list) else [])
    lines = []
    for it in items:
        lines += tree(it)
    print("hierarchy nodes:", len(lines), flush=True)
    print("\n".join(lines[:120]), flush=True)
except Exception as e:
    print("hierarchy parse fail", e, h[:1200], flush=True)

print("screenshot:", m.call("manage_camera", {
    "action": "screenshot", "capture_source": "game_view",
    "screenshot_file_name": "greybox_runtime2.png", "output_folder": "Tools",
    "include_image": False}, retries=2)[:250], flush=True)

print("stop:", m.call("manage_editor", {"action": "stop"}, retries=2)[:100], flush=True)