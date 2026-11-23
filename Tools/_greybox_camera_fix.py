import sys
import time
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()

print("stop:", m.call("manage_editor", {"action": "stop"}, retries=2)[:120], flush=True)
time.sleep(3)

fix = r'''
var sb = new System.Text.StringBuilder();
var cam = Camera.main;
if (cam == null) return "no Camera.main";
cam.transform.position = new Vector3(0f, 14f, -1.6f);
cam.transform.rotation = Quaternion.Euler(83.2f, 0f, 0f);
cam.orthographicSize = 12f;
cam.nearClipPlane = 0.3f;
sb.Append("cam pos=").Append(cam.transform.position.ToString("F2"))
  .Append(" rot=").Append(cam.transform.eulerAngles.ToString("F2"))
  .Append(" size=").Append(cam.orthographicSize).Append("\n");

var follow = cam.GetComponent<Game.Player.CameraFollow>();
if (follow != null)
{
    var so = new UnityEditor.SerializedObject(follow);
    so.FindProperty("offset").vector3Value = new Vector3(0f, 14f, -1.6f);
    so.FindProperty("lookOffset").vector3Value = new Vector3(0f, 0f, 0f);
    so.ApplyModifiedProperties();
    sb.Append("follow offset patched\n");
}
else sb.Append("no CameraFollow\n");

UnityEditor.EditorSceneManager.MarkAllScenesDirty();
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
sb.Append("scene saved\n");
return sb.ToString();
'''
print("fix:", m.call("execute_code", {"action": "execute", "code": fix}, retries=2)[:900], flush=True)

print("play:", m.call("manage_editor", {"action": "play"}, retries=2)[:120], flush=True)
time.sleep(10)

check = r'''
var cam = Camera.main;
var sb = new System.Text.StringBuilder();
sb.Append("cam pos=").Append(cam.transform.position.ToString("F2"))
  .Append(" rot=").Append(cam.transform.eulerAngles.ToString("F2"))
  .Append(" size=").Append(cam.orthographicSize).Append("\n");

int W = 720, H = 405;
var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
var old = cam.targetTexture;
cam.targetTexture = rt;
cam.Render();
RenderTexture.active = rt;
var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
tex.Apply();
RenderTexture.active = null;
cam.targetTexture = old;

string[] names = { "BG", "Ground", "Neutral", "Fire", "Water", "Thunder", "Poison", "Road", "PropO", "PropG", "Player" };
Color[] refs = {
    new Color32(49, 77, 121, 255), new Color32(41, 48, 61, 255), new Color32(184, 184, 191, 255),
    new Color32(224, 66, 51, 255), new Color32(56, 122, 235, 255), new Color32(242, 214, 51, 255),
    new Color32(77, 199, 82, 255), new Color32(77, 84, 97, 255), new Color32(242, 153, 38, 255),
    new Color32(153, 153, 158, 255), new Color32(64, 217, 242, 255),
};
string glyphs = ".GNFWTPROg#";
int cols = 72, rows = 36;
int[] counts = new int[names.Length];
var lines = new System.Text.StringBuilder();
for (int r = rows - 1; r >= 0; r--)
{
    for (int c = 0; c < cols; c++)
    {
        int x = (int)((c + 0.5f) * W / cols);
        int y = (int)((r + 0.5f) * H / rows);
        Color p = tex.GetPixel(x, y);
        int best = 0; float bd = 1e9f;
        for (int i = 0; i < refs.Length; i++)
        {
            float d = Mathf.Abs(p.r * 255f - refs[i].r * 255f) + Mathf.Abs(p.g * 255f - refs[i].g * 255f) + Mathf.Abs(p.b * 255f - refs[i].b * 255f);
            if (d < bd) { bd = d; best = i; }
        }
        counts[best]++;
        lines.Append(bd < 30f ? glyphs[best].ToString() : "?");
    }
    lines.Append("\n");
}
int total = 0;
for (int i = 0; i < counts.Length; i++) total += counts[i];
for (int i = 0; i < names.Length; i++) sb.Append(names[i]).Append("=").Append((100f * counts[i] / total).ToString("F1")).Append("% ");
sb.Append("\n").Append(lines.ToString());
UnityEngine.Object.Destroy(tex);
rt.Release();
return sb.ToString();
'''
print("render:", m.call("execute_code", {"action": "execute", "code": check}, retries=2)[:3000], flush=True)