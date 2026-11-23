import struct
import subprocess
import sys
from collections import Counter

src = sys.argv[1]
cols = int(sys.argv[2]) if len(sys.argv) > 2 else 96
bmp = "/tmp/_px_ascii2.bmp"
subprocess.run(["sips", "-s", "format", "bmp", src, "--out", bmp], capture_output=True)
d = open(bmp, "rb").read()
off = struct.unpack("<I", d[10:14])[0]
w = struct.unpack("<i", d[18:22])[0]
h = abs(struct.unpack("<i", d[22:26])[0])
bpp = struct.unpack("<H", d[28:30])[0]
step = bpp // 8
row = ((w * step) + 3) // 4 * 4

def px(x, y):
    s = off + y * row + x * step
    return (d[s + 2], d[s + 1], d[s])

rows = int(cols * h / w * 0.5)
palette = {}
sym = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz"
out = []
for cy in range(rows):
    y0 = (h - 1) - int(cy * h / rows)
    line = []
    for cx in range(cols):
        x0 = int(cx * w / cols)
        c = px(x0, y0)
        if c not in palette:
            palette[c] = sym[len(palette) % len(sym)]
        line.append(palette[c])
    out.append("".join(line))
print("|" + "|\n|".join(out) + "|")
print("legend:")
for c, s in palette.items():
    print("  " + s + " = #%02X%02X%02X rgb%s" % (c[0], c[1], c[2], c))