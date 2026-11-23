using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Game.Net
{
    public sealed class NetxUdpClient
    {
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<byte[]> OnReliable;
        public event Action<byte[]> OnSnapshot;

        readonly byte[] readBuffer = new byte[NetxConst.ReadBufferSize];
        readonly List<byte[]> reliableScratch = new(8);

        Socket socket;
        NetxSession session;

        long lastSweepMs;
        long lastProbeMs;
        long connectStartMs;

        bool hasPeer;
        bool closed = true;
        bool disconnectNotified;

        public bool HasPeer => hasPeer;
        public bool IsOpen => !closed && socket != null;
        public int InFlight => session != null ? session.InFlight : 0;
        public int ReassemblyPending => session != null ? session.ReassemblyPending : 0;

        public long SentPackets => session != null ? session.SentPackets : 0;
        public long RecvPackets => session != null ? session.RecvPackets : 0;
        public long DroppedSend => session != null ? session.DroppedSend : 0;
        public long BadPackets => session != null ? session.BadPackets : 0;
        public long LastRecvAgeMs => session == null ? -1 : NetxClock.NowMs - session.LastRecvMs;
        public long ConnectAgeMs => closed ? -1 : NetxClock.NowMs - connectStartMs;

        public bool Connect(string host, ushort port)
        {
            Close();

            closed = false;
            disconnectNotified = false;
            hasPeer = false;

            session = new NetxSession();

            try
            {
                IPAddress addr = Resolve(host);
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    Blocking = false,
                    ReceiveBufferSize = 256 * 1024,
                    SendBufferSize = 256 * 1024,
                };
                socket.Connect(new IPEndPoint(addr, port));
            }
            catch (Exception)
            {
                Close();
                return false;
            }

            connectStartMs = NetxClock.NowMs;
            lastSweepMs = connectStartMs;
            lastProbeMs = long.MinValue / 2;

            SendProbe();
            return true;
        }

        public void Close()
        {
            closed = true;
            hasPeer = false;

            if (socket != null)
            {
                try { socket.Close(); }
                catch (Exception) { }
                socket = null;
            }

            session?.Close();
            session = null;
        }

        public void Tick()
        {
            if (closed || socket == null || session == null) return;

            PumpReceive();
            if (closed) return;

            long now = NetxClock.NowMs;

            if (now - lastSweepMs >= NetxConst.SweepIntervalMs)
            {
                lastSweepMs = now;
                session.DrainDue();
                session.SweepReassembly();
            }

            PumpSend();
            if (closed) return;

            if (!hasPeer)
            {
                if (now - lastProbeMs >= NetxConst.ProbeIntervalMs) SendProbe();
                return;
            }

            if (now - session.LastRecvMs >= NetxConst.SessionTimeoutMs)
                NotifyDisconnected($"会话超时：{NetxConst.SessionTimeoutMs}ms 未收到服务端数据");
        }

        public bool SendReliable(byte[] payload, int offset, int length)
        {
            if (closed || session == null) return false;
            return session.SendReliable(payload, offset, length);
        }

        public bool SendReliable(ArraySegment<byte> data)
        {
            if (data.Array == null) return false;
            return SendReliable(data.Array, data.Offset, data.Count);
        }

        public bool SendSnapshot(byte[] payload, int offset, int length)
        {
            if (closed || session == null) return false;
            return session.SendSnapshot(payload, offset, length);
        }

        void SendProbe()
        {
            if (closed || socket == null || session == null) return;

            var h = new NetxHeader
            {
                Version = NetxConst.Version,
                Channel = NetxChannel.Snapshot,
                Seq = 0xFFFF,
            };

            lastProbeMs = NetxClock.NowMs;
            SendRaw(NetxHeader.Build(h, null, 0, 0));
        }

        void PumpReceive()
        {
            for (int i = 0; i < 256; i++)
            {
                if (closed || socket == null) return;

                bool ready;
                try { ready = socket.Poll(0, SelectMode.SelectRead); }
                catch (Exception)
                {
                    NotifyDisconnected("socket 轮询失败");
                    return;
                }
                if (!ready) return;

                int n;
                try
                {
                    n = socket.Receive(readBuffer, 0, readBuffer.Length, SocketFlags.None);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock) return;
                    if (e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    if (e.SocketErrorCode == SocketError.Interrupted) continue;
                    NotifyDisconnected($"收包失败：{e.SocketErrorCode}");
                    return;
                }
                catch (Exception)
                {
                    NotifyDisconnected("收包异常");
                    return;
                }

                if (n > 0) HandlePacket(n);
            }
        }

        void HandlePacket(int length)
        {
            if (session == null) return;

            if (!NetxHeader.TryDecode(readBuffer, 0, length, out var h, out _))
            {
                session.BadPackets++;
                return;
            }

            int payloadLength = length - NetxConst.HeaderSize;
            if (payloadLength < 0) payloadLength = 0;

            reliableScratch.Clear();
            session.HandlePacket(h, readBuffer, NetxConst.HeaderSize, payloadLength, reliableScratch, out var snapshot);

            if (!hasPeer)
            {
                hasPeer = true;
                OnConnected?.Invoke();
            }

            if (reliableScratch.Count > 0)
            {
                foreach (var msg in reliableScratch)
                    OnReliable?.Invoke(msg);
            }

            if (snapshot != null) OnSnapshot?.Invoke(snapshot);
        }

        void PumpSend()
        {
            while (!closed && socket != null && session != null && session.TryDequeuePacket(out var pkt))
                SendRaw(pkt);
        }

        void SendRaw(byte[] pkt)
        {
            if (closed || socket == null) return;

            try
            {
                socket.Send(pkt, 0, pkt.Length, SocketFlags.None);
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.WouldBlock)
                {
                    if (session != null) session.DroppedSend++;
                    return;
                }
                if (e.SocketErrorCode == SocketError.ConnectionReset) return;
                if (e.SocketErrorCode == SocketError.NetworkUnreachable) return;
                NotifyDisconnected($"发包失败：{e.SocketErrorCode}");
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception)
            {
                NotifyDisconnected("发包异常");
            }
        }

        void NotifyDisconnected(string reason)
        {
            if (disconnectNotified) return;
            disconnectNotified = true;
            hasPeer = false;

            if (socket != null)
            {
                try { socket.Close(); }
                catch (Exception) { }
                socket = null;
            }
            session?.Close();

            OnDisconnected?.Invoke(reason);
        }

        static IPAddress Resolve(string host)
        {
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";

            if (IPAddress.TryParse(host, out var literal)) return literal;

            var addrs = Dns.GetHostAddresses(host);
            if (addrs.Length == 0) throw new Exception($"无法解析地址 {host}");

            foreach (var a in addrs)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;

            return addrs[0];
        }
    }
}