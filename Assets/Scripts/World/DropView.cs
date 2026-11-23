using System;
using System.Collections;
using Game.Table;
using UnityEngine;
using UnityEngine.UI;

namespace Game.World
{
    public enum DropType : uint
    {
        Skill = 1,
        Coin = 2,
        Potion = 3,
        Item = 4,
    }

    public class DropView : MonoBehaviour
    {
        public const double Lifetime = 10.0;

        [Header("引用（留空则运行时自动生成）")]
        [SerializeField] Image decayRing;
        [SerializeField] GameObject ghostOnlyTag;
        [SerializeField] GameObject highlightFx;
        [SerializeField] SpriteRenderer icon;

        [Header("场景摆放（运行时由 DropManager 覆盖）")]
        [SerializeField] DropType type = DropType.Skill;
        [SerializeField] uint elementId = 1;
        [SerializeField] bool ghostOnly;
        [SerializeField] bool setupOnStart = true;

        [Header("外观")]
        [SerializeField] Color coinColor = new(1f, 0.83f, 0.25f, 1f);
        [SerializeField] Color potionColor = new(0.9f, 0.25f, 0.3f, 1f);
        [SerializeField] Color itemColor = new(0.78f, 0.8f, 0.88f, 1f);
        [SerializeField] Color ghostColor = new(0.5f, 0.85f, 1f, 1f);
        [SerializeField] Color warnRingColor = new(1f, 0.28f, 0.22f, 0.95f);
        [SerializeField] float iconHeight = 0.45f;
        [SerializeField] float ringDiameter = 1.2f;
        [SerializeField] float bobAmplitude = 0.06f;
        [SerializeField] float bobSpeed = 2.4f;
        [SerializeField] float spinSpeed = 60f;
        [SerializeField] float pickupSeconds = 0.25f;
        [SerializeField] float decaySeconds = 0.35f;

        static Sprite ringSprite;
        static Sprite circleSprite;
        static Sprite diamondSprite;
        static Sprite crossSprite;
        static Sprite squareSprite;
        static Font labelFont;

        double despawnAt;
        bool skillDrop;
        bool ghostHighlight;
        bool initialized;
        bool finishing;
        float bobPhase;
        Color ringColor = Color.white;
        SpriteRenderer highlightRenderer;

        public bool GhostOnly { get; private set; }
        public DropType Type { get; private set; }
        public uint DropId { get; set; }

        void Awake()
        {
            Type = type;
            bobPhase = Mathf.PerlinNoise(transform.position.x + 0.37f, transform.position.z + 0.11f) * Mathf.PI * 2f;
            EnsureVisuals();
            ApplyVisuals();
        }

        void Start()
        {
            if (initialized || !setupOnStart) return;
            Setup((uint)type, ghostOnly, elementId);
        }

        public void Setup(uint typeId, bool ghostOnlyFlag, uint element)
        {
            GhostOnly = ghostOnlyFlag;
            if (element != 0) elementId = element;

            Type = ToDropType(typeId);
            type = Type;
            skillDrop = Type == DropType.Skill;
            despawnAt = Time.timeAsDouble + Lifetime;
            initialized = true;
            finishing = false;

            EnsureVisuals();
            ApplyVisuals();

            if (ghostOnlyTag != null) ghostOnlyTag.SetActive(ghostOnlyFlag);

            if (decayRing != null)
            {
                decayRing.fillAmount = 1f;
                decayRing.gameObject.SetActive(skillDrop);
            }
        }

        public void Setup(bool ghostOnlyFlag, bool isSkillDrop)
        {
            Setup(isSkillDrop ? (uint)DropType.Skill : (uint)DropType.Item, ghostOnlyFlag, 0);
        }

        public void SetElement(uint element)
        {
            if (element == 0 || element == elementId) return;
            elementId = element;
            ApplyVisuals();
        }

        public void SetGhostHighlight(bool on)
        {
            if (ghostHighlight == on) return;
            ghostHighlight = on;
            RefreshHighlight();
        }

        public void PlayPickup(Transform picker)
        {
            if (finishing) return;
            finishing = true;

            if (!gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(PickupRoutine(picker));
        }

        public void PlayDecay()
        {
            if (finishing) return;
            finishing = true;

            if (!gameObject.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(DecayRoutine());
        }

        IEnumerator PickupRoutine(Transform picker)
        {
            SetExtrasVisible(false);

            Vector3 from = transform.position;
            Vector3 fromScale = transform.localScale;
            float t = 0f;

            while (t < pickupSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / pickupSeconds);
                if (picker != null)
                    transform.position = Vector3.Lerp(from, picker.position + Vector3.up * 0.6f, k);
                transform.localScale = Vector3.Lerp(fromScale, fromScale * 0.1f, k);
                yield return null;
            }

            Destroy(gameObject);
        }

        IEnumerator DecayRoutine()
        {
            SetExtrasVisible(false);
            if (decayRing != null) decayRing.gameObject.SetActive(false);

            Vector3 fromScale = transform.localScale;
            Color fromIcon = icon != null ? icon.color : Color.white;
            float t = 0f;

            while (t < decaySeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / decaySeconds);

                transform.localScale = Vector3.Lerp(fromScale, fromScale * 0.15f, k);

                if (icon != null)
                {
                    Color c = fromIcon;
                    c.a = 1f - k;
                    icon.color = c;
                }

                if (highlightFx != null && highlightFx.activeSelf)
                {
                    float scale = ringDiameter * 1.6f * (1f + k * 1.5f);
                    highlightFx.transform.localScale = new Vector3(scale, scale, 1f);

                    if (highlightRenderer != null)
                    {
                        Color c = highlightRenderer.color;
                        c.a = 0.6f * (1f - k);
                        highlightRenderer.color = c;
                    }
                }

                yield return null;
            }

            Destroy(gameObject);
        }

        void Update()
        {
            UpdateBillboard();
            UpdateMotion();

            if (!initialized || finishing || !skillDrop) return;

            float ratio = Mathf.Clamp01((float)((despawnAt - Time.timeAsDouble) / Lifetime));

            if (decayRing != null)
            {
                decayRing.fillAmount = ratio;
                decayRing.color = ratio <= 0.3f ? warnRingColor : ringColor;
            }

            if (ratio <= 0f) PlayDecay();
        }

        void UpdateBillboard()
        {
            if (ghostOnlyTag == null || !ghostOnlyTag.activeInHierarchy) return;

            var cam = Camera.main;
            if (cam == null) return;

            ghostOnlyTag.transform.rotation = cam.transform.rotation;
        }

        void UpdateMotion()
        {
            if (icon == null) return;

            float y = iconHeight + Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobAmplitude;
            icon.transform.localPosition = new Vector3(0f, y, 0f);

            if (Type == DropType.Coin)
                icon.transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);

            if (highlightFx != null && highlightFx.activeSelf)
            {
                float scale = ringDiameter * 1.6f * (1f + 0.05f * Mathf.Sin(Time.time * 4f));
                highlightFx.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        void SetExtrasVisible(bool visible)
        {
            if (ghostOnlyTag != null) ghostOnlyTag.SetActive(visible && GhostOnly);
            if (highlightFx != null) highlightFx.SetActive(visible && (skillDrop || ghostHighlight));
        }

        void RefreshHighlight()
        {
            if (highlightFx == null) return;

            bool on = skillDrop || ghostHighlight;
            highlightFx.SetActive(on);

            if (!on || highlightRenderer == null) return;

            Color color = skillDrop ? ringColor : ghostColor;
            color.a = 0.6f;
            highlightRenderer.color = color;
        }

        void ApplyVisuals()
        {
            Color color = ColorForType(Type);
            ringColor = color;
            ringColor.a = 0.9f;

            if (icon != null)
            {
                icon.sprite = SpriteForType(Type);
                icon.color = color;
                icon.transform.localScale = Vector3.one * ScaleForType(Type);
            }

            if (decayRing != null) decayRing.color = ringColor;

            RefreshHighlight();
        }

        void EnsureVisuals()
        {
            if (icon == null) icon = CreateIcon();
            if (highlightFx == null) highlightFx = CreateHighlight();
            if (decayRing == null) decayRing = CreateDecayRing();
            if (ghostOnlyTag == null) ghostOnlyTag = CreateGhostTag();

            if (highlightRenderer == null && highlightFx != null)
                highlightRenderer = highlightFx.GetComponent<SpriteRenderer>();
        }

        SpriteRenderer CreateIcon()
        {
            var go = new GameObject("Icon", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, iconHeight, 0f);

            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = CircleSprite;
            sr.sortingOrder = 26;
            return sr;
        }

        GameObject CreateHighlight()
        {
            var go = new GameObject("HighlightFx", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            float scale = ringDiameter * 1.6f;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = RingSprite;
            sr.color = new Color(1f, 1f, 1f, 0.6f);
            sr.sortingOrder = 22;

            highlightRenderer = sr;
            go.SetActive(false);
            return go;
        }

        Image CreateDecayRing()
        {
            var canvasGo = new GameObject("DecayRingCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            canvasGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.01f;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 24;

            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(ringDiameter * 100f, ringDiameter * 100f);

            var ringGo = new GameObject("DecayRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ringGo.transform.SetParent(canvasGo.transform, false);
            Stretch(ringGo.GetComponent<RectTransform>());

            var img = ringGo.GetComponent<Image>();
            img.sprite = RingSprite;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = false;
            img.fillAmount = 1f;
            img.raycastTarget = false;
            img.color = ringColor;

            ringGo.SetActive(false);
            return img;
        }

        GameObject CreateGhostTag()
        {
            var canvasGo = new GameObject("GhostOnlyTag", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 1f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.01f;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 28;

            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(260f, 46f);

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            Stretch(bgGo.GetComponent<RectTransform>());

            var bg = bgGo.GetComponent<Image>();
            bg.sprite = SquareSprite;
            bg.color = new Color(0.05f, 0.06f, 0.09f, 0.72f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            Stretch(textGo.GetComponent<RectTransform>());

            var text = textGo.GetComponent<Text>();
            text.font = LabelFont;
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = ghostColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "仅鬼魂可搬";

            canvasGo.SetActive(false);
            return canvasGo;
        }

        static Sprite RingSprite
        {
            get
            {
                if (ringSprite == null)
                    ringSprite = BuildSprite(96, (x, y) =>
                    {
                        float dx = x - 0.5f;
                        float dy = y - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                        return Mathf.Clamp01(Mathf.Min((1f - d) / 0.05f, (d - 0.7f) / 0.05f));
                    });

                return ringSprite;
            }
        }

        static Sprite CircleSprite
        {
            get
            {
                if (circleSprite == null)
                    circleSprite = BuildSprite(96, (x, y) =>
                    {
                        float dx = x - 0.5f;
                        float dy = y - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                        return Mathf.Clamp01((1f - d) / 0.06f);
                    });

                return circleSprite;
            }
        }

        static Sprite DiamondSprite
        {
            get
            {
                if (diamondSprite == null)
                    diamondSprite = BuildSprite(96, (x, y) =>
                    {
                        float d = (Mathf.Abs(x - 0.5f) + Mathf.Abs(y - 0.5f)) * 2f;
                        return Mathf.Clamp01((1f - d) / 0.06f);
                    });

                return diamondSprite;
            }
        }

        static Sprite CrossSprite
        {
            get
            {
                if (crossSprite == null)
                    crossSprite = BuildSprite(96, (x, y) =>
                    {
                        float dx = Mathf.Abs(x - 0.5f);
                        float dy = Mathf.Abs(y - 0.5f);
                        bool vertical = dx <= 0.13f && dy <= 0.4f;
                        bool horizontal = dy <= 0.13f && dx <= 0.4f;
                        return vertical || horizontal ? 1f : 0f;
                    });

                return crossSprite;
            }
        }

        static Sprite SquareSprite
        {
            get
            {
                if (squareSprite == null)
                    squareSprite = BuildSprite(64, (x, y) =>
                    {
                        float dx = Mathf.Abs(x - 0.5f);
                        float dy = Mathf.Abs(y - 0.5f);
                        return dx <= 0.42f && dy <= 0.42f ? 1f : 0f;
                    });

                return squareSprite;
            }
        }

        static Font LabelFont
        {
            get
            {
                if (labelFont != null) return labelFont;

                try { labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (labelFont == null)
                {
                    try { labelFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                }
                if (labelFont == null)
                    labelFont = Font.CreateDynamicFontFromOSFont("Arial", 24);

                return labelFont;
            }
        }

        static Sprite BuildSprite(int size, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float ax = (x + 0.5f) / size;
                    float ay = (y + 0.5f) / size;
                    byte a = (byte)(Mathf.Clamp01(alpha(ax, ay)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        Color ColorForType(DropType t)
        {
            switch (t)
            {
                case DropType.Skill:
                    return ElementTable.GetColor(elementId, itemColor);
                case DropType.Coin:
                    return coinColor;
                case DropType.Potion:
                    return potionColor;
                default:
                    return itemColor;
            }
        }

        static Sprite SpriteForType(DropType t)
        {
            switch (t)
            {
                case DropType.Skill:
                    return DiamondSprite;
                case DropType.Coin:
                    return CircleSprite;
                case DropType.Potion:
                    return CrossSprite;
                default:
                    return SquareSprite;
            }
        }

        static float ScaleForType(DropType t)
        {
            switch (t)
            {
                case DropType.Skill:
                    return 0.52f;
                case DropType.Coin:
                    return 0.34f;
                case DropType.Potion:
                    return 0.42f;
                default:
                    return 0.4f;
            }
        }

        static DropType ToDropType(uint typeId)
        {
            switch (typeId)
            {
                case 1:
                    return DropType.Skill;
                case 2:
                    return DropType.Coin;
                case 3:
                    return DropType.Potion;
                default:
                    return DropType.Item;
            }
        }
    }
}