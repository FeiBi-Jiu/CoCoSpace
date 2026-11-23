using System;
using Game.Net;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    public class BackpackUI : MonoBehaviour
    {
        // 可复用引导状态机：背包 / 祭坛共用「计时 + 受击打断」
        public class HoldGuide
        {
            public float HoldSeconds = 0.4f;
            public float BreakFlashSeconds = 0.35f;

            public float Progress { get; private set; }
            public bool Completed { get; private set; }
            public bool Holding { get; private set; }
            public bool Interrupted => breakTimer > 0f;
            public float BreakAlpha => breakTimer > 0f
                ? Mathf.Clamp01(breakTimer / Mathf.Max(0.01f, BreakFlashSeconds))
                : 0f;

            float breakTimer;

            public void Reset()
            {
                Progress = 0f;
                Completed = false;
                breakTimer = 0f;
            }

            public void MarkCompleted()
            {
                Progress = 1f;
                Completed = true;
            }

            // 受击打断：进度归零 + 触发闪烁，已完成的引导不受影响
            public void Break()
            {
                if (Completed) return;

                Progress = 0f;
                breakTimer = BreakFlashSeconds;
            }

            // 返回 true 表示本帧刚好完成引导
            public bool Tick(bool holding, float deltaTime)
            {
                Holding = holding;

                if (breakTimer > 0f)
                    breakTimer = Mathf.Max(0f, breakTimer - deltaTime);

                if (Completed) return false;

                if (!holding)
                {
                    Progress = 0f;
                    return false;
                }

                Progress += deltaTime / Mathf.Max(0.01f, HoldSeconds);
                if (Progress < 1f) return false;

                Progress = 1f;
                Completed = true;
                return true;
            }
        }

        class SlotView
        {
            public GameObject Root;
            public Image Bg;
            public Image Icon;
            public Text Key;
            public Text Name;
            public Image CdFill;
            public Text CdText;
            public Text Count;
            public Button Button;
        }

        class SkillData
        {
            public uint SkillId;
            public string Name;
            public float Remaining;
            public float Total;
        }

        struct ItemStack
        {
            public uint ItemId;
            public string Name;
            public int Count;
        }

        const int SkillSlotCount = 2;
        const int ItemSlotCount = 3;
        const float GuideY = 150f;

        // 后端协议尚未生成 C2S_UseItem，这里先用占位 MsgId，协议落地后替换
        const MsgId UseItemMsgId = (MsgId)1007;

        static readonly Color OverlayColor = new(0.02f, 0.03f, 0.05f, 0.72f);
        static readonly Color WindowColor = new(0.07f, 0.09f, 0.13f, 0.97f);
        static readonly Color SlotColor = new(0.13f, 0.16f, 0.21f, 1f);
        static readonly Color SlotEmptyColor = new(0.09f, 0.10f, 0.13f, 1f);
        static readonly Color EmptyIconColor = new(0.18f, 0.20f, 0.24f, 1f);
        static readonly Color LabelStripColor = new(0.04f, 0.05f, 0.07f, 0.82f);
        static readonly Color CooldownMaskColor = new(0.03f, 0.04f, 0.06f, 0.78f);
        static readonly Color DividerColor = new(0.20f, 0.24f, 0.30f, 1f);
        static readonly Color MainText = new(0.94f, 0.96f, 0.99f, 1f);
        static readonly Color DimText = new(0.66f, 0.71f, 0.78f, 1f);
        static readonly Color CountColor = new(1f, 0.86f, 0.52f, 1f);
        static readonly Color GuideBackColor = new(0.04f, 0.05f, 0.07f, 0.92f);
        static readonly Color GuideFillColor = new(0.30f, 0.72f, 1f, 1f);
        static readonly Color GuideBreakColor = new(0.90f, 0.28f, 0.26f, 1f);
        static readonly Color GuideBreakTextColor = new(1f, 0.55f, 0.52f, 1f);

        static Font cachedFont;
        static Sprite cachedWhiteSprite;

        readonly HoldGuide guide = new();
        readonly SkillData[] skills = new SkillData[SkillSlotCount];
        readonly ItemStack[] items = new ItemStack[ItemSlotCount];
        readonly SlotView[] skillSlots = new SlotView[SkillSlotCount];
        readonly SlotView[] itemSlots = new SlotView[ItemSlotCount];

        GameObject guideRoot;
        RectTransform guideRect;
        Image guideFill;
        Text guideText;

        GameObject panelRoot;
        SlotView weaponSlot;

        uint weaponId;
        string weaponName = string.Empty;

        bool built;
        bool open;

        void Awake()
        {
            SeedGrayboxData();
            EnsureUI();
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CHitApply>(OnHitApply);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CHitApply>(OnHitApply);
            Close();
        }

        void Update()
        {
            TickSkillCooldowns();
            TickGuide();
        }

        void TickGuide()
        {
            if (open)
            {
                if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape))
                    Close();

                return;
            }

            bool holding = CanOpen() && Input.GetKey(KeyCode.Tab);
            if (guide.Tick(holding, Time.deltaTime))
            {
                Open();
                return;
            }

            if (Input.GetKeyUp(KeyCode.Tab))
                guide.Reset();

            RefreshGuideVisual();
        }

        void TickSkillCooldowns()
        {
            for (int i = 0; i < skills.Length; i++)
            {
                SkillData data = skills[i];
                if (data == null || data.Remaining <= 0f) continue;

                data.Remaining = Mathf.Max(0f, data.Remaining - Time.unscaledDeltaTime);
                if (open) RefreshSkillSlot(i);
            }
        }

        // 也可由 GameRoot 在收到自己的 S2C_HitApply 时直接调用
        public void OnLocalPlayerHurt()
        {
            if (open) return;

            guide.Break();
            RefreshGuideVisual();
        }

        void OnHitApply(S2CHitApply msg)
        {
            if (msg == null || !msg.HasVictimEntityId) return;
            if (msg.VictimEntityId != LocalEntityId()) return;

            OnLocalPlayerHurt();
        }

        // 以下三个接口供 S2C_BackpackUpdate 落地后接入（客户端只渲染，不做装备逻辑）
        public void ApplyWeapon(uint id, string name)
        {
            weaponId = id;
            weaponName = name ?? string.Empty;
            if (open) RefreshWeaponSlot();
        }

        public void ApplySkill(int index, uint id, string name, float remaining, float total)
        {
            if (index < 0 || index >= skills.Length) return;

            skills[index] = new SkillData
            {
                SkillId = id,
                Name = name ?? string.Empty,
                Remaining = Mathf.Max(0f, remaining),
                Total = Mathf.Max(0.01f, total),
            };

            if (open) RefreshSkillSlot(index);
        }

        public void ApplyItem(int index, uint id, string name, int count)
        {
            if (index < 0 || index >= items.Length) return;

            items[index] = new ItemStack
            {
                ItemId = id,
                Name = name ?? string.Empty,
                Count = Mathf.Max(0, count),
            };

            if (open) RefreshItemSlot(index);
        }

        void Open()
        {
            if (open) return;

            EnsureUI();
            open = true;

            RefreshWeaponSlot();
            for (int i = 0; i < skillSlots.Length; i++) RefreshSkillSlot(i);
            for (int i = 0; i < itemSlots.Length; i++) RefreshItemSlot(i);

            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();

            if (guideRoot != null) guideRoot.SetActive(false);
        }

        void Close()
        {
            if (!open) return;

            open = false;
            guide.Reset();

            // Tab 仍按住时不允许立刻重新引导，松开后 Reset 解除
            if (Input.GetKey(KeyCode.Tab))
                guide.MarkCompleted();

            if (panelRoot != null) panelRoot.SetActive(false);
            if (guideRoot != null) guideRoot.SetActive(false);
        }

        void UseItem(int index)
        {
            if (index < 0 || index >= items.Length) return;

            ItemStack stack = items[index];
            if (stack.ItemId == 0 || stack.Count <= 0) return;

            SendUseItemIntent(stack.ItemId);
        }

        // 只发「用哪个道具」的意图，是否可用/生效由服务器判定
        static void SendUseItemIntent(uint itemId)
        {
            var net = KcpNetClient.Instance;
            if (net == null)
            {
                Debug.LogWarning("[Backpack] KcpNetClient 不存在，使用道具意图未发送");
                return;
            }

            Debug.Log($"[Backpack] 请求使用道具 item={itemId}");

            net.SendEnvelope(new Envelope
            {
                MsgId = UseItemMsgId,
                Payload = ByteString.CopyFrom(BitConverter.GetBytes(itemId)),
            });
        }

        void RefreshGuideVisual()
        {
            if (guideRoot == null) return;

            bool visible = guide.Holding || guide.Interrupted;
            guideRoot.SetActive(visible);
            if (!visible) return;

            guideFill.fillAmount = guide.Interrupted ? 1f : guide.Progress;
            guideFill.color = guide.Interrupted ? GuideBreakColor : GuideFillColor;
            guideText.text = guide.Interrupted ? "受到攻击，引导被打断" : "长按 Tab 开启背包";
            guideText.color = guide.Interrupted ? GuideBreakTextColor : DimText;

            // 受击打断表现：进度条短促左右抖动，随时间衰减
            float flash = guide.BreakAlpha;
            guideRect.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * 46f) * 10f * flash, 0f);
        }

        void RefreshWeaponSlot()
        {
            if (weaponSlot == null) return;

            bool has = weaponId != 0;
            weaponSlot.Icon.color = has ? IconColor(weaponId) : EmptyIconColor;
            weaponSlot.Name.text = has ? weaponName : "未装备";
            weaponSlot.CdFill.fillAmount = 0f;
            weaponSlot.CdText.text = string.Empty;
            weaponSlot.Count.text = string.Empty;
        }

        void RefreshSkillSlot(int index)
        {
            SlotView view = skillSlots[index];
            SkillData data = skills[index];
            if (view == null || data == null) return;

            bool ready = data.Remaining <= 0.0001f;
            float ratio = ready ? 0f : Mathf.Clamp01(data.Remaining / Mathf.Max(0.01f, data.Total));

            view.Icon.color = IconColor(data.SkillId);
            view.Name.text = data.Name;
            view.CdFill.fillAmount = ratio;
            view.CdText.text = ready ? "就绪" : Mathf.CeilToInt(data.Remaining).ToString();
            view.CdText.color = ready ? MainText : DimText;
        }

        void RefreshItemSlot(int index)
        {
            SlotView view = itemSlots[index];
            if (view == null) return;

            ItemStack stack = items[index];
            bool has = stack.ItemId != 0 && stack.Count > 0;

            view.Bg.color = has ? SlotColor : SlotEmptyColor;
            view.Icon.color = has ? IconColor(stack.ItemId) : EmptyIconColor;
            view.Name.text = has ? stack.Name : "空";
            view.Count.text = has ? "x" + stack.Count : string.Empty;
        }

        // 灰盒占位：用 ID 生成稳定色，避免依赖图标资源
        static Color IconColor(uint id)
        {
            if (id == 0) return EmptyIconColor;

            float hue = id * 0.6180339f % 1f;
            return Color.HSVToRGB(hue, 0.42f, 0.78f);
        }

        void SeedGrayboxData()
        {
            weaponId = 9001;
            weaponName = "训练长剑";

            skills[0] = new SkillData { SkillId = 9101, Name = "冲刺斩", Remaining = 0f, Total = 6f };
            skills[1] = new SkillData { SkillId = 9102, Name = "护盾术", Remaining = 3.2f, Total = 8f };

            items[0] = new ItemStack { ItemId = 9201, Name = "治疗药剂", Count = 3 };
            items[1] = new ItemStack { ItemId = 9202, Name = "燃烧瓶", Count = 2 };
            items[2] = new ItemStack { ItemId = 9203, Name = "闪光弹", Count = 1 };
        }

        static bool CanOpen()
        {
            var root = GameRoot.Instance;
            return root == null || root.State == GameRoot.GameState.InGame;
        }

        static ulong LocalEntityId()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.LocalEntityId != 0)
                return GameRoot.Instance.LocalEntityId;

            return KcpNetClient.Instance != null ? KcpNetClient.Instance.LocalEntityId : 0;
        }

        void EnsureUI()
        {
            if (built) return;
            built = true;

            EnsureCanvas();
            EnsureEventSystem();
            BuildGuide();
            BuildWindow();
        }

        void BuildGuide()
        {
            Font font = GetDefaultFont();

            guideRoot = new GameObject("BackpackGuideRoot", typeof(RectTransform));
            guideRoot.transform.SetParent(transform, false);
            StretchFullScreen(guideRoot.GetComponent<RectTransform>());
            guideRect = guideRoot.GetComponent<RectTransform>();

            GameObject back = CreatePanel("GuideBack", GuideBackColor, guideRoot.transform);
            SetRect(back.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, GuideY), new Vector2(360f, 18f));

            GameObject fill = CreatePanel("GuideFill", GuideFillColor, back.transform);
            StretchFullScreen(fill.GetComponent<RectTransform>());

            guideFill = fill.GetComponent<Image>();
            guideFill.sprite = WhiteSprite;
            guideFill.type = Image.Type.Filled;
            guideFill.fillMethod = Image.FillMethod.Horizontal;
            guideFill.fillOrigin = 0;
            guideFill.fillAmount = 0f;
            guideFill.raycastTarget = false;

            guideText = CreateText("GuideText", "长按 Tab 开启背包", 22, TextAnchor.MiddleCenter, font, guideRoot.transform);
            guideText.color = DimText;
            SetRect(guideText.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, GuideY + 30f), new Vector2(460f, 30f));

            guideRoot.SetActive(false);
        }

        void BuildWindow()
        {
            Font font = GetDefaultFont();

            panelRoot = CreatePanel("BackpackRoot", OverlayColor, transform);
            StretchFullScreen(panelRoot.GetComponent<RectTransform>());
            panelRoot.GetComponent<Image>().raycastTarget = true;

            GameObject window = CreatePanel("BackpackWindow", WindowColor, panelRoot.transform);
            SetRect(window.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(780f, 560f));
            window.GetComponent<Image>().raycastTarget = true;

            Text title = CreateText("Title", "背包", 34, TextAnchor.MiddleCenter, font, window.transform);
            title.color = MainText;
            SetRect(title.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(300f, 44f));

            Text weaponLabel = CreateText("WeaponLabel", "武器", 20, TextAnchor.MiddleLeft, font, window.transform);
            weaponLabel.color = DimText;
            PlaceTopLeft(weaponLabel.rectTransform, 40f, -96f, 120f, 26f);

            weaponSlot = CreateSlot(window.transform, "WeaponSlot", font,
                new Vector2(40f, -128f), new Vector2(140f, 140f), false);

            Text skillLabel = CreateText("SkillLabel", "技能", 20, TextAnchor.MiddleLeft, font, window.transform);
            skillLabel.color = DimText;
            PlaceTopLeft(skillLabel.rectTransform, 232f, -96f, 120f, 26f);

            for (int i = 0; i < SkillSlotCount; i++)
            {
                skillSlots[i] = CreateSlot(window.transform, "SkillSlot" + i, font,
                    new Vector2(232f + i * 136f, -128f), new Vector2(120f, 120f), false);
                skillSlots[i].Key.text = i == 0 ? "Q" : "E";
            }

            GameObject divider = CreatePanel("Divider", DividerColor, window.transform);
            PlaceTopLeft(divider.GetComponent<RectTransform>(), 40f, -300f, 700f, 2f);

            Text itemLabel = CreateText("ItemLabel", "道具", 20, TextAnchor.MiddleLeft, font, window.transform);
            itemLabel.color = DimText;
            PlaceTopLeft(itemLabel.rectTransform, 40f, -330f, 120f, 26f);

            for (int i = 0; i < ItemSlotCount; i++)
            {
                int index = i;
                itemSlots[i] = CreateSlot(window.transform, "ItemSlot" + i, font,
                    new Vector2(40f + i * 148f, -362f), new Vector2(120f, 120f), true);
                itemSlots[i].Key.text = (i + 1).ToString();
                itemSlots[i].Button.onClick.AddListener(() => UseItem(index));
            }

            Text hint = CreateText("Hint", "点击道具使用 · 按 Tab 或 Esc 关闭", 18, TextAnchor.MiddleCenter, font, window.transform);
            hint.color = DimText;
            SetRect(hint.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(600f, 24f));

            GameObject closeGo = CreatePanel("CloseButton", SlotColor, window.transform);
            PlaceTopLeft(closeGo.GetComponent<RectTransform>(), 660f, -32f, 88f, 34f);

            Button closeButton = closeGo.AddComponent<Button>();
            closeButton.targetGraphic = closeGo.GetComponent<Image>();
            closeButton.onClick.AddListener(Close);

            Text closeText = CreateText("Label", "关闭", 18, TextAnchor.MiddleCenter, font, closeGo.transform);
            closeText.color = MainText;
            StretchFullScreen(closeText.rectTransform);

            panelRoot.SetActive(false);
        }

        SlotView CreateSlot(Transform parent, string name, Font font, Vector2 position, Vector2 size, bool clickable)
        {
            GameObject slot = CreatePanel(name, SlotColor, parent);
            PlaceTopLeft(slot.GetComponent<RectTransform>(), position.x, position.y, size.x, size.y);

            Image bg = slot.GetComponent<Image>();
            bg.raycastTarget = clickable;

            GameObject icon = CreatePanel("Icon", EmptyIconColor, slot.transform);
            StretchWithPadding(icon.GetComponent<RectTransform>(), 6f, 6f);

            GameObject strip = CreatePanel("LabelStrip", LabelStripColor, slot.transform);
            RectTransform stripRect = strip.GetComponent<RectTransform>();
            stripRect.anchorMin = new Vector2(0f, 0f);
            stripRect.anchorMax = new Vector2(1f, 0f);
            stripRect.pivot = new Vector2(0.5f, 0f);
            stripRect.anchoredPosition = Vector2.zero;
            stripRect.sizeDelta = new Vector2(0f, 24f);

            SlotView view = new()
            {
                Root = slot,
                Bg = bg,
                Icon = icon.GetComponent<Image>(),
            };
            view.Icon.raycastTarget = false;

            view.Name = CreateText("Name", string.Empty, 17, TextAnchor.MiddleCenter, font, strip.transform);
            view.Name.color = MainText;
            StretchFullScreen(view.Name.rectTransform);

            view.Key = CreateText("Key", string.Empty, 16, TextAnchor.UpperLeft, font, slot.transform);
            view.Key.color = DimText;
            PlaceTopLeft(view.Key.rectTransform, 6f, -4f, size.x - 12f, 20f);

            GameObject cd = CreatePanel("Cooldown", CooldownMaskColor, slot.transform);
            StretchFullScreen(cd.GetComponent<RectTransform>());

            view.CdFill = cd.GetComponent<Image>();
            view.CdFill.sprite = WhiteSprite;
            view.CdFill.type = Image.Type.Filled;
            view.CdFill.fillMethod = Image.FillMethod.Radial360;
            view.CdFill.fillOrigin = 2;
            view.CdFill.fillClockwise = false;
            view.CdFill.fillAmount = 0f;
            view.CdFill.raycastTarget = false;

            view.CdText = CreateText("CdText", string.Empty, 20, TextAnchor.MiddleCenter, font, slot.transform);
            view.CdText.color = DimText;
            StretchFullScreen(view.CdText.rectTransform);

            view.Count = CreateText("Count", string.Empty, 18, TextAnchor.LowerRight, font, slot.transform);
            view.Count.color = CountColor;
            RectTransform countRect = view.Count.rectTransform;
            countRect.anchorMin = new Vector2(1f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(1f, 0f);
            countRect.anchoredPosition = new Vector2(-6f, 26f);
            countRect.sizeDelta = new Vector2(70f, 22f);

            if (clickable)
            {
                Button button = slot.AddComponent<Button>();
                button.targetGraphic = bg;

                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(0.88f, 0.94f, 1f, 1f);
                colors.pressedColor = new Color(0.68f, 0.78f, 0.92f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                button.colors = colors;

                view.Button = button;
            }

            return view;
        }

        void EnsureCanvas()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            if (canvas.GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static GameObject CreatePanel(string name, Color color, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return go;
        }

        static Text CreateText(string name, string content, int size, TextAnchor anchor, Font font, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = MainText;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        static void PlaceTopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        static void StretchFullScreen(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void StretchWithPadding(RectTransform rt, float horizontal, float vertical)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(horizontal, vertical);
            rt.offsetMax = new Vector2(-horizontal, -vertical);
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

                Texture2D tex = new(1, 1, TextureFormat.RGBA32, false);
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