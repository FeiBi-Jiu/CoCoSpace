using Game.Proto;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Snapshot
{
    public class RemoteEntityView : MonoBehaviour
    {
        static readonly Color PlayerColor = new(0.25f, 0.55f, 1f, 1f);
        static readonly Color MonsterColor = new(0.90f, 0.28f, 0.22f, 1f);
        static readonly Color BulletColor = new(1f, 0.86f, 0.28f, 1f);
        static readonly Color GhostColor = new(0.72f, 0.82f, 1f, 1f);
        static readonly Color HitColor = new(1f, 1f, 1f, 1f);

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly SnapshotBuffer<Entity> buffer = new();

        MeshRenderer meshRenderer;
        Material material;
        Color typeColor = PlayerColor;
        Vector3 baseScale = Vector3.one;
        bool ghostApplied;
        float flashUntil;
        float lastHp = -1f;

        public ulong EntityId { get; private set; }
        public EntityType Type { get; private set; }

        public float InterpDelay
        {
            get => (float)buffer.interpDelay;
            set => buffer.interpDelay = value;
        }

        public void Init(ulong id, EntityType type)
        {
            EntityId = id;
            Type = type;
            buffer.Clear();
            lastHp = -1f;

            ConfigureVisual(type);
        }

        public void OnSnapshot(double serverTime, Entity snap)
        {
            buffer.Push(serverTime, snap);

            if (lastHp >= 0f && snap.Hp < lastHp) flashUntil = Time.time + 0.12f;
            lastHp = snap.Hp;

            ghostApplied = snap.Flags != null && snap.Flags.IsGhost;
        }

        void Update()
        {
            if (!ServerClock.IsReady) return;

            double renderTime = ServerClock.RenderTime - buffer.interpDelay;
            if (!buffer.Sample(renderTime, out var a, out var b, out float t)) return;

            Vector3 from = new(a.X, 0f, a.Z);
            Vector3 to = new(b.X, 0f, b.Z);
            transform.position = Vector3.Lerp(from, to, t);

            float yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (material == null) return;

            if (Time.time < flashUntil)
            {
                ApplyColor(HitColor);
                return;
            }

            Color tint = ghostApplied ? GhostColor : typeColor;
            float ratio = b.HpMax > 0f ? Mathf.Clamp01(b.Hp / b.HpMax) : 1f;
            ApplyColor(Color.Lerp(tint * 0.45f, tint, ratio));
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        void ConfigureVisual(EntityType type)
        {
            meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (meshRenderer == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(transform, false);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                meshRenderer = go.GetComponent<MeshRenderer>();
            }

            switch (type)
            {
                case EntityType.EntityMonster:
                    typeColor = MonsterColor;
                    baseScale = new Vector3(0.9f, 1.2f, 0.9f);
                    break;
                case EntityType.EntityElite:
                    typeColor = new Color(0.95f, 0.45f, 0.10f, 1f);
                    baseScale = new Vector3(1.2f, 1.6f, 1.2f);
                    break;
                case EntityType.EntityBoss:
                    typeColor = new Color(0.65f, 0.15f, 0.65f, 1f);
                    baseScale = new Vector3(2.2f, 2.6f, 2.2f);
                    break;
                case EntityType.EntityBullet:
                    typeColor = BulletColor;
                    baseScale = new Vector3(0.18f, 0.18f, 0.18f);
                    break;
                case EntityType.EntityPlayer:
                    typeColor = PlayerColor;
                    baseScale = new Vector3(0.8f, 1.6f, 0.8f);
                    break;
                default:
                    typeColor = new Color(0.6f, 0.6f, 0.6f, 1f);
                    baseScale = new Vector3(0.7f, 0.7f, 0.7f);
                    break;
            }

            transform.localScale = baseScale;
            material = CreateMaterial(typeColor);
            if (meshRenderer != null && material != null)
            {
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void ApplyColor(Color color)
        {
            if (material == null) return;

            if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
            else if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
        }

        static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
            else if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
            return mat;
        }
    }
}