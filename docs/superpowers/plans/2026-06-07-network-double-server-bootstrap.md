# 双服联网启动 NetworkManager 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按设计书 [2026-06-07-network-double-server-bootstrap-design.md](../specs/2026-06-07-network-double-server-bootstrap-design.md) 在 Unity 项目里落地 `NetworkManager`，使其能编排 skynet 双服流程（login 服认证 → 切到 game 服 handshake → 进入业务循环）。

**Architecture:** `MonoSingle<NetworkManager>` 包装类持有双服编排状态机，每帧驱动 `NetCore.Dispatch()`，通过新增 `NetCore.ResetState()` 钩子规避单 socket 切服污染，会话凭据存独立 `SessionInfo : Single<SessionInfo>`，调试入口走 `OnGUI` 包在 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 里。

**Tech Stack:** Unity 2022 LTS + C# + sproto-Unity + skynet 服端 + XLua（Lua 桥接为 future work）；本计划不引入新依赖。

**🚨 阅读须知（与常规 TDD 模板不同）：**
1. **本项目无自动化测试框架**（无 `.asmdef` 测试程序集、无 NUnit/EditMode/PlayMode 配置 — 见 spec §1.3）。所以每个 Task 用「写代码 → Unity 编译验证 → 手动 nc/Editor 验证 → commit」节奏代替 TDD。
2. **NetCore.cs 改动不入主仓库**（[.gitignore](../../../.gitignore) 排除 `Assets/Sproto/sproto-Unity/`）。Task 3 的改动是本机修改，commit 步骤会跳过该文件。
3. **Task 9 是阻塞点**：必须有真实 sproto login 协议字段才能解开 Task 4 留下的 TBD 注释。前 8 个 Task 在没有真实字段时也能完整推进到「状态机可观察、TCP 建链可冒烟」。

---

## 文件结构总览

| 文件 | 操作 | 职责 |
|---|---|---|
| `Assets/Settings/GameSettings.cs` | Modify (append-only) | 加 6 个网络相关常量 |
| `Assets/Data/Player/SessionInfo.cs` | Create | 会话凭据单例（uid/subid/secret/host/port） |
| `Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs` | Modify (append-only, **不入主仓库**) | 文末追加 `public static void ResetState()` 方法 |
| `Assets/Manager/NetworkManager.cs` | Create | 双服编排 + 状态机 + 心跳 + 事件总线 |
| `Assets/Scripts/Debug/NetworkTestUI.cs` | Create | IMGUI 调试面板（按 F12 切换，仅 Editor/Dev） |
| `Assets/Scripts/GameStart.cs` | Modify (append-only) | 在 `Awake` 中加 NetworkManager.Inst() 与 NetworkTestUI 创建 |
| `Assets/Sproto/protocol/game.sproto` | Modify (Task 9, 待用户提供字段) | 追加 login 协议段 + 扩展 handshake.request |
| `Assets/Sproto/protocol/gen_cs/gamesproto.cs` | Regenerate (Task 9) | 跑 `gen_cs.bat` 重新生成 |

---

## Task 1：扩展 GameSettings 加 6 个网络常量

**Files:**
- Modify: `Assets/Settings/GameSettings.cs`（末尾追加，不动既有字段）

**目的：** 把双服地址、超时、心跳间隔集中常量化，避免硬编码散落。

- [ ] **Step 1.1：在 GameSettings.cs 类末尾追加网络常量段**

在 [Assets/Settings/GameSettings.cs](../../../Assets/Settings/GameSettings.cs) 最后一个 `public static int SOUND_VOICE_VALUE = 100;` 后面、`}` 类结束之前，插入：

```csharp

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
```

- [ ] **Step 1.2：切到 Unity Editor，等 auto-recompile**

切到 Unity 窗口，等右下角"编译指示器"转完。
Expected：Console 无 error，无 warning（YellowMessages 也不应增加）。

- [ ] **Step 1.3：commit**

```bash
cd "i:/Sofware/Unity/MukPun/ArknightsLearn"
git add Assets/Settings/GameSettings.cs
git commit -m "feat(net): GameSettings 追加 6 个网络相关常量

- LOGIN_HOST/PORT、GAME_HOST/PORT: 双服默认地址
- NET_CONNECT_TIMEOUT_MS: TCP 建链超时
- NET_RPC_TIMEOUT_SEC: RPC 响应超时
- NET_HEARTBEAT_INTERVAL_SEC: 心跳间隔

为 NetworkManager 集中网络配置，参考设计书 §6.1。"
```

---

## Task 2：新增 SessionInfo 单例

**Files:**
- Create: `Assets/Data/Player/SessionInfo.cs`

**目的：** 把"网络会话身份"从 PlayerManager（本地角色数据）解耦出来。

- [ ] **Step 2.1：创建文件 Assets/Data/Player/SessionInfo.cs**

文件完整内容：

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

- [ ] **Step 2.2：切到 Unity Editor 让它生成 SessionInfo.cs.meta**

切到 Unity 窗口，等 Editor 自动扫描新文件并生成 `SessionInfo.cs.meta`。
Expected：`Assets/Data/Player/` 目录下出现 `SessionInfo.cs` 和 `SessionInfo.cs.meta` 两个文件；Console 无 error。

- [ ] **Step 2.3：commit**

```bash
git add Assets/Data/Player/SessionInfo.cs Assets/Data/Player/SessionInfo.cs.meta
git commit -m "feat(net): 新增 SessionInfo 单例持有网络会话凭据

uid/subid/secret/GameHost/GamePort + SaveLoginResponse/Clear/HasSession。
设计上与 PlayerManager 平行（参考设计书 §6.2），避免本地角色数据被网络身份污染。"
```

---

## Task 3：NetCore.cs 末尾追加 ResetState() ⚠️ 不入主仓库

**Files:**
- Modify: `Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs`（**该目录已被 .gitignore 排除，本 Task 无 git commit**）

**目的：** 给单 socket 切服提供清理钩子，防止 `recvQueue`/`sessionDict`/`receivePosition`/`recvStream` 残留污染下一个服。

**特殊处理：** 该文件来自第三方 git 子仓库 sproto-Unity，[.gitignore](../../../.gitignore) 已排除整目录。本 Task 的改动**只存在于本地工作树**，主仓库不感知。Step 3.3 不做 git commit，只做"补丁文件归档"防丢失。

- [ ] **Step 3.1：在 NetCore.cs 末尾追加 ResetState() 方法**

打开 [Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs](../../../Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs)，定位到文件末尾第 238 行的 `}` 之前（即类的最后一个 `}` 前），插入：

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

- [ ] **Step 3.2：切到 Unity Editor，等 auto-recompile**

Expected：Console 无 error。

- [ ] **Step 3.3：把补丁归档到 docs/ 防丢失（不 commit NetCore.cs）**

创建文件 `docs/superpowers/patches/netcore-reset-state.cs.patch`，内容：

```text
# 此文件是 NetCore.cs 的 ResetState() 补丁备份
# 来源：sproto-Unity (https://github.com/m2q1n9/sproto-Unity)
# 目标：Assets/Sproto/sproto-Unity/sproto-Unity/NetCore.cs 文末（类闭合 } 之前）
# 用途：换电脑/重装/重 clone sproto-Unity 后，需要手动把下面这段贴回原位置

        /// <summary>
        /// 切换服务器前清理静态状态。
        /// 必须在 Disconnect() 之后、Connect() 之前的同一主线程帧内调用，
        /// 此时 IO 线程不会再产生新 Receive 回调（旧 socket 已 Close）。
        /// </summary>
        public static void ResetState()
        {
            recvQueue.Clear();
            sessionDict.Clear();
            receivePosition = 0;
            recvStream.Seek(0, System.IO.SeekOrigin.Begin);
        }
```

然后 commit 这个归档：

```bash
mkdir -p docs/superpowers/patches
# 然后用编辑器创建上面的 .cs.patch 文件并保存
git add docs/superpowers/patches/netcore-reset-state.cs.patch
git commit -m "docs(patches): 归档 NetCore.ResetState() 补丁

NetCore.cs 在 sproto-Unity 第三方子仓库内,已被主仓库 .gitignore 排除,
对它的改动不入主仓库版本管理。此补丁文件作为'恢复时'的源码备份,
未来重 clone sproto-Unity 后手动贴回。"
```

---

## Task 4：新增 NetworkManager（带 TBD 注释，先编译通过）

**Files:**
- Create: `Assets/Manager/NetworkManager.cs`

**目的：** 把状态机骨架先立起来，所有"发包/收包"调用先用 TBD 注释占位（因为 Task 9 才会有真实 sproto login 协议字段）。这一步完成后 NetworkManager 能编译、能调用、能跑状态机的"connect 超时 → Disconnected"分支。

- [ ] **Step 4.1：创建文件 Assets/Manager/NetworkManager.cs**

完整内容：

```csharp
using System;
using System.Collections;
using Data.Player;
using Settings;
using Sproto;
using SprotoType;
using Tools;
using UnityEngine;

namespace Manager {
    /// <summary>
    /// skynet 双服客户端编排：login → handshake → game。
    /// 静态层 NetCore 由本类驱动（Update 调 Dispatch），状态机持有阶段。
    /// 依赖：NetCore.cs 末尾的 ResetState() 方法（见 docs/superpowers/patches/netcore-reset-state.cs.patch）。
    /// </summary>
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
            // TBD(Task 9): 真实 handshake 协议字段确认后启用以下注册
            // NetReceiver.AddHandler<Protocol.handshake>(OnGameHandshakeResponseHandler);
        }

        private void Update() {
            NetCore.Dispatch();
            DetectOnlineDisconnect();
            TickHeartbeat();
        }

        private void OnApplicationQuit() {
            NetCore.Disconnect();
            NetCore.ResetState();
            SessionInfo.Inst().Clear();
        }

        /// <summary>
        /// 应用切后台/被中断时,iOS 30s 内 OS 会清掉 socket,Android 部分机型同理。
        /// 恢复时若发现 Online 阶段 socket 已断,走通用 Fail 路径让 UI 提示重连。
        /// </summary>
        private void OnApplicationPause(bool pause) {
            if (pause) return;
            if (CurrentStage == Stage.Online && !NetCore.connected) {
                Fail(Stage.Online, "socket dropped while paused");
            }
        }

        /// <summary>
        /// 每帧检查 Online 阶段 socket 是否仍然连接。
        /// NetCore 的 Send/Receive 内部已 catch SocketException,
        /// 但不会主动通知业务层 —— 由本检测兜底。
        /// </summary>
        private void DetectOnlineDisconnect() {
            if (CurrentStage == Stage.Online && !NetCore.connected) {
                Fail(Stage.Online, "disconnected");
            }
        }

        // ===== Phase 1: 连 login，发账号 =====
        public void BeginLogin(string user, string password) {
            if (CurrentStage != Stage.Idle && CurrentStage != Stage.Disconnected) {
                Debug.LogWarning($"[Net] BeginLogin called in stage {CurrentStage}, ignored");
                return;
            }
            ChangeStage(Stage.LoginConnecting);
            StartCoroutine(WatchdogTimeout(Stage.LoginConnecting, 3.5f));
            NetCore.Connect(GameSettings.LOGIN_HOST, GameSettings.LOGIN_PORT,
                () => OnLoginSocketConnected(user, password));
        }

        private void OnLoginSocketConnected(string user, string password) {
            ChangeStage(Stage.LoginRequesting);
            StartCoroutine(WatchdogTimeout(Stage.LoginRequesting,
                GameSettings.NET_RPC_TIMEOUT_SEC));
            // TBD(Task 9): 真实协议字段以用户提供清单为准，下面是 skynet 标准默认占位
            // var req = new SprotoType.login.request {
            //     user = user, password = password, server = "default"
            // };
            // NetSender.Send<Protocol.login>(req, OnLoginResponseReceived);
        }

        private void OnLoginResponseReceived(SprotoTypeBase raw) {
            // TBD(Task 9): 字段名按 skynet 标准应为 {uid, subid, server, secret, error}
            // var rsp = raw as SprotoType.login.response;
            // if (rsp.error != 0) {
            //     Fail(Stage.LoginRequesting, $"login error {rsp.error}");
            //     return;
            // }
            // var (host, port) = ParseServerAddr(rsp.server);
            // SessionInfo.Inst().SaveLoginResponse(rsp.uid, rsp.subid, host, port, rsp.secret);
            BeginSwitchToGame();
        }

        // ===== Phase Switch: 断 login → 清状态 → 连 game =====
        private void BeginSwitchToGame() {
            ChangeStage(Stage.SwitchingToGame);
            NetCore.Disconnect();
            NetCore.ResetState();          // ⭐ 关键 — 不调用=切服污染，参考设计书 §2.3
            ChangeStage(Stage.GameConnecting);
            StartCoroutine(WatchdogTimeout(Stage.GameConnecting, 3.5f));

            var s = SessionInfo.Inst();
            string host = !string.IsNullOrEmpty(s.GameHost) ? s.GameHost : GameSettings.GAME_HOST;
            int    port = s.GamePort > 0                    ? s.GamePort : GameSettings.GAME_PORT;
            NetCore.Connect(host, port, OnGameSocketConnected);
        }

        // ===== Phase 2: 连 game，发 handshake =====
        private void OnGameSocketConnected() {
            ChangeStage(Stage.GameHandshaking);
            StartCoroutine(WatchdogTimeout(Stage.GameHandshaking,
                GameSettings.NET_RPC_TIMEOUT_SEC));
            // TBD(Task 9): handshake.request 待协议确认，应携带 {subid, secret}
            // var req = new SprotoType.handshake.request {
            //     subid = SessionInfo.Inst().SubId,
            //     secret = SessionInfo.Inst().Secret
            // };
            // NetSender.Send<Protocol.handshake>(req, OnGameHandshakeResponseHandler);
        }

        private SprotoTypeBase OnGameHandshakeResponseHandler(SprotoTypeBase raw) {
            // TBD(Task 9): 真实协议字段
            // var rsp = raw as SprotoType.handshake.response;
            // if (!string.IsNullOrEmpty(rsp.msg) && rsp.msg != "ok") {
            //     Fail(Stage.GameHandshaking, $"handshake reject: {rsp.msg}");
            //     return null;
            // }
            ChangeStage(Stage.Online);
            OnOnline?.Invoke();
            return null; // handshake 单向宣告，不回包
        }

        // ===== 心跳 =====
        private void TickHeartbeat() {
            if (CurrentStage != Stage.Online) { _heartbeatTimer = 0; return; }
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer >= GameSettings.NET_HEARTBEAT_INTERVAL_SEC) {
                _heartbeatTimer = 0;
                // TBD(Task 9): heartbeat 协议已存在 (Protocol.heartbeat tag=2)，启用此行
                // NetSender.Send<Protocol.heartbeat>();
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

        private IEnumerator WatchdogTimeout(Stage expected, float seconds) {
            yield return new WaitForSeconds(seconds);
            if (CurrentStage == expected) Fail(expected, $"timeout after {seconds}s");
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

- [ ] **Step 4.2：切到 Unity Editor，等 auto-recompile**

Expected：Console 无 error。
失败排错：
- 如果报 `'Sproto' could not be found`：confirm `Assets/Sproto/sproto-Unity/` 目录实际存在且含 NetCore.cs（如不存在按 [.gitignore](../../../.gitignore) 注释里的 URL 重新 clone）。
- 如果报 `'NetCore' does not contain a definition for 'ResetState'`：Task 3 没完成。

- [ ] **Step 4.3：commit**

```bash
git add Assets/Manager/NetworkManager.cs Assets/Manager/NetworkManager.cs.meta
git commit -m "feat(net): 新增 NetworkManager 状态机骨架(8阶段+超时协程+事件)

- BeginLogin 入口 + Phase1/Switch/Phase2 编排
- WatchdogTimeout 协程: connect 3.5s, RPC 10s
- 心跳计时器: Online 阶段每 5s
- DetectOnlineDisconnect: 每帧检查 Online 阶段 socket 是否断
- OnApplicationPause: 切后台恢复后兜底检查 socket
- 事件: OnOnline/OnFailed/OnStageChanged
- 所有发包调用先用 TBD 注释占位,等 Task 9 对齐真实 sproto login 协议
  字段后解开,目前状态机能跑 connect-timeout-Disconnected 分支
- 依赖 Task 1 GameSettings 常量、Task 2 SessionInfo、Task 3 NetCore.ResetState

参考设计书 §6.4、§7.1、§7.2。"
```

---

## Task 5：新增 NetworkTestUI 调试入口

**Files:**
- Create: `Assets/Scripts/Debug/NetworkTestUI.cs`

**目的：** 在 Editor / Development Build 里提供一个 IMGUI 面板，能输入账号密码、按按钮触发 BeginLogin，观察状态变化。生产 build 自动剥离。

- [ ] **Step 5.1：创建文件 Assets/Scripts/Debug/NetworkTestUI.cs**

完整内容：

```csharp
using Manager;
using UnityEngine;

namespace Scripts.Debug {
    /// <summary>
    /// IMGUI 调试面板,按 F12 切换显隐。
    /// 仅在 Editor / Development Build 编译进来(见 GameStart)。
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

- [ ] **Step 5.2：切到 Unity Editor，等 auto-recompile**

Expected：Console 无 error。

- [ ] **Step 5.3：commit**

```bash
git add Assets/Scripts/Debug/NetworkTestUI.cs Assets/Scripts/Debug/NetworkTestUI.cs.meta
# 如果 Debug 是新目录还会有 Assets/Scripts/Debug.meta
git add Assets/Scripts/Debug.meta 2>/dev/null || true
git commit -m "feat(debug): 新增 NetworkTestUI 调试入口

IMGUI 面板按 F12 切换;订阅 NetworkManager 三个事件,
显示当前阶段 + 时间戳 log。仅 Editor/DEVELOPMENT_BUILD 编译。

参考设计书 §6.6。"
```

---

## Task 6：改 GameStart.cs 预热 NetworkManager + 注入 NetworkTestUI

**Files:**
- Modify: `Assets/Scripts/GameStart.cs`（在 `Awake` 中追加 2 处）

- [ ] **Step 6.1：修改 GameStart.cs**

打开 [Assets/Scripts/GameStart.cs](../../../Assets/Scripts/GameStart.cs)。完整替换文件内容为：

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
            NetworkManager.Inst();                              // ⭐ 新增:预热(不连任何服)
            UIManager.Inst().Show("LoginUI");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            new GameObject("NetworkTestUI")
                .AddComponent<Scripts.Debug.NetworkTestUI>();    // ⭐ 新增:调试入口
#endif
            Destroy(gameObject);
        }
    }
}
```

注意：`Scripts.Debug.NetworkTestUI` 必须用全限定名（避免与 `UnityEngine.Debug` 命名冲突）。

- [ ] **Step 6.2：切到 Unity Editor，等 auto-recompile**

Expected：Console 无 error。

- [ ] **Step 6.3：commit**

```bash
git add Assets/Scripts/GameStart.cs
git commit -m "feat(boot): GameStart 预热 NetworkManager + 注入调试 UI

Awake 顺序: GameManager → LuaEnvManager → NetworkManager(仅预热,不连接) → UI Show LoginUI。
然后在 UNITY_EDITOR/DEVELOPMENT_BUILD 条件下额外创建 NetworkTestUI GameObject。
参考设计书 §6.5。"
```

---

## Task 7：编译 + Editor Play 干跑验证状态机（无服务端）

**Files:**
- 仅 Editor 操作，无文件改动

**目的：** 验证 Task 1-6 集成正确，状态机能从 `Idle → LoginConnecting → ...timeout... → Disconnected`。

- [ ] **Step 7.1：切到 Unity Editor，按 Play 进入 Play Mode**

Expected：
- 场景里 Hierarchy 多出 4 个 GameObject（DontDestroyOnLoad）：`GameManager`、`NetworkManager`、`NetworkTestUI`、原有 LoginUI 等
- 屏幕左上角出现 IMGUI 黑框，标题 `[NetworkTestUI]  stage=Idle`
- Console 无 error

- [ ] **Step 7.2：在 IMGUI 面板里点 "BeginLogin (双阶段)" 按钮**

Expected：在 3.5 秒内 Console 看到状态机流转 log：
```
stage → LoginConnecting
✗ failed at LoginConnecting: timeout after 3.5s
stage → Disconnected
```
（因为 127.0.0.1:8001 没有任何 listener，TCP 建链立即被 RST 或 3.5s 后超时）

排错：
- 如果点按钮没反应：检查 Console 是否有 `MissingReferenceException`，可能 NetworkManager 实例创建失败
- 如果 stage 卡在 LoginConnecting 不变：超时协程没启动（检查 Step 4.1 的 `StartCoroutine(WatchdogTimeout(...))` 是否漏了）

- [ ] **Step 7.3：退出 Play Mode，无需 commit（无文件改动）**

但要确保：退出 Play Mode 后 Console 无遗留 error（异步 socket 回调有时延迟报错）。

---

## Task 8：用 nc 模拟 login server 验证 TCP 建链

**Files:**
- 仅手动验证，无文件改动

**目的：** 验证 `NetCore.Connect` 的 socket 回调链路（`OnLoginSocketConnected` 被正确触发，状态机进入 `LoginRequesting`）。

- [ ] **Step 8.1：开 nc 监听 8001 端口**

打开终端（Git Bash 或 WSL）：

```bash
nc -lk 8001
```
（Windows 没有 nc 可用 `ncat`，PowerShell 可装 `choco install ncat`，或用 Python 一行：
`python -c "import socket;s=socket.socket();s.bind(('127.0.0.1',8001));s.listen();print('listening');s.accept();input('press enter to exit')"`）

Expected：终端阻塞等待连接。

- [ ] **Step 8.2：Unity Editor 按 Play，在 IMGUI 点 BeginLogin**

Expected：
- nc 终端瞬间显示「有新连接」
- Unity Console 看到 log：
```
stage → LoginConnecting
stage → LoginRequesting          ← 新增,说明 socket 回调触发了
✗ failed at LoginRequesting: timeout after 10s    ← 10s 后才超时
```
- 然后状态机 → Disconnected

排错：
- 如果 nc 没反应：说明 NetCore.Connect 根本没发出 TCP SYN，检查 GameSettings.LOGIN_HOST/PORT
- 如果 stage 不进 LoginRequesting：说明 `OnLoginSocketConnected` 回调没被触发，可能是 `socketConnected` 委托被 NetCore 在超时分支丢弃了 —— 这是 NetCore 预存在 bug（spec §8.2 #2），需要 SocketException 兜底

- [ ] **Step 8.3：关闭 nc，退出 Play Mode**

无需 commit。

- [ ] **Step 8.4：切服路径冒烟验证（验证 NetCore.ResetState 不污染下一服）**

这一步对应 spec §7.3 Step 4，专门验证 §2.3 列出的 4 个污染点都被 ResetState 规避。

临时在 NetworkTestUI 加一个调试按钮（**验证完之后从代码里删掉，本步不 commit**）。打开 [Assets/Scripts/Debug/NetworkTestUI.cs](../../../Assets/Scripts/Debug/NetworkTestUI.cs)，在 `if (GUILayout.Button("BeginLogin (双阶段)"))` 那行之后插入：

```csharp
            if (GUILayout.Button("[TMP] Force Switch login→game")) {
                Sproto.NetCore.Disconnect();
                Sproto.NetCore.ResetState();
                Sproto.NetCore.Connect(
                    Settings.GameSettings.GAME_HOST,
                    Settings.GameSettings.GAME_PORT,
                    () => Log("[force] game socket up"));
            }
```

（如果 `Sproto.NetCore` 命名空间不存在，是因为 NetCore 在全局命名空间，直接写 `NetCore.Disconnect()` 即可，并 `using` 引入 `Settings` 命名空间。）

然后开两个终端：
```bash
# 终端 1
nc -lk 8001
# 终端 2
nc -lk 8888
```

Editor Play，IMGUI 操作流程：
1. 点 `BeginLogin (双阶段)` → 终端 1 (nc 8001) 收到新连接
2. 点 `[TMP] Force Switch login→game` → 终端 1 连接被关闭、终端 2 (nc 8888) 收到新连接、Console 看到 `[force] game socket up`

Expected：
- ✓ 两端都能正常建链/断链，无 SocketException / IndexOutOfRange / IOException 等异常
- ✓ Console 没出现 NetCore 内部的 "data.Length > 65535" 或解包错位日志（如果 ResetState 没生效就会有）

验证完毕后**删除临时按钮代码**（不进 commit），还原 NetworkTestUI.cs。

- [ ] **Step 8.5：退出 Play Mode，无需 commit**

---

## Task 9：⛔ 阻塞 — 等待用户提供真实 sproto login 协议字段后接通真实双服

**Files:**
- Modify: `Assets/Sproto/protocol/game.sproto`
- Regenerate: `Assets/Sproto/protocol/gen_cs/gamesproto.cs`
- Modify: `Assets/Manager/NetworkManager.cs`（解 TBD 注释）

**⛔ 阻塞条件：** 用户必须先提供 4 套字段清单：
1. `login.request` 字段（client → login-server）
2. `login.response` 字段（login-server → client）
3. `handshake.request` 字段（client → game-server）
4. `handshake.response` 字段（game-server → client，是否扩展现有 `msg:string` ?）

执行者拿到字段清单后再继续以下步骤。

- [ ] **Step 9.1：扩展 game.sproto 加 login 协议段**

打开 [Assets/Sproto/protocol/game.sproto](../../../Assets/Sproto/protocol/game.sproto)，在末尾追加（**字段名/类型/tag 号以用户提供清单为准；以下是 skynet 标准占位示例**）：

```text
login 3 {
    request {
        server   0 : string
        user     1 : string
        password 2 : string
    }
    response {
        uid    0 : integer
        subid  1 : integer
        server 2 : string
        secret 3 : string
        error  4 : integer
    }
}
```

同时把现有 `handshake 1` 段扩展加 request 字段：

```text
handshake 1 {
    request {
        subid  0 : integer
        secret 1 : string
    }
    response {
        msg 0 : string
    }
}
```

- [ ] **Step 9.2：跑 gen_cs.bat 重新生成 gamesproto.cs**

需要本机已装 lua 解释器（sprotodump 是 lua 脚本）。在 Windows cmd 或 PowerShell：

```cmd
cd "i:\Sofware\Unity\MukPun\ArknightsLearn\Assets\Sproto\protocol"
gen_cs.bat
```

Expected：终端输出 `sproto to cs, done`；`Assets/Sproto/protocol/gen_cs/gamesproto.cs` 被覆盖；diff 应能看到新增的 `SprotoType.login`、`SprotoType.login.request`、`SprotoType.login.response`、`Protocol.login`，以及 `SprotoType.handshake.request`。

如果 `gen_cs.bat` 报 `lua: command not found`：装 Lua（https://www.lua.org/）或直接手动跑 `lua ../sprotodump/sprotodump.lua -cs game.sproto -o gen_cs/gamesproto.cs`。

- [ ] **Step 9.3：解开 NetworkManager 里所有 TBD 注释**

打开 [Assets/Manager/NetworkManager.cs](../../../Assets/Manager/NetworkManager.cs)，把所有 `// TBD(Task 9):` 标记的注释行变成实际代码。共 6 处需要解开：

**A. `Initialization()` 里的 handler 注册**：
```csharp
NetReceiver.AddHandler<Protocol.handshake>(OnGameHandshakeResponseHandler);
```
**B. `OnLoginSocketConnected` 里发 login 请求**（字段名以你的 schema 为准）：
```csharp
var req = new SprotoType.login.request {
    user = user, password = password, server = "default"
};
NetSender.Send<Protocol.login>(req, OnLoginResponseReceived);
```
**C. `OnLoginResponseReceived` 里解析响应**：
```csharp
var rsp = raw as SprotoType.login.response;
if (rsp.error != 0) {
    Fail(Stage.LoginRequesting, $"login error {rsp.error}");
    return;
}
var (host, port) = ParseServerAddr(rsp.server);
SessionInfo.Inst().SaveLoginResponse(rsp.uid, rsp.subid, host, port, rsp.secret);
```
**D. `OnGameSocketConnected` 里发 handshake**：
```csharp
var req = new SprotoType.handshake.request {
    subid = SessionInfo.Inst().SubId,
    secret = SessionInfo.Inst().Secret
};
NetSender.Send<Protocol.handshake>(req, OnGameHandshakeResponseHandler);
```
**E. `OnGameHandshakeResponseHandler` 里校验响应**：
```csharp
var rsp = raw as SprotoType.handshake.response;
if (!string.IsNullOrEmpty(rsp.msg) && rsp.msg != "ok") {
    Fail(Stage.GameHandshaking, $"handshake reject: {rsp.msg}");
    return null;
}
```
**F. `TickHeartbeat` 里启用心跳发送**：
```csharp
NetSender.Send<Protocol.heartbeat>();
```

- [ ] **Step 9.4：切到 Unity Editor，等 auto-recompile**

Expected：Console 无 error。
失败排错：
- `'SprotoType' does not contain a definition for 'login'`：Step 9.2 的 sprotodump 没跑成功，重新跑
- 字段名报错：你 schema 里写的字段名跟代码引用不一致，回去对齐

- [ ] **Step 9.5：commit**

```bash
git add Assets/Sproto/protocol/game.sproto Assets/Sproto/protocol/gen_cs/gamesproto.cs Assets/Manager/NetworkManager.cs
git commit -m "feat(net): 接通真实 sproto login 协议,解开 NetworkManager TBD

- game.sproto: 追加 login (tag=3) 协议段 + 扩展 handshake.request
- gamesproto.cs: 由 sprotodump 重新生成
- NetworkManager.cs: 解开 6 处 TBD 注释(Init/2阶段发包/2阶段收包/心跳)

字段定义来自[用户提供的服务端协议规范],对齐 skynet login-server / agent watchdog 标准流程。"
```

---

## Task 10：接通真实 skynet 服端做 e2e 验证

**Files:**
- 仅手动验证，无文件改动

**目的：** 验证完整双阶段流程能跑到 `★ ONLINE ★`。

**前置：** 你的本地或测试环境已启动 skynet login-server 和 game-server 集群，且 GameSettings.LOGIN_HOST/PORT 指向真实地址。

- [ ] **Step 10.1：确认服务端在跑**

```bash
# 任选一种检查
netstat -an | grep 8001    # Linux/macOS
netstat -an | findstr 8001  # Windows
```
Expected：看到 `LISTENING` 在 8001 端口。

- [ ] **Step 10.2：Unity Editor Play，IMGUI 点 BeginLogin**

Expected log 流（一气呵成约 1-2 秒内）：
```
stage → LoginConnecting
stage → LoginRequesting
stage → SwitchingToGame
stage → GameConnecting
stage → GameHandshaking
stage → Online
★ ONLINE ★
```

5 秒后看到第一次心跳被发出（如果服端有日志确认）。

- [ ] **Step 10.3：失败时按 spec §7.1 错误矩阵排查**

常见 4 种失败模式：

| Console log | 原因 | 修复 |
|---|---|---|
| `✗ failed at LoginConnecting: timeout` | login-server 没 listen 或端口错 | 检查 GameSettings.LOGIN_HOST/PORT 与服端实际监听 |
| `✗ failed at LoginRequesting: login error 1` | 账号密码错 | 在 IMGUI 改账号；服端 user db 检查 |
| `✗ failed at GameConnecting: timeout` | login 响应里的 server 字段是内网地址或 game-server 没起 | 检查服端 cluster 配置、`Parse­ServerAddr` 解析结果 |
| `✗ failed at GameHandshaking: handshake reject: ...` | secret 校验不过 / agent 没收到 login server 的内部消息 | skynet 服端排查（spec §2.5 step 4、step 10） |

- [ ] **Step 10.4：成功后退出 Play Mode，本任务完成（无 commit）**

---

## 总结：完成所有 Task 后的最终状态

- Master 分支新增 8 个 commit（Task 1/2/3/4/5/6/9，Task 7/8/10 是验证不 commit）
- 主仓库版本管理范围内的新文件：`SessionInfo.cs`、`NetworkManager.cs`、`NetworkTestUI.cs`、`GameSettings.cs` 修改、`GameStart.cs` 修改、`game.sproto` 修改、`gamesproto.cs` 重生成、`netcore-reset-state.cs.patch` 归档
- 主仓库**外**的本地修改：`NetCore.cs` 末尾追加 `ResetState()` 方法（已用 patch 归档防丢失）
- 启动流程：`SampleScene → GameStart.Awake → NetworkManager.Inst() 预热 → Show LoginUI + 创建 NetworkTestUI`
- 用户在 NetworkTestUI 点登录：双阶段完成后看到 `★ ONLINE ★`，心跳每 5 秒一次

后续 future work（不在本 plan 范围）：
- LoginUI 接入网络（替代 NetworkTestUI）
- 断线重连
- Lua 桥接给 HomeUI / CharUI 用
- 修 sproto-Unity 预存在的 3 个 debt（recvQueue 线程安全、Connect 异常未 catch、CONNECT_TIMEOUT 硬编码）
