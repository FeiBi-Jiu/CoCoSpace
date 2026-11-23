import sys
import os
import time
import json
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()
print("sid", m.SID, flush=True)
print("play:", m.call("manage_editor", {"action": "play"}, retries=2)[:80], flush=True)
time.sleep(10)

probe = r'''
var sb = new System.Text.StringBuilder();
var cam = Camera.main;
sb.Append("camera=").Append(cam != null ? cam.name : "null");
if (cam != null)
    sb.Append(" ortho=").Append(cam.orthographic).Append(" size=").Append(cam.orthographicSize)
      .Append(" pos=").Append(cam.transform.position.ToString("F2"))
      .Append(" rot=").Append(cam.transform.eulerAngles.ToString("F1"))
      .Append(" clear=").Append(cam.clearFlags).Append(" bg=").Append(cam.backgroundColor.ToString());
sb.Append("\n-- cameras --\n");
foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>())
{
    sb.Append(c.name).Append(" depth=").Append(c.depth).Append(" ortho=").Append(c.orthographic)
      .Append(" size=").Append(c.orthographicSize).Append(" active=").Append(c.gameObject.activeInHierarchy)
      .Append(" pos=").Append(c.transform.position.ToString("F2")).Append("\n");
}
sb.Append("-- fullscreen ui images --\n");
foreach (var img in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>(true))
{
    if (!img.gameObject.activeInHierarchy) continue;
    var rt = img.rectTransform;
    var corners = new Vector3[4];
    rt.GetWorldCorners(corners);
    float w = Vector2.Distance(corners[0], corners[3]);
    float h = Vector2.Distance(corners[0], corners[1]);
    if (w < Screen.width * 0.8f || h < Screen.height * 0.8f) continue;
    sb.Append(img.gameObject.name).Append(" color=").Append(img.color.ToString())
      .Append(" size=").Append(w.ToString("F0")).Append("x").Append(h.ToString("F0")).Append("\n");
}
sb.Append("-- canvas roots --\n");
foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
{
    sb.Append(cv.name).Append(" mode=").Append(cv.renderMode).Append(" active=").Append(cv.gameObject.activeInHierarchy).Append("\n");
    foreach (Transform ch in cv.transform)
        sb.Append("   ").Append(ch.name).Append(" active=").Append(ch.gameObject.activeSelf).Append("\n");
}
sb.Append("-- root objects --\n");
foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
    sb.Append(go.name).Append(" active=").Append(go.activeSelf).Append("\n");
return sb.ToString();
'''

print("probe:", m.call("execute_code", {"action": "execute", "code": probe}, retries=3)[:2600], flush=True)