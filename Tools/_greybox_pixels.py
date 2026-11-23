import os
import struct
import subprocess
from collections import Counter

base = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Tools"
for name in ("greybox_runtime.png", "greybox_runtime2.png"):
    src = os.path.join(base, name)
    if not os.path.exists(src):
        print(name + " missing")
        continue
    bmp = "/tmp/_px.bmp"
    subprocess.run(["sips", "-s", "format", "bmp", src, "--out", bmp],
                   capture_output=True)
    d = open(bmp, "rb").read()
    off = struct.unpack("<I", d[10:14])[0]
    w = struct.unpack("<i", d[18:22])[0]
    h = struct.unpack("<i", d[22:26])[0]
    bpp = struct.unpack("<H", d[28:30])[0]
    row = ((w * bpp // 8) + 3) // 4 * 4
    px = bytearray()
    for y in range(abs(h)):
        s = off + y * row
        px += d[s:s + w * bpp // 8]
    cnt = Counter()
    step = bpp // 8
    for i in range(0, len(px), step):
        cnt[tuple(px[i:i + 3][::-1])] += 1
    total = len(px) // step
    print("{0} {1}x{2} bpp={3} unique={4}".format(name, w, abs(h), bpp, len(cnt)))
    for c, n in cnt.most_common(8):
        print("    rgb={0} {1:.2f}%".format(c, 100.0 * n / total))