import os
import struct
import subprocess

src = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Tools/greybox_runtime2.png"
bmp = "/tmp/_px2.bmp"
subprocess.run(["sips", "-s", "format", "bmp", src, "--out", bmp], capture_output=True)
d = open(bmp, "rb").read()
off = struct.unpack("<I", d[10:14])[0]
w = struct.unpack("<i", d[18:22])[0]
h = abs(struct.unpack("<i", d[22:26])[0])
bpp = struct.unpack("<H", d[28:30])[0]
step = bpp // 8
row = ((w * step) + 3) // 4 * 4

palette = {}
symbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz"

CW = 64
CH = 26
lines = []
for cy in range(CH):
    y0 = int((h - 1) - cy * h / CH)
    line = []
    for cx in range(CW):
        x0 = int(cx * w / CW)
        s = off + y0 * row + x0 * step
        b, g, r = d[s], d[s + 1], d[s + 2]
        key = (r // 12, g // 12, b // 12)
        if key not in palette:
            palette[key] = symbols[len(palette) % len(symbols)]
        line.append(palette[key])
    lines.append("".join(line))
print("|" + "|\n|".join(lines) + "|")
print("legend (symbol -> approx rgb):")
for k, v in palette.items():
    print("  " + v + " -> " + str((k[0] * 12, k[1] * 12, k[2] * 12)))