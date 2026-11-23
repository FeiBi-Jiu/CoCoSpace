import sys
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()

code = r'''
var sb = new System.Text.StringBuilder();
var gm = GameObject.Find("GreyboxMap");
System.Action<Transform> dump = t => {
    var mf = t.GetComponent<MeshFilter>();
    var mr = t.GetComponent<MeshRenderer>();
    sb.Append(t.name)
      .Append(" mesh=").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " b=" + mf.sharedMesh.bounds.size.ToString("F1") : "NULL")
      .Append(" mat=").Append(mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "NULL")
      .Append(" layer=").Append(t.gameObject.layer)
      .Append(" scale=").Append(t.lossyScale.ToString("F2"))
      .Append(" pos=").Append(t.position.ToString("F2"))
      .Append(" isVisible=").Append(mr != null ? mr.isVisible.ToString() : "n/a")
      .Append("\n");
};
if (gm != null) foreach (Transform ch in gm.transform) dump(ch);
var gr = GameObject.Find("GreyboxGround");
if (gr != null) dump(gr.transform);
var pr = GameObject.Find("Player");
if (pr != null) foreach (Transform ch in pr.transform) { if (ch.GetComponent<MeshRenderer>() != null) dump(ch); }
return sb.ToString();
'''

print(m.call("execute_code", {"action": "execute", "code": code}, retries=2)[:3000])