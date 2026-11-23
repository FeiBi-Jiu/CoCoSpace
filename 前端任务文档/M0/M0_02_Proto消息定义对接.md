# M0 #2 · Proto 消息定义对接

## 所属阶段
M0 · 把线连上

## 任务目标
与后端共用 proto 目录，版本号对齐，在 Unity 中生成 C# 消息类，建立消息 ID 映射与分发机制。

## 前置依赖
- 后端：Proto 消息目录冻结初版（B0 #3）
- 三类消息：状态快照（Snapshot）、输入意图（Intent）、游戏事件（Event）

## 已有骨架
- `Assets/Scripts/Net/Gen/` — 已生成的 proto C# 类（Common / Input / Login / Map / Snapshot / Table / ExtraMessages）
- `Assets/Scripts/Net/MsgId.cs` — 消息 ID 映射
- `Assets/Scripts/Net/MsgDispatcher.cs` — 消息分发器
- `Assets/Scripts/Net/ProtoHash.cs` — Proto 版本哈希

## 实现步骤

### 1. Proto 目录与生成流程
- 前后端共用同一份 `.proto` 文件（建议放在仓库根 `proto/` 目录）
- Unity 端用 `protoc` + `Grpc.Tools` 或手动生成 C# 到 `Assets/Scripts/Net/Gen/`
- 每次 proto 变更后重新生成并提交，确保前后端字段一致

### 2. 消息信封 Envelope
所有热路径消息统一封装：
```
Envelope {
  uint32 msg_id      // 消息类型 ID（MsgIdMap）
  uint32 seq         // 序列号
  bytes  payload     // 序列化后的具体消息
}
```
- `IntentSender.WrapAndSend` 已实现打包逻辑
- 接收端 `MsgDispatcher.Dispatch` 按 `msg_id` 反序列化并分发

### 3. 消息分类与命名约定
| 类别 | 命名 | 方向 | 示例 |
|---|---|---|---|
| 意图 | `C2S_*Intent` / `C2S_*` | C→S | `C2S_Intent`, `C2S_Login` |
| 快照 | `S2C_Snapshot` | S→C | `S2C_Snapshot` |
| 事件 | `S2C_*Event` / `S2C_*` | S→C | `S2C_HitEvent`, `S2C_DropEvent` |
| 冷路径 | 同命名，走 WebSocket | 双向 | `C2S_EnterRoom`, `S2C_MapLayout` |

### 4. 消息 ID 映射
- 在 `MsgId.cs` 中维护 `MsgIdMap.TypeToId` 字典（Type → uint）
- 新增消息时同步更新映射表
- `MsgId` 必须与后端一致，建议由 proto 自动生成或共享常量

### 5. 版本校验
- `ProtoHash.Version` 由 proto 内容计算哈希
- 登录时 `C2S_Login.ProtoVersion` 带上，后端校验不一致则拒绝
- 防止前后端 proto 版本错位导致解析崩溃

## 与后端对接
- proto 文件必须放在双方都能访问的位置，改一端改两端
- 消息 ID、字段编号、字段类型严格一致
- 新增/修改字段走"先加后删"原则，保持向后兼容

## 关键技术点
- `Google.Protobuf` 已在 `Plugins/` 中，无需额外安装
- 热路径消息用 protobuf（体积/解析/GC 优于 JSON）
- 冷路径（地图布局、配置表、结算）走 JSON，不经过 Envelope
- `Seq` 序列号用于重连时去重和乱序处理

## 验收标准
- [ ] proto 生成的 C# 类能在 Unity 中正常编译
- [ ] 客户端发送的 Envelope 能被后端正确解析并分发
- [ ] 后端下发的消息能被 `MsgDispatcher` 正确反序列化
- [ ] `ProtoHash.Version` 前后端一致，不一致时登录被拒