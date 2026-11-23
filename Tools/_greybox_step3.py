import sys, json
sys.path.insert(0, "Tools")
from _greybox_mcp import init, call

init()
t = call("manage_tools", {"action": "list"})
try:
    d = json.loads(t)
    print(json.dumps(d, ensure_ascii=False)[:3000])
except Exception:
    print(t[:3000])