using System;
using System.Collections.Generic;

namespace Game.Net
{
    public sealed class NetxSession
    {
        readonly NetxReliableSender reliable = new();
        readonly NetxReliableReceiver recv = new();
        readonly NetxReassembler reasm = new();
        readonly Queue<byte[]> outQueue = new(NetxConst.OutQueueSize);

        ushort snapSeq;
        ushort snapRecvSeq;
        bool hasSnapRecv;

        ushort reliableAcked;
        uint reliableBits;
        bool hasReliable;

        public long LastRecvMs;
        public long LastSendMs;

        public long SentPackets;
        public long RecvPackets;
        public long DroppedSend;
        public long BadPackets;

        public bool Closed { get; private set; }

        public int InFlight => reliable.InFlight;
        public int OutCount => outQueue.Count;
        public int ReassemblyPending => reasm.Pending;

        public NetxSession()
        {
            long now = NetxClock.NowMs;
            LastRecvMs = now;
            LastSendMs = now;
        }

        public void Close() => Closed = true;

        public bool SendReliable(byte[] payload, int offset, int length)
        {
            if (Closed) return false;

            var pkt = reliable.SendParts(payload, offset, length);
            if (pkt == null)
            {
                reliable.DroppedCount++;
                return false;
            }

            return SendParts(pkt);
        }

        bool SendParts(NetxOutstandingPacket pkt)
        {
            bool all = true;
            int last = pkt.Parts.Length - 1;
            bool fragmented = pkt.Parts.Length > 1;

            for (int i = 0; i < pkt.Parts.Length; i++)
            {
                NetxFlags flags = NetxFlags.None;
                if (fragmented)
                {
                    flags |= NetxFlags.Fragmented;
                    if (i == last) flags |= NetxFlags.LastFrag;
                }
                if (!EnqueueReliable(pkt.Seq, pkt.Parts[i], flags)) all = false;
            }
            return all;
        }

        bool EnqueueReliable(ushort seq, byte[] body, NetxFlags fragFlags)
        {
            var h = new NetxHeader
            {
                Version = NetxConst.Version,
                Channel = NetxChannel.Reliable,
                Seq = seq,
                Flags = fragFlags,
            };

            if (TryAckInfo(out ushort ackSeq, out uint ackBits))
            {
                h.AckSeq = ackSeq;
                h.AckBits = ackBits;
                h.Flags |= NetxFlags.HasAck;
            }

            return Enqueue(NetxHeader.Build(h, body, 0, body.Length));
        }

        public bool SendSnapshot(byte[] payload, int offset, int length)
        {
            if (Closed) return false;

            var h = new NetxHeader
            {
                Version = NetxConst.Version,
                Channel = NetxChannel.Snapshot,
                Seq = snapSeq,
            };
            snapSeq++;

            if (TryAckInfo(out ushort ackSeq, out uint ackBits))
            {
                h.AckSeq = ackSeq;
                h.AckBits = ackBits;
                h.Flags |= NetxFlags.HasAck;
            }

            return Enqueue(NetxHeader.Build(h, payload, offset, length));
        }

        bool TryAckInfo(out ushort ackSeq, out uint ackBits)
        {
            ackSeq = 0;
            ackBits = 0;
            if (!hasReliable) return false;
            ackSeq = reliableAcked;
            ackBits = reliableBits;
            return true;
        }

        bool Enqueue(byte[] pkt)
        {
            if (Closed) return false;

            if (outQueue.Count >= NetxConst.OutQueueSize)
            {
                outQueue.Dequeue();
                DroppedSend++;
            }

            outQueue.Enqueue(pkt);
            SentPackets++;
            LastSendMs = NetxClock.NowMs;
            return true;
        }

        public bool TryDequeuePacket(out byte[] pkt)
        {
            if (outQueue.Count == 0)
            {
                pkt = null;
                return false;
            }
            pkt = outQueue.Dequeue();
            return true;
        }

        public void HandlePacket(NetxHeader h, byte[] pkt, int offset, int length,
                                 List<byte[]> reliableOut, out byte[] snapshot)
        {
            LastRecvMs = NetxClock.NowMs;
            RecvPackets++;

            if ((h.Flags & NetxFlags.HasAck) != 0)
                reliable.Ack(h.AckSeq, h.AckBits);

            snapshot = null;

            switch (h.Channel)
            {
                case NetxChannel.Reliable:
                    HandleReliable(h, pkt, offset, length, reliableOut);
                    break;
                case NetxChannel.Snapshot:
                    snapshot = HandleSnapshot(h, pkt, offset, length);
                    break;
                default:
                    BadPackets++;
                    break;
            }
        }

        void HandleReliable(NetxHeader h, byte[] pkt, int offset, int length, List<byte[]> outMsgs)
        {
            if ((h.Flags & NetxFlags.Fragmented) != 0)
            {
                if (!NetxFragHeader.TryDecode(pkt, offset, length, out var f, out _))
                {
                    BadPackets++;
                    return;
                }

                int bodyOffset = offset + NetxConst.FragHeaderSize;
                int bodyLength = length - NetxConst.FragHeaderSize;
                if (bodyLength < 0) bodyLength = 0;

                if (!reasm.Push(f, pkt, bodyOffset, bodyLength, out var whole, out bool done, out _))
                {
                    BadPackets++;
                    RefreshReliableAck();
                    return;
                }

                if (!done)
                {
                    RefreshReliableAck();
                    return;
                }

                recv.Receive(h.Seq, whole, 0, whole.Length, outMsgs);
            }
            else
            {
                recv.Receive(h.Seq, pkt, offset, length, outMsgs);
            }

            RefreshReliableAck();
        }

        byte[] HandleSnapshot(NetxHeader h, byte[] pkt, int offset, int length)
        {
            if (hasSnapRecv)
            {
                if (h.Seq == snapRecvSeq || !NetxSeq.Greater(h.Seq, snapRecvSeq))
                {
                    BadPackets++;
                    return null;
                }
            }

            snapRecvSeq = h.Seq;
            hasSnapRecv = true;

            if (length <= 0) return null;

            var copy = new byte[length];
            Buffer.BlockCopy(pkt, offset, copy, 0, length);
            return copy;
        }

        void RefreshReliableAck()
        {
            if (!recv.TryAckInfo(out ushort ackSeq, out uint ackBits)) return;
            reliableAcked = ackSeq;
            reliableBits = ackBits;
            hasReliable = true;
        }

        public int DrainDue()
        {
            if (Closed) return 0;

            var due = reliable.Due();
            if (due == null) return 0;

            foreach (var p in due)
                SendParts(p);

            return due.Count;
        }

        public int SweepReassembly() => reasm.Sweep();

        public void ResetReceiveState()
        {
            recv.Reset();
            reasm.Reset();
            hasSnapRecv = false;
            snapRecvSeq = 0;
        }
    }
}