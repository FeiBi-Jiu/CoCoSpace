import sys, os, time, re, subprocess
sys.path.insert(0, "Tools")
import _greybox_mcp as m

ROOT = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace"
DLL = os.path.join(ROOT, "Library/ScriptAssemblies/Assembly-CSharp.dll")
LOG = os.path.expanduser("~/Library/Logs/Tuanjie/Editor.log")

m.init()
print("init ok", flush=True)

print("refresh:", m.call("refresh_unity", {"mode": "force", "scope": "all", "compile": "request"}, retries=2)[:200], flush=True)

for i in range(40):
    time.sleep(3)
    try:
        out = subprocess.run(["strings", DLL], capture_output=True, text=True).stdout
    except Exception:
        out = ""
    if "GreyboxCheck" in out:
        print("compiled with GreyboxCheck after %ds" % ((i + 1) * 3), flush=True)
        break
else:
    print("GreyboxCheck NOT in assembly", flush=True)

print("play:", m.call("manage_editor", {"action": "play"}, retries=1)[:160], flush=True)
time.sleep(8)

size = os.path.getsize(LOG)
with open(LOG, "rb") as f:
    f.seek(max(0, size - 200000))
    new = f.read().decode("utf-8", "ignore")
keep = re.compile(r"GreyboxCheck|Exception|error CS|Missing|Player\]|PlayerController|OnDestroy|HudPanel|FogOfWar|MapBuilder|BulletPool|FirePrediction")
lines = [l for l in new.splitlines() if keep.search(l)]
print("\n".join(lines[-40:]), flush=True)