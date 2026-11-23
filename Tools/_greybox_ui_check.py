import sys
import time
import os
import struct
import subprocess
sys.path.insert(0, "Tools")
import _greybox_mcp as m

BASE = "/Users/xiaoou/Projects/CoCoSpace-game/CoCoSpace/Tools"


def shot(name):
    print("shot " + name + ":", m.call("manage_camera", {
        "action": "screenshot", "capture_source": "game_view",
        "screenshot_file_name": name, "output_folder": "Tools",
        "include_image": False}, retries=2)[:160], flush=True)


def colors(png):
    bmp = "/tmp/_px_c.bmp"
    subprocess.run(["sips", "-s", "format", "bmp", png, "--out", bmp], capture_output=True)
    d = open(bmp, "rb").read()
    off = struct.unpack("<I", d[10:14])[0]
    w = struct.unpack("<i", d[18:22])[0]
    h = abs(struct.unpack("<i", d[22:26])[0])
    bpp = struct.unpack("<H", d[28:30])[0]
    step = bpp // 8
    row = ((w * step) + 3) // 4 * 4
    cnt = {}
    for y in range(0, h, 3):
        base = off + y * row
        for x in range(0, w, 3):
            s = base + x * step
            c = (d[s + 2], d[s + 1], d[s])
            cnt[c] = cnt.get(c, 0) + 1
    tot = sum(cnt.values())
    print("  size", w, "x", h)
    for c, n in sorted(cnt.items(), key=lambda kv: -kv[1])[:10]:
        print("   #%02X%02X%02X" % c, "rgb", c, "%.1f%%" % (100.0 * n / tot))


m.init()
print("play:", m.call("manage_editor", {"action": "play"}, retries=2)[:70], flush=True)
time.sleep(10)

step1 = r'''
var sb = new System.Text.StringBuilder();
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

var gr = Game.GameRoot.Instance;
if (gr != null)
{
    var st = typeof(Game.GameRoot).GetProperty("State", flags);
    st.SetValue(gr, Game.GameRoot.GameState.InGame);
    sb.Append("state=").Append(gr.State).Append("\n");
}
else sb.Append("GameRoot null\n");

var canvases = GameObject.FindObjectsOfType<Canvas>();
foreach (var cv in canvases)
{
    var login = cv.transform.Find("LoginRoot");
    if (login != null && login.gameObject.activeSelf)
    {
        login.gameObject.SetActive(false);
        sb.Append("hidden login on ").Append(cv.name).Append("\n");
    }
}

var hud = GameObject.FindObjectOfType<Game.UI.HudPanel>();
sb.Append("hud=").Append(hud != null ? hud.name : "null").Append("\n");
if (hud != null)
{
    var show = typeof(Game.UI.HudPanel).GetMethod("ShowRoot", flags);
    show.Invoke(hud, null);
    sb.Append("hud shown\n");
}

var cam = Camera.main;
if (cam != null)
{
    cam.orthographicSize = 9f;
    sb.Append("camera size=").Append(cam.orthographicSize).Append(" pos=").Append(cam.transform.position.ToString("F2")).Append("\n");
}
return sb.ToString();
'''

print("step1:", m.call("execute_code", {"action": "execute", "code": step1}, retries=2)[:1200], flush=True)
time.sleep(2)
shot("greybox_hud.png")
time.sleep(3)

step2 = r'''
var sb = new System.Text.StringBuilder();
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

var up = GameObject.FindObjectOfType<Game.UI.UpgradeChoiceUI>();
sb.Append("upgradeUI=").Append(up != null ? up.name : "null").Append("\n");
if (up != null)
{
    var msg = new Game.Proto.S2CUpgradeChoice();
    msg.TimeoutMs = 10000;
    for (int i = 0; i < 3; i++)
    {
        var opt = new Game.Proto.UpgradeOption();
        opt.ChoiceId = (uint)(i + 1);
        opt.Name = "选项" + (i + 1);
        opt.Desc = "灰盒占位描述";
        opt.IconId = (uint)(i + 1);
        opt.Rarity = (uint)(i % 3);
        msg.Options.Add(opt);
    }
    var show = typeof(Game.UI.UpgradeChoiceUI).GetMethod("Show", flags);
    sb.Append("showMethod=").Append(show != null).Append("\n");
    if (show != null) show.Invoke(up, new object[] { msg });
    sb.Append("upgrade shown\n");
}

var fire = GameObject.FindObjectOfType<Game.Combat.FirePrediction>();
sb.Append("firePrediction=").Append(fire != null ? fire.name : "null").Append("\n");
if (fire != null)
{
    fire.PredictFire(1);
    sb.Append("predicted shot, pending=").Append(fire.PendingCount).Append("\n");
}

var pool = Game.Combat.BulletPool.Instance;
sb.Append("bulletPool=").Append(pool != null ? "ok" : "null").Append("\n");
return sb.ToString();
'''

print("step2:", m.call("execute_code", {"action": "execute", "code": step2}, retries=2)[:1400], flush=True)
time.sleep(1)
shot("greybox_upgrade.png")
time.sleep(3)

print("errors:", m.call("read_console", {"action": "get", "types": ["error"], "count": 8, "format": "plain"}, retries=2)[:1500], flush=True)

for n in ("greybox_hud.png", "greybox_upgrade.png"):
    p = os.path.join(BASE, n)
    if os.path.exists(p):
        print(n + ":")
        colors(p)
    else:
        print(n, "missing")