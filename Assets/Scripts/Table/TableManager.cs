using System.Collections.Generic;
using System.Linq;
using Game.Net;
using Game.Proto;
using UnityEngine;

namespace Game.Table
{
    public class TableManager : MonoBehaviour
    {
        public static TableManager Instance { get; private set; }

        public TableBundle Bundle { get; private set; }

        void Awake()
        {
            Instance = this;
            LoadLocalElements();
        }

        void LoadLocalElements()
        {
            var asset = Resources.Load<TextAsset>("Tables/elements");
            if (asset == null) return;

            var config = JsonUtility.FromJson<ElementTableJson>(asset.text);
            if (config == null || config.elements == null || config.elements.Length == 0) return;

            var defs = new List<ElementDef>(config.elements.Length);
            foreach (var entry in config.elements)
            {
                if (entry == null) continue;

                var def = new ElementDef
                {
                    ElementId = (uint)entry.elementId,
                    Name = entry.name,
                    ColorHex = entry.colorHex,
                    AttachFxPath = entry.attachFxPath,
                };

                if (entry.comboFx != null)
                {
                    foreach (var combo in entry.comboFx)
                    {
                        if (combo == null || string.IsNullOrEmpty(combo.fxPath)) continue;
                        def.ComboFx[(uint)combo.elementId] = combo.fxPath;
                    }
                }

                defs.Add(def);
            }

            ElementTable.Load(defs);
            Debug.Log($"[Table] local elements loaded count={defs.Count}");
        }

        public void Apply(TableBundle bundle)
        {
            Bundle = bundle;

            if (bundle != null && bundle.Elements.Count > 0)
            {
                var defs = bundle.Elements.Select(MergeServerElement).ToList();
                ElementTable.Load(defs);
            }

            if (bundle != null)
            {
                BulletPatternTable.Load(bundle.BulletPatterns.Select(p => new BulletPatternDef
                {
                    Id = p.Id,
                    Shape = p.Shape,
                    Interval = p.Interval,
                    Count = p.Count,
                    Speed = p.Speed,
                    SpreadDeg = p.SpreadDeg,
                    BulletVisualId = p.BulletVisualId,
                }));
            }

            // 缓存到本地，下次进房 version 一致可直接读 Resources/Tables/
            Debug.Log($"[Table] applied version={bundle?.Version ?? "0"} elements={ElementTable.Count}");
        }

        static ElementDef MergeServerElement(Game.Proto.ElementDef server)
        {
            ElementTable.TryGet(server.ElementId, out var local);

            var def = new ElementDef
            {
                ElementId = server.ElementId,
                Name = string.IsNullOrEmpty(server.Name) ? local?.Name ?? string.Empty : server.Name,
                ColorHex = local?.ColorHex ?? string.Empty,
                color = local != null ? local.color : Color.white,
                AttachFxPath = string.IsNullOrEmpty(server.AttachFxPath) ? local?.AttachFxPath ?? string.Empty : server.AttachFxPath,
            };

            if (server.ComboFx != null && server.ComboFx.Count > 0)
                foreach (var pair in server.ComboFx) def.ComboFx[pair.Key] = pair.Value;
            else if (local != null)
                foreach (var pair in local.ComboFx) def.ComboFx[pair.Key] = pair.Value;

            return def;
        }
    }
}