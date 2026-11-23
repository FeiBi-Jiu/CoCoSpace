using System;

namespace Game.Net
{
    public enum NetxChannel : byte
    {
        Reliable = 0,
        Snapshot = 1,
    }

    [Flags]
    public enum NetxFlags : byte
    {
        None = 0,
        Fragmented = 1 << 0,
        LastFrag = 1 << 1,
        HasAck = 1 << 2,
    }

    public static class NetxConst
    {
        public const ushort Magic = 0x4B4B;
        public const byte Version = 1;

        public const int HeaderSize = 12;
        public const int MaxPayload = 1200;
        public const int MaxPacketSize = HeaderSize + MaxPayload;
        public const int MaxFragments = 256;
        public const int FragHeaderSize = 6;
        public const int FragChunk = MaxPayload - FragHeaderSize;

        public const int RtoInitialMs = 100;
        public const int RtoMaxMs = 500;
        public const int SendWindow = 1024;
        public const int RecvWindow = 32;

        public const int MaxFragSets = 16;
        public const int MaxReassemblyBytes = 64 * 1024;
        public const int ReassemblyTimeoutMs = 5000;

        public const int SweepIntervalMs = 20;
        public const int SessionTimeoutMs = 5000;
        public const int ProbeIntervalMs = 500;
        public const int ReadBufferSize = 2048;
        public const int OutQueueSize = 64;
    }

    public static class NetxClock
    {
        static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        public static long NowMs => Watch.ElapsedMilliseconds;
    }

    public static class NetxSeq
    {
        public static bool Greater(ushort a, ushort b) => unchecked((short)(a - b)) > 0;

        public static int Diff(ushort a, ushort b) => unchecked((short)(a - b));
    }

    public struct NetxHeader
    {
        public byte Version;
        public NetxChannel Channel;
        public ushort Seq;
        public ushort AckSeq;
        public uint AckBits;
        public NetxFlags Flags;

        public void Encode(byte[] dst)
        {
            dst[0] = (byte)(NetxConst.Magic & 0xFF);
            dst[1] = (byte)((NetxConst.Magic >> 8) & 0xFF);
            dst[2] = (byte)(((Version & 0x0F) << 4) | ((byte)Channel & 0x0F));
            dst[3] = (byte)(Seq & 0xFF);
            dst[4] = (byte)((Seq >> 8) & 0xFF);
            dst[5] = (byte)(AckSeq & 0xFF);
            dst[6] = (byte)((AckSeq >> 8) & 0xFF);
            dst[7] = (byte)(AckBits & 0xFF);
            dst[8] = (byte)((AckBits >> 8) & 0xFF);
            dst[9] = (byte)((AckBits >> 16) & 0xFF);
            dst[10] = (byte)((AckBits >> 24) & 0xFF);
            dst[11] = (byte)Flags;
        }

        public static bool TryDecode(byte[] src, int offset, int length, out NetxHeader h, out string error)
        {
            h = default;
            error = null;

            if (length < NetxConst.HeaderSize)
            {
                error = $"包长度不足：{length} < {NetxConst.HeaderSize}";
                return false;
            }

            ushort magic = (ushort)(src[offset] | (src[offset + 1] << 8));
            if (magic != NetxConst.Magic)
            {
                error = $"魔数不匹配：0x{magic:X4}";
                return false;
            }

            h.Version = (byte)(src[offset + 2] >> 4);
            h.Channel = (NetxChannel)(src[offset + 2] & 0x0F);
            h.Seq = (ushort)(src[offset + 3] | (src[offset + 4] << 8));
            h.AckSeq = (ushort)(src[offset + 5] | (src[offset + 6] << 8));
            h.AckBits = (uint)(src[offset + 7]
                | (src[offset + 8] << 8)
                | (src[offset + 9] << 16)
                | (src[offset + 10] << 24));
            h.Flags = (NetxFlags)src[offset + 11];

            if (h.Version != NetxConst.Version)
            {
                error = $"协议版本不匹配：收到 v{h.Version}，本端 v{NetxConst.Version}";
                return false;
            }

            if ((byte)h.Channel > (byte)NetxChannel.Snapshot)
            {
                error = $"非法通道号：{(byte)h.Channel}";
                return false;
            }

            return true;
        }

        public static byte[] Build(NetxHeader h, byte[] payload, int offset, int length)
        {
            var pkt = new byte[NetxConst.HeaderSize + length];
            h.Encode(pkt);
            if (length > 0 && payload != null)
                Buffer.BlockCopy(payload, offset, pkt, NetxConst.HeaderSize, length);
            return pkt;
        }
    }

    public struct NetxFragHeader
    {
        public ushort MsgId;
        public ushort Index;
        public ushort Count;

        public void Encode(byte[] dst, int offset)
        {
            dst[offset] = (byte)(MsgId & 0xFF);
            dst[offset + 1] = (byte)((MsgId >> 8) & 0xFF);
            dst[offset + 2] = (byte)(Index & 0xFF);
            dst[offset + 3] = (byte)((Index >> 8) & 0xFF);
            dst[offset + 4] = (byte)(Count & 0xFF);
            dst[offset + 5] = (byte)((Count >> 8) & 0xFF);
        }

        public static bool TryDecode(byte[] src, int offset, int length, out NetxFragHeader f, out string error)
        {
            f = default;
            error = null;

            if (length < NetxConst.FragHeaderSize)
            {
                error = $"分片头长度不足：{length}";
                return false;
            }

            f.MsgId = (ushort)(src[offset] | (src[offset + 1] << 8));
            f.Index = (ushort)(src[offset + 2] | (src[offset + 3] << 8));
            f.Count = (ushort)(src[offset + 4] | (src[offset + 5] << 8));

            if (f.Count == 0 || f.Count > NetxConst.MaxFragments)
            {
                error = $"分片数非法：{f.Count}（允许 1..{NetxConst.MaxFragments}）";
                return false;
            }

            if (f.Index >= f.Count)
            {
                error = $"分片下标越界：{f.Index} >= {f.Count}";
                return false;
            }

            return true;
        }
    }

    public static class NetxFrag
    {
        public static int Count(int payloadLen)
        {
            if (payloadLen <= 0) return 1;
            return (payloadLen + NetxConst.FragChunk - 1) / NetxConst.FragChunk;
        }
    }
}