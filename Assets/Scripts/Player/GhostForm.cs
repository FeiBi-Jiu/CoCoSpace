using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Game.Snapshot;
using Game.World;
using UnityEngine;

namespace Game.Player
{
    public class GhostForm : MonoBehaviour
    {
        public struct HudState
        {
            public bool Active;
            public ulong AnchorId;
            public float Radius;
            public float Distance;
            public bool Carrying;
        }

        class AuraEntry
        {
            public GameObject Root;
            public float ExpireAt;
        }

        [SerializeField] LineRenderer leashLine;
        [SerializeField] float leashRadius = 15f;
        [SerializeField] float warnRatio = 0.8f;
        [SerializeField] Color normalColor = new(0.25f, 0.95f, 0.55f, 0.85f);
        [SerializeField] Color warnColor = new(1f, 0.85f, 0.25f, 0.90f);
        [SerializeField] Color outColor = new(1f, 0.30f, 0.28f, 0.95f);
        [SerializeField] Color auraColor = new(0.55f, 0.85f, 1f, 0.85f);
        [SerializeField] float snapFlashSeconds = 0.5f;
        [SerializeField] float circleHeight = 0.06f;
        [SerializeField] float carriedHeight = 1.5f;
        [SerializeField] float auraRadius = 1.1f;
        [SerializeField] float auraHeight = 0.12f;
        [SerializeField] float highlightRange = 6f;
        [SerializeField] float highlightInterval = 0.4f;

        const int CircleSegments = 64;
        const int AuraSegments = 40;
        const float CarriedSize = 0.4f;
        const float SnapBackOffset = 0.8f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        ulong anchorId;
        float radius;
        float flashTimer;
        float nextHighlightScan;
        bool carrying;

        readonly Dictionary<ulong, GameObject> carriedProps = new();
        readonly Dictionary<ulong, AuraEntry> auras = new();
        readonly List<ulong> staleIds = new();

        public ulong AnchorId => anchorId;
        public float Radius => radius > 0f ? radius : leashRadius;
        public bool Carrying => carrying;

        public HudState State
        {
            get
            {
                bool hasAnchor = TryGetAnchorPosition(out Vector3 center);
                return new HudState
                {
                    Active = IsGhostMode(),
                    AnchorId = anchorId,
                    Radius = Radius,
                    Distance = hasAnchor ? FlatDistance(center) : 0f,
                    Carrying = carrying,
                };
            }
        }

        void Awake()
        {
            radius = leashRadius;
            EnsureLine();
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CGhostTether>(OnTether);
            MsgDispatcher.Register<S2CGhostTetherBreak>(OnTetherBreak);
            MsgDispatcher.Register<S2CItemCarried>(OnItemCarried);
            MsgDispatcher.Register<S2CGhostBuff>(OnGhostBuff);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CGhostTether>(OnTether);
            MsgDispatcher.Unregister<S2CGhostTetherBreak>(OnTetherBreak);
            MsgDispatcher.Unregister<S2CItemCarried>(OnItemCarried);
            MsgDispatcher.Unregister<S2CGhostBuff>(OnGhostBuff);

            ClearCarriedProps();
            ClearAuras();
            ClearDropHighlights();

            if (leashLine != null) leashLine.positionCount = 0;
        }

        void Update()
        {
            if (!IsGhostMode())
            {
                if (leashLine != null && leashLine.positionCount != 0) leashLine.positionCount = 0;
                return;
            }

            if (flashTimer > 0f) flashTimer -= Time.deltaTime;

            UpdateLeash();
            UpdateCarriedProps();
            UpdateAuras();
            ScanGhostDrops();
        }

        void OnTether(S2CGhostTether msg)
        {
            if (msg.GhostEntityId != LocalEntityId()) return;

            anchorId = msg.AnchorEntityId;
            radius = msg.Radius > 0f ? msg.Radius : leashRadius;
        }

        void OnTetherBreak(S2CGhostTetherBreak msg)
        {
            if (msg.GhostEntityId != LocalEntityId()) return;

            SnapBackToAnchor();
            flashTimer = snapFlashSeconds;
        }

        // 服务器已把鬼魂拉回，本地权威移动不接收纠偏，这里同步一次位置表现
        void SnapBackToAnchor()
        {
            if (!TryGetAnchorPosition(out Vector3 center)) return;

            Vector3 dir = transform.position - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;

            Vector3 target = center + dir.normalized * SnapBackOffset;
            target.y = transform.position.y;

            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = target;
            if (cc != null) cc.enabled = true;
        }

        void OnItemCarried(S2CItemCarried msg)
        {
            ulong carrier = msg.CarrierEntityId;

            if (msg.Dropped)
            {
                if (carriedProps.TryGetValue(carrier, out var old) && old != null) Destroy(old);
                carriedProps.Remove(carrier);
                if (carrier == LocalEntityId()) carrying = false;
                return;
            }

            if (carrier == LocalEntityId()) carrying = true;

            if (carriedProps.ContainsKey(carrier)) return;
            carriedProps[carrier] = CreateCarriedProp(msg.ItemType);
        }

        void OnGhostBuff(S2CGhostBuff msg)
        {
            if (msg.TargetEntityId == 0) return;

            float duration = msg.DurationMs > 0 ? msg.DurationMs / 1000f : 3f;

            if (!auras.TryGetValue(msg.TargetEntityId, out var entry) || entry.Root == null)
            {
                entry = new AuraEntry { Root = CreateAura() };
                auras[msg.TargetEntityId] = entry;
            }

            entry.ExpireAt = Time.time + duration;
        }

        void UpdateLeash()
        {
            if (leashLine == null) return;

            if (!TryGetAnchorPosition(out Vector3 center))
            {
                leashLine.positionCount = 0;
                return;
            }

            float r = Radius;
            DrawCircle(center, r, LeashColor(center, r));
        }

        Color LeashColor(Vector3 center, float r)
        {
            if (flashTimer > 0f) return outColor;

            float ratio = r > 0.01f ? FlatDistance(center) / r : 0f;
            if (ratio >= 1f) return outColor;
            if (ratio >= warnRatio) return warnColor;
            return normalColor;
        }

        void DrawCircle(Vector3 center, float r, Color color)
        {
            leashLine.positionCount = CircleSegments;
            leashLine.loop = true;
            leashLine.startColor = color;
            leashLine.endColor = color;

            float y = center.y + circleHeight;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a = i * Mathf.PI * 2f / CircleSegments;
                leashLine.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, y, center.z + Mathf.Sin(a) * r));
            }
        }

        void UpdateCarriedProps()
        {
            if (carriedProps.Count == 0) return;

            staleIds.Clear();
            foreach (var pair in carriedProps)
            {
                if (pair.Value == null)
                {
                    staleIds.Add(pair.Key);
                    continue;
                }

                if (!TryGetEntityPosition(pair.Key, out Vector3 pos))
                {
                    staleIds.Add(pair.Key);
                    continue;
                }

                pair.Value.transform.position = pos + Vector3.up * carriedHeight;
            }

            for (int i = 0; i < staleIds.Count; i++)
            {
                if (carriedProps.TryGetValue(staleIds[i], out var go) && go != null) Destroy(go);
                carriedProps.Remove(staleIds[i]);
            }
            staleIds.Clear();
        }

        void UpdateAuras()
        {
            if (auras.Count == 0) return;

            staleIds.Clear();
            foreach (var pair in auras)
            {
                var entry = pair.Value;
                if (entry.Root == null || Time.time >= entry.ExpireAt)
                {
                    staleIds.Add(pair.Key);
                    continue;
                }

                if (!TryGetEntityPosition(pair.Key, out Vector3 pos)) continue;

                entry.Root.transform.position = pos;
                float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 7f);
                entry.Root.transform.localScale = Vector3.one * pulse;
            }

            for (int i = 0; i < staleIds.Count; i++) RemoveAura(staleIds[i]);
            staleIds.Clear();
        }

        void ScanGhostDrops()
        {
            if (Time.time < nextHighlightScan) return;
            nextHighlightScan = Time.time + Mathf.Max(0.1f, highlightInterval);

            var drops = FindObjectsOfType<DropView>();
            for (int i = 0; i < drops.Length; i++)
            {
                var drop = drops[i];
                if (drop == null || !drop.GhostOnly) continue;

                drop.SetGhostHighlight(FlatDistance(drop.transform.position) <= highlightRange);
            }
        }

        void ClearDropHighlights()
        {
            var drops = FindObjectsOfType<DropView>();
            for (int i = 0; i < drops.Length; i++)
                if (drops[i] != null) drops[i].SetGhostHighlight(false);
        }

        void ClearCarriedProps()
        {
            foreach (var pair in carriedProps)
                if (pair.Value != null) Destroy(pair.Value);

            carriedProps.Clear();
            carrying = false;
        }

        void ClearAuras()
        {
            foreach (var pair in auras)
                if (pair.Value != null && pair.Value.Root != null) Destroy(pair.Value.Root);

            auras.Clear();
        }

        void RemoveAura(ulong targetId)
        {
            if (!auras.TryGetValue(targetId, out var entry)) return;
            if (entry != null && entry.Root != null) Destroy(entry.Root);
            auras.Remove(targetId);
        }

        bool TryGetAnchorPosition(out Vector3 position)
        {
            if (anchorId == 0)
            {
                position = transform.position;
                return false;
            }

            return TryGetEntityPosition(anchorId, out position);
        }

        bool TryGetEntityPosition(ulong entityId, out Vector3 position)
        {
            if (entityId == LocalEntityId())
            {
                position = transform.position;
                return true;
            }

            var director = SnapshotDirector.Instance;
            if (director != null) return director.TryGetEntityPosition(entityId, out position);

            position = Vector3.zero;
            return false;
        }

        float FlatDistance(Vector3 target)
        {
            Vector3 delta = target - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        static bool IsGhostMode() => DeathGhostTransition.IsGhost;

        static ulong LocalEntityId()
            => GameRoot.Instance != null ? GameRoot.Instance.LocalEntityId : 0;

        void EnsureLine()
        {
            if (leashLine == null)
            {
                var go = new GameObject("LeashCircle");
                go.transform.SetParent(transform, false);
                leashLine = go.AddComponent<LineRenderer>();
            }

            ConfigureLine(leashLine);
        }

        static void ConfigureLine(LineRenderer line)
        {
            line.useWorldSpace = true;
            line.loop = true;
            line.widthMultiplier = 0.12f;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (line.sharedMaterial == null) line.material = CreateLineMaterial();
        }

        static Material CreateLineMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader) { name = "GhostLineMat" };
        }

        GameObject CreateCarriedProp(uint itemType)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CarriedItem_" + itemType;
            go.transform.localScale = Vector3.one * CarriedSize;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.material = CreateUnlitMaterial(ItemColor(itemType));
            }

            return go;
        }

        GameObject CreateAura()
        {
            var root = new GameObject("GhostBuffAura");
            var line = root.AddComponent<LineRenderer>();

            ConfigureLine(line);
            line.useWorldSpace = false;
            line.widthMultiplier = 0.09f;
            line.positionCount = AuraSegments;
            line.startColor = auraColor;
            line.endColor = auraColor;

            for (int i = 0; i < AuraSegments; i++)
            {
                float a = i * Mathf.PI * 2f / AuraSegments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * auraRadius, auraHeight, Mathf.Sin(a) * auraRadius));
            }

            return root;
        }

        static Material CreateUnlitMaterial(Color color)
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = "GhostItemMat" };
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
            return mat;
        }

        static Color ItemColor(uint itemType)
        {
            switch (itemType)
            {
                case 1: return new Color(0.92f, 0.32f, 0.32f, 1f);
                case 2: return new Color(0.35f, 0.75f, 1f, 1f);
                case 3: return new Color(0.50f, 0.95f, 0.50f, 1f);
                default: return new Color(0.95f, 0.90f, 0.60f, 1f);
            }
        }
    }
}