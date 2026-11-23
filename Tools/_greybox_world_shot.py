import sys
import os
import time
import struct
import subprocess
sys.path.insert(0, "Tools")
import _greybox_mcp as m

BASE = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Tools"


def ascii_dump(png, cw=64, ch=26):
    bmp = "/tmp/_px3.bmp"
    subprocess.run(["sips", "-s", "format", "bmp", png, "--out", bmp], capture_output=True)
    d = open(bmp, "rb").read()
    off = struct.unpack("<I", d[10:14])[0]
    w = struct.unpack("<i", d[18:22])[0]
    h = abs(struct.unpack("<i", d[22:26])[0])
    bpp = struct.unpack("<H", d[28:30])[0]
    step = bpp // 8
    row = ((w * step) + 3) // 4 * 4
    palette = {}
    symbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz"
    out = []
    for cy in range(ch):
        y0 = (h - 1) - int(cy * h / ch)
        line = []
        for cx in range(cw):
            x0 = int(cx * w / cw)
            s = off + y0 * row + x0 * step
            b, g, r = d[s], d[s + 1], d[s + 2]
            key = (r // 12, g // 12, b // 12)
            if key not in palette:
                palette[key] = symbols[len(palette) % len(symbols)]
            line.append(palette[key])
        out.append("".join(line))
    print("|" + "|\n|".join(out) + "|")
    for k, v in palette.items():
        print("  " + v + " -> " + str((k[0] * 12, k[1] * 12, k[2] * 12)))


m.init()
print("play:", m.call("manage_editor", {"action": "play"}, retries=2)[:100], flush=True)
time.sleep(7)

for ui in ("LoginPanel", "ResultPanel", "ShopUI", "UpgradeChoiceUI"):
    f = m.call("find_gameobjects", {"search_term": ui, "search_method": "by_name", "include_inactive": False}, retries=2)
    print("find", ui, ":", f[:180], flush=True)
    if '"total": 0' in f or '"total":0' in f:
        continue
    r = m.call("manage_gameobject", {"action": "modify", "target": ui, "search_method": "by_name", "set_active": False}, retries=2)
    print("   hide:", r[:150], flush=True)

time.sleep(2)
png = os.path.join(BASE, "greybox_world.png")
print("shot:", m.call("manage_camera", {"action": "screenshot", "capture_source": "game_view",
                                        "screenshot_file_name": "greybox_world.png",
                                        "output_folder": "Tools", "include_image": False}, retries=2)[:200], flush=True)
time.sleep(3)

print("console:", m.call("read_console", {"action": "get", "types": ["error"], "count": 10, "format": "plain"}, retries=2)[:900], flush=True)

if os.path.exists(png):
    ascii_dump(png)
else:
    print("screenshot missing", flush=True)

print("stop:", m.call("manage_editor", {"action": "stop"}, retries=2)[:100], flush=True)