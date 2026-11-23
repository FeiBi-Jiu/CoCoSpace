using System.Collections;
using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Game.Snapshot;
using Game.Table;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.UI;

namespace Game.World
{
    [DefaultExecutionOrder(100)]
    public class Boss1View : MonoBehaviour
    {
        [Header("消息 ID（后端 proto 落地后可替换为 MsgId 枚举）")]
        [SerializeField] int phaseMsgId = 2027;
        [SerializeField] int breakMsgId = 2028;
        [SerializeField] int ultMsgId = 2029;
        [SerializeField] int attachMsgId = 2025;

        [Header("HUD 引用（留空则运行时自动创建）")]
        [SerializeField] Image hpFill;
        [SerializeField] Text hpLabel;
        [SerializeField] Text phaseLabel;
        [SerializeField] Image phaseBadge;
        [SerializeField] RectTransform vulnRoot;
        [SerializeField] Image flashOverlay;

        [Header("蓄力条引用（留空则运行时自动创建）")]
        [SerializeField] GameObject chargeRoot;
        [SerializeField] Image chargeFill;
        [SerializeField] Image breakWindow;
        [SerializeField] Text chargeLabel;

        [Header("特效引用（留空则运行时自动创建）")]
        [SerializeField] GameObject shieldVfx;
        [SerializeField] GameObject shieldBreakFx;
        [SerializeField] GameObject phaseShiftFx;

        [Header("演出参数")]
        [SerializeField] float breakWindowStart = 0.35f;
        [SerializeField] float breakWindowEnd = 0.80f;
        [SerializeField] float chargeBarHeight = 2.8f;
        [SerializeField] float hudTop = 36f;
        [SerializeField] float shakeAngle = 2.4f;
        [SerializeField] float shakeDuration = 0.45f;
        [SerializeField] Color p1Color = new(0.72f, 0.86f, 1f, 1f);
        [SerializeField] Color p2Color = new(1f, 0.42f, 0.22f, 1f);
        [SerializeField] Color hpColorP1 = new(0.86f, 0.18f, 0.20f, 1f);
        [SerializeField] Color hpColorP2 = new(1f, 0.40f, 0.15f, 1f);
        [SerializeField] Color windowColor = new(1f, 0.80f, 0.20f, 0.35f);
        [SerializeField] Color windowActiveColor = new(1f, 0.95f, 0.55f, 0.85f);
        [SerializeField] Color vulnPulseColor = new(1f, 0.95f, 0.60f, 1f);

        [SerializeField] bool debugHotkeys = true;

        const uint OilElementId = 5;
        const uint FireElementId = 1;
        const float DefaultAttachDuration = 3f;

        static Font cachedFont;
        static Sprite cachedWhiteSprite;
        static Material cachedFxMaterial;

        class VulnIcon
        {
            public uint ElementId;
            public GameObject Root;
            public Image Bg;
            public Text Label;
            public float ExpireAt;
        }

        GameObject hudCanvasGo;
        GameObject hudRoot;
        Transform fxRoot;

        ulong bossEntityId;
        float bossHp;
        float bossHpMax;
        int phase = 1;
        Transform bossTransform;

        Coroutine chargeRoutine;
        float chargeProgress;

        readonly Dictionary<uint, VulnIcon> vulnIcons = new();

        Camera shakeCam;
        float shakeTime;
        float shakeTotal;
        float shakeStrength;

        Color flashColor;
        float flashTime;
        float flashTotal;

        void Awake()
        {
            EnsureHud();
            EnsureChargeBar();
            EnsureFx();
            ApplyPhaseVisual();
            SetHudVisible(false);
            if (chargeRoot != null) chargeRoot.SetActive(false);

            MsgDispatcher.RegisterRaw((MsgId)attachMsgId, OnAttachMsg);
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2C_Snapshot>(OnSnapshot);
            MsgDispatcher.Register<S2CBossCharge>(OnCharge);
            MsgDispatcher.Register<S2CArmorBreak>(OnArmorBreak);
            MsgDispatcher.RegisterRaw((MsgId)phaseMsgId, OnPhaseMsg);
            MsgDispatcher.RegisterRaw((MsgId)breakMsgId, OnBreakMsg);
            MsgDispatcher.RegisterRaw((MsgId)ultMsgId, OnUltMsg);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2C_Snapshot>(OnSnapshot);
            MsgDispatcher.Unregister<S2CBossCharge>(OnCharge);
            MsgDispatcher.Unregister<S2CArmorBreak>(OnArmorBreak);
            MsgDispatcher.UnregisterRaw((MsgId)phaseMsgId);
            MsgDispatcher.UnregisterRaw((MsgId)breakMsgId);
            MsgDispatcher.UnregisterRaw((MsgId)ultMsgId);

            if (chargeRoutine != null)
            {
                StopCoroutine(chargeRoutine);
                chargeRoutine = null;
            }
        }

        void LateUpdate()
        {
            ResolveBossTransform();
            FollowChargeBar();
            TickVulnerable();
            TickFlash();
            ApplyShake();
            TickDebug();
        }

        void OnSnapshot(S2C_Snapshot msg)
        {
            bool found = false;

            foreach (var e in msg.Entities)
            {
                if (e.Type != EntityType.EntityBoss) continue;

                found = true;
                bossEntityId = e.EntityId;
                bossHp = e.Hp;
                bossHpMax = e.HpMax;
                break;
            }

            if (!found)
            {
                HideChargeBar();
                ClearVulnerable();
                SetHudVisible(false);
                return;
            }

            SetHudVisible(true);
            UpdateHpVisual();
        }

        void OnCharge(S2CBossCharge msg)
        {
            float seconds = msg.ChargeMs > 0 ? msg.ChargeMs / 1000f : 3f;

            ShowChargeBar();

            if (chargeRoutine != null) StopCoroutine(chargeRoutine);
            chargeRoutine = StartCoroutine(ChargeRoutine(seconds));
        }

        IEnumerator ChargeRoutine(float seconds)
        {
            chargeProgress = 0f;
            float t = 0f;

            while (t < seconds)
            {
                t += Time.deltaTime;
                chargeProgress = Mathf.Clamp01(t / seconds);
                UpdateChargeVisual();
                yield return null;
            }

            chargeProgress = 1f;
            UpdateChargeVisual();
            chargeRoutine = null;
            HideChargeBar();
        }

        void OnArmorBreak(S2CArmorBreak msg) => PlayBreakShow();

        void OnPhaseMsg(ByteString payload)
        {
            if (!TryParsePhase(payload, out int newPhase)) return;
            PlayPhaseShow(newPhase);
        }

        void OnBreakMsg(ByteString payload)
        {
            if (TryParseEntityId(payload, out ulong entityId) && bossEntityId != 0 && entityId != bossEntityId)
                return;

            PlayBreakShow();
        }

        void OnUltMsg(ByteString payload)
        {
            if (chargeRoutine != null)
            {
                StopCoroutine(chargeRoutine);
                chargeRoutine = null;
            }

            HideChargeBar();
            Shake(shakeDuration * 2f, 2f);
            Flash(new Color(1f, 0.12f, 0.06f, 0.7f), 0.6f);
        }

        void OnAttachMsg(ByteString payload)
        {
            if (!isActiveAndEnabled) return;
            if (!TryParseAttach(payload, out var msg)) return;
            if (bossEntityId == 0 || msg.EntityId != bossEntityId) return;

            ShowVulnerable(msg.ElementId, msg.Duration);
        }

        void TickDebug()
        {
            if (!debugHotkeys) return;

            if (Input.GetKeyDown(KeyCode.Alpha1)) DebugCharge();
            else if (Input.GetKeyDown(KeyCode.Alpha2)) PlayBreakShow();
            else if (Input.GetKeyDown(KeyCode.Alpha3)) DebugAttach(OilElementId);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) DebugAttach(FireElementId);
            else if (Input.GetKeyDown(KeyCode.Alpha0)) DebugReset();
        }

        void DebugCharge()
        {
            DebugEnsureVisible();
            OnCharge(new S2CBossCharge { ChargeMs = 3000 });
        }

        void DebugAttach(uint elementId)
        {
            DebugEnsureVisible();
            ShowVulnerable(elementId, DefaultAttachDuration);
        }

        void DebugEnsureVisible()
        {
            if (bossHpMax <= 0f)
            {
                bossHpMax = 10000f;
                bossHp = 10000f;
            }

            SetHudVisible(true);
            UpdateHpVisual();
        }

        void DebugReset()
        {
            if (chargeRoutine != null)
            {
                StopCoroutine(chargeRoutine);
                chargeRoutine = null;
            }

            HideChargeBar();
            ClearVulnerable();
            PlayPhaseShow(1);
        }

        void PlayBreakShow()
        {
            if (chargeRoutine != null)
            {
                StopCoroutine(chargeRoutine);
                chargeRoutine = null;
            }

            HideChargeBar();
            PlayFxAtBoss(shieldBreakFx, 2f);
            Shake(shakeDuration, 1f);
            Flash(new Color(1f, 0.85f, 0.35f, 0.55f), 0.35f);
            PlayPhaseShow(2);
        }

        void PlayPhaseShow(int newPhase)
        {
            if (newPhase < 1) newPhase = 1;
            if (newPhase > 2) newPhase = 2;

            bool changed = newPhase != phase;
            phase = newPhase;
            ApplyPhaseVisual();

            if (!changed) return;

            if (phase >= 2)
            {
                PlayFxAtBoss(phaseShiftFx, 2.2f);
                Shake(shakeDuration * 1.4f, 1.2f);
                Flash(new Color(1f, 0.35f, 0.15f, 0.45f), 0.45f);
            }
            else
            {
                Flash(new Color(0.5f, 0.8f, 1f, 0.35f), 0.35f);
            }
        }

        void ApplyPhaseVisual()
        {
            if (phaseLabel != null)
            {
                phaseLabel.text = phase >= 2 ? "P2" : "P1";
                phaseLabel.color = phase >= 2 ? p2Color : p1Color;
            }

            if (phaseBadge != null)
                phaseBadge.color = phase >= 2
                    ? new Color(0.35f, 0.12f, 0.08f, 0.92f)
                    : new Color(0.10f, 0.16f, 0.26f, 0.92f);

            if (hpFill != null)
                hpFill.color = phase >= 2 ? hpColorP2 : hpColorP1;
        }

        void UpdateHpVisual()
        {
            float ratio = bossHpMax > 0f ? Mathf.Clamp01(bossHp / bossHpMax) : 0f;

            if (hpFill != null) hpFill.fillAmount = ratio;

            if (hpLabel != null)
            {
                hpLabel.text = bossHpMax > 0f
                    ? $"{Mathf.CeilToInt(bossHp)} / {Mathf.CeilToInt(bossHpMax)}"
                    : "BOSS";
            }
        }

        void ShowChargeBar()
        {
            if (chargeRoot != null) chargeRoot.SetActive(true);
            chargeProgress = 0f;
            UpdateChargeVisual();
            SpawnShieldFx();
        }

        const float HudWidth = 900f;
        const float HudHeight = 96f;
        const float BarHeight = 34f;
        const float VulnIconSize = 44f;
        const float VulnIconGap = 8f;
        const float ChargeCanvasWidth = 600f;
        const float ChargeCanvasHeight = 120f;
        const float ChargeFillHeight = 36f;

        GameObject activeShieldFx;
        Quaternion shakeCamBaseRot;
        readonly List<uint> expiredVuln = new();

        void HideChargeBar()
        {
            if (chargeRoot != null) chargeRoot.SetActive(false);

            if (activeShieldFx != null)
            {
                Destroy(activeShieldFx);
                activeShieldFx = null;
            }
        }

        void SpawnShieldFx()
        {
            if (activeShieldFx != null || shieldVfx == null) return;

            Vector3 pos = bossTransform != null ? bossTransform.position : transform.position;
            activeShieldFx = Instantiate(shieldVfx, pos, Quaternion.identity, fxRoot != null ? fxRoot : transform);
            activeShieldFx.SetActive(true);
            PlayParticles(activeShieldFx);
        }

        void UpdateChargeVisual()
        {
            if (chargeFill != null)
                chargeFill.fillAmount = Mathf.Clamp01(chargeProgress);

            if (breakWindow != null)
            {
                bool inWindow = chargeProgress >= breakWindowStart && chargeProgress <= breakWindowEnd;
                Color c = inWindow ? windowActiveColor : windowColor;
                if (inWindow)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 9f);
                    c.a = Mathf.Lerp(0.55f, 0.95f, pulse);
                }
                breakWindow.color = c;
            }

            if (chargeLabel != null)
                chargeLabel.text = $"蓄力 {Mathf.RoundToInt(Mathf.Clamp01(chargeProgress) * 100f)}%";
        }

        void FollowChargeBar()
        {
            Vector3 basePos = bossTransform != null ? bossTransform.position : transform.position;

            if (activeShieldFx != null)
                activeShieldFx.transform.position = basePos;

            if (chargeRoot == null || !chargeRoot.activeSelf) return;

            chargeRoot.transform.position = basePos + Vector3.up * chargeBarHeight;

            var cam = Camera.main;
            if (cam != null)
                chargeRoot.transform.rotation = cam.transform.rotation;
        }

        void ResolveBossTransform()
        {
            if (bossEntityId == 0)
            {
                bossTransform = null;
                return;
            }

            if (bossTransform != null) return;

            Transform t = null;
            var root = GameRoot.Instance;
            if (root != null) t = root.GetEntityTransform(bossEntityId);
            if (t == null && SnapshotDirector.Instance != null)
                t = SnapshotDirector.Instance.GetEntityTransform(bossEntityId);

            bossTransform = t;
        }

        void ShowVulnerable(uint elementId, float duration)
        {
            if (duration <= 0f) duration = DefaultAttachDuration;

            if (vulnIcons.TryGetValue(elementId, out var existing) && existing.Root != null)
            {
                existing.ExpireAt = Time.time + duration;
                LayoutVulnIcons();
                return;
            }

            Color color = ResolveElementColor(elementId);
            string label = ResolveElementLabel(elementId);

            var root = new GameObject($"Vuln_{elementId}", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(vulnRoot, false);

            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(VulnIconSize, VulnIconSize);

            var bg = root.GetComponent<Image>();
            bg.color = new Color(color.r, color.g, color.b, 0.92f);
            bg.raycastTarget = false;

            var outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            var text = CreateText("Label", label, 26, TextAnchor.MiddleCenter, GetDefaultFont(), root.transform);
            PlaceStretch(text.rectTransform, 0f, 0f, 0f, 0f);
            text.color = new Color(0.08f, 0.06f, 0.02f, 1f);
            text.fontStyle = FontStyle.Bold;

            vulnIcons[elementId] = new VulnIcon
            {
                ElementId = elementId,
                Root = root,
                Bg = bg,
                Label = text,
                ExpireAt = Time.time + duration,
            };

            LayoutVulnIcons();
        }

        void LayoutVulnIcons()
        {
            int count = 0;
            foreach (var icon in vulnIcons.Values)
                if (icon.Root != null) count++;

            if (count == 0) return;

            float total = count * VulnIconSize + (count - 1) * VulnIconGap;
            float startX = -total * 0.5f + VulnIconSize * 0.5f;
            float step = VulnIconSize + VulnIconGap;

            int index = 0;
            foreach (var icon in vulnIcons.Values)
            {
                if (icon.Root == null) continue;
                var rt = icon.Root.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(startX + index * step, 0f);
                index++;
            }
        }

        void TickVulnerable()
        {
            if (vulnIcons.Count == 0) return;

            expiredVuln.Clear();
            float now = Time.time;

            foreach (var pair in vulnIcons)
                if (now >= pair.Value.ExpireAt) expiredVuln.Add(pair.Key);

            if (expiredVuln.Count > 0)
            {
                foreach (var id in expiredVuln)
                {
                    if (vulnIcons.TryGetValue(id, out var icon))
                    {
                        if (icon.Root != null) Destroy(icon.Root);
                        vulnIcons.Remove(id);
                    }
                }
                LayoutVulnIcons();
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 6f);
            foreach (var icon in vulnIcons.Values)
            {
                if (icon.Bg == null) continue;
                var c = icon.Bg.color;
                icon.Bg.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.70f, 0.98f, pulse));
            }
        }

        void ClearVulnerable()
        {
            foreach (var icon in vulnIcons.Values)
                if (icon.Root != null) Destroy(icon.Root);

            vulnIcons.Clear();
        }

        void TickFlash()
        {
            if (flashOverlay == null || flashTotal <= 0f) return;

            flashTime += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(flashTime / flashTotal);
            var c = flashColor;
            c.a = flashColor.a * (1f - k);
            flashOverlay.color = c;

            if (k >= 1f)
            {
                flashTotal = 0f;
                flashOverlay.enabled = false;
            }
        }

        void Flash(Color color, float duration)
        {
            if (flashOverlay == null) return;

            flashColor = color;
            flashTotal = Mathf.Max(duration, 0.01f);
            flashTime = 0f;
            flashOverlay.enabled = true;
            flashOverlay.color = color;
        }

        void Shake(float duration, float scale)
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (shakeTotal <= 0f)
                shakeCamBaseRot = cam.transform.localRotation;

            shakeCam = cam;
            shakeTime = 0f;
            shakeTotal = Mathf.Max(duration, 0.01f);
            shakeStrength = Mathf.Max(shakeStrength, scale);
        }

        void ApplyShake()
        {
            if (shakeCam == null) return;

            if (shakeTotal <= 0f)
            {
                shakeCam.transform.localRotation = shakeCamBaseRot;
                shakeCam = null;
                shakeStrength = 0f;
                return;
            }

            shakeTime += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(shakeTime / shakeTotal);
            float amp = shakeAngle * shakeStrength * (1f - k);

            float ox = Mathf.PerlinNoise(Time.time * 34f, 0.37f) - 0.5f;
            float oy = Mathf.PerlinNoise(0.73f, Time.time * 34f) - 0.5f;

            var rot = Quaternion.Euler(oy * amp * 2f, ox * amp * 2f, ox * amp);
            shakeCam.transform.localRotation = shakeCamBaseRot * rot;

            if (k >= 1f)
            {
                shakeCam.transform.localRotation = shakeCamBaseRot;
                shakeCam = null;
                shakeTotal = 0f;
                shakeStrength = 0f;
            }
        }

        void PlayFxAtBoss(GameObject template, float lifetime)
        {
            if (template == null) return;

            Vector3 pos = bossTransform != null ? bossTransform.position : transform.position;
            var go = Instantiate(template, pos, Quaternion.identity, fxRoot != null ? fxRoot : transform);
            go.SetActive(true);
            PlayParticles(go);
            Destroy(go, Mathf.Max(lifetime, 0.2f));
        }

        void SetHudVisible(bool visible)
        {
            if (hudRoot != null && hudRoot.activeSelf != visible)
                hudRoot.SetActive(visible);
        }

        void EnsureHud()
        {
            if (hudRoot != null) return;

            var canvasGo = new GameObject("BossHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            hudCanvasGo = canvasGo;
            Font font = GetDefaultFont();

            hudRoot = new GameObject("BossHud", typeof(RectTransform));
            hudRoot.transform.SetParent(canvasGo.transform, false);
            var rootRt = hudRoot.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 1f);
            rootRt.anchorMax = new Vector2(0.5f, 1f);
            rootRt.pivot = new Vector2(0.5f, 1f);
            rootRt.anchoredPosition = new Vector2(0f, -hudTop);
            rootRt.sizeDelta = new Vector2(HudWidth, HudHeight);

            var badge = new GameObject("PhaseBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(hudRoot.transform, false);
            phaseBadge = badge.GetComponent<Image>();
            var badgeRt = badge.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 1f);
            badgeRt.anchorMax = new Vector2(0f, 1f);
            badgeRt.pivot = new Vector2(0f, 1f);
            badgeRt.anchoredPosition = new Vector2(0f, 0f);
            badgeRt.sizeDelta = new Vector2(70f, 34f);
            phaseBadge.raycastTarget = false;

            phaseLabel = CreateText("PhaseLabel", "P1", 24, TextAnchor.MiddleCenter, font, badge.transform);
            PlaceStretch(phaseLabel.rectTransform, 0f, 0f, 0f, 0f);
            phaseLabel.fontStyle = FontStyle.Bold;

            var barBg = new GameObject("HpBarBg", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(hudRoot.transform, false);
            var barBgRt = barBg.GetComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0f, 0f);
            barBgRt.anchorMax = new Vector2(1f, 0f);
            barBgRt.pivot = new Vector2(0.5f, 0f);
            barBgRt.offsetMin = new Vector2(0f, 0f);
            barBgRt.offsetMax = new Vector2(0f, BarHeight);
            var bgImage = barBg.GetComponent<Image>();
            bgImage.color = new Color(0.04f, 0.04f, 0.05f, 0.9f);
            bgImage.raycastTarget = false;

            var fillGo = new GameObject("HpFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBg.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(3f, 3f);
            fillRt.offsetMax = new Vector2(-3f, -3f);
            hpFill = fillGo.GetComponent<Image>();
            hpFill.sprite = GetWhiteSprite();
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            hpFill.fillAmount = 1f;
            hpFill.raycastTarget = false;
            hpFill.color = hpColorP1;

            hpLabel = CreateText("HpLabel", "BOSS", 26, TextAnchor.MiddleCenter, font, barBg.transform);
            PlaceStretch(hpLabel.rectTransform, 8f, 0f, 8f, 0f);
            hpLabel.fontStyle = FontStyle.Bold;
            hpLabel.color = new Color(1f, 0.97f, 0.92f, 1f);
            var shadow = hpLabel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(2f, -2f);

            var vulnGo = new GameObject("VulnRoot", typeof(RectTransform));
            vulnGo.transform.SetParent(hudRoot.transform, false);
            vulnRoot = vulnGo.GetComponent<RectTransform>();
            vulnRoot.anchorMin = new Vector2(0.5f, 1f);
            vulnRoot.anchorMax = new Vector2(0.5f, 1f);
            vulnRoot.pivot = new Vector2(0.5f, 0f);
            vulnRoot.anchoredPosition = new Vector2(0f, -4f);
            vulnRoot.sizeDelta = new Vector2(240f, VulnIconSize);

            var flashGo = new GameObject("FlashOverlay", typeof(RectTransform), typeof(Image));
            flashGo.transform.SetParent(hudCanvasGo.transform, false);
            var flashRt = flashGo.GetComponent<RectTransform>();
            flashRt.anchorMin = Vector2.zero;
            flashRt.anchorMax = Vector2.one;
            flashRt.offsetMin = Vector2.zero;
            flashRt.offsetMax = Vector2.zero;
            flashOverlay = flashGo.GetComponent<Image>();
            flashOverlay.raycastTarget = false;
            flashOverlay.color = new Color(1f, 1f, 1f, 0f);
            flashOverlay.enabled = false;
        }

        void EnsureChargeBar()
        {
            if (chargeRoot != null) return;

            var canvasGo = new GameObject("BossChargeCanvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(ChargeCanvasWidth, ChargeCanvasHeight);
            canvasGo.transform.localScale = Vector3.one * 0.012f;

            chargeRoot = canvasGo;
            Font font = GetDefaultFont();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(ChargeCanvasWidth, ChargeCanvasHeight);
            panelRt.anchoredPosition = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);
            panelImage.raycastTarget = false;

            var barBg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(panel.transform, false);
            var barRt = barBg.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0f);
            barRt.anchorMax = new Vector2(1f, 0f);
            barRt.pivot = new Vector2(0.5f, 0f);
            barRt.offsetMin = new Vector2(20f, 16f);
            barRt.offsetMax = new Vector2(-20f, 16f + ChargeFillHeight);
            var barBgImage = barBg.GetComponent<Image>();
            barBgImage.color = new Color(0.10f, 0.10f, 0.14f, 0.95f);
            barBgImage.raycastTarget = false;

            var fillGo = new GameObject("ChargeFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBg.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(2f, 2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            chargeFill = fillGo.GetComponent<Image>();
            chargeFill.sprite = GetWhiteSprite();
            chargeFill.type = Image.Type.Filled;
            chargeFill.fillMethod = Image.FillMethod.Horizontal;
            chargeFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            chargeFill.fillAmount = 0f;
            chargeFill.color = new Color(0.30f, 0.75f, 1f, 0.75f);
            chargeFill.raycastTarget = false;

            var windowGo = new GameObject("BreakWindow", typeof(RectTransform), typeof(Image));
            windowGo.transform.SetParent(barBg.transform, false);
            var windowRt = windowGo.GetComponent<RectTransform>();
            windowRt.anchorMin = new Vector2(breakWindowStart, 0f);
            windowRt.anchorMax = new Vector2(breakWindowEnd, 1f);
            windowRt.offsetMin = Vector2.zero;
            windowRt.offsetMax = Vector2.zero;
            breakWindow = windowGo.GetComponent<Image>();
            breakWindow.color = windowColor;
            breakWindow.raycastTarget = false;

            chargeLabel = CreateText("ChargeLabel", "蓄力 0%", 30, TextAnchor.MiddleCenter, font, panel.transform);
            PlaceStretch(chargeLabel.rectTransform, 8f, 4f, 8f, 8f);
            chargeLabel.fontStyle = FontStyle.Bold;
            chargeLabel.color = new Color(1f, 0.92f, 0.72f, 1f);
            var shadow = chargeLabel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(2f, -2f);

            chargeRoot.SetActive(false);
        }

        void EnsureFx()
        {
            if (fxRoot == null)
            {
                var root = new GameObject("Boss1FxRoot");
                root.transform.SetParent(transform, false);
                fxRoot = root.transform;
            }

            if (shieldVfx == null)
                shieldVfx = CreateShieldTemplate();

            if (shieldBreakFx == null)
                shieldBreakFx = CreateBurstTemplate("ShieldBreakFx", new Color(1f, 0.86f, 0.42f, 1f), 40);

            if (phaseShiftFx == null)
                phaseShiftFx = CreateBurstTemplate("PhaseShiftFx", new Color(1f, 0.38f, 0.14f, 1f), 60);
        }

        GameObject CreateShieldTemplate()
        {
            var go = new GameObject("ShieldVfx");
            go.transform.SetParent(fxRoot, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startColor = new Color(0.55f, 0.85f, 1f, 1f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 45f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2.4f;
            shape.radiusThickness = 0f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.55f, 0.85f, 1f, 1f), 0f), new GradientColorKey(new Color(0.75f, 0.95f, 1f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetFxMaterial();

            go.SetActive(false);
            return go;
        }

        GameObject CreateBurstTemplate(string name, Color color, short count)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fxRoot, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startColor = color;
            main.maxParticles = 300;
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.3f;
            shape.radiusThickness = 0.5f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetFxMaterial();

            go.SetActive(false);
            return go;
        }

        static Material GetFxMaterial()
        {
            if (cachedFxMaterial != null) return cachedFxMaterial;

            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            cachedFxMaterial = new Material(shader) { name = "Boss1FxMat" };
            return cachedFxMaterial;
        }

        static void PlayParticles(GameObject fx)
        {
            if (fx == null) return;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(true);
        }

        Color ResolveElementColor(uint elementId)
        {
            if (elementId == OilElementId) return new Color(1f, 0.84f, 0.20f, 1f);
            if (elementId == FireElementId) return new Color(1f, 0.50f, 0.15f, 1f);
            return ElementTable.GetColor(elementId, new Color(0.9f, 0.9f, 0.9f, 1f));
        }

        string ResolveElementLabel(uint elementId)
        {
            if (elementId == OilElementId) return "油";
            if (elementId == FireElementId) return "火";
            return elementId.ToString();
        }

        static bool TryParsePhase(ByteString payload, out int phase)
        {
            phase = 1;

            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8:
                            phase = (int)input.ReadUInt32();
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

        static bool TryParseEntityId(ByteString payload, out ulong entityId)
        {
            entityId = 0;

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

        struct AttachMsg
        {
            public ulong EntityId;
            public uint ElementId;
            public float Duration;
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

        static Sprite GetWhiteSprite()
        {
            if (cachedWhiteSprite != null) return cachedWhiteSprite;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            cachedWhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return cachedWhiteSprite;
        }
    }
}