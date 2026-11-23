using System.Collections;
using System.Collections.Generic;
using Game.Net;
using Game.Player;
using Game.Proto;
using Game.Snapshot;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Combat
{
    // 与后端 internal/game/systems/weapons.go 的 WeaponTable 一一对应。
    // 数值写进协议，只能追加不能改号：本地开火预测用的参数必须和后端一致，
    // 否则预测子弹的速度、寿命、冷却都与权威模拟对不上。
    public struct PredictedWeapon
    {
        public uint Id;
        public string Name;
        public float Cooldown;
        public bool AutoFire;
        public float BulletSpeed;
        public float BulletLife;
        public float BulletRadius;
        public uint Damage;
    }

    public static class WeaponTable
    {
        public const uint Rifle = 1;
        public const uint HandCannon = 2;
        public const uint Default = Rifle;

        public static bool TryGet(uint id, out PredictedWeapon weapon)
        {
            switch (id)
            {
                case Rifle:
                    weapon = new PredictedWeapon
                    {
                        Id = Rifle,
                        Name = "rifle",
                        Cooldown = 0.15f,
                        AutoFire = true,
                        BulletSpeed = 20f,
                        BulletLife = 1.2f,
                        BulletRadius = 0.12f,
                        Damage = 10,
                    };
                    return true;

                case HandCannon:
                    weapon = new PredictedWeapon
                    {
                        Id = HandCannon,
                        Name = "hand_cannon",
                        Cooldown = 0.6f,
                        AutoFire = false,
                        BulletSpeed = 26f,
                        BulletLife = 1.1f,
                        BulletRadius = 0.18f,
                        Damage = 32,
                    };
                    return true;

                default:
                    weapon = default;
                    return false;
            }
        }
    }

    public class FirePrediction : MonoBehaviour
    {
        const int MaxCatchUpShots = 8;
        const float MuzzleOffset = 0.5f;

        [SerializeField] GameObject muzzlePrefab;
        [SerializeField] Transform muzzlePoint;
        [SerializeField] Transform localPlayer;
        [SerializeField] GameObject hitPrefab;

        [SerializeField] bool predictBullets = true;
        [SerializeField] bool showDamageNumbers = true;
        [SerializeField] bool rejectCancelsPrediction = true;
        [SerializeField] float directionMatchDot = 0.7f;

        [SerializeField] uint fallbackTickRate = 20;
        [SerializeField] float muzzleLifetime = 0.2f;
        [SerializeField] float confirmWindow = 1.5f;
        [SerializeField] float hitFxLifetime = 0.3f;
        [SerializeField] float damageRiseSpeed = 1.2f;
        [SerializeField] float damageLifetime = 0.7f;
        [SerializeField] Color hitColor = new(1f, 0.85f, 0.3f, 1f);
        [SerializeField] Color criticalColor = new(1f, 0.4f, 0.2f, 1f);
        [SerializeField] Color muzzleColor = new(1f, 0.9f, 0.35f, 1f);

        class PendingShot
        {
            public uint Seq;
            public uint BulletId;
            public GameObject MuzzleFx;
            public Vector3 Origin;
            public Vector3 Dir;
            public float MuzzleFxDeadline;
            public float Deadline;
        }

        readonly Dictionary<uint, PendingShot> pendingFx = new();
        readonly List<uint> confirmOrder = new();
        readonly List<uint> expired = new();

        uint seq;
        uint weaponId = WeaponTable.Default;
        bool attackHeld;
        bool prevAttackHeld;
        float fireCooldown;
        float tickAccumulator;
        IntentSender sender;
        Material hitMaterial;
        Material muzzleMaterial;
        Font damageFont;
        bool fontWarned;

        public int PendingCount => pendingFx.Count;

        void Awake()
        {
            sender = FindObjectOfType<IntentSender>();

            if (localPlayer == null)
            {
                var controller = FindObjectOfType<PlayerController>();
                if (controller != null) localPlayer = controller.transform;
            }

            if (muzzlePoint == null && localPlayer != null)
            {
                var go = new GameObject("MuzzlePoint");
                go.transform.SetParent(localPlayer, false);
                go.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                muzzlePoint = go.transform;
            }

            if (muzzlePoint == null) muzzlePoint = transform;
        }

        void OnEnable() => MsgDispatcher.Register<S2CHitApply>(OnHitApply);

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CHitApply>(OnHitApply);
            ClearPending(true);
        }

        void OnDestroy()
        {
            if (hitMaterial != null) Destroy(hitMaterial);
            if (muzzleMaterial != null) Destroy(muzzleMaterial);
        }

        public void SetWeapon(uint id)
        {
            if (id == 0) return;
            weaponId = id;
        }

        public void SetAttackHeld(bool held)
        {
            attackHeld = held;
        }

        // 开火预测按固定 tick 推进，与后端 room.fire() 的调用节奏一致：
        // 冷却每 tick 递减（允许到 -dt，保留「欠了几发」），按住连发/按下单发
        // 的判定方式、补发上限（8 发）都与 systems/fire.go 相同。
        void TickPrediction()
        {
            uint rate = KcpNetClient.Instance != null ? KcpNetClient.Instance.TickRate : 0;
            if (rate == 0) rate = fallbackTickRate;
            if (rate == 0) rate = 20;

            float dt = 1f / rate;
            tickAccumulator += Time.deltaTime;
            if (tickAccumulator > dt * MaxCatchUpShots) tickAccumulator = dt * MaxCatchUpShots;

            while (tickAccumulator >= dt)
            {
                tickAccumulator -= dt;
                TickFire(dt);
            }
        }

        void TickFire(float dt)
        {
            if (!WeaponTable.TryGet(weaponId, out var wep)) return;
            if (wep.Cooldown <= 0f) return;

            fireCooldown -= dt;
            if (fireCooldown < -dt) fireCooldown = -dt;

            bool prevHeld = prevAttackHeld;
            prevAttackHeld = attackHeld;

            if (!attackHeld) return;

            if (GameRoot.Instance != null && GameRoot.Instance.State != GameRoot.GameState.InGame)
                return;

            if (!wep.AutoFire && prevHeld) return;

            float yaw = sender != null
                ? sender.LatestInput.yaw
                : localPlayer != null ? localPlayer.eulerAngles.y : transform.eulerAngles.y;
            float aimRad = yaw * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(aimRad), 0f, Mathf.Cos(aimRad));

            int shots = 0;
            while (fireCooldown <= 0f && shots < MaxCatchUpShots)
            {
                SpawnShot(wep, dir);
                fireCooldown += wep.Cooldown;
                shots++;
            }
        }

        void SpawnShot(PredictedWeapon wep, Vector3 dir)
        {
            Vector3 origin = localPlayer != null
                ? new Vector3(localPlayer.position.x, muzzlePoint.position.y, localPlayer.position.z) + dir * MuzzleOffset
                : muzzlePoint.position;

            var shot = new PendingShot
            {
                Seq = ++seq,
                Origin = origin,
                Dir = dir,
                MuzzleFxDeadline = Time.time + muzzleLifetime,
                Deadline = Time.time + confirmWindow,
            };

            if (muzzlePrefab != null)
                shot.MuzzleFx = Instantiate(muzzlePrefab, origin, Quaternion.LookRotation(dir));
            else
                shot.MuzzleFx = CreateMuzzleFx(origin, dir);

            if (predictBullets && BulletPool.Instance != null)
            {
                shot.BulletId = BulletPool.Instance.Spawn(new BulletPool.SpawnParams
                {
                    Position = origin,
                    Direction = dir,
                    Speed = wep.BulletSpeed,
                    Lifetime = wep.BulletLife,
                    Radius = wep.BulletRadius,
                });
            }

            pendingFx[shot.Seq] = shot;
            confirmOrder.Add(shot.Seq);
        }

        void Update()
        {
            TickPrediction();
            UpdatePendingFx();
        }

        void UpdatePendingFx()
        {
            if (pendingFx.Count == 0) return;

            float now = Time.time;
            expired.Clear();

            foreach (var pair in pendingFx)
            {
                var shot = pair.Value;

                if (shot.MuzzleFx != null && now >= shot.MuzzleFxDeadline)
                {
                    Destroy(shot.MuzzleFx);
                    shot.MuzzleFx = null;
                }

                if (now >= shot.Deadline) expired.Add(pair.Key);
            }

            foreach (uint key in expired)
            {
                if (!pendingFx.TryGetValue(key, out var shot)) continue;

                pendingFx.Remove(key);
                confirmOrder.Remove(key);

                if (shot.MuzzleFx != null) Destroy(shot.MuzzleFx);
            }
        }

        void OnHitApply(S2CHitApply msg)
        {
            ulong self = GameRoot.Instance != null ? GameRoot.Instance.LocalEntityId : 0;
            bool mine = self != 0 && msg.AttackerEntityId == self;

            if (!msg.HasVictimEntityId || msg.VictimEntityId == 0)
            {
                if (mine && rejectCancelsPrediction) ResolveOldest();
                return;
            }

            bool hasVictimPoint = TryResolveEntityPoint(msg.VictimEntityId, out var victimPoint);

            if (mine)
            {
                if (!hasVictimPoint || !TryResolveByDirection(victimPoint))
                    ResolveOldest();
            }

            Vector3 point = hasVictimPoint ? victimPoint : FallbackPoint();
            PlayHitFx(point);

            if (showDamageNumbers)
                ShowDamage(point, msg.DamageDealt, msg.Critical);
        }

        void ResolveOldest()
        {
            if (confirmOrder.Count == 0) return;
            Resolve(confirmOrder[0]);
        }

        bool TryResolveByDirection(Vector3 hitPoint)
        {
            uint bestKey = 0;
            float bestDot = directionMatchDot;

            for (int i = 0; i < confirmOrder.Count; i++)
            {
                uint key = confirmOrder[i];
                if (!pendingFx.TryGetValue(key, out var shot)) continue;

                Vector3 toHit = hitPoint - shot.Origin;
                float sqr = toHit.sqrMagnitude;
                if (sqr < 0.0001f) continue;

                float dot = Vector3.Dot(toHit / Mathf.Sqrt(sqr), shot.Dir);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestKey = key;
                }
            }

            if (bestKey == 0) return false;

            Resolve(bestKey);
            return true;
        }

        void Resolve(uint key)
        {
            confirmOrder.Remove(key);

            if (!pendingFx.TryGetValue(key, out var shot)) return;
            pendingFx.Remove(key);

            if (shot.BulletId != 0 && BulletPool.Instance != null)
                BulletPool.Instance.Kill(shot.BulletId);

            if (shot.MuzzleFx != null) Destroy(shot.MuzzleFx);
        }

        public void ClearPending(bool destroyBullets)
        {
            foreach (var shot in pendingFx.Values)
            {
                if (shot.MuzzleFx != null) Destroy(shot.MuzzleFx);

                if (destroyBullets && shot.BulletId != 0 && BulletPool.Instance != null)
                    BulletPool.Instance.Kill(shot.BulletId);
            }

            pendingFx.Clear();
            confirmOrder.Clear();
        }

        bool TryResolveEntityPoint(ulong victimId, out Vector3 point)
        {
            if (GameRoot.Instance != null && victimId == GameRoot.Instance.LocalEntityId && localPlayer != null)
            {
                point = localPlayer.position + Vector3.up * 0.9f;
                return true;
            }

            if (SnapshotDirector.Instance != null &&
                SnapshotDirector.Instance.TryGetEntityPosition(victimId, out var remote))
            {
                point = remote + Vector3.up * 0.9f;
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        Vector3 FallbackPoint()
        {
            if (confirmOrder.Count > 0 &&
                pendingFx.TryGetValue(confirmOrder[^1], out var last))
                return last.Origin + last.Dir * 6f;

            return muzzlePoint.position + muzzlePoint.forward * 6f;
        }

        void PlayHitFx(Vector3 point)
        {
            if (hitPrefab != null)
            {
                var fx = Instantiate(hitPrefab, point, Quaternion.identity);
                Destroy(fx, hitFxLifetime);
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "HitFx";
            go.transform.position = point;
            go.transform.localScale = Vector3.one * 0.35f;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var renderer = go.GetComponent<MeshRenderer>();
            var mat = GetHitMaterial();
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            Destroy(go, hitFxLifetime);
        }

        Material GetHitMaterial()
        {
            if (hitMaterial != null) return hitMaterial;

            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            hitMaterial = new Material(shader);
            if (hitMaterial.HasProperty("_Color"))
                hitMaterial.SetColor("_Color", hitColor);

            return hitMaterial;
        }

        GameObject CreateMuzzleFx(Vector3 origin, Vector3 dir)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MuzzleFx";
            go.transform.position = origin + dir * 0.2f;
            go.transform.localScale = Vector3.one * 0.22f;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var renderer = go.GetComponent<MeshRenderer>();
            var mat = GetMuzzleMaterial();
            if (renderer != null && mat != null)
            {
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            return go;
        }

        Material GetMuzzleMaterial()
        {
            if (muzzleMaterial != null) return muzzleMaterial;

            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            muzzleMaterial = new Material(shader);
            if (muzzleMaterial.HasProperty("_Color"))
                muzzleMaterial.SetColor("_Color", muzzleColor);

            return muzzleMaterial;
        }

        void ShowDamage(Vector3 point, uint damage, bool critical)
        {
            var font = GetFont();
            if (font == null) return;

            var go = new GameObject("DamageNumber");
            go.transform.position = point + Vector3.up * 0.2f;

            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.06f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = critical ? criticalColor : Color.white;
            text.text = critical ? damage + "!" : damage.ToString();

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;

            StartCoroutine(FloatDamage(go, text, go.transform.position));
        }

        IEnumerator FloatDamage(GameObject go, TextMesh text, Vector3 start)
        {
            var cam = Camera.main;
            float t = 0f;

            while (t < damageLifetime && go != null)
            {
                t += Time.deltaTime;
                go.transform.position = start + Vector3.up * (t * damageRiseSpeed);

                if (cam != null)
                {
                    Vector3 toCam = go.transform.position - cam.transform.position;
                    if (toCam.sqrMagnitude > 0.0001f)
                        go.transform.rotation = Quaternion.LookRotation(toCam);
                }

                var c = text.color;
                c.a = Mathf.Clamp01(1f - t / damageLifetime);
                text.color = c;

                yield return null;
            }

            if (go != null) Destroy(go);
        }

        Font GetFont()
        {
            if (damageFont != null) return damageFont;

            try { damageFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (damageFont == null)
            {
                try { damageFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }

            if (damageFont == null && !fontWarned)
            {
                fontWarned = true;
                Debug.LogWarning("[FirePrediction] 找不到内置字体，伤害数字不可用");
            }

            return damageFont;
        }
    }
}