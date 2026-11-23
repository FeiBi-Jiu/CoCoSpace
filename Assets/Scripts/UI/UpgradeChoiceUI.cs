using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    public class UpgradeChoiceUI : MonoBehaviour
    {
        const int MaxChoices = 3;
        const float DefaultTimeoutSeconds = 10f;

        static readonly Color OverlayColor = new(0.02f, 0.03f, 0.05f, 0.72f);
        static readonly Color WindowColor = new(0.07f, 0.09f, 0.13f, 0.97f);
        static readonly Color CardColor = new(0.11f, 0.13f, 0.18f, 1f);
        static readonly Color MainText = new(0.94f, 0.96f, 0.99f, 1f);
        static readonly Color DimText = new(0.66f, 0.71f, 0.78f, 1f);
        static readonly Color BarBack = new(0.04f, 0.05f, 0.07f, 0.95f);
        static readonly Color BarFill = new(0.30f, 0.72f, 1f, 1f);

        [SerializeField] Button[] cards;
        [SerializeField] Image countdownFill;
        [SerializeField] Text countdownText;
        [SerializeField] GameObject panelRoot;
        [SerializeField, Range(0.05f, 1f)] float bulletTimeScale = 0.3f;
        [SerializeField] bool autoPickOnTimeout = true;

        class CardView
        {
            public Button Button;
            public Image Border;
            public Image Icon;
            public Text Name;
            public Text Description;
            public Text Rarity;
        }

        static Font cachedFont;
        static Sprite cachedWhiteSprite;

        readonly List<CardView> cardViews = new();
        S2CUpgradeChoice current;
        bool open;
        bool built;
        float remaining;
        float total;
        float previousTimeScale = 1f;

        void Awake()
        {
            EnsureUI();
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CUpgradeChoice>(Show);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CUpgradeChoice>();
            Close(true);
        }

        void Update()
        {
            if (!open) return;

            remaining -= Time.unscaledDeltaTime;
            if (remaining <= 0f)
            {
                remaining = 0f;
                RefreshCountdown();

                if (autoPickOnTimeout) Choose(0);
                else Close();
                return;
            }

            RefreshCountdown();
        }

        void Show(S2CUpgradeChoice msg)
        {
            if (msg == null) return;

            EnsureUI();

            if (msg.Options.Count == 0)
            {
                Close();
                return;
            }

            current = msg;

            if (!open)
            {
                previousTimeScale = Time.timeScale;
                open = true;
            }

            Time.timeScale = bulletTimeScale;

            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();

            for (int i = 0; i < cardViews.Count; i++)
            {
                CardView view = cardViews[i];
                bool has = i < msg.Options.Count && i < MaxChoices;

                view.Button.gameObject.SetActive(has);
                if (!has) continue;

                ApplyOption(view, msg.Options[i]);

                int index = i;
                view.Button.onClick.RemoveAllListeners();
                view.Button.onClick.AddListener(() => Choose(index));
            }

            total = msg.TimeoutMs > 0 ? msg.TimeoutMs / 1000f : DefaultTimeoutSeconds;
            remaining = total;
            RefreshCountdown();
        }

        void Choose(int index)
        {
            if (!open) return;

            if (current != null && current.Options.Count > 0)
            {
                int safeIndex = Mathf.Clamp(index, 0, current.Options.Count - 1);
                KcpNetClient net = KcpNetClient.Instance;

                if (net != null)
                {
                    net.SendEnvelope(new Envelope
                    {
                        MsgId = MsgId.C2SPickUpgrade,
                        Payload = ByteString.CopyFrom(
                            new C2SPickUpgrade { Index = (uint)safeIndex }.ToByteArray()),
                    });
                }
            }

            Close();
        }

        void Close(bool force = false)
        {
            if (!open && !force) return;

            bool wasOpen = open;
            open = false;
            current = null;

            if (panelRoot != null)
                panelRoot.SetActive(false);

            if (wasOpen && Mathf.Approximately(Time.timeScale, bulletTimeScale))
                Time.timeScale = previousTimeScale;
        }

        void RefreshCountdown()
        {
            if (countdownFill != null)
                countdownFill.fillAmount = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;

            if (countdownText != null)
                countdownText.text = Mathf.CeilToInt(remaining) + " 秒";
        }

        static void ApplyOption(CardView view, UpgradeOption option)
        {
            Color rarityColor = RarityColor(option.Rarity);

            if (view.Border != null)
                view.Border.color = rarityColor;

            if (view.Icon != null)
                view.Icon.color = rarityColor;

            if (view.Name != null)
                view.Name.text = option.Name;

            if (view.Description != null)
                view.Description.text = option.Desc;

            if (view.Rarity != null)
                view.Rarity.text = RarityName(option.Rarity);
        }

        static Color RarityColor(uint rarity)
        {
            switch (rarity)
            {
                case 1: return new Color(0.25f, 0.62f, 1f, 1f);
                case 2: return new Color(0.72f, 0.40f, 1f, 1f);
                case 3: return new Color(1f, 0.72f, 0.25f, 1f);
                default: return new Color(0.55f, 0.60f, 0.66f, 1f);
            }
        }

        static string RarityName(uint rarity)
        {
            switch (rarity)
            {
                case 1: return "稀有";
                case 2: return "史诗";
                case 3: return "传说";
                default: return "普通";
            }
        }

        void EnsureUI()
        {
            if (built) return;
            built = true;

            EnsureCanvas();
            EnsureEventSystem();

            if (panelRoot != null && cards != null && cards.Length > 0)
            {
                WrapCards(cards);
                panelRoot.SetActive(false);
                return;
            }

            BuildRuntimeUI();
        }

        void BuildRuntimeUI()
        {
            Font font = GetDefaultFont();

            panelRoot = CreatePanel("UpgradeChoiceRoot", OverlayColor, transform);
            StretchFullScreen(panelRoot.GetComponent<RectTransform>());
            panelRoot.GetComponent<Image>().raycastTarget = true;

            GameObject window = CreatePanel("UpgradeWindow", WindowColor, panelRoot.transform);
            SetRect(window.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1240f, 680f));

            Text title = CreateText("Title", "升级！选择一项强化", 36, TextAnchor.MiddleCenter, font, window.transform);
            title.color = MainText;
            SetRect(title.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -52f), new Vector2(800f, 50f));

            CreateCountdown(window.transform, font);

            GameObject cardRoot = new("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cardRoot.transform.SetParent(window.transform, false);
            SetRect(cardRoot.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -68f), new Vector2(1120f, 420f));

            HorizontalLayoutGroup layout = cardRoot.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 32f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            cards = new Button[MaxChoices];
            for (int i = 0; i < MaxChoices; i++)
                cards[i] = CreateCard(cardRoot.transform, font, i);

            WrapCards(cards);
            panelRoot.SetActive(false);
        }

        void CreateCountdown(Transform parent, Font font)
        {
            GameObject bar = CreatePanel("CountdownBack", BarBack, parent);
            bar.GetComponent<Image>().raycastTarget = false;
            SetRect(bar.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -108f), new Vector2(760f, 22f));

            GameObject fill = CreatePanel("CountdownFill", BarFill, bar.transform);
            StretchFullScreen(fill.GetComponent<RectTransform>());

            countdownFill = fill.GetComponent<Image>();
            countdownFill.sprite = WhiteSprite;
            countdownFill.type = Image.Type.Filled;
            countdownFill.fillMethod = Image.FillMethod.Horizontal;
            countdownFill.fillOrigin = 0;
            countdownFill.fillAmount = 1f;
            countdownFill.raycastTarget = false;

            countdownText = CreateText("CountdownText", "", 24, TextAnchor.MiddleCenter, font, parent);
            countdownText.color = DimText;
            SetRect(countdownText.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -146f), new Vector2(760f, 36f));
        }

        Button CreateCard(Transform parent, Font font, int index)
        {
            GameObject card = CreatePanel("Card" + index, Color.white, parent);
            LayoutElement layoutElement = card.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 340f;
            layoutElement.preferredHeight = 420f;

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

            GameObject inner = CreatePanel("Inner", CardColor, card.transform);
            StretchWithPadding(inner.GetComponent<RectTransform>(), 4f, 4f);
            inner.GetComponent<Image>().raycastTarget = false;

            GameObject icon = CreatePanel("Icon", Color.gray, inner.transform);
            icon.GetComponent<Image>().raycastTarget = false;
            SetRect(icon.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(104f, 104f));

            Text rarity = CreateText("Rarity", "", 20, TextAnchor.MiddleLeft, font, inner.transform);
            rarity.color = DimText;
            SetRect(rarity.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(160f, 28f));

            Text name = CreateText("Name", "", 28, TextAnchor.MiddleCenter, font, inner.transform);
            name.color = MainText;
            SetRect(name.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -154f), new Vector2(300f, 42f));

            Text desc = CreateText("Desc", "", 20, TextAnchor.UpperLeft, font, inner.transform);
            desc.color = DimText;
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.verticalOverflow = VerticalWrapMode.Truncate;
            desc.lineSpacing = 1.2f;
            desc.rectTransform.anchorMin = Vector2.zero;
            desc.rectTransform.anchorMax = Vector2.one;
            desc.rectTransform.offsetMin = new Vector2(24f, 24f);
            desc.rectTransform.offsetMax = new Vector2(-24f, -214f);

            return button;
        }

        void WrapCards(Button[] source)
        {
            cardViews.Clear();

            for (int i = 0; i < source.Length; i++)
            {
                Button button = source[i];
                if (button == null) continue;

                cardViews.Add(new CardView
                {
                    Button = button,
                    Border = button.GetComponent<Image>(),
                    Icon = FindChild<Image>(button.transform, "Icon"),
                    Name = FindChild<Text>(button.transform, "Name"),
                    Description = FindChild<Text>(button.transform, "Desc"),
                    Rarity = FindChild<Text>(button.transform, "Rarity"),
                });
            }
        }

        static T FindChild<T>(Transform root, string childName) where T : Component
        {
            T[] children = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].gameObject.name == childName)
                    return children[i];
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
            go.GetComponent<Image>().color = color;
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