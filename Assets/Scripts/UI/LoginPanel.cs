using Game.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    public class LoginPanel : MonoBehaviour
    {
        [SerializeField] InputField ipField;
        [SerializeField] InputField portField;
        [SerializeField] InputField roomIdField;
        [SerializeField] InputField nameField;
        [SerializeField] Button joinBtn;
        [SerializeField] Text statusText;

        static readonly Color NormalColor = new(0.86f, 0.89f, 0.93f, 1f);
        static readonly Color ErrorColor = new(1f, 0.42f, 0.38f, 1f);

        public const string DefaultServerIp = "10.215.163.93";

        static Font cachedFont;

        GameObject loginRoot;
        GameObject reconnectRoot;
        Text reconnectText;
        Button retryBtn;
        GameRoot gameRoot;
        bool uiBuilt;

        void Start()
        {
            EnsureCanvas();
            EnsureEventSystem();
            EnsureUI();

            gameRoot = GameRoot.Instance;
            if (gameRoot == null)
            {
                SetStatus("场景缺少 GameRoot，无法登录", ErrorColor);
                return;
            }

            gameRoot.LoginFailed += OnLoginFailed;
            gameRoot.LoginSucceeded += OnLoginSucceeded;
            gameRoot.LoginProgress += OnLoginProgress;
            gameRoot.Disconnected += OnDisconnected;
            gameRoot.Reconnected += OnReconnected;

            SetStatus("输入服务器地址与房间号后点击连接", NormalColor);
            if (reconnectRoot != null) reconnectRoot.SetActive(false);
        }

        void OnDestroy()
        {
            if (gameRoot == null) return;

            gameRoot.LoginFailed -= OnLoginFailed;
            gameRoot.LoginSucceeded -= OnLoginSucceeded;
            gameRoot.LoginProgress -= OnLoginProgress;
            gameRoot.Disconnected -= OnDisconnected;
            gameRoot.Reconnected -= OnReconnected;
        }

        void OnClickJoin()
        {
            if (gameRoot == null)
            {
                SetStatus("场景缺少 GameRoot，无法登录", ErrorColor);
                return;
            }

            string ip = string.IsNullOrWhiteSpace(ipField.text) ? DefaultServerIp : ipField.text.Trim();
            if (!ushort.TryParse(portField.text.Trim(), out ushort port) || port == 0)
            {
                SetStatus("端口格式不正确", ErrorColor);
                return;
            }

            string roomId = roomIdField.text.Trim();
            if (string.IsNullOrEmpty(roomId))
            {
                SetStatus("请输入房间号", ErrorColor);
                return;
            }

            string playerName = nameField.text.Trim();
            if (string.IsNullOrEmpty(playerName)) playerName = "Player";

            joinBtn.interactable = false;
            SetStatus($"正在连接 {ip}:{port} …", NormalColor);
            gameRoot.BeginLogin(ip, port, roomId, playerName);
        }

        void OnLoginProgress(string message) => SetStatus(message, NormalColor);

        void OnLoginFailed(string reason)
        {
            if (joinBtn != null) joinBtn.interactable = true;
            if (loginRoot != null) loginRoot.SetActive(true);
            if (reconnectRoot != null) reconnectRoot.SetActive(false);
            SetStatus(reason, ErrorColor);
        }

        void OnLoginSucceeded()
        {
            if (loginRoot != null) loginRoot.SetActive(false);
            if (reconnectRoot != null) reconnectRoot.SetActive(false);
        }

        void OnDisconnected()
        {
            if (loginRoot != null) loginRoot.SetActive(false);
            if (reconnectRoot != null) reconnectRoot.SetActive(true);
            if (reconnectText != null) reconnectText.text = "连接已断开，正在自动重连…";
            if (retryBtn != null) retryBtn.interactable = true;
        }

        void OnReconnected()
        {
            if (reconnectRoot != null) reconnectRoot.SetActive(false);
        }

        void OnClickRetry()
        {
            if (reconnectText != null) reconnectText.text = "正在重连…";
            if (retryBtn != null) retryBtn.interactable = false;
            KcpNetClient.Instance?.ReconnectNow();
        }

        void SetStatus(string message, Color color)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = color;
        }

        void EnsureUI()
        {
            if (uiBuilt) return;
            uiBuilt = true;

            bool manuallyWired = ipField != null && portField != null &&
                                 roomIdField != null && nameField != null && joinBtn != null;
            if (manuallyWired) return;

            Font font = GetDefaultFont();

            loginRoot = CreatePanel("LoginRoot", new Color(0.04f, 0.05f, 0.07f, 0.92f), transform);
            StretchFullScreen(loginRoot.GetComponent<RectTransform>());

            GameObject card = CreatePanel("LoginCard", new Color(0.11f, 0.12f, 0.15f, 1f), loginRoot.transform);
            SetRect(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 470f));

            Text title = CreateText("Title", "CoCoSpace · 进入房间", 30, TextAnchor.MiddleCenter, font, card.transform);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(520f, 50f));

            ipField = CreateInput(card.transform, "服务器 IP", DefaultServerIp, -110f, font);
            portField = CreateInput(card.transform, "端口", "7777", -175f, font);
            roomIdField = CreateInput(card.transform, "房间号", "room01", -240f, font);
            nameField = CreateInput(card.transform, "玩家名", "玩家1", -305f, font);

            joinBtn = CreateButton("JoinButton", "连接", new Color(0.24f, 0.58f, 0.95f, 1f), card.transform, font);
            SetRect(joinBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 84f), new Vector2(240f, 56f));
            joinBtn.onClick.AddListener(OnClickJoin);

            statusText = CreateText("Status", "", 19, TextAnchor.MiddleCenter, font, card.transform);
            SetRect(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 36f), new Vector2(520f, 40f));

            reconnectRoot = CreatePanel("ReconnectBar", new Color(0.1f, 0.11f, 0.14f, 0.96f), transform);
            SetRect(reconnectRoot.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(640f, 108f));
            reconnectRoot.SetActive(false);

            reconnectText = CreateText("ReconnectText", "", 22, TextAnchor.MiddleLeft, font, reconnectRoot.transform);
            StretchWithPadding(reconnectText.rectTransform, 24f, 12f);
            reconnectText.rectTransform.offsetMax = new Vector2(-190f, -12f);

            retryBtn = CreateButton("RetryButton", "立即重连", new Color(0.2f, 0.5f, 0.85f, 1f), reconnectRoot.transform, font);
            SetRect(retryBtn.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(150f, 52f));
            retryBtn.onClick.AddListener(OnClickRetry);
        }

        InputField CreateInput(Transform parent, string label, string value, float y, Font font)
        {
            Text labelText = CreateText($"Label_{label}", label, 22, TextAnchor.MiddleRight, font, parent);
            SetRect(labelText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(160f, 46f));

            GameObject box = CreatePanel($"Input_{label}", new Color(0.18f, 0.19f, 0.23f, 1f), parent);
            SetRect(box.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(195f, y), new Vector2(330f, 46f));

            Text text = CreateText("Text", value, 22, TextAnchor.MiddleLeft, font, box.transform);
            text.color = new Color(0.95f, 0.96f, 0.98f, 1f);
            text.supportRichText = false;
            StretchWithPadding(text.rectTransform, 12f, 6f);

            Text placeholder = CreateText("Placeholder", value, 22, TextAnchor.MiddleLeft, font, box.transform);
            placeholder.color = new Color(0.55f, 0.58f, 0.62f, 1f);
            placeholder.supportRichText = false;
            StretchWithPadding(placeholder.rectTransform, 12f, 6f);

            InputField field = box.AddComponent<InputField>();
            field.targetGraphic = box.GetComponent<Image>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.text = value;
            return field;
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
            text.color = NormalColor;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Button CreateButton(string name, string label, Color color, Transform parent, Font font)
        {
            GameObject go = CreatePanel(name, Color.white, parent);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();

            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = new Color(color.r * 1.15f, color.g * 1.15f, color.b * 1.15f, 1f);
            colors.pressedColor = new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, 1f);
            colors.disabledColor = new Color(0.32f, 0.33f, 0.36f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text text = CreateText("Label", label, 24, TextAnchor.MiddleCenter, font, go.transform);
            text.color = Color.white;
            StretchWithPadding(text.rectTransform, 0f, 0f);
            return button;
        }

        void EnsureCanvas()
        {
            if (GetComponent<Canvas>() == null)
            {
                Canvas canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
            }

            if (GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
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
    }
}