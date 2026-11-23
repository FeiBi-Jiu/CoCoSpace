using System;
using Game.Map;
using Game.Net;
using Game.Proto;
using Game.Snapshot;
using Game.Table;
using Google.Protobuf;
using UnityEngine;

namespace Game
{
    public class GameRoot : MonoBehaviour
    {
        public enum GameState
        {
            Idle,
            Connecting,
            Authenticating,
            InGame,
            Reconnecting,
        }

        public static GameRoot Instance { get; private set; }

        public ulong LocalEntityId { get; private set; }
        public GameState State { get; private set; } = GameState.Idle;
        public Transform LocalPlayer => localPlayer;

        public Transform GetEntityTransform(ulong entityId)
        {
            if (entityId == LocalEntityId && localPlayer != null) return localPlayer;

            var director = SnapshotDirector.Instance;
            return director != null ? director.GetEntityTransform(entityId) : null;
        }

        public event Action<string> LoginFailed;
        public event Action<string> LoginProgress;
        public event Action LoginSucceeded;
        public event Action Disconnected;
        public event Action Reconnected;

        [SerializeField] MapBuilder mapBuilder;
        [SerializeField] Transform localPlayer;
        [SerializeField] GameObject localPlayerPrefab;
        [SerializeField] float loginTimeout = 8f;
        [SerializeField] float reconnectTimeout = 12f;

        const int ErrVersionMismatch = 4001;
        const int ErrRoomFull = 4002;
        const int ErrRoomNotFound = 4003;

        IntentSender intentSender;
        string pendingRoomId;
        string pendingPlayerName;
        float stateTimer;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void Start()
        {
            DontDestroyOnLoad(gameObject);

            MsgDispatcher.Register<S2C_LoginOk>(OnLoginOk);
            MsgDispatcher.Register<S2CReconnectAck>(OnReconnectAck);
            MsgDispatcher.Register<Error>(OnServerError);
            MsgDispatcher.Register<MapLayout>(OnMapLayout);
            MsgDispatcher.RegisterRaw(MsgId.S2CMapLayout, OnMapLayoutRaw);

            var net = KcpNetClient.Instance;
            if (net == null)
            {
                Debug.LogError("[Game] KcpNetClient.Instance is null — 场景中缺少 KcpNetClient 组件");
                return;
            }
            net.OnConnectedEvent += OnConnected;
            net.OnDisconnectedEvent += OnDisconnected;
        }

        void OnDestroy()
        {
            MsgDispatcher.Unregister<S2C_LoginOk>();
            MsgDispatcher.Unregister<S2CReconnectAck>();
            MsgDispatcher.Unregister<Error>();
            MsgDispatcher.Unregister<MapLayout>();
            MsgDispatcher.UnregisterRaw(MsgId.S2CMapLayout);

            var net = KcpNetClient.Instance;
            if (net != null)
            {
                net.OnConnectedEvent -= OnConnected;
                net.OnDisconnectedEvent -= OnDisconnected;
            }
        }

        void Update()
        {
            switch (State)
            {
                case GameState.Connecting:
                case GameState.Authenticating:
                    stateTimer += Time.unscaledDeltaTime;
                    if (stateTimer >= loginTimeout)
                        FailLogin("无法连接服务器，请检查 IP/端口后重试");
                    break;

                case GameState.Reconnecting:
                    stateTimer += Time.unscaledDeltaTime;
                    if (stateTimer >= reconnectTimeout)
                        FailLogin("重连超时，请重新登录");
                    break;
            }
        }

        public void BeginLogin(string ip, ushort port, string roomId, string playerName)
        {
            if (State != GameState.Idle)
            {
                Debug.LogWarning($"[Game] BeginLogin ignored, state={State}");
                return;
            }

            var net = KcpNetClient.Instance;
            if (net == null)
            {
                LoginFailed?.Invoke("缺少 KcpNetClient，无法连接");
                return;
            }

            pendingRoomId = roomId;
            pendingPlayerName = playerName;
            State = GameState.Connecting;
            stateTimer = 0f;

            Debug.Log($"[Game] BeginLogin → {ip}:{port} room={roomId} name={playerName}");
            net.Connect(ip, port);
        }

        IntentSender GetIntentSender()
        {
            if (intentSender != null) return intentSender;
            intentSender = FindObjectOfType<IntentSender>();
            if (intentSender == null)
                Debug.LogWarning("[Game] IntentSender 未找到，无法发送登录/重连消息");
            return intentSender;
        }

        void OnConnected()
        {
            var net = KcpNetClient.Instance;
            if (net == null) return;

            if (State == GameState.Reconnecting)
            {
                stateTimer = 0f;
                LoginProgress?.Invoke("已连接服务器，正在恢复会话…");
                Debug.Log($"[Game] auto-reconnect → sending C2S_Reconnect with token={net.SessionToken}");
                GetIntentSender()?.SendReconnect(net.SessionToken);
                return;
            }

            if (State != GameState.Connecting)
            {
                Debug.Log($"[Game] connected but state={State}, ignore");
                return;
            }

            State = GameState.Authenticating;
            stateTimer = 0f;
            LoginProgress?.Invoke("已连接服务器，正在登录…");
            Debug.Log($"[Game] connected → sending C2S_Login room={pendingRoomId}");
            GetIntentSender()?.SendLogin(pendingRoomId, pendingPlayerName);
        }

        void OnDisconnected()
        {
            if (State == GameState.Connecting || State == GameState.Authenticating)
                return;

            if (State != GameState.InGame)
                return;

            State = GameState.Reconnecting;
            stateTimer = 0f;
            Debug.LogWarning("[Game] disconnected → 进入自动重连");
            Disconnected?.Invoke();
        }

        void OnLoginOk(S2C_LoginOk msg)
        {
            if (State != GameState.Authenticating)
                Debug.LogWarning($"[Game] unexpected login ok in state {State}");

            var net = KcpNetClient.Instance;
            if (net == null) return;

            LocalEntityId = msg.EntityId;
            net.SaveSession(msg.EntityId, msg.SessionToken);

            if (TableManager.Instance != null)
                TableManager.Instance.Apply(msg.Tables);
            else
                Debug.LogWarning("[Game] TableManager.Instance is null, skip Apply");

            if (mapBuilder != null)
                mapBuilder.Build(msg.Map);
            else
                Debug.LogWarning("[Game] mapBuilder 未赋值，skip Build");

            BindLocalPlayer(msg);

            State = GameState.InGame;
            stateTimer = 0f;
            Debug.Log($"[Game] ✅ logged in, entity={msg.EntityId}, token={msg.SessionToken}, players={msg.Players.Count}");
            LoginSucceeded?.Invoke();
        }

        void OnMapLayout(MapLayout layout)
        {
            if (mapBuilder == null)
            {
                Debug.LogWarning("[Game] mapBuilder 未赋值，skip map layout");
                return;
            }

            mapBuilder.Build(layout);
        }

        void OnMapLayoutRaw(ByteString payload)
        {
            if (mapBuilder == null)
            {
                Debug.LogWarning("[Game] mapBuilder 未赋值，skip map layout json");
                return;
            }

            mapBuilder.BuildFromJson(payload.ToStringUtf8());
        }

        void OnReconnectAck(S2CReconnectAck msg)
        {
            var net = KcpNetClient.Instance;
            if (net == null) return;

            LocalEntityId = msg.EntityId;
            net.SaveSession(msg.EntityId, msg.SessionToken);

            if (msg.Snapshot != null)
            {
                Debug.Log($"[Game] reconnect snapshot: time={msg.Snapshot.ServerTime}, entities={msg.Snapshot.Entities.Count}");
                SnapshotRecovery.Apply(msg.Snapshot);
                ApplySelfFromSnapshot(msg.Snapshot);
            }

            State = GameState.InGame;
            stateTimer = 0f;
            Debug.Log($"[Game] ✅ reconnected, entity={msg.EntityId}");
            Reconnected?.Invoke();
        }

        void OnServerError(Error msg)
        {
            Debug.LogWarning($"[Game] server error code={msg.Code} message={msg.Message}");

            if (State != GameState.Connecting && State != GameState.Authenticating)
                return;

            FailLogin(DescribeError(msg));
        }

        public void ReturnToLobby()
        {
            State = GameState.Idle;
            stateTimer = 0f;
            LocalEntityId = 0;
            Time.timeScale = 1f;

            KcpNetClient.Instance?.Close();
            Debug.Log("[Game] 返回大厅");
            LoginFailed?.Invoke("已返回大厅，可重新连接");
        }

        void FailLogin(string reason)
        {
            var net = KcpNetClient.Instance;
            State = GameState.Idle;
            stateTimer = 0f;
            LocalEntityId = 0;
            net?.Close();

            Debug.LogWarning($"[Game] login failed: {reason}");
            LoginFailed?.Invoke(reason);
        }

        static string DescribeError(Error msg)
        {
            if (!string.IsNullOrEmpty(msg.Message)) return msg.Message;

            switch (msg.Code)
            {
                case ErrVersionMismatch: return "请更新客户端（协议版本不一致）";
                case ErrRoomFull: return "房间已满";
                case ErrRoomNotFound: return "房间不存在";
                default: return $"登录失败（错误码 {msg.Code}）";
            }
        }

        void BindLocalPlayer(S2C_LoginOk msg)
        {
            if (localPlayer == null && localPlayerPrefab != null)
            {
                var go = Instantiate(localPlayerPrefab);
                localPlayer = go.transform;
            }

            if (localPlayer == null)
            {
                var controller = FindObjectOfType<Player.PlayerController>();
                if (controller != null) localPlayer = controller.transform;
            }

            if (localPlayer == null)
            {
                Debug.LogWarning("[Game] 找不到本地玩家，跳过位置初始化");
                return;
            }

            foreach (var p in msg.Players)
            {
                if (!p.IsSelf) continue;
                localPlayer.position = new Vector3(p.X, localPlayer.position.y, p.Z);
                break;
            }

            localPlayer.name = $"LocalPlayer_{LocalEntityId}";
        }

        void ApplySelfFromSnapshot(S2C_Snapshot snapshot)
        {
            if (localPlayer == null) return;

            foreach (var entity in snapshot.Entities)
            {
                if (entity.EntityId != LocalEntityId) continue;
                localPlayer.position = new Vector3(entity.X, localPlayer.position.y, entity.Z);
                return;
            }
        }
    }
}