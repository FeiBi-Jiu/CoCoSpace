using System.Collections.Generic;
using Game.Snapshot;
using Game.Table;
using UnityEngine;

namespace Game.Combat
{
    public class BulletPatternPlayer : MonoBehaviour
    {
        public static readonly Dictionary<string, BulletPatternPlayer> Active = new();

        public BulletPatternDef def;
        [SerializeField] Vector3 spawnOffset = new(0f, 0.9f, 0f);
        [SerializeField] float emitterGraceFrames = 90f;
        [SerializeField] bool emitImmediately = true;

        public string PatternId { get; private set; }
        public ulong OwnerEntityId { get; private set; }
        public int VolleyIndex => volleyIndex;
        public bool Emitting => emitting;

        ulong emitterEntityId;
        float volleyTimer;
        float elapsed;
        float patternAngle;
        float spiralAngle;
        int volleyIndex;
        int missingFrames;
        bool emitting;
        bool initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();

        public static BulletPatternPlayer Create(string patternId, ulong emitterId, BulletPatternDef patternDef)
        {
            if (patternDef == null) return null;

            if (!string.IsNullOrEmpty(patternId)) Stop(patternId);

            var go = new GameObject($"BulletPattern_{patternId}_{emitterId}");
            var player = go.AddComponent<BulletPatternPlayer>();
            player.Init(patternId, emitterId, patternDef);
            return player;
        }

        public static void Stop(string patternId)
        {
            if (string.IsNullOrEmpty(patternId)) return;

            if (Active.TryGetValue(patternId, out var player) && player != null)
                player.StopEmit();
            else
                Active.Remove(patternId);
        }

        public static void StopAll()
        {
            var players = new List<BulletPatternPlayer>(Active.Values);
            Active.Clear();

            foreach (var player in players)
                if (player != null) player.StopEmit();
        }

        public void Init(string patternId, ulong emitterId, BulletPatternDef patternDef)
        {
            if (patternDef == null) return;

            initialized = true;
            def = patternDef;
            emitterEntityId = emitterId;
            OwnerEntityId = emitterId;

            PatternId = string.IsNullOrEmpty(patternId) ? patternDef.Id : patternId;
            if (string.IsNullOrEmpty(PatternId))
                PatternId = $"pattern_{emitterId}_{GetInstanceID()}";

            Active[PatternId] = this;

            emitting = true;
            elapsed = 0f;
            volleyIndex = 0;
            patternAngle = patternDef.StartAngleDeg;
            spiralAngle = patternDef.StartAngleDeg;
            missingFrames = 0;
            volleyTimer = emitImmediately ? Mathf.Max(0.02f, patternDef.Interval) : 0f;

            if (emitterId != 0 && TryResolveEmitter(out var position, out var rotation))
                transform.SetPositionAndRotation(position, rotation);
        }

        void Awake()
        {
            if (!initialized && def != null)
                Init(def.Id, emitterEntityId, def);
        }

        void OnDestroy()
        {
            if (!string.IsNullOrEmpty(PatternId) &&
                Active.TryGetValue(PatternId, out var player) && player == this)
                Active.Remove(PatternId);
        }

        void Update()
        {
            if (!initialized || !emitting || def == null) return;

            if (def.Duration > 0f)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= def.Duration)
                {
                    StopEmit();
                    return;
                }
            }

            if (!TryResolveEmitter(out var position, out var rotation)) return;
            transform.SetPositionAndRotation(position, rotation);

            float interval = Mathf.Max(0.02f, def.Interval);
            volleyTimer += Time.deltaTime;
            if (volleyTimer < interval) return;

            volleyTimer -= interval;
            if (volleyTimer > interval) volleyTimer = interval;

            FireVolley();
            volleyIndex++;

            if (def.AngularSpeedDeg != 0f)
                patternAngle += def.AngularSpeedDeg * interval;
        }

        public void StopEmit()
        {
            if (!emitting && !initialized) return;
            emitting = false;
            initialized = false;

            if (!string.IsNullOrEmpty(PatternId) &&
                Active.TryGetValue(PatternId, out var player) && player == this)
                Active.Remove(PatternId);

            Destroy(gameObject);
        }

        bool TryResolveEmitter(out Vector3 position, out Quaternion rotation)
        {
            if (emitterEntityId == 0)
            {
                position = transform.position;
                rotation = transform.rotation;
                return true;
            }

            var director = SnapshotDirector.Instance;
            if (director != null && director.TryGetEntityTransform(emitterEntityId, out position, out rotation))
            {
                missingFrames = 0;
                return true;
            }

            missingFrames++;
            if (missingFrames > emitterGraceFrames)
            {
                StopEmit();
            }

            position = transform.position;
            rotation = transform.rotation;
            return false;
        }

        void FireVolley()
        {
            var pool = BulletPool.Instance;
            if (pool == null) return;

            int count = Mathf.Max(1, def.Count);
            float speed = def.Speed > 0f ? def.Speed : 8f;
            float lifetime = def.BulletLifetime > 0f ? def.BulletLifetime : pool.DefaultLifetime;
            float radius = def.BulletRadius > 0f ? def.BulletRadius : pool.DefaultRadius;
            Color color = def.ResolveColor(pool.DefaultColor);

            Vector3 origin = transform.TransformPoint(spawnOffset);

            for (int i = 0; i < count; i++)
            {
                float angle = ShotAngle(i, count);
                Vector3 spawnPosition = origin;

                if (def.Shape == "line")
                {
                    float spacing = def.LineSpacing > 0f ? def.LineSpacing : 0.6f;
                    float lateral = (i - (count - 1) * 0.5f) * spacing;
                    spawnPosition += transform.right * lateral;
                }

                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;

                var p = new BulletPool.SpawnParams
                {
                    Position = spawnPosition,
                    Direction = dir,
                    Speed = speed,
                    Lifetime = lifetime,
                    Radius = radius,
                    Color = color,
                    Acceleration = def.Acceleration,
                    BulletId = BulletPool.ComposeId(PatternId, OwnerEntityId, volleyIndex, i),
                };
                pool.Spawn(p);
            }

            if (def.Shape == "spiral")
                spiralAngle += def.AngularSpeedDeg != 0f
                    ? def.AngularSpeedDeg * Mathf.Max(0.02f, def.Interval)
                    : 15f;
        }

        float ShotAngle(int index, int count)
        {
            switch (def.Shape)
            {
                case "fan":
                {
                    float spread = def.SpreadDeg > 0f ? def.SpreadDeg : 60f;
                    float step = count > 1 ? spread / (count - 1) : 0f;
                    return patternAngle - spread * 0.5f + step * index;
                }
                case "line":
                    return patternAngle;
                case "spiral":
                {
                    float arm = def.SpreadDeg > 0f ? def.SpreadDeg : 360f / count;
                    return spiralAngle + arm * index;
                }
                case "ring":
                default:
                    return patternAngle + index * 360f / count;
            }
        }
    }
}