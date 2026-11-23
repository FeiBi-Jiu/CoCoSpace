using System;
using System.Collections.Generic;
using Google.Protobuf;
using Game.Proto;
using Game.Table;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.Map
{
    public struct NodeArea
    {
        public ulong NodeId;
        public Vector3 Center;
        public float Radius;
        public uint Attribute;

        public bool Contains(Vector3 worldPos)
        {
            float dx = worldPos.x - Center.x;
            float dz = worldPos.z - Center.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }
    }

    public struct PropArea
    {
        public ulong PropId;
        public uint PropType;
        public Vector3 Position;
        public float Radius;
    }

    public class MapBuilder : MonoBehaviour
    {
        [Header("材质（留空则自动生成纯色材质）")]
        [SerializeField] Material roadMat;
        [SerializeField] Material nodeMat;
        [SerializeField] Material propMat;

        [Header("属性配色（按 attribute 索引，留空用默认色表）")]
        [SerializeField] Color[] attributeColors;

        [Header("道具配色/尺寸（按 prop_type 索引，留空用默认表）")]
        [SerializeField] Color[] propColors;
        [SerializeField] float[] propSizes;

        [Header("地面与道具")]
        [SerializeField] Color roadColor = new(0.28f, 0.28f, 0.32f, 1f);
        [SerializeField] float roadY = 0.01f;
        [SerializeField] float nodeY = 0.02f;
        [SerializeField] float propHeight = 1.2f;
        [SerializeField] bool addGroundColliders = true;

        [Header("构建")]
        [SerializeField] int circleSegments = 48;
        [SerializeField] bool batchStaticLayer = true;
        [SerializeField] bool batchProps = false;
        [SerializeField] bool logSummary = true;

        static readonly Color[] DefaultAttributeColors =
        {
            new(0.72f, 0.72f, 0.75f, 1f),
            new(0.88f, 0.26f, 0.20f, 1f),
            new(0.22f, 0.48f, 0.92f, 1f),
            new(0.95f, 0.84f, 0.20f, 1f),
            new(0.30f, 0.78f, 0.32f, 1f),
            new(0.62f, 0.46f, 0.26f, 1f),
            new(0.35f, 0.82f, 0.88f, 1f),
        };

        static readonly Color[] DefaultPropColors =
        {
            new(0.60f, 0.60f, 0.62f, 1f),
            new(0.90f, 0.30f, 0.15f, 1f),
            new(0.95f, 0.60f, 0.15f, 1f),
            new(0.30f, 0.85f, 0.35f, 1f),
        };

        static readonly float[] DefaultPropSizes = { 0.6f, 0.8f, 0.5f, 0.4f };

        static readonly JsonParser LayoutJsonParser = new(new JsonParser.Settings(64));

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public IReadOnlyList<NodeArea> Nodes => nodes;
        public IReadOnlyList<PropArea> Props => props;
        public bool HasMap => built;
        public uint BuiltVersion => builtVersion;
        public int BuildSerial => buildSerial;
        public Bounds WorldBounds => worldBounds;

        readonly List<NodeArea> nodes = new();
        readonly List<PropArea> props = new();
        readonly Dictionary<ulong, GameObject> propObjects = new();
        readonly List<UnityEngine.Object> owned = new();
        readonly Dictionary<Color32, Material> nodeMats = new();
        readonly Dictionary<Color32, Material> propMats = new();
        readonly Dictionary<Color32, Material> miscMats = new();

        GameObject staticRoot;
        GameObject propRoot;
        Mesh discMesh;
        bool built;
        uint builtVersion;
        int buildSerial;
        Bounds worldBounds;

        public bool Build(MapLayout layout)
        {
            if (layout == null)
            {
                Debug.LogWarning("[Map] MapLayout 为空，跳过构建");
                return false;
            }

            if (layout.Nodes.Count == 0 && layout.Roads.Count == 0)
            {
                Debug.LogWarning("[Map] MapLayout 无据点/道路，跳过构建");
                return false;
            }

            if (built && layout.MapVersion != 0 && layout.MapVersion == builtVersion)
            {
                if (logSummary) Debug.Log($"[Map] version={builtVersion} 已构建，跳过重复构建");
                return true;
            }

            ClearMap();
            worldBounds = ComputeWorldBounds(layout);

            staticRoot = CreateRoot("StaticLayer");
            propRoot = CreateRoot("Props");

            BuildDiscMesh();

            foreach (var road in layout.Roads)
                BuildRoad(road, staticRoot.transform);

            foreach (var node in layout.Nodes)
                BuildNode(node, staticRoot.transform);

            foreach (var prop in layout.Props)
                BuildProp(prop, propRoot.transform);

            if (batchStaticLayer) StaticBatchingUtility.Combine(staticRoot);
            if (batchProps) StaticBatchingUtility.Combine(propRoot);

            built = true;
            builtVersion = layout.MapVersion;
            buildSerial++;

            if (logSummary)
                Debug.Log($"[Map] 构建完成 version={layout.MapVersion} nodes={layout.Nodes.Count} roads={layout.Roads.Count} props={layout.Props.Count}");

            return true;
        }

        public bool BuildFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[Map] MapLayout JSON 为空，跳过构建");
                return false;
            }

            try
            {
                var layout = LayoutJsonParser.Parse<MapLayout>(json);
                return Build(layout);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Map] MapLayout JSON 解析失败：{e.Message}");
                return false;
            }
        }

        public void ClearMap()
        {
            nodes.Clear();
            props.Clear();
            propObjects.Clear();
            DestroyRoot(staticRoot);
            DestroyRoot(propRoot);
            staticRoot = null;
            propRoot = null;
            discMesh = null;

            foreach (var obj in owned)
                SafeDestroy(obj);
            owned.Clear();

            nodeMats.Clear();
            propMats.Clear();
            miscMats.Clear();

            built = false;
            builtVersion = 0;
            worldBounds = default;
        }

        public bool DestroyProp(ulong propId)
        {
            if (!propObjects.TryGetValue(propId, out var go))
                return false;

            propObjects.Remove(propId);

            for (int i = props.Count - 1; i >= 0; i--)
            {
                if (props[i].PropId == propId) props.RemoveAt(i);
            }

            if (go != null) SafeDestroy(go);
            return true;
        }

        void OnDestroy()
        {
            ClearMap();
        }

        GameObject CreateRoot(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go;
        }

        void DestroyRoot(GameObject root)
        {
            if (root == null) return;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || owned.Contains(mesh)) continue;
                if (mesh.name.StartsWith("Combined Mesh", StringComparison.Ordinal))
                    SafeDestroy(mesh);
            }

            SafeDestroy(root);
        }

        static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        void BuildDiscMesh()
        {
            if (discMesh != null) return;

            int seg = Mathf.Max(8, circleSegments);
            var verts = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            var normals = new Vector3[seg + 1];

            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.up;

            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                float x = Mathf.Cos(a);
                float z = Mathf.Sin(a);
                verts[i + 1] = new Vector3(x, 0f, z);
                uvs[i + 1] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);
                normals[i + 1] = Vector3.up;
            }

            var tris = new int[seg * 3];
            for (int i = 0; i < seg; i++)
            {
                int cur = i + 1;
                int next = (i + 1) % seg + 1;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = next;
                tris[i * 3 + 2] = cur;
            }

            discMesh = new Mesh { name = "NodeDisc" };
            if (verts.Length > 65535) discMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            discMesh.SetVertices(verts);
            discMesh.SetUVs(0, uvs);
            discMesh.SetNormals(normals);
            discMesh.SetTriangles(tris, 0);
            discMesh.RecalculateBounds();
            owned.Add(discMesh);
        }

        void BuildNode(Node node, Transform parent)
        {
            float radius = Mathf.Max(0.1f, node.Radius);

            var go = new GameObject($"Node_{node.NodeId}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(node.X, nodeY, node.Z);
            go.transform.localScale = new Vector3(radius, 1f, radius);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = discMesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetTintedMaterial(nodeMats, nodeMat, ResolveAttributeColor(node.Attribute));

            if (addGroundColliders)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = discMesh;
            }

            nodes.Add(new NodeArea
            {
                NodeId = node.NodeId,
                Center = new Vector3(node.X, nodeY, node.Z),
                Radius = radius,
                Attribute = node.Attribute,
            });

            MarkStatic(go);
        }

        void BuildRoad(Road road, Transform parent)
        {
            if (road == null || road.Points.Count < 2) return;

            float width = Mathf.Max(0.1f, road.Width);
            float half = width * 0.5f;

            var pts = new List<Vector3>(road.Points.Count);
            foreach (var p in road.Points)
            {
                var v = new Vector3(p.X, roadY, p.Z);
                if (pts.Count > 0 && (pts[pts.Count - 1] - v).sqrMagnitude < 1e-6f) continue;
                pts.Add(v);
            }
            if (pts.Count < 2) return;

            int n = pts.Count;
            var left = new Vector3[n];
            var right = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                Vector3 dirIn = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                Vector3 dirOut = i < n - 1 ? (pts[i + 1] - pts[i]).normalized : dirIn;

                Vector3 nIn = Vector3.Cross(Vector3.up, dirIn);
                Vector3 nOut = Vector3.Cross(Vector3.up, dirOut);

                Vector3 miter = nIn + nOut;
                if (miter.sqrMagnitude < 1e-4f) miter = nIn;
                else miter.Normalize();

                float cosHalf = Vector3.Dot(miter, nIn);
                float offset = cosHalf > 0.25f ? half / cosHalf : half * 4f;

                left[i] = pts[i] + miter * offset;
                right[i] = pts[i] - miter * offset;
            }

            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var normals = new Vector3[n * 2];

            float dist = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) dist += Vector3.Distance(pts[i - 1], pts[i]);
                float v = dist / width;

                verts[i * 2] = left[i];
                verts[i * 2 + 1] = right[i];
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            var tris = new int[(n - 1) * 6];
            int t = 0;
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2;
                tris[t++] = a;
                tris[t++] = a + 1;
                tris[t++] = a + 2;
                tris[t++] = a + 1;
                tris[t++] = a + 3;
                tris[t++] = a + 2;
            }

            var mesh = new Mesh { name = $"Road_{road.RoadId}" };
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            owned.Add(mesh);

            var go = new GameObject($"Road_{road.RoadId}");
            go.transform.SetParent(parent, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = roadMat != null ? roadMat : GetTintedMaterial(miscMats, null, roadColor);

            if (addGroundColliders)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }

            MarkStatic(go);
        }

        void BuildProp(PropSpawn prop, Transform parent)
        {
            float size = ResolvePropSize(prop.PropType);
            var scale = new Vector3(size, propHeight * size, size);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Prop_{prop.PropId}";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(prop.X, scale.y * 0.5f, prop.Z);
            go.transform.localScale = scale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetTintedMaterial(propMats, propMat, ResolvePropColor(prop.PropType));

            var collider = go.GetComponent<Collider>();
            if (collider != null) collider.isTrigger = false;

            props.Add(new PropArea
            {
                PropId = prop.PropId,
                PropType = prop.PropType,
                Position = go.transform.position,
                Radius = size * 0.5f,
            });
            propObjects[prop.PropId] = go;

            MarkStatic(go);
        }

        Bounds ComputeWorldBounds(MapLayout layout)
        {
            bool has = false;
            float minX = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxZ = float.MinValue;

            void Encapsulate(float x, float z, float r)
            {
                if (x - r < minX) minX = x - r;
                if (z - r < minZ) minZ = z - r;
                if (x + r > maxX) maxX = x + r;
                if (z + r > maxZ) maxZ = z + r;
                has = true;
            }

            foreach (var node in layout.Nodes)
                Encapsulate(node.X, node.Z, Mathf.Max(0.1f, node.Radius));

            foreach (var road in layout.Roads)
            {
                if (road == null) continue;
                float r = Mathf.Max(0.1f, road.Width) * 0.5f;
                foreach (var p in road.Points)
                    Encapsulate(p.X, p.Z, r);
            }

            foreach (var prop in layout.Props)
                Encapsulate(prop.X, prop.Z, ResolvePropSize(prop.PropType) * 0.5f);

            if (!has) return default;

            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            var size = new Vector3(maxX - minX, 0f, maxZ - minZ);
            return new Bounds(center, size);
        }

        Color ResolveAttributeColor(uint attribute)
        {
            if (ElementTable.TryGet(attribute, out var element))
                return element.color;
            if (attributeColors != null && attribute < (uint)attributeColors.Length)
                return attributeColors[attribute];
            if (attribute < (uint)DefaultAttributeColors.Length)
                return DefaultAttributeColors[attribute];
            return DefaultAttributeColors[0];
        }

        Color ResolvePropColor(uint propType)
        {
            if (propColors != null && propType < (uint)propColors.Length)
                return propColors[propType];
            if (propType < (uint)DefaultPropColors.Length)
                return DefaultPropColors[propType];
            return DefaultPropColors[0];
        }

        float ResolvePropSize(uint propType)
        {
            if (propSizes != null && propType < (uint)propSizes.Length && propSizes[propType] > 0f)
                return propSizes[propType];
            if (propType < (uint)DefaultPropSizes.Length)
                return DefaultPropSizes[propType];
            return DefaultPropSizes[0];
        }

        Material GetTintedMaterial(Dictionary<Color32, Material> cache, Material baseMat, Color color)
        {
            var key = (Color32)color;
            if (cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            Material mat;
            if (baseMat != null)
            {
                mat = new Material(baseMat) { name = $"{baseMat.name}_{key}" };
                SetColor(mat, color);
            }
            else
            {
                mat = CreateDefaultMaterial(color);
            }

            owned.Add(mat);
            cache[key] = mat;
            return mat;
        }

        static Material CreateDefaultMaterial(Color color)
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "MapMat" };
            SetColor(mat, color);
            return mat;
        }

        static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
        }

        static void MarkStatic(GameObject go)
        {
#if UNITY_EDITOR
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
#endif
        }
    }
}