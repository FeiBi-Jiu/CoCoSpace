using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Game.Snapshot;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.UI;

namespace Game.World
{
    public class RageWarning : MonoBehaviour
    {
        [Header("消息 ID（后端 proto 落地后可替换为 MsgId 枚举）")]
        [SerializeField] int rageStateMsgId = 2023;
        [SerializeField] int rageEndMsgId = 2024;

        [Header("变色")]
        [SerializeField] Color level1Color = new Color(1f, 0.84f, 0.16f, 1f);
        [SerializeField] Color level2Color = new Color(1f, 0.18f, 0.15f, 1f);
        [SerializeField] float level1PulseSpeed = 1.4f;
        [SerializeField] float level2PulseSpeed = 4.5f;
        [SerializeField] float pulseDepth = 0.45f;

        [Header("横幅")]
        [SerializeField] string bannerTitle = "队伍分散！";
        [SerializeField] string bannerSubtitle = "距离惩罚 Lv2 · 怪物狂暴";
        [SerializeField] Color bannerBack = new Color(0.58f, 0.05f, 0.06f, 0.92f);
        [SerializeField] float bannerTop = 48f;

        [Header("音效")]
        [SerializeField] AudioClip warnClip;
        [SerializeField] AudioClip rageClip;
        [SerializeField] float sfxVolume = 0.85f;

        [Header("调试")]
        [SerializeField] bool debugHotkeys = true;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        class RageView
        {
            public ulong EntityId;
            public int Level;
            public bool Resolved;
            public float EnteredAt;
            public Transform Target;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly Dictionary<Renderer, MaterialPropertyBlock> Originals = new Dictionary<Renderer, MaterialPropertyBlock>();
        }

        readonly Dictionary<ulong, RageView> views = new Dictionary<ulong, RageView>();
        readonly List<ulong> stale = new List<ulong>();
        readonly List<ulong> endIds = new List<ulong>();
        MaterialPropertyBlock tintBlock;

        GameObject banner;
        RectTransform bannerRect;
        CanvasGroup bannerGroup;
        Text bannerLevelText;
        AudioSource source;
        float bannerShowAt = -1f;
        float nextResolve;

        static Font cachedFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectOfType<RageWarning>() != null) return;

            var go = new GameObject("RageWarning");
            DontDestroyOnLoad(go);
            go.AddComponent<RageWarning>();
        }

        void Awake()
        {
            tintBlock = new MaterialPropertyBlock();
            EnsureAudio();
            BuildBanner();
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CEnrage>(OnEnrage);
            MsgDispatcher.RegisterRaw((MsgId)rageStateMsgId, OnRageState);
            MsgDispatcher.RegisterRaw((MsgId)rageEndMsgId, OnRageEnd);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CEnrage>();
            MsgDispatcher.UnregisterRaw((MsgId)rageStateMsgId);
            MsgDispatcher.UnregisterRaw((MsgId)rageEndMsgId);
            ClearAll();
        }

        void Update()
        {
            TickDebug();
            TickViews();
            TickBanner();
        }

        public bool ApplyRageState(ulong entityId, int level)
        {
            if (level <= 0)
            {
                EndRage(entityId);
                return false;
            }

            if (!ApplyRage(entityId, level)) return false;

            if (level >= 2) PlayRageSfx();
            else PlayWarnSfx();

            RefreshBanner();
            return true;
        }

        public void EndRage(ulong entityId)
        {
            if (!views.TryGetValue(entityId, out var view)) return;

            Restore(view);
            views.Remove(entityId);
            RefreshBanner();
        }

        public void ClearAll()
        {
            foreach (var view in views.Values)
                Restore(view);

            views.Clear();
            HideBanner();
        }

        bool ApplyRage(ulong entityId, int level)
        {
            level = Mathf.Clamp(level, 1, 2);

            if (!views.TryGetValue(entityId, out var view))
            {
                view = new RageView { EntityId = entityId, Level = level, EnteredAt = Time.unscaledTime };
                views[entityId] = view;
                ResolveTarget(view);
                return true;
            }

            if (view.Level == level) return false;

            view.Level = level;
            view.EnteredAt = Time.unscaledTime;
            return true;
        }

        void OnEnrage(S2CEnrage msg)
        {
            if (msg.MonsterIds.Count == 0)
            {
                ClearAll();
                return;
            }

            bool changed = false;
            foreach (var id in msg.MonsterIds)
                changed |= ApplyRage(id, 2);

            if (!changed) return;

            PlayRageSfx();
            RefreshBanner();
        }

        void OnRageState(ByteString payload)
        {
            if (!TryParseRageState(payload, out ulong entityId, out int level)) return;
            ApplyRageState(entityId, level);
        }

        void OnRageEnd(ByteString payload)
        {
            endIds.Clear();
            if (!TryParseRageEnd(payload, endIds)) return;

            if (endIds.Count == 0)
            {
                ClearAll();
                return;
            }

            foreach (var id in endIds)
                EndRage(id);
        }

        void TickViews()
        {
            if (views.Count == 0) return;

            bool resolve = Time.unscaledTime >= nextResolve;
            if (resolve) nextResolve = Time.unscaledTime + 0.5f;

            stale.Clear();

            foreach (var pair in views)
            {
                var view = pair.Value;

                if (view.Target == null)
                {
                    if (view.Resolved)
                    {
                        stale.Add(pair.Key);
                        continue;
                    }

                    if (resolve) ResolveTarget(view);
                    continue;
                }

                if (view.Renderers.Count == 0) CollectRenderers(view);
                Tint(view, Time.unscaledTime);
            }

            if (stale.Count == 0) return;

            foreach (var id in stale)
                views.Remove(id);

            RefreshBanner();
        }

        void ResolveTarget(RageView view)
        {
            Transform target = null;

            var root = GameRoot.Instance;
            if (root != null) target = root.GetEntityTransform(view.EntityId);

            if (target == null && SnapshotDirector.Instance != null)
                target = SnapshotDirector.Instance.GetEntityTransform(view.EntityId);

            if (target == null) return;

            view.Target = target;
            view.Resolved = true;
            CollectRenderers(view);
        }

        void CollectRenderers(RageView view)
        {
            view.Renderers.Clear();
            view.Originals.Clear();

            if (view.Target == null) return;

            foreach (var renderer in view.Target.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                view.Originals[renderer] = block;
                view.Renderers.Add(renderer);
            }
        }

        void Tint(RageView view, float now)
        {
            if (view.Renderers.Count == 0) return;

            float speed = view.Level >= 2 ? level2PulseSpeed : level1PulseSpeed;
            float phase = Mathf.Sin((now - view.EnteredAt) * speed * Mathf.PI * 2f);
            float scale = 1f - pulseDepth * (0.5f - 0.5f * phase);

            Color baseColor = view.Level >= 2 ? level2Color : level1Color;
            Color tint = new Color(baseColor.r * scale, baseColor.g * scale, baseColor.b * scale, 1f);

            tintBlock.Clear();
            tintBlock.SetColor(ColorId, tint);
            tintBlock.SetColor(BaseColorId, tint);

            foreach (var renderer in view.Renderers)
            {
                if (renderer == null) continue;
                renderer.SetPropertyBlock(tintBlock);
            }
        }

        void Restore(RageView view)
        {
            foreach (var pair in view.Originals)
                if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value);

            view.Renderers.Clear();
            view.Originals.Clear();
        }

        void RefreshBanner()
        {
            bool raging = false;

            foreach (var view in views.Values)
            {
                if (view.Level < 2) continue;
                raging = true;
                break;
            }

            if (raging) ShowBanner();
            else HideBanner();
        }

        void ShowBanner()
        {
            if (banner == null || banner.activeSelf) return;

            banner.SetActive(true);
            bannerShowAt = Time.unscaledTime;
        }

        void HideBanner()
        {
            if (banner == null || !banner.activeSelf) return;

            banner.SetActive(false);
            bannerShowAt = -1f;
        }

        void TickBanner()
        {
            if (banner == null || !banner.activeSelf) return;

            float elapsed = Time.unscaledTime - bannerShowAt;
            float pop = Mathf.Clamp01(elapsed / 0.24f);

            float scale = Mathf.Lerp(0.90f, 1f, pop * pop);
            bannerRect.localScale = new Vector3(scale, scale, 1f);
            bannerRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(40f, -bannerTop, pop));

            bannerGroup.alpha = 0.78f + 0.22f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f));
        }

        void BuildBanner()
        {
            if (banner != null) return;

            var canvasGo = new GameObject("RageBannerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Font font = GetDefaultFont();

            banner = new GameObject("RageBanner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            banner.transform.SetParent(canvasGo.transform, false);

            bannerRect = banner.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.anchoredPosition = new Vector2(0f, -bannerTop);
            bannerRect.sizeDelta = new Vector2(760f, 104f);

            var image = banner.GetComponent<Image>();
            image.color = bannerBack;
            image.raycastTarget = false;

            bannerGroup = banner.GetComponent<CanvasGroup>();
            bannerGroup.interactable = false;
            bannerGroup.blocksRaycasts = false;

            var title = CreateText("Title", bannerTitle, 46, TextAnchor.MiddleCenter, font, banner.transform);
            PlaceStretch(title.rectTransform, 16f, 6f, 16f, 46f);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(1f, 0.95f, 0.90f, 1f);

            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);

            bannerLevelText = CreateText("Level", bannerSubtitle, 22, TextAnchor.MiddleCenter, font, banner.transform);
            PlaceStretch(bannerLevelText.rectTransform, 16f, 58f, 16f, 10f);
            bannerLevelText.color = new Color(1f, 0.86f, 0.45f, 1f);

            banner.SetActive(false);
        }

        void EnsureAudio()
        {
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;

            if (warnClip == null) warnClip = CreateWarnClip();
            if (rageClip == null) rageClip = CreateRageClip();
        }

        void PlayWarnSfx() => Play(warnClip, 0.75f);

        void PlayRageSfx() => Play(rageClip, 1f);

        void Play(AudioClip clip, float scale)
        {
            if (source == null || clip == null) return;
            source.PlayOneShot(clip, sfxVolume * scale);
        }

        static AudioClip CreateWarnClip()
        {
            const int rate = 44100;
            const float duration = 1.1f;
            int samples = Mathf.CeilToInt(rate * duration);
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float freq = Mathf.Lerp(190f, 130f, t / duration);
                float env = Mathf.Min(1f, t / 0.15f) * Mathf.Exp(-t * 1.5f);
                float wave = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * freq * t);
                data[i] = Mathf.Clamp(wave * env * 0.42f, -1f, 1f);
            }

            var clip = AudioClip.Create("rage_warn", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip CreateRageClip()
        {
            const int rate = 44100;
            const float duration = 1f;
            int samples = Mathf.CeilToInt(rate * duration);
            var data = new float[samples];
            var rng = new System.Random(2027);
            float phase = 0f;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float freq = Mathf.Lerp(680f, 120f, Mathf.Clamp01(t / 0.55f));
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Exp(-t * 2.4f);
                phase += 2f * Mathf.PI * freq / rate;
                float saw = Mathf.Repeat(phase / Mathf.PI, 2f) - 1f;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                data[i] = Mathf.Clamp((saw * 0.6f + noise * 0.35f) * env * 0.7f, -1f, 1f);
            }

            var clip = AudioClip.Create("rage_roar", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void TickDebug()
        {
            if (!debugHotkeys) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) DebugRage(1);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) DebugRage(2);
            else if (Input.GetKeyDown(KeyCode.Alpha0)) ClearAll();
        }

        void DebugRage(int level)
        {
            ulong entityId = FindNearestCombatEntity();
            if (entityId == 0) return;

            ApplyRageState(entityId, level);
        }

        static ulong FindNearestCombatEntity()
        {
            var cam = Camera.main;
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;

            RemoteEntityView nearest = null;
            float best = float.MaxValue;

            foreach (var view in FindObjectsOfType<RemoteEntityView>())
            {
                if (view == null) continue;

                var type = view.Type;
                if (type != EntityType.EntityMonster && type != EntityType.EntityElite && type != EntityType.EntityBoss)
                    continue;

                float dist = (view.transform.position - origin).sqrMagnitude;
                if (dist >= best) continue;

                best = dist;
                nearest = view;
            }

            return nearest != null ? nearest.EntityId : 0;
        }

        static Text CreateText(string name, string content, int size, TextAnchor anchor, Font font, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;
            return text;
        }

        static void PlaceStretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static Font GetDefaultFont()
        {
            if (cachedFont != null) return cachedFont;

            try { cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (cachedFont == null)
            {
                try { cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            if (cachedFont == null)
                cachedFont = Font.CreateDynamicFontFromOSFont("Arial", 24);

            return cachedFont;
        }

        static bool TryParseRageState(ByteString payload, out ulong entityId, out int level)
        {
            entityId = 0;
            level = 0;

            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8:
                            entityId = input.ReadUInt64();
                            break;
                        case 16:
                            level = (int)input.ReadUInt32();
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

            return entityId != 0;
        }

        static bool TryParseRageEnd(ByteString payload, List<ulong> ids)
        {
            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8:
                            ids.Add(input.ReadUInt64());
                            break;
                        case 10:
                            var packed = input.ReadBytes().CreateCodedInput();
                            while (!packed.IsAtEnd) ids.Add(packed.ReadUInt64());
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