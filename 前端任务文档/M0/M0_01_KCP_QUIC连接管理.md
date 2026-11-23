# M0 #1 · KCP/QUIC 连接管理

## 所属阶段
M0 · 把线连上

## 任务目标
建立 Unity 客户端到 Go 服务器的 KCP/QUIC 连接，管理连接生命周期，预留断线重连入口（重连=复活，状态由服务器恢复）。

## 前置依赖
- 后端：KCP/QUIC 监听服务就绪（B0 #2）
- 协议：消息 ID 映射表、Envelope 信封格式（B0 #3）

## 已有骨架
- `Assets/Scripts/Net/KcpNetClient.cs` — KCP 连接主类（单例，DontDestroyOnLoad）
- `Assets/Scripts/Net/StubKcp2k.cs` — kcp2k 库的桩/适配层
- `Assets/Plugins/` — Google.Protobuf.dll 已就位

## 实现步骤

### 1. 确认底层传输库
- 当前使用 kcp2k（见 `KcpNetClient` 中 `using kcp2k;` 与 `KcpClient`）
- 若后端最终选 QUIC，需替换 `KcpClient` 为 QUIC 实现，对外接口保持 `Connect/Send/Close/OnConnected/OnData/OnDisconnected` 不变
- `KcpNetClient` 已通过 `KcpChannel.Reliable` 发送，热路径消息默认走可靠通道；若需不可靠通道（如快照），在 `Send` 中增加 `KcpChannel.Unreliable` 重载

### 2. 连接生命周期
```
Connect(ip, port)
  └─ new KcpClient(onConnected, onData, onDisconnected)
  └─ client.Connect(serverIp, port)

Update()
  └─ client.Tick()                          // 驱动 KCP 收发
  └─ 断线检测：!isConnected && !manualDisconnect
        └─ reconnectTimer > 3f → 重连
```
- `manualDisconnect` 标志区分主动断开与异常掉线，避免主动关闭后自动重连
- `OnConnectedEvent` / `OnDisconnectedEvent` 事件供上层（登录、UI）订阅

### 3. 断线重连入口
- 预留 `SendReconnect(token)`（已在 `IntentSender` 中实现），重连时携带 `SessionToken`
- 重连流程：触发 `OnDisconnectedEvent` → UI 提示 → 调用 `Connect()` → 收到 `S2C_ReconnectAck` 后从快照恢复
- 灰盒阶段重连由后端把玩家转为鬼魂并下发快照（B2 #21），前端只做"恢复状态"不做"复活判定"

### 4. 会话与房间
- 登录成功后保存 `SessionToken`（从 `S2C_LoginAck` 取），重连时带上
- 连接参数（ip/port）可在 Inspector 配置，也可由登录界面传入

## 与后端对接
| 方向 | 消息 | 说明 |
|---|---|---|
| C→S | `C2S_Login` | 携带 RoomId / PlayerName / ClientVersion / ProtoVersion |
| S→C | `S2C_LoginAck` | 返回 SessionToken、分配的 EntityId、初始快照 |
| C→S | `C2S_Reconnect` | 断线重连，携带 SessionToken |
| S→C | `S2C_ReconnectAck` | 重连确认 + 最新快照 |

## 关键技术点
- KCP 需要在 `Update` 中持续 `Tick()`，否则不会收发
- `DontDestroyOnLoad` 保证切场景不丢连接
- 重连退避：灰盒固定 3 秒，正式版可加指数退避
- `ProtoHash.Version` 用于前后端 proto 版本一致性校验，版本不匹配应拒绝连接

## 验收标准
- [ ] 客户端能连接到服务器并触发 `OnConnectedEvent`
- [ ] 服务器主动断开或网络中断后，客户端在 3 秒内自动重连
- [ ] 主动调用 `Close()` 后不会自动重连
- [ ] 重连时携带的 SessionToken 能被服务器识别并恢复快照