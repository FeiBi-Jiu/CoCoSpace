import sys, json
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()


def read(uri):
    sid, body = m.post({"jsonrpc": "2.0", "id": 4, "method": "resources/read",
                        "params": {"uri": uri}}, m.SID)
    resp = m.parse_body(body)
    if not resp or "result" not in resp:
        return "ERR " + str(resp)[:400]
    out = ""
    for c in resp["result"].get("contents", []):
        out += c.get("text", "")
    return out


print("== editor state ==")
print(read("mcpforunity://editor/state")[:1200])
print("== cameras ==")
print(read("mcpforunity://scene/cameras")[:1200])
print("== prefab stage ==")
print(read("mcpforunity://editor/prefab-stage")[:400])
print("== instances ==")
print(read("mcpforunity://instances")[:600])