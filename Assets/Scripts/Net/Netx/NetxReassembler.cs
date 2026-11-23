using System;
using System.Collections.Generic;

namespace Game.Net
{
    public sealed class NetxReassembler
    {
        sealed class FragSet
        {
            public ushort Count;
            public byte[][] Parts;
            public ushort Got;
            public int Size;
            public long StartedMs;
        }

        readonly Dictionary<ushort, FragSet> sets = new(NetxConst.MaxFragSets);

        public int Pending => sets.Count;

        public bool Push(NetxFragHeader f, byte[] payload, int offset, int length,
                         out byte[] whole, out bool done, out string error)
        {
            whole = null;
            done = false;
            error = null;
            long now = NetxClock.NowMs;

            if (!sets.TryGetValue(f.MsgId, out var set))
            {
                DropExpired(now);
                if (sets.Count >= NetxConst.MaxFragSets)
                {
                    error = $"分片重组队列已满（{sets.Count} 条）";
                    return false;
                }
                set = new FragSet
                {
                    Count = f.Count,
                    Parts = new byte[f.Count][],
                    StartedMs = now,
                };
                sets[f.MsgId] = set;
            }

            if (set.Count != f.Count)
            {
                sets.Remove(f.MsgId);
                error = $"分片总数不一致：MsgId {f.MsgId} 前 {set.Count} 片，现 {f.Count} 片";
                return false;
            }

            if (set.Parts[f.Index] != null)
                return true;

            if (set.Size + length > NetxConst.MaxReassemblyBytes)
            {
                sets.Remove(f.MsgId);
                error = $"分片消息过大：已收 {set.Size} 字节，再加 {length} 超限";
                return false;
            }

            var buf = new byte[length];
            if (length > 0) Buffer.BlockCopy(payload, offset, buf, 0, length);

            set.Parts[f.Index] = buf;
            set.Got++;
            set.Size += length;

            if (set.Got < set.Count) return true;

            var outBuf = new byte[set.Size];
            int cursor = 0;
            foreach (var p in set.Parts)
            {
                Buffer.BlockCopy(p, 0, outBuf, cursor, p.Length);
                cursor += p.Length;
            }
            sets.Remove(f.MsgId);
            whole = outBuf;
            done = true;
            return true;
        }

        public int Sweep() => DropExpired(NetxClock.NowMs);

        public void Reset() => sets.Clear();

        int DropExpired(long now)
        {
            if (sets.Count == 0) return 0;

            List<ushort> expired = null;
            foreach (var pair in sets)
            {
                if (now - pair.Value.StartedMs >= NetxConst.ReassemblyTimeoutMs)
                    (expired ??= new List<ushort>()).Add(pair.Key);
            }

            if (expired == null) return 0;
            foreach (var id in expired) sets.Remove(id);
            return expired.Count;
        }
    }
}