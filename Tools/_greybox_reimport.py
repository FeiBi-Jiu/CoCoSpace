import sys, os, json, time
sys.path.insert(0, "Tools")
import _greybox_mcp as m

ROOT = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace"
DLL = os.path.join(ROOT, "Library/ScriptAssemblies/Assembly-CSharp.dll")
png = os.path.join(ROOT, "Tools/greybox_runtime.png")

if os.path.exists(png):
    import struct
    d = open(png, "rb").read()
    print("png bytes", len(d), "dims", struct.unpack(">II", d[16:24]), flush=True)
else:
    print("png missing", flush=True)

m.init()
_, body = m.post({"jsonrpc": "2.0", "id": 3, "method": "tools/list", "params": {}}, m.SID)
resp = m.parse_body(body)
for t in resp["result"]["tools"]:
    if t["name"] == "manage_asset":
        print("asset actions:", t["inputSchema"]["properties"]["action"].get("enum"), flush=True)

before = os.path.getmtime(DLL)
print("dll before:", time.strftime("%H:%M:%S", time.localtime(before)), flush=True)

r = m.call("manage_asset", {"action": "import", "path": "Assets/Scripts/Player/PlayerController.cs"}, retries=2)
print("import:", r[:300], flush=True)

for i in range(20):
    time.sleep(2)
    if os.path.getmtime(DLL) > before + 0.5:
        print("recompiled after %ds" % (2 * (i + 1)), flush=True)
        break
else:
    print("still stale", flush=True)

try:
    import subprocess
    out = subprocess.run(["strings", DLL], capture_output=True, text=True).stdout
    print("has GreyboxCheck:", "GreyboxCheck" in out, flush=True)
    print("has 未初始化:", "未初始化" in out, flush=True)
except Exception as e:
    print("strings failed", e, flush=True)