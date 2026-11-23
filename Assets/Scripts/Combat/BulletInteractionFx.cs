using System.Collections;
using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Game.Table;
using Google.Protobuf;
using UnityEngine;

namespace Game.Combat
{
    public class BulletInteractionFx : MonoBehaviour
    {
        const MsgId ReflectMsgId = (MsgId)2023;
        const MsgId ComboMsgId = (MsgId)2024;

        [SerializeField] GameObject reflectFxPrefab;
        [SerializeField] string reflectFxPath = "Fx/BulletReflect";
        [SerializeField] Transform fxRoot;
        [SerializeField] float defaultFxLifetime = 1.2f;

        readonly Dictionary<GameObject, Stack<GameObject>> pools = new();
        readonly Dictionary<GameObject, float> lifetimes = new();
        readonly Dictionary<string, GameObject> prefabCache = new();
        readonly Dictionary<GameObject, GameObject> sources = new();
        readonly HashSet<GameObject> active = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectOfType<BulletInteractionFx>() != null) return;

            var go = new GameObject("BulletInteractionFx");
            DontDestroyOnLoad(go);
            go.AddComponent<BulletInteractionFx>();
        }

        void OnEnable()
        {
            MsgDispatcher.RegisterRaw(ReflectMsgId, OnReflect);
            MsgDispatcher.RegisterRaw(ComboMsgId, OnCombo);
        }

        void OnDisable()
        {
            MsgDispatcher.UnregisterRaw(ReflectMsgId);
            MsgDispatcher.UnregisterRaw(ComboMsgId);

            StopAllCoroutines();
            foreach (var instance in active)
            {
                if (sources.TryGetValue(instance, out var prefab))
                    Recycle(instance, prefab);
                else if (instance != null)
                    instance.SetActive(false);
            }
            active.Clear();
            sources.Clear();
        }

        void OnReflect(ByteString payload)
        {
            if (!TryParseReflect(payload, out var msg)) return;

            var pool = BulletPool.Instance;
            if (pool == null) return;

            if (!pool.TryRedirect(msg.BulletId, msg.NewDir, out Vector3 position))
            {
                Debug.LogWarning($"[BulletInteractionFx] 反射目标不存在 bullet_id={msg.BulletId}");
                return;
            }

            var prefab = ResolveReflectPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("[BulletInteractionFx] 缺少反射特效 prefab");
                return;
            }

            Quaternion rotation = msg.NewDir.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(msg.NewDir)
                : Quaternion.identity;
            Play(prefab, position, rotation);
        }

        void OnCombo(ByteString payload)
        {
            if (!TryParseCombo(payload, out var msg)) return;

            var prefab = ResolveComboPrefab(msg.ElementA, msg.ElementB);
            if (prefab == null)
            {
                Debug.LogWarning($"[BulletInteractionFx] 缺少交汇特效 {msg.ElementA}+{msg.ElementB}");
                return;
            }

            Play(prefab, msg.Position, Quaternion.identity);
        }

        GameObject ResolveReflectPrefab()
        {
            if (reflectFxPrefab != null) return reflectFxPrefab;
            return ResolvePrefab(reflectFxPath);
        }

        GameObject ResolveComboPrefab(uint a, uint b)
        {
            string path = ElementTable.GetComboFxPath(a, b);
            return string.IsNullOrEmpty(path) ? null : ResolvePrefab(path);
        }

        GameObject ResolvePrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (prefabCache.TryGetValue(path, out var prefab)) return prefab;

            prefab = Resources.Load<GameObject>(path);
            prefabCache[path] = prefab;
            if (prefab == null)
                Debug.LogWarning($"[BulletInteractionFx] Resources 找不到特效 {path}");
            return prefab;
        }

        void Play(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;

            var instance = Rent(prefab);
            if (instance == null) return;

            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(true);

            active.Add(instance);
            sources[instance] = prefab;
            StartCoroutine(RecycleAfter(instance, prefab, ResolveLifetime(prefab)));
        }

        GameObject Rent(GameObject prefab)
        {
            if (pools.TryGetValue(prefab, out var stack))
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (pooled != null) return pooled;
                }
            }

            Transform parent = fxRoot != null ? fxRoot : transform;
            return Instantiate(prefab, parent);
        }

        IEnumerator RecycleAfter(GameObject instance, GameObject prefab, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            active.Remove(instance);
            Recycle(instance, prefab);
        }

        void Recycle(GameObject instance, GameObject prefab)
        {
            if (instance == null)
            {
                sources.Remove(instance);
                return;
            }

            sources.Remove(instance);
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            instance.SetActive(false);

            if (!pools.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>();
                pools[prefab] = stack;
            }
            stack.Push(instance);
        }

        float ResolveLifetime(GameObject prefab)
        {
            if (lifetimes.TryGetValue(prefab, out var cached)) return cached;

            float max = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (main.loop) continue;

                float value = main.duration + main.startLifetime.constantMax;
                if (value > max) max = value;
            }

            if (max <= 0f) max = defaultFxLifetime;
            lifetimes[prefab] = max;
            return max;
        }

        struct ReflectMsg
        {
            public uint BulletId;
            public Vector3 NewDir;
            public ulong ReflectBy;
        }

        struct ComboMsg
        {
            public Vector3 Position;
            public uint ElementA;
            public uint ElementB;
        }

        static bool TryParseReflect(ByteString payload, out ReflectMsg msg)
        {
            msg = default;
            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8:
                            msg.BulletId = input.ReadUInt32();
                            break;
                        case 21:
                            msg.NewDir.x = input.ReadFloat();
                            break;
                        case 29:
                            msg.NewDir.y = input.ReadFloat();
                            break;
                        case 37:
                            msg.NewDir.z = input.ReadFloat();
                            break;
                        case 40:
                            msg.ReflectBy = input.ReadUInt64();
                            break;
                        default:
                            input.SkipLastField();
                            break;
                    }
                }
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }

            return msg.BulletId != 0;
        }

        static bool TryParseCombo(ByteString payload, out ComboMsg msg)
        {
            msg = default;
            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 13:
                            msg.Position.x = input.ReadFloat();
                            break;
                        case 21:
                            msg.Position.y = input.ReadFloat();
                            break;
                        case 29:
                            msg.Position.z = input.ReadFloat();
                            break;
                        case 32:
                            msg.ElementA = input.ReadUInt32();
                            break;
                        case 40:
                            msg.ElementB = input.ReadUInt32();
                            break;
                        default:
                            input.SkipLastField();
                            break;
                    }
                }
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }

            return true;
        }
    }
}