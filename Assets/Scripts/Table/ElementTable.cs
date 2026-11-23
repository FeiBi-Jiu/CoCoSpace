using System.Collections.Generic;
using UnityEngine;

namespace Game.Table
{
    public static class ElementTable
    {
        static readonly Dictionary<uint, ElementDef> table = new();
        static readonly Dictionary<uint, GameObject> attachFxCache = new();
        static readonly Dictionary<string, GameObject> comboFxCache = new();

        static readonly Color[] FallbackColors =
        {
            new(1.00f, 1.00f, 1.00f, 1f),
            new(0.88f, 0.26f, 0.20f, 1f),
            new(0.22f, 0.48f, 0.92f, 1f),
            new(0.95f, 0.84f, 0.20f, 1f),
            new(0.30f, 0.78f, 0.32f, 1f),
            new(0.62f, 0.46f, 0.26f, 1f),
            new(0.35f, 0.82f, 0.88f, 1f),
        };

        public static int Count => table.Count;

        public static void Load(IEnumerable<ElementDef> defs)
        {
            table.Clear();
            attachFxCache.Clear();
            comboFxCache.Clear();

            if (defs == null) return;
            foreach (var def in defs)
            {
                if (def == null) continue;
                def.color = def.ResolveColor(FallbackColor(def.ElementId));
                table[def.ElementId] = def;
            }
        }

        public static bool TryGet(uint elementId, out ElementDef def) =>
            table.TryGetValue(elementId, out def);

        public static Color GetColor(uint elementId) =>
            table.TryGetValue(elementId, out var def) ? def.color : FallbackColor(elementId);

        public static Color GetColor(uint elementId, Color fallback) =>
            table.TryGetValue(elementId, out var def) && def.color.a > 0f ? def.color : fallback;

        public static string GetAttachFxPath(uint elementId) =>
            table.TryGetValue(elementId, out var def) ? def.AttachFxPath : string.Empty;

        public static GameObject GetAttachFx(uint elementId)
        {
            if (attachFxCache.TryGetValue(elementId, out var cached)) return cached;

            GameObject fx = null;
            string path = GetAttachFxPath(elementId);
            if (!string.IsNullOrEmpty(path))
                fx = Resources.Load<GameObject>(path);

            attachFxCache[elementId] = fx;
            return fx;
        }

        public static string GetComboFxPath(uint a, uint b)
        {
            if (table.TryGetValue(a, out var da) && da.ComboFx.TryGetValue(b, out var path))
                return path;
            if (table.TryGetValue(b, out var db) && db.ComboFx.TryGetValue(a, out path))
                return path;
            return string.Empty;
        }

        public static GameObject GetComboFx(uint a, uint b)
        {
            string path = GetComboFxPath(a, b);
            if (string.IsNullOrEmpty(path)) return null;
            if (comboFxCache.TryGetValue(path, out var cached)) return cached;

            var fx = Resources.Load<GameObject>(path);
            comboFxCache[path] = fx;
            return fx;
        }

        static Color FallbackColor(uint elementId)
        {
            if (elementId < (uint)FallbackColors.Length)
                return FallbackColors[elementId];

            return Color.HSVToRGB((elementId * 0.17f) % 1f, 0.72f, 0.95f);
        }
    }
}