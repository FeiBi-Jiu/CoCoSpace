import struct
import subprocess
import sys
from collections import Counter

src = sys.argv[1]
bmp = "/tmp/_px_bbox.bmp"
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

bg = px(5, 5)
print("size", w, "x", h, "corner", bg, "#%02X%02X%02X" % bg)

minx, maxx, miny, maxy = w, -1, h, -1
n = 0
for y in range(0, h, 2):
    for x in range(0, w, 2):
        c = px(x, y)
        if abs(c[0] - bg[0]) + abs(c[1] - bg[1]) + abs(c[2] - bg[2]) > 20:
            n += 1
            minx = min(minx, x); maxx = max(maxx, x)
            miny = min(miny, y); maxy = max(maxy, y)
print("non-bg bbox", (minx, miny, maxx, maxy), "w", maxx - minx, "h", maxy - miny, "sampled", n)

print("row profile (every 40 rows, count non-bg in 64 cols):")
for y in range(0, h, 40):
    cnt = 0
    for x in range(0, w, w // 64):
        c = px(x, y)
        if abs(c[0] - bg[0]) + abs(c[1] - bg[1]) + abs(c[2] - bg[2]) > 20:
            cnt += 1
    print("  y=%4d %s" % (y, "#" * cnt))