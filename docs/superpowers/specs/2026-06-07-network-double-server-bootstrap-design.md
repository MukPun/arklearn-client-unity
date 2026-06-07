# 设计书：双服联网启动 —— NetworkManager 引导方案

| 项 | 值 |
|---|---|
| 创建日期 | 2026-06-07 |
| 状态 | 设计中 · 待对齐真实 sproto 协议字段后进入实现 |
| 作者 | MukPun |
| 协助 | Claude Code |
| 对应代码分支 | `master` |
| 相关 commit 前置 | `6f52d8b` 之后 |

---

## 1. 背景与目标

### 1.1 项目背景
《ArknightsLearn》是《明日方舟》的复刻学习项目，目标是**在尽可能不改原代码的前提下，把项目跑起来并补足缺失资源**（见 [.claude/CLAUDE.md](../../../.claude/CLAUDE.md)）。

项目现有：
- 一套 sproto 客户端库（C# + Unity 胶水，来自 [lvzixun/sproto-Csharp](https://github.com/lvzixun/sproto-Csharp) + [m2q1n9/sproto-Unity](https://github.com/m2q1n9/sproto-Unity)），含三个静态网络类：[NetCore.cs](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs)、[NetSender.cs](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetSender.cs)、[NetReceiver.cs](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetReceiver.cs)
- 一个启动场景 [SampleScene.unity](../../../Assets/Scenes/SampleScene.unity) + 一个引导脚本 [GameStart.cs](../../../Assets/Scripts/GameStart.cs)
- 业务 Manager 模式分两类：`MonoSingle<T>`（带 MonoBehaviour，自动 `DontDestroyOnLoad`）和纯 `Single<T>`（见 [Single.cs](../../../Assets/Utils/Single.cs)）
- 单 sproto schema [game.sproto](../../../Assets/Sproto/protocol/game.sproto)：仅含 `handshake (tag=1)` 与 `heartbeat (tag=2)`
- **没有** NetworkManager、没有 login 协议、没有连接生命周期管理

### 1.2 本次目标
对接 skynet 服务端的**经典双服流程**（login server + game server），在客户端补齐：
1. **网络组件初始化**与**生命周期管理**（NetCore/NetSender/NetReceiver 的 Init + Dispatch + Disconnect）
2. **双阶段连接编排**：先连 login 服认证拿凭据 → 断 login → 连 game 服 handshake → 进入业务
3. **会话凭据存储**（uid/subid/secret）
4. **可视化调试入口**（在编辑器/Dev Build 里能手动触发完整流程并观察状态）

### 1.3 非目标（明确不做）
- 不做 LoginUI 网络化改造（保留 [LoginUI.cs](../../../Assets/UI/Sub/LoginUI.cs) 的本地账号列表逻辑作为离线兜底）
- 不做断线重连 / 会话恢复（先把单次完整登录走通）
- 不做 Lua 桥接（业务调用全在 C# 侧；future work）
- 不动 sproto-Unity 源码的既有方法逻辑（仅在 NetCore.cs 末尾追加一个 `ResetState()` 方法）
- 不引入自动化测试框架（项目当前无 EditMode/PlayMode test 基础设施，本次走手动验证路径）

---

## 2. 关键调研发现（决定方案路径的硬约束）

### 2.1 NetCore 是单 socket 全局静态库
- 持有一个 `private static Socket socket;`（[NetCore.cs:14](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L14)）
- 切换服务器必须经过 `Disconnect()` → `Connect()`，无法同时持有两条链路
- `BeginReceive` 回调在 .NET ThreadPool 线程，不在 Unity 主线程 —— 数据先入 `recvQueue`，主线程 `Dispatch()` 出队消费

### 2.2 `Dispatch()` 必须每帧调用（主线程）
[NetCore.cs:196-236](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L196-L236) 是唯一安全消费 RPC 回调的入口；不被 MonoBehaviour 的 `Update` 驱动，业务层将永远收不到服务器消息。

### 2.3 NetCore.Disconnect 不清静态状态 —— 切服污染陷阱
[NetCore.cs:72-78](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L72-L78) 的 `Disconnect()` 只 `socket.Close()`，**不重置** 4 个关键字段：

| 字段 | 切服时不清的后果 |
|---|---|
| `recvQueue` (line 22) | login server 残留响应被错误派发给 game server 的 handler |
| `sessionDict` (line 31) | 过期 RPC session 永久残留，字典持续膨胀 |
| `receivePosition` (line 137) | game socket 的新字节按"旧流偏移"解析，**length-prefix 全错位** |
| `recvStream` | 半接收数据残留在 65536 字节 buffer 内 |

→ **决策**：在 NetCore.cs **末尾追加**（不改既有方法）一个 `public static void ResetState()`，由 NetworkManager 在切服时显式调用。

### 2.4 项目目前没有 login 协议定义
- 现有 sproto schema 仅 `handshake (tag=1)` + `heartbeat (tag=2)`
- 必须扩展 [game.sproto](../../../Assets/Sproto/protocol/game.sproto)：追加 `login` 协议段，并扩展 `handshake.request` 字段以携带 `subid/secret`
- 用 [gen_cs.bat](../../../Assets/Sproto/protocol/gen_cs.bat)（调用 sprotodump）重新生成 [gamesproto.cs](../../../Assets/Sproto/protocol/gen_cs/gamesproto.cs)
- **本设计文档中 login 协议字段为 skynet 标准默认占位，真实字段以用户提供为准**（见 §8.1 待对齐项）

### 2.5 skynet 双服标准流程
```
client → [TCP] login-server               (建链 1)
client ──login{server,user,password}──→
client ←──login.response{uid,subid,server,secret}──
client → [close login socket]             (断 1)
client → [TCP] game-server (用 response.server)  (建链 2)
client ──handshake{subid,secret}──→ watchdog
watchdog → agent 校验 secret → forward fd → 进入游戏
```
两条 socket 严格串行；login 短连接用完即弃；secret 由 login server 颁发，client 仅搬运。

### 2.6 PlayerManager 现有职责
[PlayerManager.cs](../../../Assets/Data/Player/PlayerManager.cs) 当前只持有 `list:List<PlayerData>` + `playerData:PlayerData`，处理**本地 ScriptableObject 角色存档**。**不应**把网络会话凭据（uid/secret）塞进它，否则混淆"本地数据 vs 网络身份"两个语义。

---

## 3. 用户确认的关键决策

| 决策点 | 选择 | 理由 |
|---|---|---|
| 网络组件挂载方式 | **方案 B**：新建 `NetworkManager : MonoSingle<NetworkManager>` 包装类 | 与项目现有 GameManager/SoundManager 等 MonoSingle 风格一致；MonoSingle 的 `new GameObject + DontDestroyOnLoad` 天然解决 Update 驱动与跨场景驻留 |
| 连接时机 | **LoginUI 点击登录时才触发双阶段** | skynet login server 是无状态短连接，启动时空连过去无意义；推迟到用户输入账号密码后再 connect |
| 业务调用语言 | **C# 主调** | 类型安全、IDE 补全好；Lua 桥接留为 future work |
| 服务器地址配置 | **GameSettings 常量**（双服分开） | 与项目现有 [GameSettings.cs](../../../Assets/Settings/GameSettings.cs) 风格一致；运行时若 login 响应里带了 game server 地址则覆盖默认值 |
| login 协议占位策略 | **先用 skynet 标准字段做占位 + `TBD` 注释，真实字段后续 swap** | 不阻塞架构落地；spec review 阶段对齐字段 |
| LoginUI 是否走网络 | **LoginUI 不动**，另加独立 `NetworkTestUI` 调试入口（仅 Editor / Dev Build） | 保留原有本地登录逻辑作为兜底；调试 UI 与生产 UI 完全隔离 |
| uid/secret 存储位置 | **新增独立 `SessionInfo : Single<SessionInfo>`** | 与 PlayerManager 平行存在，语义清晰；登出时一键 Clear |

---

## 4. 架构设计

### 4.1 双服时序架构图

```
┌── SampleScene (启动场景) ─────────────────────────────────────────┐
│                                                                  │
│  [EventSystem]              [GameStart] (Awake 后自毁)             │
│                                │                                 │
│                                ├─ GameManager.Inst()             │
│                                ├─ LuaEnvManager.Inst()           │
│                                ├─ NetworkManager.Inst()  ⭐预热    │
│                                │     └─ NetCore.Init / Sender.Init│
│                                │     └─ Receiver 注册 handshake   │
│                                ├─ UIManager.Inst().Show(LoginUI) │
│                                └─ NetworkTestUI 创建(仅 Dev)      │
└──────────────────────────────┬───────────────────────────────────┘
                               │ 用户在 NetworkTestUI 点「测试登录」
                               ▼
   ┌──────────────── NetworkManager (MonoSingle) ─────────────────┐
   │  State: Idle → LoginConnecting → LoginRequesting             │
   │       → SwitchingToGame → GameConnecting → GameHandshaking   │
   │       → Online | Disconnected                                │
   │                                                              │
   │  Update():        NetCore.Dispatch() + TickHeartbeat()       │
   │  OnAppQuit():     NetCore.Disconnect() + ResetState()        │
   │  BeginLogin(u,p): 启动双阶段编排                                │
   └──────────────────────────────────────────────────────────────┘
                  │ 静态调用
                  ▼
   ┌─── 第三方库 + 末尾追加 ResetState ────────────────────────────┐
   │  NetCore / NetSender / NetReceiver                            │
   │  + NetCore.ResetState()  ← 仅 append, 不改既有方法            │
   └──────────────────────────────────────────────────────────────┘
                  │ 持有
                  ▼
   ┌─── SessionInfo : Single<SessionInfo> ────────────────────────┐
   │  uid / subid / secret / GameHost / GamePort                  │
   │  + SaveLoginResponse() + Clear() + HasSession                │
   └──────────────────────────────────────────────────────────────┘
```

### 4.2 状态机定义

| Stage | 进入条件 | 触发的动作 | 后继状态 |
|---|---|---|---|
| `Idle` | 初始 / 已登出 | 等待 `BeginLogin` 触发 | `LoginConnecting` |
| `LoginConnecting` | `BeginLogin` 被调用 | `NetCore.Connect(LOGIN_HOST, LOGIN_PORT)` | `LoginRequesting`（连上）/ `Disconnected`（超时） |
| `LoginRequesting` | login socket 已建立 | 发 `Protocol.login` 请求 | `SwitchingToGame`（收到响应）/ `Disconnected`（超时/被拒） |
| `SwitchingToGame` | login 响应已到、SessionInfo 已保存 | `Disconnect + ResetState + Connect(game_host)` | `GameConnecting` |
| `GameConnecting` | game 服 connect 已发起 | 等 socket 建立 | `GameHandshaking`（连上）/ `Disconnected`（超时） |
| `GameHandshaking` | game socket 已建立 | 发 `Protocol.handshake { subid, secret }` | `Online`（响应 ok）/ `Disconnected`（被踢） |
| `Online` ★ | handshake 完成 | 心跳计时 + 业务 RPC 可用 | `Disconnected`（网络断） |
| `Disconnected` | 任一阶段失败 / 主动登出 | `Clear` + 等待重新 `BeginLogin` | `LoginConnecting` |

---

## 5. 文件级实施清单

| 操作 | 文件 | 内容 | 改既有逻辑？ |
|---|---|---|---|
| 新增 | `Assets/Manager/NetworkManager.cs` | 双阶段 NetworkManager (MonoSingle) | 无 |
| 新增 | `Assets/Data/Player/SessionInfo.cs` | 会话凭据 (Single) | 无 |
| 新增 | `Assets/Scripts/Debug/NetworkTestUI.cs` | IMGUI 调试面板 | 无 |
| 改 | `Assets/Settings/GameSettings.cs` | 末尾追加 6 个网络常量 | 仅 append |
| 改 | `Assets/Scripts/GameStart.cs` | Awake 中加 2 行 | 仅 append |
| 改 ⚠️ | `Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs` | 文末追加 `public static void ResetState()` | 仅 append（不动既有方法） |
| 改 ⚠️ | `Assets/Sproto/protocol/game.sproto` | 追加 `login` 协议段 + 扩展 `handshake.request` 字段 | 追加字段 |
| 重新生成 | `Assets/Sproto/protocol/gen_cs/gamesproto.cs` | 跑 [gen_cs.bat](../../../Assets/Sproto/protocol/gen_cs.bat) 重生成 | 自动 |
| 零改动 | NetSender.cs / NetReceiver.cs / SampleScene.unity / LoginUI.cs | — | — |

> ⚠️ **关于 NetCore.cs 改动的版本管理风险**
> [.gitignore](../../../.gitignore) 已把 `Assets/Sproto/sproto-Unity/` 整目录排除（视为第三方库），意味着 **NetCore.cs 末尾追加的 `ResetState()` 方法不会被主仓库 git 跟踪**。这是用户在 git 整理阶段的有意决策（详见 commit `6f63b48`）。
>
> 影响与建议：
> - 换电脑/重装/误删 sproto-Unity 后，需要按 [.gitignore](../../../.gitignore) 里的注释重新 `git clone https://github.com/m2q1n9/sproto-Unity.git`，然后**手动重新追加 `ResetState()` 方法**。
> - 建议在本设计文档同级目录额外维护一个 `netcore-reset-state-patch.cs` 片段文件（即本 §6.3 的代码块），作为恢复时的"补丁源"。或者在 `Assets/Manager/NetworkManager.cs` 顶部加一段 `// 依赖：NetCore.cs 末尾的 ResetState() 方法` 注释，提示后人。

---

## 6. 代码骨架（最终落地版）

### 6.1 `Assets/Settings/GameSettings.cs` — 末尾追加

```csharp
namespace Settings {
    public class GameSettings {
        // ... 现有字段保留不动 ...

        // ====== 网络 ======
        // 账号中心（登录服）
        public const string LOGIN_HOST = "127.0.0.1";
        public const int    LOGIN_PORT = 8001;

        // 游戏服默认值（开发期）；运行时若 login 响应里带了 server 字段则覆盖
        public const string GAME_HOST  = "127.0.0.1";
        public const int    GAME_PORT  = 8888;

        // TCP 建链超时（毫秒）。NetCore 内部硬编码 3000，这里集中化方便以后改
        public const int    NET_CONNECT_TIMEOUT_MS = 3000;

        // 单次 RPC 等待响应超时（秒）。超时后由 NetworkManager 的协程 Fail 出去
        public const float  NET_RPC_TIMEOUT_SEC = 10f;

        // 心跳间隔（秒）。skynet 标准做法是 5 秒一次
        public const float  NET_HEARTBEAT_INTERVAL_SEC = 5f;
    }
}
```

### 6.2 `Assets/Data/Player/SessionInfo.cs` — 新增

```csharp
using Tools;

namespace Data.Player {
    /// <summary>
    /// 网络会话身份：login 响应下发，game handshake 时携带。
    /// 与本地角色数据 PlayerData 解耦 —— PlayerManager 管「本地存档」，
    /// SessionInfo 管「网络会话凭证」。
    /// </summary>
    public class SessionInfo : Single<SessionInfo> {
        public long   Uid       { get; private set; }
        public long   SubId     { get; private set; }
        public string Secret    { get; private set; }
        public string GameHost  { get; private set; }
        public int    GamePort  { get; private set; }

        public bool   HasSession => !string.IsNullOrEmpty(Secret);

        public void SaveLoginResponse(long uid, long subid,
                                       string gameHost, int gamePort,
                                       string secret) {
            Uid = uid; SubId = subid;
            GameHost = gameHost; GamePort = gamePort;
            Secret = secret;
        }

        public void Clear() {
            Uid = 0; SubId = 0;
            Secret = null;
            GameHost = null; GamePort = 0;
        }
    }
}
```

### 6.3 `NetCore.cs` — 在文末追加（不动既有 1 行）

```csharp
        /// <summary>
        /// 切换服务器前清理静态状态。
        /// 必须在 Disconnect() 之后、Connect() 之前的同一主线程帧内调用，
        /// 此时 IO 线程不会再产生新 Receive 回调（旧 socket 已 Close）。
        /// </summary>
        public static void ResetState()
        {
            recvQueue.Clear();           // 旧 server 残留字节包
            sessionDict.Clear();         // 旧 server 未完成 RPC session
            receivePosition = 0;         // 接收游标
            recvStream.Seek(0, System.IO.SeekOrigin.Begin); // 接收 stream 位置
        }
```

### 6.4 `Assets/Manager/NetworkManager.cs` — 完整实现

```csharp
using System;
using Data.Player;
using Settings;
using Sproto;
using SprotoType;
using Tools;
using UnityEngine;

namespace Manager {
    public class NetworkManager : MonoSingle<NetworkManager> {
        public enum Stage {
            Idle, LoginConnecting, LoginRequesting,
            SwitchingToGame, GameConnecting, GameHandshaking,
            Online, Disconnected,
        }

        public Stage CurrentStage { get; private set; } = Stage.Idle;

        public event Action          OnOnline;
        public event Action<Stage, string> OnFailed;
        public event Action<Stage>   OnStageChanged;

        private float _heartbeatTimer;

        protected override void Initialization() {
            NetCore.Init();
            NetSender.Init();
            NetReceiver.Init();
            NetCore.enabled = true;
            // TBD: 真实协议字段确认后启用
            // NetReceiver.AddHandler<Protocol.handshake>(OnGameHandshakeResponse);
        }

        private void Update() {
            NetCore.Dispatch();
            TickHeartbeat();
        }

        private void OnApplicationQuit() {
            NetCore.Disconnect();
            NetCore.ResetState();
            SessionInfo.Inst().Clear();
        }

        // ===== Phase 1: 连 login，发账号 =====
        public void BeginLogin(string user, string password) {
            if (CurrentStage != Stage.Idle && CurrentStage != Stage.Disconnected) {
                Debug.LogWarning($"[Net] BeginLogin called in stage {CurrentStage}, ignored");
                return;
            }
            ChangeStage(Stage.LoginConnecting);
            NetCore.Connect(GameSettings.LOGIN_HOST, GameSettings.LOGIN_PORT,
                () => OnLoginSocketConnected(user, password));
        }

        private void OnLoginSocketConnected(string user, string password) {
            ChangeStage(Stage.LoginRequesting);
            // TBD: 真实协议字段以用户提供清单为准，下面是 skynet 标准默认
            // var req = new SprotoType.login.request {
            //     user = user, password = password, server = "default"
            // };
            // NetSender.Send<Protocol.login>(req, OnLoginResponseReceived);
        }

        private void OnLoginResponseReceived(SprotoTypeBase raw) {
            // TBD: 字段名按 skynet 标准应为 {uid, subid, server, secret, error}
            // var rsp = raw as SprotoType.login.response;
            // if (rsp.error != 0) { Fail(Stage.LoginRequesting, $"login error {rsp.error}"); return; }
            // var (host, port) = ParseServerAddr(rsp.server);
            // SessionInfo.Inst().SaveLoginResponse(rsp.uid, rsp.subid, host, port, rsp.secret);
            BeginSwitchToGame();
        }

        // ===== Phase Switch: 断 login → 清状态 → 连 game =====
        private void BeginSwitchToGame() {
            ChangeStage(Stage.SwitchingToGame);
            NetCore.Disconnect();
            NetCore.ResetState();          // ⭐ 关键 — 不调用=灾难，参考 §2.3
            ChangeStage(Stage.GameConnecting);

            var s = SessionInfo.Inst();
            string host = !string.IsNullOrEmpty(s.GameHost) ? s.GameHost : GameSettings.GAME_HOST;
            int    port = s.GamePort > 0                    ? s.GamePort : GameSettings.GAME_PORT;
            NetCore.Connect(host, port, OnGameSocketConnected);
        }

        // ===== Phase 2: 连 game，发 handshake =====
        private void OnGameSocketConnected() {
            ChangeStage(Stage.GameHandshaking);
            // TBD: handshake.request 待协议确认，应携带 {subid, secret}
            // var req = new SprotoType.handshake.request {
            //     subid = SessionInfo.Inst().SubId, secret = SessionInfo.Inst().Secret
            // };
            // NetSender.Send<Protocol.handshake>(req, OnGameHandshakeResponse);
        }

        private void OnGameHandshakeResponse(SprotoTypeBase raw) {
            // var rsp = raw as SprotoType.handshake.response;
            // if (!string.IsNullOrEmpty(rsp.msg) && rsp.msg != "ok") {
            //     Fail(Stage.GameHandshaking, $"handshake reject: {rsp.msg}"); return;
            // }
            ChangeStage(Stage.Online);
            OnOnline?.Invoke();
        }

        // ===== 心跳 =====
        private void TickHeartbeat() {
            if (CurrentStage != Stage.Online) { _heartbeatTimer = 0; return; }
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer >= GameSettings.NET_HEARTBEAT_INTERVAL_SEC) {
                _heartbeatTimer = 0;
                // NetSender.Send<Protocol.heartbeat>();  // 已存在的 tag=2 协议
            }
        }

        // ===== 工具 =====
        private void ChangeStage(Stage next) {
            if (CurrentStage == next) return;
            CurrentStage = next;
            OnStageChanged?.Invoke(next);
        }

        private void Fail(Stage atStage, string reason) {
            Debug.LogWarning($"[Net] Failed at {atStage}: {reason}");
            NetCore.Disconnect();
            NetCore.ResetState();
            SessionInfo.Inst().Clear();
            ChangeStage(Stage.Disconnected);
            OnFailed?.Invoke(atStage, reason);
        }

        private static (string host, int port) ParseServerAddr(string addr) {
            if (string.IsNullOrEmpty(addr)) return (null, 0);
            var p = addr.Split(':');
            return p.Length == 2 && int.TryParse(p[1], out var port)
                ? (p[0], port) : (addr, 0);
        }
    }
}
```

### 6.5 `Assets/Scripts/GameStart.cs` — 改动 2 行

```csharp
using Data.Player;
using Manager;
using Scripts.Game;
using UI;
using UnityEngine;

namespace Scripts {
    public class GameStart : MonoBehaviour {
        private void Awake() {
            GameManager.Inst();
            LuaEnvManager.Inst();
            NetworkManager.Inst();                  // ⭐ 新增：预热（不连任何服）
            UIManager.Inst().Show("LoginUI");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            new GameObject("NetworkTestUI")
                .AddComponent<Debug.NetworkTestUI>();  // ⭐ 新增：仅 Editor/Dev
#endif
            Destroy(gameObject);
        }
    }
}
```

### 6.6 `Assets/Scripts/Debug/NetworkTestUI.cs` — 临时调试入口

```csharp
using Manager;
using UnityEngine;

namespace Scripts.Debug {
    /// <summary>
    /// IMGUI 调试面板，按 F12 切换显隐。
    /// 仅在 Editor / Development Build 编译进来（见 GameStart）。
    /// 真实 LoginUI 接入网络后可删除本文件。
    /// </summary>
    public class NetworkTestUI : MonoBehaviour {
        private string _user = "dev01";
        private string _pwd  = "123456";
        private bool   _show = true;
        private string _log  = "";

        private void Awake() {
            var nm = NetworkManager.Inst();
            nm.OnStageChanged += s => Log($"stage → {s}");
            nm.OnOnline       += () => Log("★ ONLINE ★");
            nm.OnFailed       += (st, r) => Log($"✗ failed at {st}: {r}");
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F12)) _show = !_show;
        }

        private void OnGUI() {
            if (!_show) return;
            GUILayout.BeginArea(new Rect(10, 10, 320, 240), GUI.skin.box);
            GUILayout.Label($"[NetworkTestUI]  stage={NetworkManager.Inst().CurrentStage}");
            _user = GUILayout.TextField(_user);
            _pwd  = GUILayout.TextField(_pwd);
            if (GUILayout.Button("BeginLogin (双阶段)"))
                NetworkManager.Inst().BeginLogin(_user, _pwd);
            GUILayout.Label(_log);
            GUILayout.EndArea();
        }

        private void Log(string msg) {
            _log = $"{System.DateTime.Now:HH:mm:ss}  {msg}\n" + _log;
            if (_log.Length > 800) _log = _log.Substring(0, 800);
        }
    }
}
```

---

## 7. 错误处理、边界与验证

### 7.1 错误处理矩阵
所有 Fail 走统一出口 `NetworkManager.Fail(stage, reason)`：`Disconnect + ResetState + SessionInfo.Clear + 状态机置 Disconnected + 触发 OnFailed 事件`。

| 阶段 | 触发原因 | 检测点 | 处理 |
|---|---|---|---|
| `LoginConnecting` | login server 未启动 / 防火墙 | [NetCore.cs:55](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L55) `WaitOne` 返回 false，**socketConnected 回调不被调用** | 加超时协程：`BeginLogin` 后 3.5 秒还在该阶段则 `Fail` |
| `LoginConnecting` | TCP 建链失败（RST） | `EndConnect` 抛 SocketException —— **当前代码未 catch** | 超时协程兜底 |
| `LoginRequesting` | 密码错 / 账号被禁 | `OnLoginResponseReceived` 中 `rsp.error != 0` | `Fail(LoginRequesting, ...)` |
| `LoginRequesting` | login server 永远不回 | 状态停在该阶段 | RPC 级超时协程（`GameSettings.NET_RPC_TIMEOUT_SEC`，默认 10 秒） |
| `GameConnecting` | game 服未启动 | 同 LoginConnecting | 同上 |
| `GameHandshaking` | secret 校验失败 | response.msg ≠ "ok" 或 watchdog 关 socket | `Fail` |
| `Online` | 网络中断 | 每帧检查 `NetCore.connected` 跌 false | `Fail(Online, "disconnected")` |

### 7.2 边界条件

| 场景 | 处理 |
|---|---|
| 用户双击登录 | `BeginLogin` 入口检查 `CurrentStage` 拦截 |
| 切换账号 | 调 `Fail(CurrentStage, "user-logout")` 复用通用清理路径；NetworkManager 不单独暴露 Logout 方法 |
| 应用切后台 | 监听 `OnApplicationPause(bool)`，恢复后检查 `connected` |
| Unity Editor 域重载 | 关 "Enter Play Mode Settings → Reload Domain"；或在 `[RuntimeInitializeOnLoadMethod]` 里强制 `ResetState` |
| 场景切换 | NetworkManager 是 `DontDestroyOnLoad`，OK；但 NetReceiver handler 注册者必须在 `OnDestroy` 时 `RemoveHandler` 避免悬空引用 |
| `NetCore.enabled = false` | 默认 false，Send 会静默吃包 → `Initialization` 中已置 true |

### 7.3 验证步骤（手动）

#### Step 1：编译通过
- 保留所有 `TBD` 注释，Edit → Play
- ✓ 期望：左上角出现 IMGUI 调试面板 [NetworkTestUI]
- ✗ 排错：99% 是 namespace 路径不对

#### Step 2：状态机干跑（不需要服务端）
- 在 NetworkTestUI 点 BeginLogin
- ✓ 期望：3.5 秒内 Console 出现 `Idle → LoginConnecting → ...timeout... → Disconnected`

#### Step 3：用 nc/Python 假装 login server
```bash
nc -lk 8001
```
- 点 BeginLogin
- ✓ 期望：状态机走到 `LoginRequesting` 后停住；nc 终端看到新连接
- 验证：`NetCore.Connect` + `OnLoginSocketConnected` 回调链路工作

#### Step 4：切服路径冒烟
临时按钮调用 `Disconnect + ResetState + Connect(GAME_HOST)`，另开 `nc -lk 8888` 验证
- ✓ 期望：旧 fd 释放、新 fd 建立 → 验证调研发现 §2.3 的 4 个污染点都已规避

#### Step 5：拿到真实 sproto 字段后
1. 扩展 [game.sproto](../../../Assets/Sproto/protocol/game.sproto) 加 login 协议段
2. 跑 [gen_cs.bat](../../../Assets/Sproto/protocol/gen_cs.bat) 重生成
3. 解开 NetworkManager 中所有 `TBD` 注释
4. 接通真实 skynet 服端 → Editor → Play → BeginLogin
5. ✓ 期望：看到 `★ ONLINE ★`

---

## 8. 待对齐项 / 已知 debt

### 8.1 真实 sproto 协议字段（阻塞实现）⚠️
本设计文档中所有 `TBD` 注释处需要由用户提供 skynet 服务端实际使用的协议字段清单。**最小必填**：

- `login.request`：要发的字段（默认推测：`server, user, password`）
- `login.response`：要收的字段（默认推测：`uid, subid, server, secret, error`）
- `handshake.request`：game 阶段要发的字段（默认推测：`subid, secret`，需扩展现有 schema）
- `handshake.response`：已有 `msg:string`，是否够用？

字段确认后即可：
- 把字段填进 [game.sproto](../../../Assets/Sproto/protocol/game.sproto)
- 跑 [gen_cs.bat](../../../Assets/Sproto/protocol/gen_cs.bat)
- 解开 NetworkManager 中 TBD 注释

### 8.2 sproto-Unity 预存在的代码 debt（本次不修，仅记录）

1. **`recvQueue` 线程安全缺陷**：[NetCore.cs:22](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L22) 是普通 `Queue<byte[]>`，IO 线程写、主线程读、无锁。建议未来换成 `ConcurrentQueue<byte[]>`。
2. **`Connected` 回调无 try/catch**：[NetCore.cs:66-70](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L66-L70) `EndConnect` 抛异常时在 ThreadPool 线程裸抛，移动平台可能闪退。
3. **`NetSender.session` 是全局累加 long**：长期运行不会冲突但内存持续增长（int64 几乎不会溢出，只是 dict 残留）。
4. **`CONNECT_TIMEOUT` 硬编码 3000ms**：[NetCore.cs:19](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs#L19)；不读 GameSettings。本次设计也未要求改它，但 `GameSettings.NET_CONNECT_TIMEOUT_MS` 暂时是"占位常量"，目前没生效。

### 8.3 不在本设计范围（future work）
- 断线重连 / 会话恢复
- Lua 桥接：在 [Global.lua.txt](../../../Assets/Resources/LuaScripts/Global/Global.lua.txt) 加 `Net = CS.NetSender` 让 Lua 也能发包
- 用 EditMode/PlayMode Test 框架做自动化网络测试
- HTTPS / TLS 加密（skynet 默认 TCP 明文，生产环境需另行设计）

---

## 9. 参考资料

- **sproto-Unity 上游**：https://github.com/m2q1n9/sproto-Unity
- **sproto-Csharp 上游**：https://github.com/lvzixun/sproto-Csharp
- **sprotodump 上游**：https://github.com/lvzixun/sprotodump
- **skynet 框架**：https://github.com/cloudwu/skynet
- **skynet LoginServer 文档**：https://github.com/cloudwu/skynet/wiki/LoginServer
- **skynet 标准示例**：
  - `examples/loginserver.lua` —— 登录网关
  - `examples/watchdog.lua` —— game 入口
  - `examples/agent.lua` —— 玩家会话

---

## 附录 A：本设计涉及的 Unity 知识点速查

1. **`MonoSingle<T>` vs `Single<T>`**：MonoBehaviour 单例有 Update/Coroutine/OnApplicationQuit 能力但需要 GameObject 寄宿；纯 Single 只有静态字段语义但无生命周期回调。网络需要 Update → 选 MonoSingle。
2. **`DontDestroyOnLoad` 的隐式约定**：MonoSingle 都跨场景驻留 → 业务对象注册的 handler / 事件订阅必须在自己 `OnDestroy` 时取消，否则 NetworkManager 会持有死引用。
3. **主线程封送模式**：socket 异步回调跑在 .NET ThreadPool；要进 Unity API 必须先入队、主线程出队消费。`recvQueue + Dispatch()` 就是这个模式。
4. **`#if UNITY_EDITOR || DEVELOPMENT_BUILD`**：条件编译，发布正式包时这块代码完全剥离。所有调试 UI / 作弊面板 / 性能 overlay 都该用这个开关。
5. **OnGUI / IMGUI**：每帧重绘，性能差但写起来快，适合**临时调试面板**；生产 UI 必须用 UGUI 或 UI Toolkit。
6. **`const` vs `static`**：const 编译期内联进调用方，最高效但不可运行时改；static 可改。本设计的 NETWORK_* 用 const 因为目前无运行时切换需求。
7. **C# event vs UnityEvent**：C# event 性能高，适合每帧/每包高频事件；UnityEvent 能 Inspector 拖拽配置但反射慢约 10x。
8. **静态字段陷阱**：sproto-Unity 把所有状态做 static，便利但 domain reload / 切服时不会自动复位。**`ResetState()` 钩子**就是补这个缺。

---

**文档结束。下一步：spec 自审 → 用户复审 → 写实现计划（writing-plans skill）**。
