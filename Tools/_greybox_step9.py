import sys, time, json
sys.path.insert(0, "Tools")
from _greybox_mcp import init, call, run

init()

for i in range(10):
    p = call("manage_editor", {"action": "telemetry_ping"})
    if '"success":true' in p.replace(" ", ""):
        print("ping ok")
        break
    time.sleep(4)

run("manage_editor", {"action": "play"})
time.sleep(6)

h = call("manage_scene", {"action": "get_hierarchy"})
try:
    d = json.loads(h)
    print("ROOTS:", [it["name"] for it in d["data"]["items"]])
except Exception:
    print("HIER:", h[:800])

c = call("read_console", {"action": "get", "types": ["error"], "count": 20, "format": "detailed"})
print("ERRORS:", c[:2500])