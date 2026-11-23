import sys, time
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()

p = m.call("manage_editor", {"action": "telemetry_status"}, retries=1)
print("telemetry_status:", p[:500])

c = m.call("read_console", {"action": "get", "types": ["error"], "count": 20, "format": "detailed"}, retries=1)
print("console:", c[:2000])