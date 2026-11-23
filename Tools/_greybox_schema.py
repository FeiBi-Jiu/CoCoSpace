import sys
import json
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()
_, body = m.post({"jsonrpc": "2.0", "id": 3, "method": "tools/list", "params": {}}, m.SID)
resp = m.parse_body(body)
for t in resp["result"]["tools"]:
    if t["name"] in ("manage_gameobject", "find_gameobjects"):
        print("=====", t["name"])
        props = t["inputSchema"]["properties"]
        print("props:", list(props.keys()))
        if "action" in props:
            print("action desc:", str(props["action"].get("description"))[:600])