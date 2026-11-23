using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    public class ShopUI : MonoBehaviour
    {
        enum Tab { Mine, Team }

        const int BuyResultMsgId = 2016;
        const float WindowWidth = 1060f;
        const float WindowHeight = 720f;
        const float ToastSeconds = 1.8f;
        const float FloatSeconds = 1.2f;
        const int Columns = 4;

        static readonly Color OverlayColor = new(0.02f, 0.03f, 0.05f, 0.72f);
        static readonly Color WindowColor = new(0.07f, 0.09f, 0.13f, 0.97f);
        static readonly Color CardColor = new(0.11f, 0.13f, 0.18f, 1f);
        static readonly Color TabOn = new(0.18f, 0.42f, 0.78f, 1f);
        static readonly Color TabOff = new(0.10f, 0.12f, 0.17f, 1f);
        static readonly Color MainText = new(0.94f, 0.96f, 0.99f, 1f);
        static readonly Color DimText = new(0.66f, 0.71f, 0.78f, 1f);
        static readonly Color GoldText = new(0.96f, 0.82f, 0.34f, 1f);
        static readonly Color PoorText = new(0.86f, 0.42f, 0.40f, 1f);
        static readonly Color WarnText = new(0.95f, 0.62f, 0.32f, 1f);
        static readonly Color DimMask = new(0f, 0f, 0f, 0.45f);

        class CardView
        {
            public Button Button;
            public Image Icon;
            public Image Dim;
            public Text NameLabel;
            public Text TypeLabel;
            public Text PriceLabel;
            public uint Price;
        }

        class FloatItem
        {
            public Text Text;
            public float Life;
        }

        [SerializeField] GameObject itemCardPrefab;
        [SerializeField] HudPanel hudPanel;

        GameObject panelRoot;
        GameObject window;
        GameObject shelfRoot;
        GameObject teammateBar;
        Button myTab;
        Button teamTab;
        Text goldLabel;
        Text toastLabel;
        Text emptyLabel;

        readonly List<ShopItem> myItems = new();
        readonly Dictionary<ulong, List<ShopItem>> teamShelves = new();
        readonly List<ulong> teamOrder = new();
        readonly List<CardView> cards = new();
        readonly List<FloatItem> floats = new();

        Tab tab = Tab.Mine;
        ulong currentTeamOwner;
        bool open;
        bool built;
        bool goldInitialized;
        uint goldShown;
        float toastRemaining;

        static Font cachedFont;

        public bool IsOpen => open;

        void Awake()
        {
            if (hudPanel == null) hudPanel = FindObjectOfType<HudPanel>();
            EnsureUI();
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CShopOpen>(OnShopOpen);
            MsgDispatcher.RegisterRaw((MsgId)BuyResultMsgId, OnBuyResult);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CShopOpen>();
            MsgDispatcher.UnregisterRaw((MsgId)BuyResultMsgId);
        }

        void Update()
        {
            if (!open) return;

            RefreshGold();
            TickToast();
            TickFloats();

            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void OnShopOpen(S2CShopOpen msg)
        {
            if (msg == null) return;

            EnsureUI();

            myItems.Clear();
            myItems.AddRange(msg.MyItems);

            if (msg.OwnerEntityId != 0)
            {
                if (!teamShelves.TryGetValue(msg.OwnerEntityId, out List<ShopItem> shelf))
                {
                    shelf = new List<ShopItem>();
                    teamShelves[msg.OwnerEntityId] = shelf;
                    teamOrder.Add(msg.OwnerEntityId);
                }

                shelf.Clear();
                shelf.AddRange(msg.TeamItems);
                currentTeamOwner = msg.OwnerEntityId;
            }

            if (currentTeamOwner == 0 && teamOrder.Count > 0)
                currentTeamOwner = teamOrder[0];

            Show();
        }

        void Show()
        {
            open = true;
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();

            goldInitialized = false;
            RefreshGold();

            RebuildTeammateBar();
            SelectTab(tab);
        }

        void Close()
        {
            open = false;
            if (panelRoot != null) panelRoot.SetActive(false);

            cards.Clear();
            toastRemaining = 0f;
            if (toastLabel != null) toastLabel.text = string.Empty;

            for (int i = floats.Count - 1; i >= 0; i--)
            {
                if (floats[i].Text != null) Destroy(floats[i].Text.gameObject);
            }
            floats.Clear();
        }

        void SelectTab(Tab next)
        {
            tab = next;

            if (myTab != null && myTab.targetGraphic != null)
                myTab.targetGraphic.color = next == Tab.Mine ? TabOn : TabOff;

            if (teamTab != null && teamTab.targetGraphic != null)
                teamTab.targetGraphic.color = next == Tab.Team ? TabOn : TabOff;

            RenderCurrentShelf();
        }

        void RenderCurrentShelf()
        {
            if (tab == Tab.Mine)
            {
                RenderShelf(myItems, 0);
                return;
            }

            List<ShopItem> shelf = null;
            if (currentTeamOwner != 0)
                teamShelves.TryGetValue(currentTeamOwner, out shelf);

            RenderShelf(shelf, currentTeamOwner);
        }

        void RenderShelf(List<ShopItem> items, ulong owner)
        {
            for (int i = shelfRoot.transform.childCount - 1; i >= 0; i--)
                Destroy(shelfRoot.transform.GetChild(i).gameObject);

            cards.Clear();

            if (items == null || items.Count == 0)
            {
                emptyLabel.gameObject.SetActive(true);
                emptyLabel.text = tab == Tab.Team && teamOrder.Count == 0
                    ? "还没有队友打开过商店"
                    : "货架空空如也";
                return;
            }

            emptyLabel.gameObject.SetActive(false);

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                cards.Add(CreateCard(items[i], owner));
            }

            RefreshCardState(CurrentGold());
        }

        CardView CreateCard(ShopItem item, ulong owner)
        {
            GameObject go = itemCardPrefab != null
                ? Instantiate(itemCardPrefab, shelfRoot.transform)
                : BuildRuntimeCard(shelfRoot.transform);

            CardView view = ResolveCard(go);
            view.Price = item.Price;

            if (view.NameLabel != null) view.NameLabel.text = item.Name;
            if (view.TypeLabel != null)
            {
                view.TypeLabel.text = TypeName(item.Type);
                view.TypeLabel.color = DimText;
            }
            if (view.PriceLabel != null) view.PriceLabel.text = item.Price.ToString();

            ApplyIcon(view.Icon, item);

            if (view.Button != null)
            {
                view.Button.onClick.RemoveAllListeners();
                view.Button.onClick.AddListener(() => Buy(item.ItemId, item.Price, owner));
            }

            return view;
        }

        void Buy(uint itemId, uint price, ulong owner)
        {
            KcpNetClient net = KcpNetClient.Instance;
            if (net == null) return;

            if (net.LocalGold < price) ShowToast("金币不足", WarnText);

            net.SendEnvelope(new Envelope
            {
                MsgId = MsgId.C2SBuy,
                Payload = ByteString.CopyFrom(
                    new C2SBuy { ItemId = itemId, ForTeammateEntityId = owner }.ToByteArray()),
            });
        }

        void OnBuyResult(ByteString payload)
        {
            if (!TryParseBuyResult(payload, out bool success, out ulong receiver, out uint newGold)) return;

            if (!success)
            {
                ShowToast("金币不足，购买失败", WarnText);
                RefreshCardState(CurrentGold());
                return;
            }

            ulong selfId = GameRoot.Instance != null ? GameRoot.Instance.LocalEntityId : 0ul;
            ShowToast(receiver != 0 && receiver != selfId ? "已为队友购买" : "购买成功", MainText);

            if (newGold == 0) return;

            goldInitialized = true;
            goldShown = newGold;
            goldLabel.text = "金币 " + newGold;
            RefreshCardState(newGold);

            if (hudPanel != null) hudPanel.ApplyGold(newGold);
        }

        void RefreshGold()
        {
            uint gold = CurrentGold();

            if (!goldInitialized)
            {
                goldInitialized = true;
                goldShown = gold;
                goldLabel.text = "金币 " + gold;
                RefreshCardState(gold);
                return;
            }

            if (gold == goldShown) return;

            if (gold > goldShown) ShowFloat("+" + (gold - goldShown) + " 金币");

            goldShown = gold;
            goldLabel.text = "金币 " + gold;
            RefreshCardState(gold);
        }

        void RefreshCardState(uint gold)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                CardView card = cards[i];
                bool affordable = gold >= card.Price;

                if (card.PriceLabel != null)
                    card.PriceLabel.color = affordable ? GoldText : PoorText;

                if (card.NameLabel != null)
                    card.NameLabel.color = affordable ? MainText : DimText;

                if (card.Dim != null)
                    card.Dim.gameObject.SetActive(!affordable);
            }
        }

        void RebuildTeammateBar()
        {
            if (teammateBar == null) return;

            for (int i = teammateBar.transform.childCount - 1; i >= 0; i--)
                Destroy(teammateBar.transform.GetChild(i).gameObject);

            for (int i = 0; i < teamOrder.Count; i++)
            {
                ulong id = teamOrder[i];

                GameObject go = CreatePanel("Teammate" + i, id == currentTeamOwner ? TabOn : TabOff, teammateBar.transform);
                go.GetComponent<Image>().raycastTarget = true;

                LayoutElement element = go.AddComponent<LayoutElement>();
                element.preferredWidth = 176f;
                element.preferredHeight = 40f;

                Button button = go.AddComponent<Button>();
                button.targetGraphic = go.GetComponent<Image>();

                Text label = CreateText("Label", "队友 " + (i + 1), 20, TextAnchor.MiddleCenter, GetDefaultFont(), go.transform);
                StretchFullScreen(label.rectTransform);

                ulong captured = id;
                button.onClick.AddListener(() =>
                {
                    currentTeamOwner = captured;
                    RebuildTeammateBar();
                    SelectTab(Tab.Team);
                });
            }
        }

        void ShowToast(string content, Color color)
        {
            toastLabel.text = content;
            toastLabel.color = color;
            toastRemaining = ToastSeconds;
        }

        void TickToast()
        {
            if (toastRemaining <= 0f) return;

            toastRemaining -= Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(toastRemaining / ToastSeconds);

            Color color = toastLabel.color;
            toastLabel.color = new Color(color.r, color.g, color.b, alpha);

            if (toastRemaining > 0f) return;

            toastRemaining = 0f;
            toastLabel.text = string.Empty;
        }

        void ShowFloat(string content)
        {
            Text text = CreateText("Float", content, 26, TextAnchor.MiddleCenter, GetDefaultFont(), window.transform);
            SetRect(text.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-190f, -74f), new Vector2(320f, 34f));
            text.color = GoldText;

            floats.Add(new FloatItem { Text = text, Life = FloatSeconds });
        }

        void TickFloats()
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                FloatItem item = floats[i];
                if (item.Text == null)
                {
                    floats.RemoveAt(i);
                    continue;
                }

                item.Life -= Time.unscaledDeltaTime;

                RectTransform rt = item.Text.rectTransform;
                rt.anchoredPosition += new Vector2(0f, 52f * Time.unscaledDeltaTime);

                float alpha = Mathf.Clamp01(item.Life / (FloatSeconds * 0.6f));
                item.Text.color = new Color(GoldText.r, GoldText.g, GoldText.b, alpha);

                if (item.Life > 0f) continue;

                Destroy(item.Text.gameObject);
                floats.RemoveAt(i);
            }
        }

        void EnsureUI()
        {
            if (built) return;
            built = true;

            EnsureCanvas();
            EnsureEventSystem();
            BuildRuntimeUI();
        }

        void BuildRuntimeUI()
        {
            Font font = GetDefaultFont();

            panelRoot = CreatePanel("ShopRoot", OverlayColor, transform);
            StretchFullScreen(panelRoot.GetComponent<RectTransform>());
            panelRoot.GetComponent<Image>().raycastTarget = true;

            window = CreatePanel("ShopWindow", WindowColor, panelRoot.transform);
            SetRect(window.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(WindowWidth, WindowHeight));

            Text title = CreateText("Title", "商店", 34, TextAnchor.MiddleLeft, font, window.transform);
            title.color = MainText;
            SetRect(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -22f), new Vector2(240f, 44f));

            goldLabel = CreateText("Gold", "金币 0", 26, TextAnchor.MiddleRight, font, window.transform);
            goldLabel.color = GoldText;
            SetRect(goldLabel.rectTransform,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-136f, -24f), new Vector2(340f, 38f));

            CreateCloseButton(font);

            myTab = CreateTabButton("MyShelfTab", "我的货架", font, 28f, Tab.Mine);
            teamTab = CreateTabButton("TeamShelfTab", "队友货架", font, 250f, Tab.Team);

            teammateBar = new GameObject("TeammateBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            teammateBar.transform.SetParent(window.transform, false);
            SetRect(teammateBar.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -150f), new Vector2(760f, 40f));

            HorizontalLayoutGroup bar = teammateBar.GetComponent<HorizontalLayoutGroup>();
            bar.spacing = 12f;
            bar.childAlignment = TextAnchor.MiddleLeft;
            bar.childControlWidth = false;
            bar.childControlHeight = false;
            bar.childForceExpandWidth = false;
            bar.childForceExpandHeight = false;

            BuildShelfGrid();

            emptyLabel = CreateText("EmptyHint", "货架空空如也", 24, TextAnchor.MiddleCenter, font, window.transform);
            emptyLabel.color = DimText;
            SetRect(emptyLabel.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -40f), new Vector2(600f, 40f));
            emptyLabel.gameObject.SetActive(false);

            toastLabel = CreateText("Toast", string.Empty, 24, TextAnchor.MiddleCenter, font, window.transform);
            SetRect(toastLabel.rectTransform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 32f), new Vector2(820f, 36f));

            panelRoot.SetActive(false);
        }

        void BuildShelfGrid()
        {
            shelfRoot = new GameObject("Shelf", typeof(RectTransform), typeof(GridLayoutGroup));
            shelfRoot.transform.SetParent(window.transform, false);

            RectTransform rt = shelfRoot.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(28f, 96f);
            rt.offsetMax = new Vector2(-28f, -200f);

            GridLayoutGroup grid = shelfRoot.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(232f, 152f);
            grid.spacing = new Vector2(16f, 16f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperLeft;
        }

        Button CreateTabButton(string name, string content, Font font, float x, Tab target)
        {
            GameObject go = CreatePanel(name, TabOff, window.transform);
            SetRect(go.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -82f), new Vector2(206f, 54f));

            go.GetComponent<Image>().raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();

            Text label = CreateText("Label", content, 24, TextAnchor.MiddleCenter, font, go.transform);
            StretchFullScreen(label.rectTransform);

            button.onClick.AddListener(() => SelectTab(target));
            return button;
        }

        void CreateCloseButton(Font font)
        {
            GameObject go = CreatePanel("CloseButton", TabOff, window.transform);
            SetRect(go.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-28f, -24f), new Vector2(96f, 42f));

            go.GetComponent<Image>().raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();

            Text label = CreateText("Label", "关闭", 22, TextAnchor.MiddleCenter, font, go.transform);
            label.color = DimText;
            StretchFullScreen(label.rectTransform);

            button.onClick.AddListener(Close);
        }

        GameObject BuildRuntimeCard(Transform parent)
        {
            Font font = GetDefaultFont();

            GameObject card = CreatePanel("ItemCard", CardColor, parent);
            card.GetComponent<Image>().raycastTarget = true;

            Button button = card.AddComponent<Button>();
            button.targetGraphic = card.GetComponent<Image>();

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(0.76f, 0.82f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            GameObject icon = CreatePanel("Icon", Color.gray, card.transform);
            icon.GetComponent<Image>().raycastTarget = false;
            SetRect(icon.GetComponent<RectTransform>(),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -14f), new Vector2(62f, 62f));

            Text name = CreateText("Name", string.Empty, 22, TextAnchor.MiddleLeft, font, card.transform);
            name.color = MainText;
            SetRect(name.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(86f, -18f), new Vector2(132f, 30f));

            Text type = CreateText("Type", string.Empty, 16, TextAnchor.MiddleLeft, font, card.transform);
            type.color = DimText;
            SetRect(type.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(86f, -52f), new Vector2(132f, 24f));

            Text price = CreateText("Price", string.Empty, 20, TextAnchor.MiddleRight, font, card.transform);
            price.color = GoldText;
            SetRect(price.rectTransform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-14f, 14f), new Vector2(150f, 28f));

            GameObject dim = CreatePanel("Dim", DimMask, card.transform);
            StretchFullScreen(dim.GetComponent<RectTransform>());
            dim.GetComponent<Image>().raycastTarget = false;
            dim.SetActive(false);

            return card;
        }

        static CardView ResolveCard(GameObject card)
        {
            Button button = card.GetComponent<Button>();
            if (button == null) button = card.GetComponentInChildren<Button>(true);

            return new CardView
            {
                Button = button,
                Icon = FindChild<Image>(card.transform, "Icon"),
                Dim = FindChild<Image>(card.transform, "Dim"),
                NameLabel = FindChild<Text>(card.transform, "Name"),
                TypeLabel = FindChild<Text>(card.transform, "Type"),
                PriceLabel = FindChild<Text>(card.transform, "Price"),
            };
        }

        static void ApplyIcon(Image icon, ShopItem item)
        {
            if (icon == null) return;

            Sprite sprite = string.IsNullOrEmpty(item.IconPath) ? null : Resources.Load<Sprite>(item.IconPath);
            icon.sprite = sprite;
            icon.color = sprite != null ? Color.white : TypeColor(item.Type);
        }

        static Color TypeColor(uint type)
        {
            switch (type)
            {
                case 1: return new Color(0.78f, 0.42f, 0.30f, 1f);
                case 2: return new Color(0.36f, 0.56f, 0.92f, 1f);
                case 3: return new Color(0.42f, 0.74f, 0.46f, 1f);
                default: return new Color(0.45f, 0.48f, 0.55f, 1f);
            }
        }

        static string TypeName(uint type)
        {
            switch (type)
            {
                case 1: return "武器";
                case 2: return "技能";
                case 3: return "道具";
                default: return "道具";
            }
        }

        static bool TryParseBuyResult(ByteString payload, out bool success, out ulong receiver, out uint newGold)
        {
            success = false;
            receiver = 0;
            newGold = 0;

            if (payload == null || payload.Length == 0) return false;

            try
            {
                CodedInputStream input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8: success = input.ReadBool(); break;
                        case 16: input.ReadUInt32(); break;
                        case 24: input.ReadUInt64(); break;
                        case 32: receiver = input.ReadUInt64(); break;
                        case 40: newGold = input.ReadUInt32(); break;
                        default: input.SkipLastField(); break;
                    }
                }
            }
            catch (InvalidProtocolBufferException e)
            {
                Debug.LogWarning("[Shop] S2C_BUY_RESULT 解析失败: " + e.Message);
                return false;
            }

            return true;
        }

        static uint CurrentGold()
        {
            KcpNetClient net = KcpNetClient.Instance;
            return net != null ? net.LocalGold : 0u;
        }

        static T FindChild<T>(Transform root, string childName) where T : Component
        {
            T[] children = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].gameObject.name == childName) return children[i];
            }

            return null;
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

        static void StretchFullScreen(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
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
    }
}