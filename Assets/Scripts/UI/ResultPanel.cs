using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Net;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    public class ResultPanel : MonoBehaviour
    {
        [Serializable]
        public class StatJson
        {
            public int entity_id;
            public string player_name;
            public string name;
            public int damage_dealt;
            public int damage;
            public int contribution;
            public int kills;
            public int death_count;
            public int deaths;
        }

        [Serializable]
        public class GameResultJson
        {
            public bool victory;
            public bool win;
            public string result;
            public StatJson[] stats;
        }

        public class PlayerStat
        {
            public ulong EntityId;
            public string Name;
            public int Damage;
            public int Contribution;
            public int Kills;
            public int Deaths;
            public bool IsSelf;
        }

        class RowView
        {
            public GameObject Root;
        }

        enum SortColumn
        {
            Damage,
            Contribution,
            Kills,
        }

        const float WindowWidth = 1080f;
        const float WindowHeight = 720f;
        const float TableWidth = 1000f;
        const float HeaderTop = -136f;
        const float HeaderHeight = 46f;
        const float RowHeight = 56f;
        const float RowStep = 62f;
        const float CellNameX = 12f;
        const float CellNameWidth = 280f;
        const float CellDamageX = 304f;
        const float CellDamageWidth = 170f;
        const float CellContributionX = 486f;
        const float CellContributionWidth = 170f;
        const float CellKillsX = 668f;
        const float CellKillsWidth = 150f;
        const float CellDeathsX = 830f;
        const float CellDeathsWidth = 140f;

        static readonly Color OverlayColor = new(0.02f, 0.03f, 0.05f, 0.88f);
        static readonly Color WindowColor = new(0.06f, 0.08f, 0.12f, 0.98f);
        static readonly Color HeaderColor = new(0.13f, 0.16f, 0.22f, 1f);
        static readonly Color RowColorA = new(0.10f, 0.12f, 0.16f, 0.96f);
        static readonly Color RowColorB = new(0.13f, 0.15f, 0.20f, 0.96f);
        static readonly Color SelfRowColor = new(0.14f, 0.30f, 0.46f, 0.98f);
        static readonly Color MainText = new(0.94f, 0.96f, 0.99f, 1f);
        static readonly Color DimText = new(0.66f, 0.71f, 0.78f, 1f);
        static readonly Color SelfText = new(1f, 0.86f, 0.52f, 1f);
        static readonly Color MaxText = new(1f, 0.42f, 0.38f, 1f);
        static readonly Color WinColor = new(0.42f, 0.90f, 0.55f, 1f);
        static readonly Color LoseColor = new(1f, 0.42f, 0.38f, 1f);
        static readonly Color ButtonColor = new(0.24f, 0.58f, 0.95f, 1f);

        static readonly Regex LabelRegex = new(
            @"(伤害|贡献|击杀|死亡|编号|damage_dealt|damage|contribution|kills|death_count|deaths|death|id)\s*[:：=]?\s*(\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static Font cachedFont;

        readonly List<PlayerStat> stats = new();
        readonly List<RowView> rows = new();

        GameObject root;
        GameObject window;
        GameObject headerPanel;
        Text titleText;
        Text subtitleText;
        Text emptyText;
        Text damageHeader;
        Text contributionHeader;
        Text killsHeader;

        SortColumn sortColumn = SortColumn.Damage;
        bool sortDesc = true;
        bool built;

        GameRoot gameRoot;

        void Awake()
        {
            if (!IsPrimaryInstance())
            {
                enabled = false;
                return;
            }

            EnsureUI();
        }

        bool IsPrimaryInstance()
        {
            ResultPanel[] all = FindObjectsOfType<ResultPanel>();
            if (all.Length <= 1) return true;

            ResultPanel best = this;
            for (int i = 0; i < all.Length; i++)
            {
                ResultPanel candidate = all[i];
                if (candidate == this) continue;

                int rank = Rank(candidate);
                int bestRank = Rank(best);
                if (rank < bestRank || (rank == bestRank && candidate.GetInstanceID() < best.GetInstanceID()))
                    best = candidate;
            }

            return best == this;
        }

        static int Rank(ResultPanel panel)
            => panel.gameObject.name == "ResultPanel" ? 0 : 1;

        void Start()
        {
            gameRoot = GameRoot.Instance;
            if (gameRoot != null) gameRoot.LoginSucceeded += OnLoginSucceeded;
        }

        void OnDestroy()
        {
            if (gameRoot == null) return;
            gameRoot.LoginSucceeded -= OnLoginSucceeded;
            gameRoot = null;
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CGameOver>(OnOver);
            MsgDispatcher.RegisterRaw(MsgId.S2CGameOver, OnRawOver);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CGameOver>(OnOver);
            MsgDispatcher.UnregisterRaw(MsgId.S2CGameOver);
        }

        void OnLoginSucceeded()
        {
            if (root != null) root.SetActive(false);
            Time.timeScale = 1f;
        }

        void OnOver(S2CGameOver msg)
        {
            if (msg == null) return;
            Show(msg.Win, ParseStats(msg.StatLines));
        }

        void OnRawOver(ByteString payload)
        {
            string text = payload == null ? string.Empty : payload.ToStringUtf8();
            if (TryParseJson(text, out bool win, out List<PlayerStat> parsed))
            {
                Show(win, parsed);
                return;
            }

            Debug.LogWarning($"[Result] 无法解析结算数据：{text}");
        }

        public void Show(bool win, IList<PlayerStat> items)
        {
            EnsureUI();

            stats.Clear();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    PlayerStat stat = items[i];
                    if (stat != null) stats.Add(stat);
                }
            }

            MarkSelfRows();

            titleText.text = win ? "通关成功" : "团灭";
            titleText.color = win ? WinColor : LoseColor;
            subtitleText.text = win ? "Boss 已被击败，本局数据如下" : "队伍全员阵亡，本局数据如下";

            sortColumn = SortColumn.Damage;
            sortDesc = true;

            RebuildRows();

            root.SetActive(true);
            transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        public void ReturnToLobby()
        {
            if (root != null) root.SetActive(false);
            Time.timeScale = 1f;

            if (GameRoot.Instance != null)
            {
                GameRoot.Instance.ReturnToLobby();
                return;
            }

            KcpNetClient.Instance?.Close();
        }

        void MarkSelfRows()
        {
            ulong selfId = SelfEntityId();

            for (int i = 0; i < stats.Count; i++)
                stats[i].IsSelf = selfId != 0 && stats[i].EntityId == selfId;
        }

        static ulong SelfEntityId()
        {
            if (GameRoot.Instance != null && GameRoot.Instance.LocalEntityId != 0)
                return GameRoot.Instance.LocalEntityId;

            return KcpNetClient.Instance != null ? KcpNetClient.Instance.LocalEntityId : 0;
        }

        List<PlayerStat> SortedStats()
        {
            var list = new List<PlayerStat>(stats);
            Comparison<PlayerStat> comparison = sortColumn switch
            {
                SortColumn.Contribution => (a, b) => a.Contribution.CompareTo(b.Contribution),
                SortColumn.Kills => (a, b) => a.Kills.CompareTo(b.Kills),
                _ => (a, b) => a.Damage.CompareTo(b.Damage),
            };

            list.Sort((a, b) => sortDesc ? comparison(b, a) : comparison(a, b));
            return list;
        }

        void OnClickHeader(SortColumn column)
        {
            if (sortColumn == column)
                sortDesc = !sortDesc;
            else
            {
                sortColumn = column;
                sortDesc = true;
            }

            RebuildRows();
        }

        void RefreshHeaders()
        {
            damageHeader.text = "伤害" + SortArrow(SortColumn.Damage);
            contributionHeader.text = "贡献" + SortArrow(SortColumn.Contribution);
            killsHeader.text = "击杀" + SortArrow(SortColumn.Kills);
        }

        string SortArrow(SortColumn column)
        {
            if (sortColumn != column) return string.Empty;
            return sortDesc ? " ▼" : " ▲";
        }

        void RebuildRows()
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i]?.Root != null) Destroy(rows[i].Root);

            rows.Clear();
            RefreshHeaders();

            emptyText.gameObject.SetActive(stats.Count == 0);
            if (stats.Count == 0) return;

            int maxDamage = 0;
            int maxContribution = 0;
            int maxKills = 0;
            for (int i = 0; i < stats.Count; i++)
            {
                maxDamage = Mathf.Max(maxDamage, stats[i].Damage);
                maxContribution = Mathf.Max(maxContribution, stats[i].Contribution);
                maxKills = Mathf.Max(maxKills, stats[i].Kills);
            }

            List<PlayerStat> sorted = SortedStats();
            for (int i = 0; i < sorted.Count; i++)
            {
                PlayerStat stat = sorted[i];
                Color rowColor = stat.IsSelf ? SelfRowColor : (i % 2 == 0 ? RowColorA : RowColorB);

                GameObject rowGo = CreatePanel("Row" + i, rowColor, window.transform, false);
                PlaceTopLeft(rowGo.GetComponent<RectTransform>(), 40f, HeaderTop - HeaderHeight - 8f - i * RowStep, TableWidth, RowHeight);

                var row = new RowView { Root = rowGo };
                rows.Add(row);

                CreateCell(rowGo.transform, stat.Name + (stat.IsSelf ? "（你）" : string.Empty),
                    TextAnchor.MiddleLeft, CellNameX, CellNameWidth, stat.IsSelf ? SelfText : MainText, 20);

                CreateCell(rowGo.transform, stat.Damage.ToString(), TextAnchor.MiddleCenter,
                    CellDamageX, CellDamageWidth, maxDamage > 0 && stat.Damage >= maxDamage ? MaxText : MainText, 21);

                CreateCell(rowGo.transform, stat.Contribution.ToString(), TextAnchor.MiddleCenter,
                    CellContributionX, CellContributionWidth, maxContribution > 0 && stat.Contribution >= maxContribution ? MaxText : MainText, 21);

                CreateCell(rowGo.transform, stat.Kills.ToString(), TextAnchor.MiddleCenter,
                    CellKillsX, CellKillsWidth, maxKills > 0 && stat.Kills >= maxKills ? MaxText : MainText, 21);

                CreateCell(rowGo.transform, stat.Deaths.ToString(), TextAnchor.MiddleCenter,
                    CellDeathsX, CellDeathsWidth, DimText, 21);
            }
        }

        void CreateCell(Transform parent, string content, TextAnchor anchor, float x, float width, Color color, int size)
        {
            Text text = CreateText("Cell", content, size, anchor, GetDefaultFont(), parent);
            text.color = color;
            PlaceTopLeft(text.rectTransform, x, 0f, width, RowHeight);
        }

        void EnsureUI()
        {
            if (built) return;
            built = true;

            EnsureCanvas();
            EnsureEventSystem();
            EnsureContainer();

            Font font = GetDefaultFont();

            root = CreatePanel("ResultRoot", OverlayColor, transform, true);
            StretchFullScreen(root.GetComponent<RectTransform>());

            window = CreatePanel("ResultWindow", WindowColor, root.transform, true);
            SetRect(window.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WindowWidth, WindowHeight));

            titleText = CreateText("Title", "结算", 44, TextAnchor.MiddleCenter, font, window.transform);
            SetRect(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(TableWidth, 60f));

            subtitleText = CreateText("Subtitle", string.Empty, 20, TextAnchor.MiddleCenter, font, window.transform);
            subtitleText.color = DimText;
            SetRect(subtitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(TableWidth, 26f));

            headerPanel = CreatePanel("Header", HeaderColor, window.transform, false);
            PlaceTopLeft(headerPanel.GetComponent<RectTransform>(), 40f, HeaderTop, TableWidth, HeaderHeight);

            Text nameHeader = CreateText("NameHeader", "玩家", 21, TextAnchor.MiddleLeft, font, headerPanel.transform);
            PlaceTopLeft(nameHeader.rectTransform, CellNameX, 0f, CellNameWidth, HeaderHeight);

            damageHeader = CreateHeaderButton("DamageHeader", "伤害", CellDamageX, CellDamageWidth, SortColumn.Damage, font);
            contributionHeader = CreateHeaderButton("ContributionHeader", "贡献", CellContributionX, CellContributionWidth, SortColumn.Contribution, font);
            killsHeader = CreateHeaderButton("KillsHeader", "击杀", CellKillsX, CellKillsWidth, SortColumn.Kills, font);

            Text deathsHeader = CreateText("DeathsHeader", "死亡", 21, TextAnchor.MiddleCenter, font, headerPanel.transform);
            PlaceTopLeft(deathsHeader.rectTransform, CellDeathsX, 0f, CellDeathsWidth, HeaderHeight);
            deathsHeader.color = DimText;

            emptyText = CreateText("Empty", "无结算数据", 22, TextAnchor.MiddleCenter, font, window.transform);
            emptyText.color = DimText;
            PlaceTopLeft(emptyText.rectTransform, 40f, HeaderTop - HeaderHeight - 48f, TableWidth, 40f);

            Text hintText = CreateText("Hint", "点击表头切换排序 · 红色为该列最高值", 18, TextAnchor.MiddleCenter, font, window.transform);
            hintText.color = DimText;
            SetRect(hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(TableWidth, 24f));

            Button returnButton = CreateButton("ReturnButton", "返回大厅", ButtonColor, window.transform, font);
            SetRect(returnButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(260f, 58f));
            returnButton.onClick.AddListener(ReturnToLobby);

            root.SetActive(false);
        }

        Text CreateHeaderButton(string name, string label, float x, float width, SortColumn column, Font font)
        {
            GameObject go = CreatePanel(name, Color.white, headerPanel.transform, true);
            PlaceTopLeft(go.GetComponent<RectTransform>(), x, 0f, width, HeaderHeight);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.16f, 0.19f, 0.26f, 1f);
            colors.highlightedColor = new Color(0.24f, 0.29f, 0.38f, 1f);
            colors.pressedColor = new Color(0.10f, 0.12f, 0.16f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => OnClickHeader(column));

            Text text = CreateText("Label", label, 21, TextAnchor.MiddleCenter, font, go.transform);
            text.color = MainText;
            StretchFullScreen(text.rectTransform);
            return text;
        }

        static GameObject CreatePanel(string name, Color color, Transform parent, bool raycastTarget)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
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
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;
            return text;
        }

        static Button CreateButton(string name, string label, Color color, Transform parent, Font font)
        {
            GameObject go = CreatePanel(name, Color.white, parent, true);

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
            StretchFullScreen(text.rectTransform);
            return button;
        }

        static void PlaceTopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
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

        void EnsureContainer()
        {
            if (GetComponent<Canvas>() != null) return;

            if (transform is RectTransform rt) StretchFullScreen(rt);
        }

        Canvas EnsureCanvas()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null) return canvas;

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            if (GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
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

        static List<PlayerStat> ParseStats(IEnumerable<string> lines)
        {
            var result = new List<PlayerStat>();
            if (lines == null) return result;

            var raw = new List<string>();
            foreach (string line in lines)
                if (!string.IsNullOrWhiteSpace(line)) raw.Add(line.Trim());

            if (raw.Count == 0) return result;

            if (raw.Count == 1 && (raw[0].StartsWith("{") || raw[0].StartsWith("[")))
            {
                if (TryParseJson(raw[0], out _, out List<PlayerStat> parsed) && parsed != null)
                    return parsed;
            }

            for (int i = 0; i < raw.Count; i++)
            {
                PlayerStat stat = ParseLine(raw[i]);
                if (stat != null) result.Add(stat);
            }

            return result;
        }

        static bool TryParseJson(string text, out bool victory, out List<PlayerStat> stats)
        {
            victory = false;
            stats = null;

            if (string.IsNullOrWhiteSpace(text)) return false;

            string json = text.Trim();
            if (json.StartsWith("[")) json = "{\"stats\":" + json + "}";

            GameResultJson data;
            try { data = JsonUtility.FromJson<GameResultJson>(json); }
            catch { return false; }

            if (data == null || data.stats == null) return false;

            victory = data.victory || data.win;
            bool? parsedResult = ParseResultText(data.result);
            if (parsedResult.HasValue) victory = parsedResult.Value;

            stats = new List<PlayerStat>(data.stats.Length);
            for (int i = 0; i < data.stats.Length; i++)
            {
                StatJson stat = data.stats[i];
                if (stat != null) stats.Add(FromJson(stat));
            }

            return true;
        }

        static bool? ParseResultText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string value = text.Trim().ToLowerInvariant();
            if (value == "victory" || value == "win" || value == "true" || value == "1") return true;
            if (value == "defeat" || value == "fail" || value == "false" || value == "0") return false;
            if (value.Contains("通关") || value.Contains("胜利")) return true;
            if (value.Contains("团灭") || value.Contains("失败")) return false;

            return null;
        }

        static PlayerStat FromJson(StatJson stat)
        {
            int damage = stat.damage_dealt > 0 ? stat.damage_dealt : stat.damage;
            int deaths = stat.death_count > 0 ? stat.death_count : stat.deaths;

            return new PlayerStat
            {
                EntityId = (ulong)Mathf.Max(0, stat.entity_id),
                Name = ResolveName(stat.player_name, stat.name),
                Damage = Mathf.Max(0, damage),
                Contribution = Mathf.Max(0, stat.contribution),
                Kills = Mathf.Max(0, stat.kills),
                Deaths = Mathf.Max(0, deaths),
            };
        }

        static string ResolveName(string primary, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(primary)) return primary.Trim();
            if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim();
            return "玩家";
        }

        static PlayerStat ParseLine(string raw)
        {
            string line = raw.Trim();

            if (line.StartsWith("{"))
            {
                try
                {
                    StatJson stat = JsonUtility.FromJson<StatJson>(line);
                    return stat == null ? null : FromJson(stat);
                }
                catch { return null; }
            }

            if (TryParseDelimited(line, out PlayerStat delimited)) return delimited;
            if (TryParseLabeled(line, out PlayerStat labeled)) return labeled;
            if (TryParseWhitespace(line, out PlayerStat whitespace)) return whitespace;

            return null;
        }

        static bool TryParseDelimited(string line, out PlayerStat stat)
        {
            stat = null;

            string[] parts = line.Split(new[] { '|', ',', '，', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return false;

            if (!TryInt(parts[1], out int damage)) return false;
            if (!TryInt(parts[2], out int contribution)) return false;
            if (!TryInt(parts[3], out int kills)) return false;
            if (!TryInt(parts[4], out int deaths)) return false;

            ulong entityId = 0;
            if (parts.Length >= 6 && ulong.TryParse(parts[5].Trim(), out ulong parsedId))
                entityId = parsedId;

            stat = new PlayerStat
            {
                EntityId = entityId,
                Name = ResolveName(parts[0], null),
                Damage = Mathf.Max(0, damage),
                Contribution = Mathf.Max(0, contribution),
                Kills = Mathf.Max(0, kills),
                Deaths = Mathf.Max(0, deaths),
            };
            return true;
        }

        static bool TryParseLabeled(string line, out PlayerStat stat)
        {
            stat = null;

            MatchCollection matches = LabelRegex.Matches(line);
            if (matches.Count < 2) return false;

            int damage = -1;
            int contribution = -1;
            int kills = -1;
            int deaths = -1;
            ulong entityId = 0;
            int firstIndex = -1;

            foreach (Match match in matches)
            {
                if (!TryInt(match.Groups[2].Value, out int value)) continue;

                string key = match.Groups[1].Value.ToLowerInvariant();
                if (key.Contains("伤害") || key.StartsWith("damage")) damage = value;
                else if (key.Contains("贡献") || key.StartsWith("contribution")) contribution = value;
                else if (key.Contains("击杀") || key.StartsWith("kill")) kills = value;
                else if (key.Contains("死亡") || key.StartsWith("death")) deaths = value;
                else if (key.Contains("编号") || key == "id") entityId = (ulong)Mathf.Max(0, value);

                if (firstIndex < 0) firstIndex = match.Index;
            }

            if (damage < 0 && contribution < 0 && kills < 0 && deaths < 0) return false;

            string name = firstIndex > 0
                ? line.Substring(0, firstIndex).Trim(' ', '|', ',', ';', '\t', ':', '：', '=', '-')
                : string.Empty;

            stat = new PlayerStat
            {
                EntityId = entityId,
                Name = ResolveName(name, null),
                Damage = Mathf.Max(0, damage),
                Contribution = Mathf.Max(0, contribution),
                Kills = Mathf.Max(0, kills),
                Deaths = Mathf.Max(0, deaths),
            };
            return true;
        }

        static bool TryParseWhitespace(string line, out PlayerStat stat)
        {
            stat = null;

            string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 5) return false;

            int numeric = 0;
            while (numeric < tokens.Length && TryInt(tokens[tokens.Length - 1 - numeric], out _))
                numeric++;

            if (numeric < 4) return false;

            int first = tokens.Length - numeric;
            int damage = ParseInt(tokens[first]);
            int contribution = ParseInt(tokens[first + 1]);
            int kills = ParseInt(tokens[first + 2]);
            int deaths = ParseInt(tokens[first + 3]);
            ulong entityId = numeric >= 5 ? (ulong)Mathf.Max(0, ParseInt(tokens[first + 4])) : 0;

            stat = new PlayerStat
            {
                EntityId = entityId,
                Name = ResolveName(string.Join(" ", tokens, 0, first), null),
                Damage = Mathf.Max(0, damage),
                Contribution = Mathf.Max(0, contribution),
                Kills = Mathf.Max(0, kills),
                Deaths = Mathf.Max(0, deaths),
            };
            return true;
        }

        static bool TryInt(string text, out int value) => int.TryParse(text.Trim(), out value);

        static int ParseInt(string text) => int.TryParse(text, out int value) ? value : 0;
    }
}