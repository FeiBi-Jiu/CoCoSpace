using System;
using System.Collections.Generic;

namespace Game.Net
{
    public sealed class NetxOutstandingPacket
    {
        public ushort Seq;
        public byte[][] Parts;
        public long FirstAtMs;
        public long LastAtMs;
        public int Attempts;
    }

    public sealed class NetxReliableSender
    {
        ushort nextSeq;
        readonly List<NetxOutstandingPacket> inflight = new();

        ushort lastAckSeq;
        uint lastAckBits;
        bool haveAck;

        int rtoMs = NetxConst.RtoInitialMs;

        public long SentCount;
        public long RetransmitCount;
        public long AckedCount;
        public long DroppedCount;

        public int InFlight => inflight.Count;
        public ushort NextSeq => nextSeq;

        public bool CanSend => inflight.Count < NetxConst.SendWindow;

        public NetxOutstandingPacket SendParts(byte[] payload, int offset, int length)
        {
            int frags = NetxFrag.Count(length);
            if (frags > NetxConst.MaxFragments) return null;
            if (!CanSend) return null;

            if (frags == 1 && length <= NetxConst.MaxPayload)
            {
                var single = new byte[length];
                if (length > 0) Buffer.BlockCopy(payload, offset, single, 0, length);
                ushort seq = Enqueue(new[] { single });
                return new NetxOutstandingPacket { Seq = seq, Parts = new[] { single }, Attempts = 1 };
            }

            ushort msgId = nextSeq;
            var parts = new byte[frags][];
            for (int i = 0; i < frags; i++)
            {
                int start = i * NetxConst.FragChunk;
                int end = start + NetxConst.FragChunk;
                if (end > length) end = length;

                var body = new byte[NetxConst.FragHeaderSize + (end - start)];
                new NetxFragHeader { MsgId = msgId, Index = (ushort)i, Count = (ushort)frags }
                    .Encode(body, 0);
                Buffer.BlockCopy(payload, offset + start, body, NetxConst.FragHeaderSize, end - start);
                parts[i] = body;
            }

            ushort seqF = Enqueue(parts);
            return new NetxOutstandingPacket { Seq = seqF, Parts = parts, Attempts = 1 };
        }

        ushort Enqueue(byte[][] parts)
        {
            ushort seq = nextSeq;
            nextSeq++;

            var copied = new byte[parts.Length][];
            for (int i = 0; i < parts.Length; i++)
            {
                var buf = new byte[parts[i].Length];
                Buffer.BlockCopy(parts[i], 0, buf, 0, parts[i].Length);
                copied[i] = buf;
            }

            long now = NetxClock.NowMs;
            inflight.Add(new NetxOutstandingPacket
            {
                Seq = seq,
                Parts = copied,
                FirstAtMs = now,
                LastAtMs = now,
                Attempts = 1,
            });
            SentCount++;
            return seq;
        }

        public void Ack(ushort ackSeq, uint ackBits)
        {
            if (!haveAck || !NetxSeq.Greater(lastAckSeq, ackSeq))
                rtoMs = NetxConst.RtoInitialMs;

            if (haveAck && NetxSeq.Greater(lastAckSeq, ackSeq))
                return;

            lastAckSeq = ackSeq;
            lastAckBits = ackBits;
            haveAck = true;

            int kept = 0;
            for (int i = 0; i < inflight.Count; i++)
            {
                var p = inflight[i];
                if (AckedBy(ackSeq, ackBits, p.Seq))
                {
                    AckedCount++;
                    continue;
                }
                inflight[kept++] = p;
            }
            if (kept < inflight.Count) inflight.RemoveRange(kept, inflight.Count - kept);
        }

        public static bool AckedBy(ushort ackSeq, uint ackBits, ushort seq)
        {
            int diff = NetxSeq.Diff(seq, ackSeq);
            if (diff == 0) return true;
            if (diff > 0) return false;
            if (diff > -NetxConst.RecvWindow) return (ackBits & (1u << (-diff - 1))) != 0;
            return false;
        }

        public List<NetxOutstandingPacket> Due()
        {
            if (inflight.Count == 0) return null;

            long now = NetxClock.NowMs;
            List<NetxOutstandingPacket> due = null;
            foreach (var p in inflight)
            {
                if (now - p.LastAtMs < RtoFor(p.Attempts)) continue;
                p.LastAtMs = now;
                p.Attempts++;
                RetransmitCount++;
                due ??= new List<NetxOutstandingPacket>();
                due.Add(p);
            }
            return due;
        }

        int RtoFor(int attempts)
        {
            if (attempts <= 1) return rtoMs;
            int d = rtoMs;
            for (int i = 1; i < attempts; i++)
            {
                d *= 2;
                if (d >= NetxConst.RtoMaxMs) return NetxConst.RtoMaxMs;
            }
            return d;
        }
    }

    public sealed class NetxReliableReceiver
    {
        ushort nextExpected;
        readonly Dictionary<ushort, byte[]> received = new(8);

        ushort maxSeen;
        ushort lowDelivered;
        bool hasDelivered;
        bool haveReceived;

        public long DroppedCount;
        public long DeliveredCount;

        public int Pending => received.Count;

        public bool TryAckInfo(out ushort ackSeq, out uint ackBits)
        {
            ackSeq = 0;
            ackBits = 0;
            if (!haveReceived) return false;

            ackSeq = maxSeen;

            int dLow = 0;
            if (hasDelivered) dLow = NetxSeq.Diff(nextExpected, lowDelivered);

            for (int i = 0; i < NetxConst.RecvWindow; i++)
            {
                ushort seq = unchecked((ushort)(ackSeq - 1 - (ushort)i));

                if (received.ContainsKey(seq))
                {
                    ackBits |= 1u << i;
                    continue;
                }

                if (dLow > 0)
                {
                    int d = NetxSeq.Diff(nextExpected, seq);
                    if (d > 0 && d <= dLow) ackBits |= 1u << i;
                }
            }
            return true;
        }

        public void Receive(ushort seq, byte[] payload, int offset, int length, List<byte[]> outMsgs)
        {
            haveReceived = true;
            if (NetxSeq.Greater(seq, maxSeen)) maxSeen = seq;

            if (seq != nextExpected && !NetxSeq.Greater(seq, nextExpected))
            {
                DroppedCount++;
                return;
            }

            if (received.ContainsKey(seq))
            {
                DroppedCount++;
                return;
            }

            var buf = new byte[length];
            if (length > 0) Buffer.BlockCopy(payload, offset, buf, 0, length);
            received[seq] = buf;

            while (received.TryGetValue(nextExpected, out var p))
            {
                received.Remove(nextExpected);
                outMsgs.Add(p);
                DeliveredCount++;

                if (!hasDelivered)
                {
                    lowDelivered = nextExpected;
                    hasDelivered = true;
                }
                nextExpected++;
                if (NetxSeq.Diff(nextExpected, lowDelivered) > NetxConst.RecvWindow)
                    lowDelivered = unchecked((ushort)(nextExpected - NetxConst.RecvWindow));
            }
        }

        public void Reset()
        {
            received.Clear();
            nextExpected = 0;
            maxSeen = 0;
            lowDelivered = 0;
            hasDelivered = false;
            haveReceived = false;
        }
    }
}