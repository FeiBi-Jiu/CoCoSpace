import sys, os, time, json, re
sys.path.insert(0, "Tools")
import _greybox_mcp as m

ROOT = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace"
DLL = os.path.join(ROOT, "Library/ScriptAssemblies/Assembly-CSharp.dll")


def log(*a):
    print(*a, flush=True)


m.init()

log("[1] stop play:", m.call("manage_editor", {"action": "stop"}, retries=2)[:120])
time.sleep(2)

log("[2] compile:", m.call("refresh_unity", {"mode": "force", "scope": "scripts", "compile": "request"}, retries=2)[:160])

mtime = os.path.getmtime(DLL)
for i in range(30):
    time.sleep(2)
    if os.path.getmtime(DLL) > mtime + 0.5:
        log("    recompiled after %ds" % (2 * (i + 1)))
        break
else:
    log("    WARN: assembly mtime unchanged")

time.sleep(4)
log("[3] clear console:", m.call("read_console", {"action": "clear"}, retries=2)[:80])

log("[4] play:", m.call("manage_editor", {"action": "play"}, retries=2)[:120])
time.sleep(9)

log("[5] console after 9s:")
console = m.call("read_console", {"action": "get", "types": ["error", "warning"], "count": 15, "format": "plain"}, retries=2)
log(console[:2500])

log("[6] runtime hierarchy:")
hier = m.call("manage_scene", {"action": "get_hierarchy", "max_depth": 2, "max_children_per_node": 40}, retries=2)
try:
    data = json.loads(hier)
except Exception:
    data = None
if isinstance(data, dict):
    payload = data.get("data", data)
    txt = json.dumps(payload, ensure_ascii=False)
    log(txt[:2500])
else:
    log(hier[:2000])

log("[7] screenshot:", m.call("manage_camera", {
    "action": "screenshot",
    "capture_source": "game_view",
    "screenshot_file_name": "greybox_runtime.png",
    "output_folder": "Tools",
    "include_image": False,
}, retries=2)[:400])

log("[8] stop play:", m.call("manage_editor", {"action": "stop"}, retries=2)[:120])