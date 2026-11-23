using System.Collections.Generic;
using UnityEngine;

namespace Game.Table
{
    public class ElementDef
    {
        public uint ElementId;
        public string Name;
        public Color color;
        public string ColorHex;
        public string AttachFxPath;
        public Dictionary<uint, string> ComboFx = new();

        public Color ResolveColor(Color fallback)
        {
            if (color.a > 0f) return color;
            if (string.IsNullOrEmpty(ColorHex)) return fallback;

            string html = ColorHex.StartsWith("#") ? ColorHex : "#" + ColorHex;
            return ColorUtility.TryParseHtmlString(html, out var parsed) ? parsed : fallback;
        }
    }

    [System.Serializable]
    public class ElementTableJson
    {
        public ElementJson[] elements;
    }

    [System.Serializable]
    public class ElementJson
    {
        public int elementId;
        public string name;
        public string colorHex;
        public string attachFxPath;
        public ComboFxJson[] comboFx;
    }

    [System.Serializable]
    public class ComboFxJson
    {
        public int elementId;
        public string fxPath;
    }

    public class SkillDef
    {
        public uint SkillId;
        public string Name;
        public string BulletPatternId;
        public float Cooldown;
    }

    public class BulletPatternDef
    {
        public string Id;
        public string Shape;
        public float Interval;
        public int Count;
        public float Speed;
        public float SpreadDeg;
        public string BulletVisualId;

        public float Duration;
        public float AngularSpeedDeg;
        public float Acceleration;
        public float BulletLifetime;
        public float BulletRadius;
        public float Damage;
        public float StartAngleDeg;
        public float LineSpacing;
        public string ColorHex;

        public Color ResolveColor(Color fallback)
        {
            if (string.IsNullOrEmpty(ColorHex)) return fallback;

            string html = ColorHex.StartsWith("#") ? ColorHex : "#" + ColorHex;
            return ColorUtility.TryParseHtmlString(html, out var color) ? color : fallback;
        }
    }
}