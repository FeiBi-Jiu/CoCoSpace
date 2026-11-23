using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Game.Table;
using Google.Protobuf;
using UnityEngine;

namespace Game.Combat
{
    public class ElementFxController : MonoBehaviour
    {
        [Header("消息 ID（后端 proto 落地后可替换为 MsgId 枚举）")]
        [SerializeField] int attachMsgId = 2025;
        [SerializeField] int comboMsgId = 2026;

        [Header("特效")]
        [SerializeField] Transform fxRoot;
        [SerializeField] float defaultAttachDuration = 3f;
        [SerializeField] float attachHeight = 0.9f;
        [SerializeField] float comboHeight = 0.7f;
        [SerializeField] float comboFallbackLifetime = 1.5f;
        [SerializeField] bool tintEntity = true;

        public static ElementFxController Instance { get; private set; }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly Dictionary<int, EntityFxState> states = new();
        readonly List<int> staleStates = new();
        readonly List<uint> expiredElements = new();
        readonly Dictionary<string, GameObject> prefabCache = new();
        readonly HashSet<string> missingPaths = new();

        static Material fallbackParticleMaterial;

        class AttachEntry
        {
            public uint ElementId;
            public float ExpireAt;
            public GameObject Fx;
        }

        class EntityFxState
        {
            public Transform Target;
            public readonly Dictionary<uint, AttachEntry> Attaches = new();
            public readonly List<Renderer> Renderers = new();
            public readonly Dictionary<Renderer, MaterialPropertyBlock> OriginalBlocks = new();
            public uint TintElement;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectOfType<ElementFxController>() != null) return;

            var go = new GameObject("ElementFxController");
            DontDestroyOnLoad(go);
            go.AddComponent<ElementFxController>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (fxRoot == null)
            {
                var existing = transform.Find("FxRoot");
                if (existing != null)
                {
                    fxRoot = existing;
                }
                else
                {
                    var root = new GameObject("FxRoot");
                    root.transform.SetParent(transform, false);
                    fxRoot = root.transform;
                }
            }
        }

        void OnEnable()
        {
            MsgDispatcher.RegisterRaw((MsgId)attachMsgId, OnAttachMsg);
            MsgDispatcher.RegisterRaw((MsgId)comboMsgId, OnComboMsg);
        }

        void OnDisable()
        {
            MsgDispatcher.UnregisterRaw((MsgId)attachMsgId);
            MsgDispatcher.UnregisterRaw((MsgId)comboMsgId);
            ClearAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (states.Count == 0) return;

            staleStates.Clear();

            foreach (var pair in states)
            {
                var state = pair.Value;

                if (state.Target == null)
                {
                    staleStates.Add(pair.Key);
                    continue;
                }

                expiredElements.Clear();
                foreach (var attach in state.Attaches)
                {
                    if (Time.time >= attach.Value.ExpireAt)
                        expiredElements.Add(attach.Key);
                }

                foreach (var elementId in expiredElements)
                {
                    var entry = state.Attaches[elementId];
                    if (entry.Fx != null) Destroy(entry.Fx);
                    state.Attaches.Remove(elementId);
                }

                if (state.Attaches.Count == 0)
                {
                    RestoreTint(state);
                    staleStates.Add(pair.Key);
                    continue;
                }

                if (!state.Attaches.ContainsKey(state.TintElement))
                    Retint(state);
            }

            foreach (var key in staleStates)
                states.Remove(key);
        }

        public bool PlayAttach(ulong entityId, uint elementId, float duration)
        {
            var target = ResolveEntityTransform(entityId);
            if (target == null)
            {
                Debug.LogWarning($"[ElementFx] 找不到实体 entity_id={entityId}，无法播放附着 element={elementId}");
                return false;
            }

            return PlayAttach(target, elementId, duration);
        }

        public bool PlayAttach(Transform target, uint elementId, float duration)
        {
            if (target == null || elementId == 0) return false;

            if (duration <= 0f) duration = defaultAttachDuration;

            var state = GetOrCreateState(target);
            if (state == null) return false;

            Color color = ElementTable.GetColor(elementId);
            GameObject fx = CreateAttachFx(elementId, color, target);

            if (state.Attaches.TryGetValue(elementId, out var existing))
            {
                if (existing.Fx != null) Destroy(existing.Fx);
                existing.ExpireAt = Time.time + duration;
                existing.Fx = fx;
            }
            else
            {
                state.Attaches[elementId] = new AttachEntry
                {
                    ElementId = elementId,
                    ExpireAt = Time.time + duration,
                    Fx = fx,
                };
            }

            ApplyTint(state, elementId, color);
            Debug.Log($"[ElementFx] attach element={elementId} entity={target.name} duration={duration:0.0}s");
            return true;
        }

        public bool PlayCombo(ulong entityId, uint elementA, uint elementB)
        {
            var target = ResolveEntityTransform(entityId);
            if (target == null)
            {
                Debug.LogWarning($"[ElementFx] 找不到实体 entity_id={entityId}，无法播放组合 {elementA}+{elementB}");
                return false;
            }

            return PlayCombo(target, elementA, elementB);
        }

        public bool PlayCombo(Transform target, uint elementA, uint elementB)
        {
            if (target == null || elementA == 0 || elementB == 0) return false;

            string path = ElementTable.GetComboFxPath(elementA, elementB);
            GameObject prefab = ResolvePrefab(path);
            Color colorA = ElementTable.GetColor(elementA);
            Color colorB = ElementTable.GetColor(elementB);
            Color blended = Color.Lerp(colorA, colorB, 0.5f);

            Vector3 position = target.position + Vector3.up * comboHeight;

            if (prefab != null)
            {
                var fx = Instantiate(prefab, fxRoot != null ? fxRoot : transform);
                fx.transform.SetPositionAndRotation(position, Quaternion.identity);
                EnsureFxMaterial(fx);
                TintFx(fx, blended);
                PlayParticles(fx);
                Destroy(fx, ResolveLifetime(prefab));
            }
            else
            {
                var fx = CreateComboFallback(colorA, colorB, position);
                Destroy(fx, comboFallbackLifetime);
            }

            Debug.Log($"[ElementFx] combo {elementA}+{elementB} at {target.name} path='{path}'");
            return true;
        }

        Transform ResolveEntityTransform(ulong entityId)
        {
            var root = GameRoot.Instance;
            return root != null ? root.GetEntityTransform(entityId) : null;
        }

        EntityFxState GetOrCreateState(Transform target)
        {
            int key = target.GetInstanceID();
            if (states.TryGetValue(key, out var state) && state.Target == target)
                return state;

            state = new EntityFxState { Target = target };
            CollectRenderers(state);
            states[key] = state;
            return state;
        }

        void CollectRenderers(EntityFxState state)
        {
            state.Renderers.Clear();
            state.OriginalBlocks.Clear();

            foreach (var renderer in state.Target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                state.OriginalBlocks[renderer] = block;
                state.Renderers.Add(renderer);
            }
        }

        void ApplyTint(EntityFxState state, uint elementId, Color color)
        {
            if (!tintEntity) return;

            if (state.Renderers.Count == 0)
                CollectRenderers(state);

            state.TintElement = elementId;

            foreach (var renderer in state.Renderers)
            {
                if (renderer == null) continue;

                var block = new MaterialPropertyBlock();
                block.SetColor(ColorId, color);
                block.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(block);
            }
        }

        void Retint(EntityFxState state)
        {
            uint next = 0;
            float latest = float.MinValue;

            foreach (var pair in state.Attaches)
            {
                if (pair.Value.ExpireAt <= latest) continue;
                latest = pair.Value.ExpireAt;
                next = pair.Key;
            }

            if (next != 0)
                ApplyTint(state, next, ElementTable.GetColor(next));
        }

        void RestoreTint(EntityFxState state)
        {
            if (!tintEntity) return;

            foreach (var pair in state.OriginalBlocks)
            {
                if (pair.Key == null) continue;
                pair.Key.SetPropertyBlock(pair.Value);
            }

            state.OriginalBlocks.Clear();
            state.Renderers.Clear();
            state.TintElement = 0;
        }

        GameObject CreateAttachFx(uint elementId, Color color, Transform target)
        {
            GameObject prefab = ElementTable.GetAttachFx(elementId);
            if (prefab != null)
            {
                var fx = Instantiate(prefab, target);
                fx.transform.localPosition = Vector3.up * attachHeight;
                fx.transform.localRotation = Quaternion.identity;
                fx.transform.localScale = Vector3.one;
                EnsureFxMaterial(fx);
                TintFx(fx, color);
                PlayParticles(fx);
                return fx;
            }

            string path = ElementTable.GetAttachFxPath(elementId);
            if (!string.IsNullOrEmpty(path)) LogMissingPath(path);

            var fallback = CreateAttachFallback(color);
            fallback.transform.SetParent(target, false);
            fallback.transform.localPosition = Vector3.up * attachHeight;
            return fallback;
        }

        static GameObject CreateAttachFallback(Color color)
        {
            var go = new GameObject("ElementAttachFx");

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.8f;
            main.startSpeed = 0.35f;
            main.startSize = 0.28f;
            main.startColor = color;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 24f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f;
            shape.radiusThickness = 0f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetFallbackParticleMaterial();

            ps.Play();
            return go;
        }

        static GameObject CreateComboFallback(Color colorA, Color colorB, Vector3 position)
        {
            var go = new GameObject("ElementComboFx");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)40) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            shape.radiusThickness = 0f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetFallbackParticleMaterial();

            ps.Play();
            return go;
        }

        static Material GetFallbackParticleMaterial()
        {
            if (fallbackParticleMaterial != null) return fallbackParticleMaterial;

            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            fallbackParticleMaterial = new Material(shader) { name = "ElementFxParticleMat" };
            if (fallbackParticleMaterial.HasProperty(ColorId))
                fallbackParticleMaterial.SetColor(ColorId, Color.white);
            if (fallbackParticleMaterial.HasProperty(BaseColorId))
                fallbackParticleMaterial.SetColor(BaseColorId, Color.white);

            return fallbackParticleMaterial;
        }

        static void EnsureFxMaterial(GameObject fx)
        {
            if (fx == null) return;

            foreach (var renderer in fx.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.sharedMaterial != null) continue;
                renderer.material = GetFallbackParticleMaterial();
            }
        }

        static void TintFx(GameObject fx, Color color)
        {
            if (fx == null) return;

            foreach (var renderer in fx.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(ColorId, color);
                block.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(block);
            }
        }

        static void PlayParticles(GameObject fx)
        {
            if (fx == null) return;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(true);
        }

        GameObject ResolvePrefab(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (prefabCache.TryGetValue(path, out var cached)) return cached;

            var prefab = Resources.Load<GameObject>(path);
            prefabCache[path] = prefab;

            if (prefab == null) LogMissingPath(path);
            return prefab;
        }

        float ResolveLifetime(GameObject prefab)
        {
            float max = 0f;

            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (main.loop) continue;

                float value = main.duration + main.startLifetime.constantMax;
                if (value > max) max = value;
            }

            return max > 0f ? max : comboFallbackLifetime;
        }

        void LogMissingPath(string path)
        {
            if (!missingPaths.Add(path)) return;
            Debug.LogWarning($"[ElementFx] Resources 找不到特效 {path}，使用占位粒子");
        }

        void ClearAll()
        {
            foreach (var state in states.Values)
            {
                foreach (var entry in state.Attaches)
                    if (entry.Value.Fx != null) Destroy(entry.Value.Fx);

                RestoreTint(state);
            }

            states.Clear();
        }

        void OnAttachMsg(ByteString payload)
        {
            if (!TryParseAttach(payload, out var msg)) return;
            PlayAttach(msg.EntityId, msg.ElementId, msg.Duration);
        }

        void OnComboMsg(ByteString payload)
        {
            if (!TryParseCombo(payload, out var msg)) return;
            PlayCombo(msg.EntityId, msg.ElementA, msg.ElementB);
        }

        struct AttachMsg
        {
            public ulong EntityId;
            public uint ElementId;
            public float Duration;
        }

        struct ComboMsg
        {
            public ulong EntityId;
            public uint ElementA;
            public uint ElementB;
        }

        static bool TryParseAttach(ByteString payload, out AttachMsg msg)
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
                            msg.EntityId = input.ReadUInt64();
                            break;
                        case 16:
                            msg.ElementId = input.ReadUInt32();
                            break;
                        case 24:
                            msg.Duration = input.ReadUInt32();
                            break;
                        case 29:
                            msg.Duration = input.ReadFloat();
                            break;
                        case 32:
                            msg.Duration = input.ReadUInt32() / 1000f;
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

            return msg.EntityId != 0 && msg.ElementId != 0;
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
                        case 8:
                            msg.EntityId = input.ReadUInt64();
                            break;
                        case 16:
                            msg.ElementA = input.ReadUInt32();
                            break;
                        case 24:
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

            return msg.EntityId != 0 && msg.ElementA != 0 && msg.ElementB != 0;
        }
    }
}