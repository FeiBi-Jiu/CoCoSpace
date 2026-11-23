import sys
sys.path.insert(0, "Tools")
import _greybox_mcp as m

m.init()

NAMES = ["GreyboxGround", "Road_N", "Node_02_Fire", "Prop_01", "GreyboxBody", "GreyboxHead"]

parts = []
parts.append('var sb=new System.Text.StringBuilder();')
parts.append('sb.Append("screen=").Append(Screen.width).Append("x").Append(Screen.height).Append("\\n");')
for n in NAMES:
    parts.append('var go_{0}=GameObject.Find("{0}");'.format(n.replace("_", "")))
    parts.append('sb.Append("{0}: ");'.format(n))
    parts.append('if (go_{0}==null) sb.Append("NOT FOUND\\n"); else {{ var r=go_{0}.GetComponent<Renderer>(); sb.Append(r!=null ? ("shader="+r.sharedMaterial.shader.name+" color="+r.sharedMaterial.color+" pos="+go_{0}.transform.position.ToString("F2")+" enabled="+r.enabled) : "no renderer"); sb.Append("\\n"); }}'.format(n.replace("_", "")))
parts.append('sb.Append("-- canvas --\\n");')
parts.append('foreach (var cv in GameObject.FindObjectsOfType<Canvas>()) { sb.Append(cv.name).Append(" active=").Append(cv.gameObject.activeInHierarchy).Append("\\n"); foreach (Transform ch in cv.transform) sb.Append("   ").Append(ch.name).Append(" active=").Append(ch.gameObject.activeSelf).Append("\\n"); }')
parts.append('return sb.ToString();')

code = "\n".join(parts)
print(m.call("execute_code", {"action": "execute", "code": code}, retries=2)[:2000])