using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Game.Net;

static class Program
{
    const int Port = 19999;

    static readonly byte[] Greet = Pattern(200);
    static readonly byte[] BigMsg = Pattern(3000);
    static readonly byte[] SnapMsg = Pattern(500);

    static Socket srvSock;
    static NetxSession srv;
    static EndPoint peer;
    static readonly byte[] rbuf = new byte[NetxConst.ReadBufferSize];
    static readonly List<byte[]> srvMsgs = new();
    static int srvBad;
    static bool dropFragOnce = true;

    static NetxUdpClient cli;
    static readonly List<byte[]> cliMsgs = new();
    static readonly List<byte[]> cliSnaps = new();
    static bool cliConnected;
    static string cliDisc;

    static int failures;

    static byte[] Pattern(int n)
    {
        var a = new byte[n];
        for (int i = 0; i < n; i++) a[i] = (byte)((i * 7 + 13) & 0xFF);
        return a;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) failures++;
    }

    static void PumpUntil(Func<bool> cond, int timeoutMs)
    {
        long start = NetxClock.NowMs;
        while (NetxClock.NowMs - start < timeoutMs)
        {
            srvSock?.Poll(0, SelectMode.SelectRead);
            ServerTick();
            cli?.Tick();
            if (cond()) return;
            System.Threading.Thread.Sleep(2);
        }
    }

    static void ServerTick()
    {
        if (srvSock == null) return;

        for (int i = 0; i < 64; i++)
        {
            bool ready;
            try { ready = srvSock.Poll(0, SelectMode.SelectRead); }
            catch (Exception) { return; }
            if (!ready) break;

            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int n;
            try { n = srvSock.ReceiveFrom(rbuf, ref from); }
            catch (SocketException) { break; }

            if (!NetxHeader.TryDecode(rbuf, 0, n, out var h, out _)) { srvBad++; continue; }

            bool first = srv == null;
            if (first)
            {
                srv = new NetxSession();
                peer = from;
            }

            int payloadLen = n - NetxConst.HeaderSize;

            if (dropFragOnce && (h.Flags & NetxFlags.Fragmented) != 0)
            {
                if (NetxFragHeader.TryDecode(rbuf, NetxConst.HeaderSize, payloadLen, out var f, out _) && f.Index == 1)
                {
                    dropFragOnce = false;
                    Console.WriteLine("[srv] 故意丢弃一个中间分片（测试重传）");
                    continue;
                }
            }

            srvMsgs.Clear();
            srv.HandlePacket(h, rbuf, NetxConst.HeaderSize, payloadLen, srvMsgs, out var snap);

            foreach (var m in srvMsgs)
                srv.SendReliable(m, 0, m.Length);

            if (snap != null)
                srv.SendSnapshot(snap, 0, snap.Length);

            if (first)
            {
                srv.SendReliable(Greet, 0, Greet.Length);
                Console.WriteLine("[srv] 新会话 → 发送 200B 问候");
            }
        }

        if (srv != null)
        {
            srv.DrainDue();
            while (srv.TryDequeuePacket(out var pkt))
            {
                try { srvSock.SendTo(pkt, peer); }
                catch (SocketException) { }
            }
        }
    }

    static int Main()
    {
        srvSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        srvSock.Bind(new IPEndPoint(IPAddress.Loopback, Port));
        srvSock.Blocking = false;

        cli = new NetxUdpClient();
        cli.OnConnected += () => cliConnected = true;
        cli.OnReliable += m => cliMsgs.Add(m);
        cli.OnSnapshot += m => cliSnaps.Add(m);
        cli.OnDisconnected += r => cliDisc = r;

        Check(cli.Connect("127.0.0.1", Port), "客户端 Connect 成功");

        PumpUntil(() => cliConnected, 3000);
        Check(cliConnected, "收到服务端首个包 → 触发 OnConnected");

        PumpUntil(() => cliMsgs.Count >= 1, 3000);
        Check(cliMsgs.Count >= 1 && Same(cliMsgs[0], Greet), "200B 可靠消息内容一致");

        Check(cli.SendReliable(BigMsg, 0, BigMsg.Length), "发送 3000B 可靠消息（3 个分片）");
        PumpUntil(() => cliMsgs.Count >= 2, 5000);
        Check(cliMsgs.Count >= 2 && Same(cliMsgs[1], BigMsg), "3000B 分片消息经丢包重传后完整还原");

        Check(cli.SendSnapshot(SnapMsg, 0, SnapMsg.Length), "发送 500B 快照");
        PumpUntil(() => cliSnaps.Count >= 1, 3000);
        Check(cliSnaps.Count >= 1 && Same(cliSnaps[0], SnapMsg), "快照通道内容一致");

        Check(cli.BadPackets == 0, $"客户端无坏包（BadPackets={cli.BadPackets}）");
        Check(srvBad == 0, $"服务端无坏包（BadPackets={srvBad}）");
        Check(cli.InFlight == 0, $"重传队列已清空（InFlight={cli.InFlight}）");
        Check(cli.ReassemblyPending == 0, $"无残留半成品分片（Pending={cli.ReassemblyPending}）");

        Console.WriteLine("[t] 关闭服务端，等待客户端会话超时 ...");
        srvSock.Close();
        srvSock = null;
        PumpUntil(() => cliDisc != null, 8000);
        Check(cliDisc != null, $"服务端静默 5s 后触发断线回调（{cliDisc}）");

        cli.Close();
        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} 项失败");
        return failures == 0 ? 0 : 1;
    }
}