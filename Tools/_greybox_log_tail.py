import os, re
LOG = os.path.expanduser("~/Library/Logs/Tuanjie/Editor.log")
with open(LOG, "rb") as f:
    lines = f.read().decode("utf-8", "ignore").splitlines()

tail = lines[3824600:]
keep = re.compile(r"Exception|Error|error|Missing|missing|Warning|GreyboxCheck|\[Player\]|Entered|Play mode|Fog|Hud|Upgrade|Bullet|Map")
noise = re.compile(r"^\s*#|^\s+at |Licensing|utp:|NWB-|^\s*$|Mono JIT|scripting_method_invoke|ScriptingInvocation")
for l in tail:
    if keep.search(l) and not noise.search(l):
        print(l)