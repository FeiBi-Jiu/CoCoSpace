import struct
import subprocess
import sys
from collections import Counter

src = sys.argv[1] if len(sys.argv) > 1 else "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Tools/greybox_world2.png"
bmp = "/tmp/_px_exact.bmp"
subprocess.run(["sips", "-s", "format", "bmp", src, "--out", bmp], capture_output=True)
d = open(bmp, "rb").read()
off = struct.unpack("<I", d[10:14])[0]
w = struct.unpack("<i", d[18:22])[0]
h = abs(struct.unpack("<i", d[22:26])[0])
bpp = struct.unpack("<H", d[28:30])[0]
step = bpp // 8
row = ((w * step) + 3) // 4 * 4
print("size", w, "x", h, "bpp", bpp, "off", off)

px = []
for y in range(h):
    base = off + y * row
    for x in range(w):
        s = base + x * step
        b, g, r = d[s], d[s + 1], d[s + 2]
        px.append((r, g, b))

cnt = Counter(px)
print("top colors:")
for c, n in cnt.most_common(12):
    print("  rgb", c, "#%02X%02X%02X" % c, "count", n, "%.1f%%" % (100.0 * n / len(px)))

bg = cnt.most_common(1)[0][0]
minx, maxx, miny, maxy = w, -1, h, -1
n_diff = 0
for y in range(h):
    for x in range(w):
        c = px[y * w + x]
        if abs(c[0] - bg[0]) > 6 or abs(c[1] - bg[1]) > 6 or abs(c[2] - bg[2]) > 6:
            n_diff += 1
            minx = min(minx, x); maxx = max(maxx, x)
            miny = min(miny, y); maxy = max(maxy, y)
print("bg", bg, "non-bg bbox", (minx, miny, maxx, maxy), "count", n_diff)