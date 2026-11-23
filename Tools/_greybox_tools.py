import sys
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()
_, body = m.post({"jsonrpc": "2.0", "id": 3, "method": "tools/list", "params": {}}, m.SID)
resp = m.parse_body(body)
for t in resp["result"]["tools"]:
    print(t["name"], "|", str(t.get("description", ""))[:110].replace("\n", " "))