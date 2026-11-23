import sys
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()

code = r'''
var sb = new System.Text.StringBuilder();
var gr = GameObject.Find("GreyboxGround");
var p1 = GameObject.Find("Prop_01");
var cam = Camera.main;
sb.Append("cam occlusion=").Append(cam.useOcclusionCulling).Append(" hdr=").Append(cam.allowHDR).Append(" clearFlags=").Append(cam.clearFlags).Append("\n");
System.Action<GameObject> info = go => {
    var mr = go.GetComponent<Renderer>();
    var mf = go.GetComponent<MeshFilter>();
    sb.Append(go.name)
      .Append(" enabled=").Append(mr.enabled)
      .Append(" forceOff=").Append(mr.forceRenderingOff)
      .Append(" layerMask=").Append(mr.renderingLayerMask)
      .Append(" static=").Append(go.isStatic)
      .Append(" shadow=").Append(mr.shadowCastingMode)
      .Append(" queue=").Append(mr.sharedMaterial.renderQueue)
      .Append(" color=").Append(mr.sharedMaterial.color)
      .Append(" hideFlags=").Append(mr.sharedMaterial.hideFlags)
      .Append(" bounds=").Append(mr.bounds.ToString())
      .Append(" shader=").Append(mr.sharedMaterial.shader.name)
      .Append(" pass=").Append(mr.sharedMaterial.passCount)
      .Append(" parent=").Append(go.transform.parent != null ? go.transform.parent.name : "-")
      .Append("\n");
};
if (gr != null) info(gr);
if (p1 != null) info(p1);
var parentOfMap = gr != null ? gr.transform.parent : null;
if (parentOfMap != null) sb.Append("groundParent=").Append(parentOfMap.name).Append(" active=").Append(parentOfMap.gameObject.activeInHierarchy).Append("\n");
return sb.ToString();
'''

print(m.call("execute_code", {"action": "execute", "code": code}, retries=2)[:2000])