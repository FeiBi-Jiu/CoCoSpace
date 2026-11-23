using System;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;
using B = Game.Proto.Backend;

namespace Game.Net
{
    public class KcpNetClient : MonoBehaviour
    {
        enum AckMode { None, Login, Reconnect }

        public static KcpNetClient Instance { get; private set; }

        [SerializeField] string serverIp = "127.0.0.1";
        [SerializeField] ushort port = 7777;
        [SerializeField] float reconnectInterval = 3f;

        NetxUdpClient client;
        float reconnectTimer;
        bool manualDisconnect;

        readonly NetxSnapshotAssembler assembler = new();
        readonly HashSet<string> warnedLegacy = new();

        AckMode ackMode = AckMode.None;
        bool ackSent;

        string pendingRoomId = string.Empty;
        string pendingPlayerName = string.Empty;

        public bool IsConnected => client != null && client.HasPeer;
        public string SessionToken { get; private set; }
        public ulong LocalEntityId { get; private set; }
        public bool HasSession => !string.IsNullOrEmpty(SessionToken);

        public string RoomId { get; private set; } = string.Empty;
        public uint TickRate { get; private set; } = NetxProto.DefaultTickRate;
        public uint Slot { get; private set; }
        public long WorldSeed { get; private set; }
        public uint StartTick { get; private set; }
        public uint ServerTick => assembler.LastServerTick;
        public uint AckedInputTick => assembler.LastAckedInputTick;
        public uint LocalGold => assembler.SelfGold;

        public int InFlight => client != null ? client.InFlight : 0;
        public long BadPackets => client != null ? client.BadPackets : 0;
        public long LastRecvAgeMs => client != null ? client.LastRecvAgeMs : -1;

        public event Action OnConnectedEvent;
        public event Action OnDisconnectedEvent;
        public event Action<byte[]> OnSnapshotData;
        public event Action<B.HelloAck> OnHelloAck;
        public event Action<B.JoinOk> OnJoinOk;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            client?.Close();
            client = null;
            if (Instance == this) Instance = null;
        }

        public void Connect(string ip = null, ushort? p = null)
        {
            manualDisconnect = false;
            reconnectTimer = 0f;

            if (ip != null) serverIp = ip;
            if (p.HasValue) port = p.Value;

            client?.Close();
            client = new NetxUdpClient();
            client.OnConnected += HandleConnected;
            client.OnDisconnected += HandleDisconnected;
            client.OnReliable += HandleReliable;
            client.OnSnapshot += HandleSnapshot;

            ResetSessionState();

            Debug.Log($"[Net] Connect → {serverIp}:{port}  netx v{NetxConst.Version}  protoVersion={NetxProto.ProtocolVersion}");

            if (!client.Connect(serverIp, port))
            {
                Debug.LogError($"[Net] UDP 连接失败：{serverIp}:{port}");
                return;
            }
        }

        public void ReconnectNow()
        {
            Debug.Log("[Net] ReconnectNow");
            Connect();
        }

        public void Close()
        {
            manualDisconnect = true;
            reconnectTimer = 0f;
            Debug.Log("[Net] Close — manual disconnect, 清 session");
            SessionToken = null;
            LocalEntityId = 0;
            ResetSessionState();
            client?.Close();
        }

        public void SaveSession(ulong entityId, string token)
        {
            LocalEntityId = entityId;
            SessionToken = token;
            Debug.Log($"[Net] session saved: entity={entityId}, token={token}");
        }

        void ResetSessionState()
        {
            assembler.Reset();
            BackendBridge.Reset();
            ackMode = AckMode.None;
            ackSent = false;
        }

        void Update()
        {
            client?.Tick();

            if (client != null && !client.IsOpen && !manualDisconnect)
            {
                reconnectTimer += Time.unscaledDeltaTime;
                if (reconnectTimer >= reconnectInterval)
                {
                    reconnectTimer = 0f;
                    Debug.Log($"[Net] auto-reconnecting to {serverIp}:{port} ...");
                    Connect();
                }
            }
        }

        void HandleConnected()
        {
            Debug.Log($"[Net] ✅ connected（已收到服务端首个包，inFlight={client.InFlight}）");
            OnConnectedEvent?.Invoke();
        }

        void HandleDisconnected(string reason)
        {
            Debug.LogWarning($"[Net] ❌ disconnected：{reason}");
            OnDisconnectedEvent?.Invoke();
        }

        public void Login(string roomId, string playerName)
        {
            pendingRoomId = roomId ?? string.Empty;
            pendingPlayerName = playerName ?? string.Empty;

            ResetSessionState();
            ackMode = AckMode.Login;

            string targetRoom = ResolveRoom(pendingRoomId);

            SendEnvelope(NetxProto.BuildHello(pendingPlayerName, string.Empty));
            SendEnvelope(NetxProto.BuildJoinRoom(targetRoom));

            Debug.Log($"[Net] Login → name={pendingPlayerName} room={targetRoom}");
        }

        public void ReconnectWithToken(string token)
        {
            ResetSessionState();
            ackMode = AckMode.Reconnect;

            SendEnvelope(NetxProto.BuildHello(pendingPlayerName, token ?? string.Empty));
            SendEnvelope(NetxProto.BuildJoinRoom(ResolveRoom(RoomId)));

            Debug.Log($"[Net] Reconnect → name={pendingPlayerName} room={RoomId}");
        }

        string ResolveRoom(string requested)
        {
            if (string.IsNullOrEmpty(RoomId)) return requested ?? string.Empty;

            if (!string.IsNullOrEmpty(requested) && requested != RoomId)
                Debug.LogWarning($"[Net] 请求房间 {requested} 与服务端 {RoomId} 不一致，按服务端房间加入");

            return RoomId;
        }

        void HandleReliable(byte[] data)
        {
            B.Envelope env;
            try
            {
                env = B.Envelope.Parser.ParseFrom(data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Net] 可靠通道解码失败：{e.Message}（{data.Length} 字节）");
                return;
            }

            switch (env.BodyCase)
            {
                case B.Envelope.BodyOneofCase.HelloAck:
                    HandleHelloAck(env.HelloAck);
                    break;
                case B.Envelope.BodyOneofCase.JoinOk:
                    HandleJoinOk(env.JoinOk);
                    break;
                case B.Envelope.BodyOneofCase.PlayerJoined:
                    Debug.Log($"[Net] player joined slot={env.PlayerJoined.Slot} name={env.PlayerJoined.Name}");
                    break;
                case B.Envelope.BodyOneofCase.PlayerLeft:
                    Debug.Log($"[Net] player left slot={env.PlayerLeft.Slot} reason={env.PlayerLeft.Reason}");
                    break;
                case B.Envelope.BodyOneofCase.Snapshot:
                    ApplySnapshot(env.Snapshot, env.Snapshot.ToByteArray());
                    break;
                case B.Envelope.BodyOneofCase.Pong:
                    break;
                default:
                    Debug.LogWarning($"[Net] 未处理的可靠消息：{env.BodyCase}");
                    break;
            }
        }

        void HandleHelloAck(B.HelloAck ack)
        {
            SessionToken = ack.Token;
            RoomId = ack.RoomId ?? string.Empty;
            if (ack.TickRate != 0) TickRate = ack.TickRate;

            Debug.Log($"[Net] HelloAck session={ack.SessionId} room={RoomId} tickRate={TickRate}");

            if (ackMode == AckMode.Login && !string.IsNullOrEmpty(pendingRoomId) && pendingRoomId != RoomId)
            {
                Debug.LogWarning($"[Net] 房间号 {pendingRoomId} 与服务端 {RoomId} 不一致，改按服务端房间重发 JoinRoom");
                SendEnvelope(NetxProto.BuildJoinRoom(RoomId));
            }

            OnHelloAck?.Invoke(ack);
        }

        void HandleJoinOk(B.JoinOk ok)
        {
            Slot = ok.Slot;
            WorldSeed = ok.WorldSeed;
            StartTick = ok.StartTick;

            Debug.Log($"[Net] JoinOk slot={ok.Slot} seed={ok.WorldSeed} startTick={ok.StartTick}");

            OnJoinOk?.Invoke(ok);
        }

        void HandleSnapshot(byte[] data)
        {
            B.Envelope env;
            try
            {
                env = B.Envelope.Parser.ParseFrom(data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Net] 快照通道解码失败：{e.Message}（{data.Length} 字节）");
                return;
            }

            switch (env.BodyCase)
            {
                case B.Envelope.BodyOneofCase.Snapshot:
                    ApplySnapshot(env.Snapshot, data);
                    break;
                case B.Envelope.BodyOneofCase.Pong:
                    break;
                default:
                    Debug.LogWarning($"[Net] 快照通道上的未处理消息：{env.BodyCase}");
                    break;
            }
        }

        void ApplySnapshot(B.Snapshot snap, byte[] raw)
        {
            var full = assembler.Apply(snap, TickRate);
            if (full == null) return;

            if (!ackSent && assembler.SelfEntityId != 0)
            {
                ackSent = true;
                LocalEntityId = assembler.SelfEntityId;
                EmitHandshakeAck(full);
            }

            OnSnapshotData?.Invoke(raw);
            MsgDispatcher.Dispatch(full);
            BackendBridge.Apply(snap, assembler);
        }

        void EmitHandshakeAck(Game.Proto.S2C_Snapshot firstSnapshot)
        {
            if (ackMode == AckMode.Reconnect)
            {
                Debug.Log($"[Net] → S2CReconnectAck entity={LocalEntityId}");
                MsgDispatcher.Dispatch(new Game.Proto.S2CReconnectAck
                {
                    EntityId = LocalEntityId,
                    SessionToken = SessionToken ?? string.Empty,
                    Snapshot = firstSnapshot,
                });
                return;
            }

            Debug.Log($"[Net] → S2C_LoginOk entity={LocalEntityId} slot={Slot}");

            var self = assembler.Find(LocalEntityId);

            var ok = new Game.Proto.S2C_LoginOk
            {
                EntityId = LocalEntityId,
                SessionToken = SessionToken ?? string.Empty,
                Tables = new Game.Proto.TableBundle(),
                Map = new Game.Proto.MapLayout(),
            };
            ok.Players.Add(new Game.Proto.PlayerInit
            {
                EntityId = LocalEntityId,
                Name = pendingPlayerName,
                X = self != null ? self.X : 0f,
                Z = self != null ? self.Z : 0f,
                IsSelf = true,
            });

            Debug.LogWarning("[Net] 后端 proto 暂无表格与地图消息，Tables/Map 为空占位（M3 前需补协议）");

            MsgDispatcher.Dispatch(ok);
        }

        public void Send(ArraySegment<byte> data, NetxChannel channel = NetxChannel.Reliable)
        {
            if (client == null || !client.IsOpen || data.Array == null) return;

            bool ok = channel == NetxChannel.Snapshot
                ? client.SendSnapshot(data.Array, data.Offset, data.Count)
                : client.SendReliable(data.Array, data.Offset, data.Count);

            if (!ok)
                Debug.LogWarning($"[Net] 发送失败（{channel}）：{data.Count} 字节，inFlight={client.InFlight}");
        }

        public void SendUnreliable(ArraySegment<byte> data)
        {
            Send(data, NetxChannel.Snapshot);
        }

        public void SendEnvelope(B.Envelope env)
        {
            Send(new ArraySegment<byte>(env.ToByteArray()), NetxChannel.Reliable);
        }

        public void SendEnvelopeUnreliable(B.Envelope env)
        {
            Send(new ArraySegment<byte>(env.ToByteArray()), NetxChannel.Snapshot);
        }

        public void SendEnvelope(Game.Proto.Envelope env)
        {
            string key = env.MsgId.ToString();
            if (warnedLegacy.Add(key))
                Debug.LogWarning($"[Net] 后端协议不支持旧消息 {env.MsgId}，已丢弃（后续同类型不再提示）");
        }

        public void SendEnvelopeUnreliable(Game.Proto.Envelope env)
        {
            SendEnvelope(env);
        }
    }
}