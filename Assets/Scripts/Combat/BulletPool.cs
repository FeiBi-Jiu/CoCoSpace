using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Combat
{
    public class BulletPool : MonoBehaviour
    {
        public static BulletPool Instance { get; private set; }

        public struct SpawnParams
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public float Lifetime;
            public float Radius;
            public Color Color;
            public float Acceleration;
            public uint BulletId;
        }

        [SerializeField] Mesh bulletMesh;
        [SerializeField] Material bulletMat;
        [SerializeField] Color bulletColor = new(1f, 0.9f, 0.35f, 1f);
        [SerializeField] float bulletScale = 0.12f;
        [SerializeField] float defaultLifetime = 2f;
        [SerializeField] bool dropOldestWhenFull = true;
        [SerializeField] bool cullOutsideBounds = true;
        [SerializeField] Vector3 boundsCenter = Vector3.zero;
        [SerializeField] float boundsRadius = 60f;

        const int MAX = 4096;
        const int BATCH_SIZE = 1023;
        const uint AUTO_ID_BASE = 0x80000000u;

        readonly Matrix4x4[] matrices = new Matrix4x4[MAX];
        readonly Vector3[] velocities = new Vector3[MAX];
        readonly float[] life = new float[MAX];
        readonly float[] accelerations = new float[MAX];
        readonly float[] radii = new float[MAX];
        readonly Color[] colors = new Color[MAX];
        readonly uint[] ids = new uint[MAX];
        readonly Matrix4x4[] batch = new Matrix4x4[BATCH_SIZE];

        readonly List<Color> colorKeys = new();
        readonly List<List<int>> colorGroups = new();

        MaterialPropertyBlock props;
        int colorPropertyId;
        int aliveCount;
        uint nextId = AUTO_ID_BASE;
        bool warned;
        bool ownMaterial;

        public int ActiveCount => aliveCount;
        public int Capacity => MAX;
        public float DefaultLifetime => defaultLifetime;
        public float DefaultRadius => bulletScale;
        public Color DefaultColor => bulletColor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("BulletPool");
            DontDestroyOnLoad(go);
            go.AddComponent<BulletPool>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            props = new MaterialPropertyBlock();
            EnsureResources();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (ownMaterial && bulletMat != null) Destroy(bulletMat);
        }

        void EnsureResources()
        {
            if (bulletMesh == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var filter = probe.GetComponent<MeshFilter>();
                if (filter != null) bulletMesh = filter.sharedMesh;
                Destroy(probe);
            }

            if (bulletMat == null)
            {
                var shader = Resources.Load<Shader>("Shaders/BulletInstanced");
                if (shader == null) shader = Shader.Find("CoCoSpace/BulletInstanced");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");

                if (shader != null)
                {
                    bulletMat = new Material(shader);
                    bulletMat.enableInstancing = true;
                    if (bulletMat.HasProperty("_Color")) bulletMat.SetColor("_Color", bulletColor);
                    ownMaterial = true;
                }
            }

            colorPropertyId = bulletMat != null && bulletMat.HasProperty("_Color")
                ? Shader.PropertyToID("_Color")
                : 0;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < aliveCount; i++)
            {
                life[i] -= dt;
                if (life[i] <= 0f)
                {
                    KillSlot(i);
                    i--;
                    continue;
                }

                Vector3 velocity = velocities[i];
                if (accelerations[i] != 0f && velocity.sqrMagnitude > 0.000001f)
                    velocity += velocity.normalized * (accelerations[i] * dt);

                Vector3 position = matrices[i].GetPosition() + velocity * dt;
                velocities[i] = velocity;

                if (cullOutsideBounds &&
                    (position - boundsCenter).sqrMagnitude > boundsRadius * boundsRadius)
                {
                    KillSlot(i);
                    i--;
                    continue;
                }

                Quaternion rotation = velocity.sqrMagnitude > 0.000001f
                    ? Quaternion.LookRotation(velocity)
                    : matrices[i].rotation;

                matrices[i] = Matrix4x4.TRS(position, rotation, Vector3.one * radii[i]);
            }

            DrawInstanced();
        }

        void DrawInstanced()
        {
            if (aliveCount == 0) return;

            if (bulletMesh == null || bulletMat == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[BulletPool] 缺少 bulletMesh/bulletMat，子弹不可见");
                }
                return;
            }

            colorKeys.Clear();
            for (int g = 0; g < colorGroups.Count; g++) colorGroups[g].Clear();

            for (int i = 0; i < aliveCount; i++)
            {
                Color c = colors[i];
                int group = -1;
                for (int k = 0; k < colorKeys.Count; k++)
                {
                    if (colorKeys[k] == c)
                    {
                        group = k;
                        break;
                    }
                }

                if (group < 0)
                {
                    group = colorKeys.Count;
                    colorKeys.Add(c);
                    if (group < colorGroups.Count) colorGroups[group].Clear();
                    else colorGroups.Add(new List<int>());
                }

                colorGroups[group].Add(i);
            }

            for (int g = 0; g < colorKeys.Count; g++)
            {
                var group = colorGroups[g];
                if (group.Count == 0) continue;

                if (colorPropertyId != 0) props.SetColor(colorPropertyId, colorKeys[g]);

                for (int start = 0; start < group.Count; start += BATCH_SIZE)
                {
                    int n = Mathf.Min(BATCH_SIZE, group.Count - start);
                    for (int k = 0; k < n; k++) batch[k] = matrices[group[start + k]];

                    Graphics.DrawMeshInstanced(bulletMesh, 0, bulletMat, batch, n, props,
                        ShadowCastingMode.Off, false);
                }
            }
        }

        public uint Spawn(in SpawnParams p)
        {
            if (aliveCount >= MAX)
            {
                if (!dropOldestWhenFull) return 0;
                KillSlot(0);
            }

            uint id = p.BulletId;
            if (id != 0)
            {
                int existing = FindSlot(id);
                if (existing >= 0) KillSlot(existing);
            }
            else
            {
                id = nextId++;
                if (nextId < AUTO_ID_BASE) nextId = AUTO_ID_BASE;
            }

            int index = aliveCount++;

            Vector3 velocity = p.Direction.sqrMagnitude > 0.000001f
                ? p.Direction.normalized * p.Speed
                : Vector3.zero;

            Quaternion rotation = velocity.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(velocity)
                : Quaternion.identity;

            float radius = p.Radius > 0f ? p.Radius : bulletScale;
            Color color = p.Color.a > 0f ? p.Color : bulletColor;
            float lifetime = p.Lifetime > 0f ? p.Lifetime : defaultLifetime;

            matrices[index] = Matrix4x4.TRS(p.Position, rotation, Vector3.one * radius);
            velocities[index] = velocity;
            life[index] = lifetime;
            accelerations[index] = p.Acceleration;
            radii[index] = radius;
            colors[index] = color;
            ids[index] = id;
            return id;
        }

        public uint Spawn(Vector3 pos, Vector3 dir, float speed)
        {
            return Spawn(pos, dir, speed, defaultLifetime);
        }

        public uint Spawn(Vector3 pos, Vector3 dir, float speed, float lifetime)
        {
            var p = new SpawnParams
            {
                Position = pos,
                Direction = dir,
                Speed = speed,
                Lifetime = lifetime,
                Color = bulletColor,
            };
            return Spawn(p);
        }

        public bool TryGetPosition(uint id, out Vector3 position)
        {
            int slot = FindSlot(id);
            if (slot < 0)
            {
                position = Vector3.zero;
                return false;
            }

            position = matrices[slot].GetPosition();
            return true;
        }

        public bool TryGetVelocity(uint id, out Vector3 velocity)
        {
            int slot = FindSlot(id);
            if (slot < 0)
            {
                velocity = Vector3.zero;
                return false;
            }

            velocity = velocities[slot];
            return true;
        }

        public bool TryRedirect(uint id, Vector3 newDirection, out Vector3 position)
        {
            int slot = FindSlot(id);
            if (slot < 0)
            {
                position = Vector3.zero;
                return false;
            }

            position = matrices[slot].GetPosition();
            if (newDirection.sqrMagnitude <= 0.000001f) return false;

            Vector3 direction = newDirection.normalized;
            float speed = velocities[slot].magnitude;
            if (speed <= 0.000001f) speed = 1f;

            velocities[slot] = direction * speed;
            matrices[slot] = Matrix4x4.TRS(position, Quaternion.LookRotation(direction),
                Vector3.one * radii[slot]);
            return true;
        }

        public static uint ComposeId(string patternId, ulong emitterId, int volleyIndex, int bulletIndex)
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(patternId))
            {
                for (int i = 0; i < patternId.Length; i++)
                {
                    hash ^= patternId[i];
                    hash *= 16777619u;
                }
            }

            hash ^= (uint)emitterId;
            hash *= 16777619u;
            hash ^= (uint)volleyIndex;
            hash *= 16777619u;
            hash ^= (uint)bulletIndex;
            hash *= 16777619u;
            hash &= 0x7FFFFFFFu;
            return hash == 0 ? 1u : hash;
        }

        public bool Kill(uint id)
        {
            int slot = FindSlot(id);
            if (slot < 0) return false;

            KillSlot(slot);
            return true;
        }

        public void RecycleAll()
        {
            for (int i = 0; i < aliveCount; i++) ids[i] = 0;
            aliveCount = 0;
        }

        int FindSlot(uint id)
        {
            for (int i = 0; i < aliveCount; i++)
                if (ids[i] == id) return i;

            return -1;
        }

        void KillSlot(int index)
        {
            if (index < 0 || index >= aliveCount) return;

            aliveCount--;
            matrices[index] = matrices[aliveCount];
            velocities[index] = velocities[aliveCount];
            life[index] = life[aliveCount];
            accelerations[index] = accelerations[aliveCount];
            radii[index] = radii[aliveCount];
            colors[index] = colors[aliveCount];
            ids[index] = ids[aliveCount];
            ids[aliveCount] = 0;
        }
    }
}