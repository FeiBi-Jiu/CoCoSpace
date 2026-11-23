using System.Collections.Generic;

namespace Game.Table
{
    public static class BulletPatternTable
    {
        static readonly Dictionary<string, BulletPatternDef> table = new();

        public static int Count => table.Count;

        public static void Load(IEnumerable<BulletPatternDef> defs)
        {
            table.Clear();
            foreach (var d in defs)
                if (!string.IsNullOrEmpty(d.Id)) table[d.Id] = d;
        }

        public static BulletPatternDef Get(string id) =>
            !string.IsNullOrEmpty(id) && table.TryGetValue(id, out var d) ? d : null;
    }
}