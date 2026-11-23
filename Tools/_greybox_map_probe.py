import sys
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()

code = r'''
var sb = new System.Text.StringBuilder();
var gm = GameObject.Find("GreyboxMap");
sb.Append("GreyboxMap=").Append(gm != null ? (gm.activeInHierarchy ? "active" : "inactive") : "null").Append("\n");
if (gm != null)
{
    foreach (Transform ch in gm.transform)
    {
        var r = ch.GetComponent<Renderer>();
        sb.Append("  ").Append(ch.name)
          .Append(" active=").Append(ch.gameObject.activeInHierarchy)
          .Append(" pos=").Append(ch.position.ToString("F1"))
          .Append(" scale=").Append(ch.lossyScale.ToString("F1"));
        if (r != null && r.sharedMaterial != null)
            sb.Append(" color=").Append(r.sharedMaterial.color);
        sb.Append("\n");
    }
}
var gr = GameObject.Find("GreyboxGround");
sb.Append("GreyboxGround=").Append(gr != null ? gr.transform.position.ToString("F1") + " scale=" + gr.transform.lossyScale.ToString("F1") + " active=" + gr.activeInHierarchy : "null").Append("\n");

var mb = GameObject.FindObjectOfType<Game.Map.MapBuilder>();
sb.Append("MapBuilder=").Append(mb != null ? mb.name : "null");
if (mb != null)
{
    sb.Append(" HasMap=").Append(mb.HasMap).Append(" bounds=").Append(mb.WorldBounds.ToString());
}
sb.Append("\n");
var fog = GameObject.FindObjectOfType<Game.Map.FogOfWarMVP>();
sb.Append("FogOfWar=").Append(fog != null ? fog.name : "null").Append("\n");
return sb.ToString();
'''

print(m.call("execute_code", {"action": "execute", "code": code}, retries=2)[:2500])