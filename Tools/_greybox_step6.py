import sys, json, time
sys.path.insert(0, "Tools")
from _greybox_mcp import init, call

init()

for i in range(5):
    p = call("manage_editor", {"action": "telemetry_ping"})
    print("PING:", p[:300])
    if '"success":true' in p.replace(" ", ""):
        break
    time.sleep(6)

c = call("read_console", {"action": "get", "types": ["error"], "count": 30, "format": "detailed"})
print("CONSOLE:", c[:3000])