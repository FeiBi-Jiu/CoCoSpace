using System.Collections.Generic;
using UnityEngine;

namespace Game.Map
{
    [RequireComponent(typeof(MapBuilder))]
    public class FogOfWarMVP : MonoBehaviour
    {
        [Header("遮罩纹理")]
        [SerializeField] int maskResolution = 512;
        [SerializeField] float edgeFeather = 0.15f;

        [Header("遮罩平面")]
        [SerializeField] Color fogColor = new(0f, 0f, 0f, 1f);
        [SerializeField] float worldPadding = 4f;
        [SerializeField] float fogHeight = 1.5f;

        [Header("点亮规则")]
        [SerializeField] bool autoRevealByPlayer = true;
        [SerializeField] Transform playerTarget;
        [SerializeField] float nodeRadiusPadding = 0.5f;
        [SerializeField] float trailRadius = 1.2f;
        [SerializeField] float trailStep = 0.8f;

        static readonly int FogMaskId = Shader.PropertyToID("_FogMask");
        static readonly int FogColorId = Shader.PropertyToID("_FogColor");

        MapBuilder mapBuilder;
        GameObject overlay;
        Mesh overlayMesh;
        Material overlayMat;
        Texture2D fogMask;
        Color32[] maskData;

        int maskW;
        int maskH;
        Vector3 maskCenter;
        float maskWorldSize = 1f;

        bool initialized;
        int appliedSerial;

        Player.PlayerController player;
        Transform playerTransform;
        readonly HashSet<ulong> revealedNodes = new();
        bool hasTrailPos;
        Vector3 trailPos;

        void Awake()
        {
            mapBuilder = GetComponent<MapBuilder>();
        }

        void Update()
        {
            if (mapBuilder == null) return;

            if (!mapBuilder.HasMap)
            {
                if (initialized) ResetFog();
                return;
            }

            if (!initialized || appliedSerial != mapBuilder.BuildSerial)
                Initialize();

            if (autoRevealByPlayer) TickPlayerReveal();
        }

        void OnDestroy()
        {
            ResetFog();
        }

        public void Initialize()
        {
            Bounds bounds = mapBuilder.WorldBounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.z);
            if (size < 0.01f) size = 100f;

            maskWorldSize = size + worldPadding * 2f;
            maskCenter = bounds.center;
            maskCenter.y = 0f;

            maskW = Mathf.Max(32, maskResolution);
            maskH = maskW;

            CreateMaskTexture();
            CreateOverlay();

            revealedNodes.Clear();
            hasTrailPos = false;
            appliedSerial = mapBuilder.BuildSerial;
            initialized = true;
        }

        public void RevealSpot(Vector3 worldPos, float radius)
        {
            if (!initialized || maskData == null) return;
            StampCircle(worldPos, Mathf.Max(0.05f, radius));
        }

        public void RevealNode(ulong nodeId)
        {
            if (!initialized) return;

            var nodes = mapBuilder.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].NodeId != nodeId) continue;
                RevealNode(nodes[i]);
                return;
            }
        }

        void RevealNode(NodeArea node)
        {
            if (!revealedNodes.Add(node.NodeId)) return;
            StampCircle(node.Center, node.Radius + nodeRadiusPadding);
        }

        void TickPlayerReveal()
        {
            if (playerTransform == null) playerTransform = ResolvePlayer();
            if (playerTransform == null) return;

            Vector3 pos = playerTransform.position;

            var nodes = mapBuilder.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Contains(pos)) RevealNode(nodes[i]);
            }

            if (!hasTrailPos || (pos - trailPos).sqrMagnitude >= trailStep * trailStep)
            {
                StampCircle(pos, trailRadius);
                trailPos = pos;
                hasTrailPos = true;
            }
        }

        Transform ResolvePlayer()
        {
            if (playerTarget != null) return playerTarget;

            if (player == null) player = FindObjectOfType<Player.PlayerController>();
            if (player != null) return player.transform;

            var go = GameObject.Find("Player");
            return go != null ? go.transform : null;
        }

        void StampCircle(Vector3 worldPos, float worldRadius)
        {
            float u = (worldPos.x - maskCenter.x) / maskWorldSize + 0.5f;
            float v = (worldPos.z - maskCenter.z) / maskWorldSize + 0.5f;
            float rUv = worldRadius / maskWorldSize;

            float cx = u * maskW;
            float cy = v * maskH;
            float rpx = rUv * maskW;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rpx));
            int x1 = Mathf.Min(maskW - 1, Mathf.CeilToInt(cx + rpx));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rpx));
            int y1 = Mathf.Min(maskH - 1, Mathf.CeilToInt(cy + rpx));
            if (x0 > x1 || y0 > y1) return;

            float feather = Mathf.Max(1f, rpx * edgeFeather);

            for (int y = y0; y <= y1; y++)
            {
                float dy = y + 0.5f - cy;
                int row = y * maskW;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01((rpx - dist) / feather);
                    byte value = (byte)(t * 255f);

                    int index = row + x;
                    if (value > maskData[index].r)
                        maskData[index] = new Color32(value, value, value, 255);
                }
            }

            fogMask.SetPixels32(maskData);
            fogMask.Apply(false, false);
        }

        void CreateMaskTexture()
        {
            ReleaseMaskTexture();

            fogMask = new Texture2D(maskW, maskH, TextureFormat.RGBA32, false)
            {
                name = "FogMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            maskData = new Color32[maskW * maskH];
            for (int i = 0; i < maskData.Length; i++)
                maskData[i] = new Color32(0, 0, 0, 255);

            fogMask.SetPixels32(maskData);
            fogMask.Apply(false, false);
        }

        void CreateOverlay()
        {
            if (overlay == null)
            {
                overlay = new GameObject("FogOverlay");
                overlay.transform.SetParent(transform, false);

                overlayMesh = new Mesh { name = "FogOverlayQuad" };
                overlay.AddComponent<MeshFilter>().sharedMesh = overlayMesh;

                var renderer = overlay.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var shader = Resources.Load<Shader>("Shaders/FogOfWar");
                if (shader == null) shader = Shader.Find("CoCoSpace/FogOfWar");
                if (shader == null)
                {
                    Debug.LogError("[Fog] 找不到 CoCoSpace/FogOfWar shader");
                    return;
                }

                overlayMat = new Material(shader) { name = "FogOverlayMat" };
                renderer.sharedMaterial = overlayMat;
            }

            float half = maskWorldSize * 0.5f;

            overlayMesh.Clear();
            overlayMesh.SetVertices(new List<Vector3>
            {
                new(-half, 0f, -half),
                new(half, 0f, -half),
                new(half, 0f, half),
                new(-half, 0f, half),
            });
            overlayMesh.SetUVs(0, new List<Vector2>
            {
                new(0f, 0f),
                new(1f, 0f),
                new(1f, 1f),
                new(0f, 1f),
            });
            overlayMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            overlayMesh.RecalculateBounds();

            overlay.transform.position = new Vector3(maskCenter.x, fogHeight, maskCenter.z);

            if (overlayMat != null)
            {
                overlayMat.SetTexture(FogMaskId, fogMask);
                overlayMat.SetColor(FogColorId, fogColor);
            }
        }

        void ResetFog()
        {
            if (overlay != null) DestroySafe(overlay);
            overlay = null;
            overlayMesh = null;

            if (overlayMat != null) DestroySafe(overlayMat);
            overlayMat = null;

            ReleaseMaskTexture();

            revealedNodes.Clear();
            hasTrailPos = false;
            initialized = false;
            appliedSerial = 0;
        }

        void ReleaseMaskTexture()
        {
            if (fogMask != null) DestroySafe(fogMask);
            fogMask = null;
            maskData = null;
        }

        static void DestroySafe(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}