using System.Collections.Generic;
using Game.Net;
using Game.Player;
using Game.Proto;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class HudPanel : MonoBehaviour
    {
        const int MaxTeammates = 4;
        const int SkillSlots = 4;
        const int DespawnFrames = 15;
        const float GoldSpeed = 9f;
        const float ExpSpeed = 10f;

        static readonly Color NormalText = new(0.90f, 0.93f, 0.97f, 1f);
        static readonly Color DimText = new(0.55f, 0.60f, 0.66f, 1f);
        static readonly Color PanelColor = new(0.07f, 0.08f, 0.11f, 0.72f);
        static readonly Color RowColor = new(0.10f, 0.12f, 0.16f, 0.55f);
        static readonly Color BarBack = new(0.04f, 0.05f, 0.07f, 0.85f);
        static readonly Color HpColor = new(0.84f, 0.27f, 0.25f, 1f);
        static readonly Color ExpColor = new(0.33f, 0.66f, 1f, 1f);
        static readonly Color SlotReady = new(0.19f, 0.24f, 0.32f, 0.92f);
        static readonly Color SlotCooling = new(0.05f, 0.06f, 0.09f, 0.90f);
        static readonly Color GhostText = new(0.66f, 0.78f, 1f, 1f);
        static readonly Color GhostBarFill = new(0.35f, 0.80f, 1f, 1f);
        static readonly Color GhostBarWarn = new(1f, 0.85f, 0.25f, 1f);
        static readonly Color DeadText = new(0.78f, 0.36f, 0.36f, 1f);

        class TeammateRow
        {
            public ulong EntityId;
            public GameObject Root;
            public Image HpFill;
            public Text NameText;
            public Text StatusText;
            public string StatusCache = string.Empty;
            public int Missing;
        }

        class SkillSlot
        {
            public Image Bg;
            public Image Fill;
            public Text CdText;
            public float Remaining;
            public float Total = 1f;
            public int LastSecond = -1;
            public bool Ready;
        }

        GameObject root;
        Image selfHpFill;
        Text selfHpText;
        Text levelText;
        Image expFill;
        Text expText;
        Text goldText;
        Text selfStatusText;

        GameObject ghostPanel;
        Text ghostAnchorText;
        Text ghostDistanceText;
        Image ghostLeashFill;
        Text ghostCarryText;
        GhostForm ghostForm;
        float ghostNextRefresh;
        float ghostLookupNext;
        float ghostShownRatio = -1f;
        ulong ghostShownAnchor = ulong.MaxValue;
        int ghostShownTenths = -1;
        bool ghostShownCarrying;

        readonly SkillSlot[] skillSlots = new SkillSlot[SkillSlots];
        readonly List<TeammateRow> teammateRows = new(MaxTeammates);
        readonly Dictionary<ulong, TeammateRow> rowByEntity = new();
        readonly HashSet<ulong> seenPlayers = new();
        readonly List<TeammateRow> freedRows = new();

        GameRoot gameRoot;
        bool built;

        int lastHp = -1;
        int lastHpMax = -1;
        string selfStatusCache = string.Empty;

        bool goldInitialized;
        bool goldAnimating;
        int goldTarget;
        float goldShown;
        int goldLastShown = -1;

        bool expInitialized;
        bool expAnimating;
        float expTarget;
        float expShown;
        uint level = 1;
        int expLast = -1;
        int expNextLast = -1;

        static Font cachedFont;
        static Sprite cachedWhiteSprite;

        void OnEnable()
        {
            MsgDispatcher.Register<S2C_Snapshot>(OnSnapshot);
            MsgDispatcher.Register<S2CReconnectAck>(OnReconnectAck);
            MsgDispatcher.Register<S2CHitApply>(OnHitApply);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2C_Snapshot>(OnSnapshot);
            MsgDispatcher.Unregister<S2CReconnectAck>(OnReconnectAck);
            MsgDispatcher.Unregister<S2CHitApply>(OnHitApply);
        }

        void Start()
        {
            BuildUI();
            HideRoot();
            BindGameRoot();

            if (GameRoot.Instance != null && GameRoot.Instance.State == GameRoot.GameState.InGame)
                ShowRoot();
        }

        void OnDestroy()
        {
            UnbindGameRoot();
        }

        void Update()
        {
            TickGold();
            TickExp();
            TickSkillCooldowns();
            TickGhost();
        }

        void TickGhost()
        {
            if (!built || ghostPanel == null) return;

            if (ghostForm == null)
            {
                if (Time.unscaledTime < ghostLookupNext) return;
                ghostLookupNext = Time.unscaledTime + 1f;
                ghostForm = FindObjectOfType<GhostForm>();
                if (ghostForm == null) return;
            }

            GhostForm.HudState state = ghostForm != null ? ghostForm.State : default;

            if (ghostPanel.activeSelf != state.Active) ghostPanel.SetActive(state.Active);
            if (!state.Active) return;

            if (Time.unscaledTime < ghostNextRefresh) return;
            ghostNextRefresh = Time.unscaledTime + 0.1f;

            float radius = state.Radius > 0.01f ? state.Radius : 1f;
            float remain = Mathf.Max(0f, radius - state.Distance);
            float ratio = Mathf.Clamp01(remain / radius);

            if (Mathf.Abs(ratio - ghostShownRatio) > 0.005f)
            {
                ghostShownRatio = ratio;
                ghostLeashFill.fillAmount = ratio;
                ghostLeashFill.color = ratio <= 0.2f ? GhostBarWarn : GhostBarFill;
            }

            if (state.AnchorId != ghostShownAnchor)
            {
                ghostShownAnchor = state.AnchorId;
                ghostAnchorText.text = state.AnchorId != 0
                    ? "跟随队友 #" + state.AnchorId
                    : "跟随队友：无";
            }

            int tenths = Mathf.RoundToInt(remain * 10f);
            if (tenths != ghostShownTenths)
            {
                ghostShownTenths = tenths;
                ghostDistanceText.text = "拴绳剩余 " + (tenths / 10f).ToString("0.0")
                    + " / " + radius.ToString("0.0") + " m";
            }

            if (state.Carrying != ghostShownCarrying)
            {
                ghostShownCarrying = state.Carrying;
                ghostCarryText.text = state.Carrying ? "搬运中：物品已附身" : "搬运中：无";
                ghostCarryText.color = state.Carrying ? GhostText : DimText;
            }
        }

        public void ApplySelfHp(float hp, float maxHp)
        {
            if (!built) return;

            selfHpFill.fillAmount = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;

            int hpInt = Mathf.CeilToInt(hp);
            int maxInt = Mathf.CeilToInt(maxHp);
            if (hpInt == lastHp && maxInt == lastHpMax) return;

            lastHp = hpInt;
            lastHpMax = maxInt;
            selfHpText.text = hpInt + " / " + maxInt;
        }

        public void ApplyGold(uint gold)
        {
            if (!built) return;

            int target = (int)gold;
            if (target == goldTarget) return;
            goldTarget = target;

            if (!goldInitialized)
            {
                goldInitialized = true;
                goldShown = target;
                goldLastShown = target;
                goldText.text = "金币 " + target;
                return;
            }

            goldAnimating = true;
        }

        public void ApplySelfProgress(uint newLevel, uint exp, uint expToNext)
        {
            if (!built) return;

            if (level != newLevel)
            {
                level = newLevel;
                levelText.text = "Lv " + newLevel;
            }

            int expInt = (int)exp;
            int nextInt = (int)expToNext;
            if (expInt != expLast || nextInt != expNextLast)
            {
                expLast = expInt;
                expNextLast = nextInt;
                expText.text = expInt + " / " + nextInt;
            }

            expTarget = nextInt > 0 ? Mathf.Clamp01((float)expInt / nextInt) : 0f;

            if (!expInitialized)
            {
                expInitialized = true;
                expShown = expTarget;
                expFill.fillAmount = expTarget;
                return;
            }

            expAnimating = true;
        }

        public void ApplySkillCooldown(int slot, float remaining, float total)
        {
            if (!built || slot < 0 || slot >= SkillSlots) return;

            var s = skillSlots[slot];
            s.Remaining = Mathf.Max(0f, remaining);
            s.Total = Mathf.Max(0.01f, total);
            s.LastSecond = -1;
            RefreshSkillSlot(s);
        }

        void OnSnapshot(S2C_Snapshot msg)
        {
            if (!built || msg == null) return;

            ulong selfId = LocalEntityId();
            seenPlayers.Clear();

            foreach (var e in msg.Entities)
            {
                if (e == null) continue;

                if (e.EntityId == selfId)
                {
                    ApplySelfHp(e.Hp, e.HpMax);
                    UpdateSelfStatus(e);
                    continue;
                }

                if (e.Type != EntityType.EntityPlayer) continue;

                seenPlayers.Add(e.EntityId);
                UpdateTeammate(e);
            }

            HideUnseenTeammates();
            ApplyGold(KcpNetClient.Instance != null ? KcpNetClient.Instance.LocalGold : 0u);
            ShowRoot();
        }

        void OnReconnectAck(S2CReconnectAck msg)
        {
            if (msg != null && msg.Snapshot != null) OnSnapshot(msg.Snapshot);
        }

        void OnHitApply(S2CHitApply msg)
        {
            if (!built || !msg.HasVictimEntityId) return;
            if (msg.VictimEntityId != LocalEntityId()) return;
            ApplySelfHp(msg.HpAfter, msg.HpMaxAfter);
        }

        void UpdateSelfStatus(Entity e)
        {
            string status = DescribeStatus(e.Hp, e.HpMax, e.Flags);
            if (status == selfStatusCache) return;

            selfStatusCache = status;
            selfStatusText.text = status;
            selfStatusText.color = StatusColor(status);
        }

        void UpdateTeammate(Entity e)
        {
            var row = AcquireRow(e.EntityId);
            if (row == null) return;

            row.HpFill.fillAmount = e.HpMax > 0f ? Mathf.Clamp01(e.Hp / e.HpMax) : 0f;

            string status = DescribeStatus(e.Hp, e.HpMax, e.Flags);
            if (status == row.StatusCache) return;

            row.StatusCache = status;
            row.StatusText.text = status;
            row.StatusText.color = StatusColor(status);
        }

        TeammateRow AcquireRow(ulong entityId)
        {
            if (rowByEntity.TryGetValue(entityId, out var row)) return row;

            for (int i = 0; i < teammateRows.Count; i++)
            {
                var candidate = teammateRows[i];
                if (candidate.EntityId != 0) continue;

                candidate.EntityId = entityId;
                candidate.NameText.text = "队友 " + entityId;
                candidate.Root.SetActive(true);
                rowByEntity[entityId] = candidate;
                return candidate;
            }

            return null;
        }

        void HideUnseenTeammates()
        {
            freedRows.Clear();

            for (int i = 0; i < teammateRows.Count; i++)
            {
                var row = teammateRows[i];
                if (row.EntityId == 0) continue;

                if (seenPlayers.Contains(row.EntityId))
                {
                    row.Missing = 0;
                    continue;
                }

                if (++row.Missing < DespawnFrames) continue;

                row.Missing = 0;
                row.Root.SetActive(false);
                rowByEntity.Remove(row.EntityId);
                row.EntityId = 0;
                row.StatusCache = string.Empty;
                freedRows.Add(row);
            }

            for (int i = 0; i < freedRows.Count; i++)
                freedRows[i].NameText.text = string.Empty;
        }

        void TickGold()
        {
            if (!goldAnimating) return;

            float k = 1f - Mathf.Exp(-GoldSpeed * Time.unscaledDeltaTime);
            goldShown = Mathf.Lerp(goldShown, goldTarget, k);

            if (Mathf.Abs(goldShown - goldTarget) < 0.4f)
            {
                goldShown = goldTarget;
                goldAnimating = false;
            }

            int shown = Mathf.RoundToInt(goldShown);
            if (shown == goldLastShown) return;

            goldLastShown = shown;
            goldText.text = "金币 " + shown;
        }

        void TickExp()
        {
            if (!expAnimating) return;

            float k = 1f - Mathf.Exp(-ExpSpeed * Time.unscaledDeltaTime);
            expShown = Mathf.Lerp(expShown, expTarget, k);

            if (Mathf.Abs(expShown - expTarget) < 0.005f)
            {
                expShown = expTarget;
                expAnimating = false;
            }

            expFill.fillAmount = expShown;
        }

        void TickSkillCooldowns()
        {
            for (int i = 0; i < SkillSlots; i++)
            {
                var s = skillSlots[i];
                if (s == null || s.Remaining <= 0f) continue;

                s.Remaining -= Time.unscaledDeltaTime;
                if (s.Remaining < 0f) s.Remaining = 0f;

                RefreshSkillSlot(s);
            }
        }

        void RefreshSkillSlot(SkillSlot s)
        {
            bool ready = s.Remaining <= 0.0001f;
            s.Fill.fillAmount = ready ? 0f : Mathf.Clamp01(s.Remaining / s.Total);

            int sec = ready ? 0 : Mathf.CeilToInt(s.Remaining);
            if (sec == s.LastSecond && ready == s.Ready) return;

            s.LastSecond = sec;
            s.Ready = ready;
            s.Bg.color = ready ? SlotReady : SlotCooling;
            s.CdText.text = ready ? "就绪" : sec.ToString();
            s.CdText.color = ready ? NormalText : DimText;
        }

        static string DescribeStatus(float hp, float hpMax, EntityFlag flags)
        {
            if (hpMax > 0f && hp <= 0f) return "阵亡";

            if (flags != null)
            {
                if (flags.IsGhost) return "鬼魂";
                if (flags.IsEnraged) return "狂暴";
                if (flags.ArmorBroken) return "破甲";
            }

            return string.Empty;
        }

        static Color StatusColor(string status)
        {
            if (status == "阵亡") return DeadText;
            if (status == "鬼魂") return GhostText;
            return NormalText;
        }

        static ulong LocalEntityId()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.LocalEntityId != 0)
                return GameRoot.Instance.LocalEntityId;

            return KcpNetClient.Instance != null ? KcpNetClient.Instance.LocalEntityId : 0;
        }

        void BindGameRoot()
        {
            var g = GameRoot.Instance;
            if (g == null) return;

            gameRoot = g;
            g.LoginSucceeded += ShowRoot;
            g.Reconnected += ShowRoot;
            g.LoginFailed += OnLoginFailed;
            g.Disconnected += HideRoot;
        }

        void UnbindGameRoot()
        {
            if (gameRoot == null) return;

            gameRoot.LoginSucceeded -= ShowRoot;
            gameRoot.Reconnected -= ShowRoot;
            gameRoot.LoginFailed -= OnLoginFailed;
            gameRoot.Disconnected -= HideRoot;
            gameRoot = null;
        }

        void OnLoginFailed(string reason)
        {
            HideRoot();
        }

        void ShowRoot()
        {
            if (root == null || root.activeSelf) return;
            root.SetActive(true);
        }

        void HideRoot()
        {
            if (root == null || !root.activeSelf) return;
            root.SetActive(false);
        }

        void BuildUI()
        {
            if (built) return;
            built = true;

            EnsureCanvas();
            Font font = GetDefaultFont();

            root = new GameObject("HudRoot", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            StretchFullScreen(root.GetComponent<RectTransform>());

            BuildSelfPanel(font);
            BuildTeamPanel(font);
            BuildSkillBar(font);
            BuildGhostPanel(font);
        }

        void BuildSelfPanel(Font font)
        {
            var panel = CreatePanel("SelfPanel", PanelColor, root.transform);
            PlaceTopLeft(panel.GetComponent<RectTransform>(), 24f, -24f, 340f, 146f);

            selfHpFill = CreateBarFill(panel.transform, "Hp", HpColor, 16f, -14f, 308f, 26f);

            selfHpText = CreateText("HpText", "100 / 100", 18, TextAnchor.MiddleCenter, font, panel.transform);
            PlaceTopLeft(selfHpText.rectTransform, 16f, -14f, 308f, 26f);

            levelText = CreateText("Level", "Lv 1", 20, TextAnchor.MiddleLeft, font, panel.transform);
            PlaceTopLeft(levelText.rectTransform, 16f, -46f, 80f, 22f);

            expText = CreateText("ExpText", "0 / 0", 16, TextAnchor.MiddleRight, font, panel.transform);
            PlaceTopLeft(expText.rectTransform, 100f, -46f, 224f, 22f);

            expFill = CreateBarFill(panel.transform, "Exp", ExpColor, 100f, -70f, 224f, 10f);
            expFill.fillAmount = 0f;

            goldText = CreateText("Gold", "金币 0", 22, TextAnchor.MiddleLeft, font, panel.transform);
            PlaceTopLeft(goldText.rectTransform, 16f, -84f, 308f, 26f);

            selfStatusText = CreateText("Status", string.Empty, 16, TextAnchor.MiddleLeft, font, panel.transform);
            PlaceTopLeft(selfStatusText.rectTransform, 16f, -112f, 308f, 22f);
        }

        void BuildTeamPanel(Font font)
        {
            var panel = CreatePanel("TeamPanel", PanelColor, root.transform);
            PlaceTopLeft(panel.GetComponent<RectTransform>(), 24f, -182f, 340f, 238f);

            for (int i = 0; i < MaxTeammates; i++)
            {
                var row = new TeammateRow();
                var go = CreatePanel("Teammate" + (i + 1), RowColor, panel.transform);
                PlaceTopLeft(go.GetComponent<RectTransform>(), 10f, -(10f + i * 56f), 320f, 50f);
                row.Root = go;

                row.NameText = CreateText("Name", string.Empty, 18, TextAnchor.MiddleLeft, font, go.transform);
                PlaceTopLeft(row.NameText.rectTransform, 8f, -6f, 180f, 20f);

                row.StatusText = CreateText("Status", string.Empty, 16, TextAnchor.MiddleRight, font, go.transform);
                PlaceTopLeft(row.StatusText.rectTransform, 160f, -6f, 152f, 20f);

                row.HpFill = CreateBarFill(go.transform, "Hp", HpColor, 8f, -32f, 304f, 10f);

                go.SetActive(false);
                teammateRows.Add(row);
            }
        }

        void BuildSkillBar(Font font)
        {
            var panel = CreatePanel("SkillBar", PanelColor, root.transform);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 24f);
            rt.sizeDelta = new Vector2(316f, 92f);

            for (int i = 0; i < SkillSlots; i++)
            {
                var s = new SkillSlot();
                var bgGo = CreatePanel("Skill" + (i + 1), SlotReady, panel.transform);
                PlaceTopLeft(bgGo.GetComponent<RectTransform>(), 10f + i * 76f, -12f, 68f, 68f);

                s.Bg = bgGo.GetComponent<Image>();
                s.Bg.raycastTarget = false;

                var fillGo = CreatePanel("Cooldown", SlotCooling, bgGo.transform);
                StretchFullScreen(fillGo.GetComponent<RectTransform>());

                s.Fill = fillGo.GetComponent<Image>();
                s.Fill.sprite = WhiteSprite;
                s.Fill.type = Image.Type.Filled;
                s.Fill.fillMethod = Image.FillMethod.Radial360;
                s.Fill.fillOrigin = 2;
                s.Fill.fillClockwise = false;
                s.Fill.fillAmount = 0f;
                s.Fill.raycastTarget = false;

                var key = CreateText("Key", (i + 1).ToString(), 16, TextAnchor.MiddleLeft, font, bgGo.transform);
                PlaceTopLeft(key.rectTransform, 6f, -44f, 24f, 18f);
                key.color = DimText;

                s.CdText = CreateText("Cd", "就绪", 20, TextAnchor.MiddleCenter, font, bgGo.transform);
                StretchFullScreen(s.CdText.rectTransform);
                s.CdText.raycastTarget = false;

                skillSlots[i] = s;
            }
        }

        void BuildGhostPanel(Font font)
        {
            ghostPanel = CreatePanel("GhostPanel", PanelColor, root.transform);
            PlaceTopRight(ghostPanel.GetComponent<RectTransform>(), -24f, -24f, 320f, 140f);

            var title = CreateText("Title", "鬼魂形态", 20, TextAnchor.MiddleLeft, font, ghostPanel.transform);
            PlaceTopLeft(title.rectTransform, 16f, -10f, 288f, 24f);
            title.color = GhostText;

            ghostAnchorText = CreateText("Anchor", "跟随队友：无", 16, TextAnchor.MiddleLeft, font, ghostPanel.transform);
            PlaceTopLeft(ghostAnchorText.rectTransform, 16f, -38f, 288f, 20f);

            ghostDistanceText = CreateText("Distance", string.Empty, 16, TextAnchor.MiddleLeft, font, ghostPanel.transform);
            PlaceTopLeft(ghostDistanceText.rectTransform, 16f, -60f, 288f, 20f);

            ghostLeashFill = CreateBarFill(ghostPanel.transform, "Leash", GhostBarFill, 16f, -84f, 288f, 10f);
            ghostLeashFill.fillAmount = 1f;

            ghostCarryText = CreateText("Carry", "搬运中：无", 16, TextAnchor.MiddleLeft, font, ghostPanel.transform);
            PlaceTopLeft(ghostCarryText.rectTransform, 16f, -100f, 288f, 20f);
            ghostCarryText.color = DimText;

            ghostPanel.SetActive(false);
        }

        Image CreateBarFill(Transform parent, string name, Color color, float x, float y, float width, float height)
        {
            var bgGo = CreatePanel(name + "Back", BarBack, parent);
            PlaceTopLeft(bgGo.GetComponent<RectTransform>(), x, y, width, height);
            bgGo.GetComponent<Image>().raycastTarget = false;

            var fillGo = CreatePanel(name + "Fill", color, bgGo.transform);
            StretchFullScreen(fillGo.GetComponent<RectTransform>());

            var fill = fillGo.GetComponent<Image>();
            fill.sprite = WhiteSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 1f;
            fill.raycastTarget = false;
            return fill;
        }

        static GameObject CreatePanel(string name, Color color, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        static Text CreateText(string name, string content, int size, TextAnchor anchor, Font font, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = NormalText;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;
            return text;
        }

        static void PlaceTopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        static void PlaceTopRight(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        static void StretchFullScreen(RectTransform rt)        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void EnsureCanvas()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            if (GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
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

        static Sprite WhiteSprite
        {
            get
            {
                if (cachedWhiteSprite != null) return cachedWhiteSprite;

                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;

                cachedWhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
                cachedWhiteSprite.hideFlags = HideFlags.HideAndDontSave;
                return cachedWhiteSprite;
            }
        }
    }
}