using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Data.Player;
using Settings;
using Sproto;
using SprotoType;
using Tools;
using UnityEngine;
using XLua;

namespace Manager {
    /// <summary>
    /// skynet 双服客户端编排：login 加密握手 → game sproto 握手 → 业务 RPC。
    ///
    /// 阶段划分（14 阶段）：
    ///   Phase 1 — 走 skynet 自定义文本加密协议（独立 TcpClient，与 NetCore 解耦）：
    ///     Idle → LoginTcpConnecting → LoginWaitChallenge → LoginDHExchange
    ///     → LoginWaitServerPub → LoginVerifyHmac → LoginSendToken
    ///     → LoginParseResponse → SwitchingToGame → LoginGameHandshaking
    ///   Phase 2 — 走 sproto（用 NetCore 静态层）：
    ///     LoginGameHandshaking → GameConnecting → GameHandshaking → Online
    ///   终态：Disconnected（等待 BeginLogin 重置）。
    ///
    /// 静态层 NetCore 由本类驱动（Update 调 Dispatch），状态机持有阶段。
    /// 依赖：NetCore.cs 末尾的 ResetState() 方法（切换服务器时清理 recvQueue / sessionDict）。
    /// </summary>
    public class NetworkManager : MonoSingle<NetworkManager> {
        public enum Stage {
            // 终态
            Idle,
            Disconnected,

            // Phase 1：skynet 加密握手（独立 TcpClient）
            LoginTcpConnecting,      // TCP 三次握手
            LoginWaitChallenge,      // 等待 server 8 字节 challenge (base64 line)
            LoginDHExchange,         // 生成 clientKey 并发送 dhexchange(clientKey)
            LoginWaitServerPub,      // 等待 server DH 公钥 (base64 line)
            LoginVerifyHmac,         // 计算共享 secret 与 hmac 并发送
            LoginSendToken,          // 发送 DES 加密的 "user@server:pass" token
            LoginParseResponse,      // 读取 "200 <subid>" 行并解析

            // Phase 切换
            SwitchingToGame,         // 关闭 login socket、NetCore.ResetState、准备连 game
            LoginGameHandshaking,    // skynet gateserver 文本握手 (raw 通道, 1 次响应即切 sproto)

            // Phase 2：sproto 握手（用 NetCore）
            GameConnecting,
            GameHandshaking,

            // Phase 2.5:业务数据同步(getPlayerData)
            PlayerSync,                  // 等待 PlayerDataSync.FetchAndApply 完成
            Online,
        }

        public Stage CurrentStage { get; private set; } = Stage.Idle;

        public event Action             OnOnline;
        public event Action<Stage,string> OnFailed;
        public event Action<Stage>      OnStageChanged;

        // ===== Phase 1 资源 =====
        private TcpClient     _loginClient;
        private NetworkStream _loginStream;

        // 缓存 login 阶段产生的中间量，避免反复 alloc
        private byte[] _clientKey;        // 8B 客户端 DH 私钥
        private byte[] _loginChallenge;   // 8B server challenge
        private byte[] _loginSecret;      // 8B 共享 secret

        // 从 login response 缓存的 subid, 给 game 服文本握手用 (不要每次都从 SessionInfo 取)
        private string _loginSubid;

        // BeginLogin 携带的账号信息（用于 Phase 1 token 编码）
        private string _loginUser;
        private string _loginPassword;
        private long _loginUid;       // 角色唯一id
        private string _loginServer = "10001";   // 固定为 10001（暂不开放选服）

        // 心跳计时器
        private float _heartbeatTimer;

        // Watchdog 句柄,阶段推进时主动取消,避免协程空转。
        // Key 是 expected stage 枚举,Value 是 StartCoroutine 返回的 handle。
        private readonly System.Collections.Generic.Dictionary<Stage, Coroutine> _watchdogs
            = new System.Collections.Generic.Dictionary<Stage, Coroutine>();

        // Fail 入口幂等保护,避免 watchdog / 多重回调重复触发 OnFailed 给订阅者
        private bool _failureFired;

        // Lua相关
        private LuaEnv _luaEnv;
        private LuaTable _cryptModule;

        // ===== 生命周期 =====
        protected override void Initialization() {
            NetCore.Init();
            NetSender.Init();
            NetReceiver.Init();
            NetCore.enabled = true;

            // 1. Lua虚拟机初始化
            _luaEnv = new LuaEnv();
            _luaEnv.AddBuildin("crypt", XLua.LuaDLL.Lua.LoadCrypt);
            _luaEnv.AddLoader(XLuaFolderLoader);

            // 2. 加载自定义Lua脚本
            object[] ret = _luaEnv.DoString("return require 'XLua.Lua.crypt_test'");
            _cryptModule = (ret != null && ret.Length > 0) ? ret[0] as LuaTable : null;
            if (_cryptModule == null) {
                Debug.LogError("[Net] Failed to load Lua module 'XLua.Lua.crypt_test'");
            }
        }

        private void Update() {
            NetCore.Dispatch();
            DetectOnlineDisconnect();
            TickHeartbeat();
            _luaEnv?.Tick();
        }

        private T CallLuaFunc<T>(string funcName, params object[] args) where T : class
        {
            LuaFunction func = _cryptModule?.Get<LuaFunction>(funcName);
            if (func == null)
            {
                Debug.LogError($"找不到Lua函数：{funcName}");
                return null;
            }
            try {
                object[] ret;
                if (typeof(T) == typeof(byte[])) {
                    ret = func.Call(args, new Type[] { typeof(byte[]) });
                } else {
                    ret = func.Call(args);
                }
                return (ret != null && ret.Length > 0) ? ret[0] as T : null;
            } finally {
                func.Dispose(); // 及时释放函数引用,防内存泄漏
            }
        }

        private static byte[] XLuaFolderLoader(ref string filepath) {
            // Lua 路径分隔符 '.' 转系统路径分隔符 '/';同时改写 filepath 让 XLua 报错时能打印最终路径
            filepath = filepath.Replace(".", "/") + ".lua";
            string absPath = Path.Combine(Application.dataPath, filepath);
            return !File.Exists(absPath) ? null : Encoding.UTF8.GetBytes(File.ReadAllText(absPath));
        }


        private void OnApplicationQuit() {
            CloseLoginSocket();
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

        // =====================================================================
        // Phase 1：skynet 加密握手（独立 TcpClient）
        // =====================================================================

        /// <summary>
        /// 登录入口。从 Idle/Disconnected 发起一轮完整握手。
        /// 内部缓存 user/password 以便 Phase 1 的 token 编码。
        /// </summary>
        public void BeginLogin(string user, string password) {
            if (CurrentStage != Stage.Idle && CurrentStage != Stage.Disconnected) {
                Debug.LogWarning($"[Net] BeginLogin called in stage {CurrentStage}, ignored");
                return;
            }

            _loginUser     = user;
            _loginPassword = password;

            // C-2 修复: 显式清空 Phase 1 残留状态(前一次失败留下的 socket / key / 缓存)
            // 防御性 — CloseLoginSocket 在 _loginClient 为 null 时是 no-op
            CloseLoginSocket();
            _failureFired = false;

            BeginLoginTcpConnect();
        }

        private void BeginLoginTcpConnect() {
            ChangeStage(Stage.LoginTcpConnecting);
            StartWatchdog(Stage.LoginTcpConnecting, 3.5f);

            // 独立 TcpClient：与 skynet Lua 一致，login 完成后立刻 socket.close，
            // 不污染 NetCore 静态层。
            try {
                _loginClient = new TcpClient();
                _loginClient.NoDelay = true;
                // ConnectAsync 走 TPL Task，但我们在协程里用同步 Connect + yield，
                // 简单且与现有 Unity 协程风格一致。
                // TCP 建链超时由 WatchdogTimeout 协程兜底（3.5s）。
                _loginClient.Connect(GameSettings.LOGIN_HOST, GameSettings.LOGIN_PORT);
            } catch (Exception e) {
                Fail(Stage.LoginTcpConnecting, $"tcp connect failed: {e.Message}");
                return;
            }

            // 同步 Connect 在远端拒绝 / 路由失败时通常 < 1s 内抛 SocketException；
            // 万一没抛（比如链路被静默丢），Watchdog 仍会兜底。
            if (!_loginClient.Connected) {
                Fail(Stage.LoginTcpConnecting, "tcp not connected");
                return;
            }

            LoginTcpConnectCallback();
        }

        private void LoginTcpConnectCallback() {
            // 防御性 stage guard：避免外部 / 库内部异常路径下重复推进
            if (CurrentStage != Stage.LoginTcpConnecting) return;

            try {
                _loginStream = _loginClient.GetStream();
            } catch (Exception e) {
                Fail(Stage.LoginTcpConnecting, $"get stream failed: {e.Message}");
                return;
            }

            ChangeStage(Stage.LoginWaitChallenge);
            StartWatchdog(Stage.LoginWaitChallenge,
                GameSettings.NET_RPC_TIMEOUT_SEC);

            // skynet 流程：server 先发一行 base64(challenge)（8 字节）
            StartCoroutine(CoReadLineAndThen(_loginStream, line => {
                if (CurrentStage != Stage.LoginWaitChallenge) return;
                OnLoginChallenge(line);
            }));
        }

        private void OnLoginChallenge(byte[] line) {
            byte[] challenge;
            try {
                challenge = CallLuaFunc<byte[]>("Base64Decode", line);
            } catch (Exception e) {
                Fail(Stage.LoginWaitChallenge, $"decode challenge failed: {e.Message}");
                return;
            }
            if (challenge == null || challenge.Length != 8) {
                Fail(Stage.LoginWaitChallenge, $"bad challenge length {challenge?.Length ?? 0}");
                return;
            }
            _loginChallenge = challenge;
            // 推进到 LoginDHExchange：本帧内同步生成 key + 发送 clientPub
            ChangeStage(Stage.LoginDHExchange);
            StartWatchdog(Stage.LoginDHExchange,
                GameSettings.NET_RPC_TIMEOUT_SEC);

            _clientKey = CallLuaFunc<byte[]>("RandomKey");
            byte[] clientPub;
            try {
                clientPub = CallLuaFunc<byte[]>("DhExchange", _clientKey);
            } catch (Exception e) {
                Fail(Stage.LoginDHExchange, $"dhexchange failed: {e.Message}");
                return;
            }

            string clientPubLine = CallLuaFunc<string>("Base64Encode", clientPub) + "\n";
            if (!WriteLine(_loginStream, clientPubLine, GameSettings.NET_CONNECT_TIMEOUT_MS)) {
                Fail(Stage.LoginDHExchange, "write client pub failed");
                return;
            }

            // 等待 server DH 公钥
            ChangeStage(Stage.LoginWaitServerPub);
            StartWatchdog(Stage.LoginWaitServerPub,
                GameSettings.NET_RPC_TIMEOUT_SEC);
            StartCoroutine(CoReadLineAndThen(_loginStream, line => {
                if (CurrentStage != Stage.LoginWaitServerPub) return;
                OnLoginServerPub(Encoding.ASCII.GetString(line));
            }));
        }

        private void OnLoginServerPub(string line) {
            byte[] serverPub;
            try {
                serverPub = CallLuaFunc<byte[]>("Base64Decode", line);
            } catch (Exception e) {
                Fail(Stage.LoginWaitServerPub, $"decode server pub failed: {e.Message}");
                return;
            }
            if (serverPub == null || serverPub.Length != 8) {
                Fail(Stage.LoginWaitServerPub, $"bad server pub length {serverPub?.Length ?? 0}");
                return;
            }

            // 算共享 secret
            byte[] secret;
            try {
                secret = CallLuaFunc<byte[]>("DhSecret", serverPub, _clientKey);
                Debug.Log("secret = " + BitConverter.ToString(secret).Replace("-", "").ToLowerInvariant());
            } catch (Exception e) {
                Fail(Stage.LoginWaitServerPub, $"dhsecret failed: {e.Message}");
                return;
            }
            _loginSecret = secret;

            // 进入 hmac 阶段：计算 hmac64(challenge, secret) 并发送
            ChangeStage(Stage.LoginVerifyHmac);
            StartWatchdog(Stage.LoginVerifyHmac,
                GameSettings.NET_RPC_TIMEOUT_SEC);

            byte[] hmac;
            try {
                hmac = CallLuaFunc<byte[]>("Hmac64", _loginChallenge, _loginSecret);
                Debug.Log("hmac = " + BitConverter.ToString(hmac).Replace("-", "").ToLowerInvariant());
            } catch (Exception e) {
                Fail(Stage.LoginVerifyHmac, $"hmac64 failed: {e.Message}");
                return;
            }

            string hmacLine = CallLuaFunc<string>("Base64Encode", hmac) + "\n";
            if (!WriteLine(_loginStream, hmacLine, GameSettings.NET_CONNECT_TIMEOUT_MS)) {
                Fail(Stage.LoginVerifyHmac, "write hmac failed");
                return;
            }

            // 进入 token 阶段：encode_token + DES + base64
            ChangeStage(Stage.LoginSendToken);
            StartWatchdog(Stage.LoginSendToken,
                GameSettings.NET_RPC_TIMEOUT_SEC);

            string etoken;
            try {
                string tokenPlain = EncodeToken(_loginUser, _loginServer, _loginPassword);
                byte[] tokenBytes = CallLuaFunc<byte[]>("DesEncode", _loginSecret,
                    tokenPlain);
                etoken = CallLuaFunc<string>("Base64Encode", tokenBytes);
            } catch (Exception e) {
                Fail(Stage.LoginSendToken, $"encode token failed: {e.Message}");
                return;
            }

            string etokenLine = etoken + "\n";
            if (!WriteLine(_loginStream, etokenLine, GameSettings.NET_CONNECT_TIMEOUT_MS)) {
                Fail(Stage.LoginSendToken, "write etoken failed");
                return;
            }

            // 等待 server 返回 "200 <subid-base64>"
            ChangeStage(Stage.LoginParseResponse);
            StartWatchdog(Stage.LoginParseResponse,
                GameSettings.NET_RPC_TIMEOUT_SEC);
            StartCoroutine(CoReadLineAndThen(_loginStream, line => {
                if (CurrentStage != Stage.LoginParseResponse) return;
                OnLoginParseResponse(Encoding.ASCII.GetString(line));
            }));
        }

        private void OnLoginParseResponse(string line) {
            // skynet 流程：server 返回 "200 <subid-base64>"（8 字节 subid 的 base64）
            // 401/403/500 等错误码（login.lua 内部处理）这里也兼容：非 200 即 Fail。
            if (string.IsNullOrEmpty(line) || line.Length < 5 || line.Substring(0, 3) != "200") {
                Fail(Stage.LoginParseResponse, $"login rejected: '{line}'");
                return;
            }
            string resB64 = line.Substring(4);  // 跳过 "200 " 获取后面的 subid
            string subid;
            long uid;
            try {
                string res = CallLuaFunc<string>("Base64Decode", resB64);
                string[] arr = res.Split(':');
                subid = arr[0];
                uid = long.Parse(arr[1]);
                Debug.Log("subid = " + subid + " uid= " + uid);
            } catch (Exception e) {
                Fail(Stage.LoginParseResponse, $"decode subid failed: {e.Message}");
                return;
            }
            // login 服响应成功：关闭 login socket、缓存 session、转入 game
            // uid 由 game 服 handshake.response 下发(见 game.sproto handshake.uid)
            // login 服目前不下发,先存 0 占位,OnGameHandshakeResponseHandler 里再用真实值覆盖 SessionInfo
            byte[] secret = _loginSecret;
            CloseLoginSocket();

            // 缓存 subid、uid 后续 game 服文本握手要用 (OnGameSocketConnected 里)
            _loginSubid = subid;
            _loginUid = uid;

            SessionInfo.Inst().SaveLoginResponse(
                uid, subid,
                GameSettings.GAME_HOST, GameSettings.GAME_PORT,
                secret);

            // 进入 Phase 2
            BeginSwitchToGame();
        }

        // =====================================================================
        // Phase 切换
        // =====================================================================

        private void BeginSwitchToGame() {
            ChangeStage(Stage.SwitchingToGame);
            NetCore.Disconnect();
            NetCore.ResetState();          // ⭐ 关键 — 不调用=切服污染，参考设计书 §2.3
            // ★ 改动: 推进到 LoginGameHandshaking (skynet 文本握手) 而非 GameConnecting
            ChangeStage(Stage.LoginGameHandshaking);
            StartWatchdog(Stage.LoginGameHandshaking, 3.5f);

            var s = SessionInfo.Inst();
            string host = !string.IsNullOrEmpty(s.GameHost) ? s.GameHost : GameSettings.GAME_HOST;
            int    port = s.GamePort > 0                    ? s.GamePort : GameSettings.GAME_PORT;
            NetCore.Connect(host, port, OnGameSocketConnected);
        }

        // =====================================================================
        // Phase 2：sproto 握手（用 NetCore）
        // =====================================================================

        private void OnGameSocketConnected() {
            if (CurrentStage != Stage.LoginGameHandshaking) return;

            // === 1. 构造 skynet gateserver 文本握手字符串 ===
            //    格式: base64(user)@base64(server)#base64(subid):1
            //    index = 1 (跟官方 Lua 参考一致, skynet 协议默认)
            string index = "1";
            string userB64   = CallLuaFunc<string>("Base64Encode", _loginUid);
            string serverB64 = CallLuaFunc<string>("Base64Encode", _loginServer);
            string subidB64  = CallLuaFunc<string>("Base64Encode", _loginSubid);
            string handshake = $"{userB64}@{serverB64}#{subidB64}:{index}";
            Debug.Log($"[Net] game auth handshake = {handshake}");

            // === 2. hmac = hmac64(hashkey(handshake), secret) ===
            //    跟官方 Lua 参考一致: 走 Lua crypt 模块 (xlua 内置的 luaopen_skynet_crypt)
            byte[] hash = CallLuaFunc<byte[]>("HashKey", handshake);
            byte[] secretBytes = SessionInfo.Inst().Secret;
            byte[] hmac = CallLuaFunc<byte[]>("Hmac64", hash, secretBytes);
            Debug.Log("hmac = " + BitConverter.ToString(hmac).Replace("-", "").ToLowerInvariant());

            // === 3. 拼 final payload: handshake + ":" + base64(hmac) + "\n" ===
            string hmacB64 = CallLuaFunc<string>("Base64Encode", hmac);
            string payload = handshake + ":" + hmacB64;
            byte[] payloadBytes = Encoding.ASCII.GetBytes(payload);

            // === 4. 通过 NetCore raw 通道发出 (不走 sproto pack) ===
            NetCore.SendRaw(payloadBytes);
            // 注册 raw 响应 handler, 收到 200 OK 后切到 sproto 握手
            NetCore.SetRawHandler(OnGameAuthRawResponse);
        }

        /// <summary>
        /// skynet gateserver 文本握手的 raw 响应回调 (NetCore.SetRawHandler 注册).
        /// server 回 "200 OK\n" 表示 HMAC 校验通过, 之后切 sproto.
        /// </summary>
        private void OnGameAuthRawResponse(byte[] data) {
            // 防御性 stage guard
            if (CurrentStage != Stage.LoginGameHandshaking) return;

            string response = Encoding.ASCII.GetString(data).Trim();
            if (!response.StartsWith("200")) {
                Fail(Stage.LoginGameHandshaking, $"game auth rejected: '{response}'");
                return;
            }

            // 200 OK -> 切到现有 sproto 握手阶段
            ChangeStage(Stage.GameHandshaking);
            StartWatchdog(Stage.GameHandshaking, GameSettings.NET_RPC_TIMEOUT_SEC);
            NetSender.Send<Protocol.handshake>(null, OnGameHandshakeResponseHandler);   // 现有 sproto 握手, 不动
            NetSender.Send<Protocol.heartbeat>(null, OnHeartbeatRpcResponse);           // 首次心跳
        }

        private void OnGameHandshakeResponseHandler(SprotoTypeBase raw) {
            if (CurrentStage != Stage.GameHandshaking) return;
            var rsp = raw as SprotoType.handshake.response;
            if (rsp == null || !rsp.HasResult || rsp.result != 1) {
                Fail(Stage.GameHandshaking, $"handshake reject: result={rsp?.result}");
                return;
            }

            // 解析新增字段:uid(写进 SessionInfo)+ dataVersion
            long uid = rsp.HasUid ? rsp.uid : 0;
            int dataVersion = rsp.HasDataVersion ? (int)rsp.dataVersion : 0;
            if (rsp.HasUid) {
                SessionInfo.Inst().Uid = uid;   // Step 3 暴露 setter 后可用
            }
            Debug.Log($"[Net] handshake ok uid={uid} dataVersion={dataVersion}");

            // 切到 PlayerSync 阶段(skynet 协议只回了鉴权结果,业务数据走 getPlayerData)
            ChangeStage(Stage.PlayerSync);
            StartWatchdog(Stage.PlayerSync, GameSettings.NET_RPC_TIMEOUT_SEC);

            // 触发 PlayerDataSync。完成后由 PlayerDataSync.OnPlayerReady → 这里转 OnOnline
            // 静态事件需要清旧订阅(避免 BeginLogin 多次调用时 handler 堆叠)
            PlayerDataSync.Reset();
            PlayerDataSync.OnPlayerReady += v => {
                if (CurrentStage != Stage.PlayerSync) return;
                ChangeStage(Stage.Online);
                OnOnline?.Invoke();
            };
            PlayerDataSync.OnPlayerReadyFailed += reason => {
                // 同步失败:不回退到 Disconnected(否则玩家连登录都进不去),
                // 而是允许"用过期本地数据"进入,UI 给个警告横幅
                Debug.LogWarning($"[Net] PlayerData sync failed: {reason}, fallback to local data");
                if (CurrentStage != Stage.PlayerSync) return;
                ChangeStage(Stage.Online);
                OnOnline?.Invoke();
            };

            StartCoroutine(PlayerDataSync.FetchAndApply(uid, dataVersion));
        }

        // =====================================================================
        // 心跳
        // =====================================================================
        private void TickHeartbeat() {
            if (CurrentStage != Stage.Online) { _heartbeatTimer = 0; return; }
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer >= GameSettings.NET_HEARTBEAT_INTERVAL_SEC) {
                _heartbeatTimer = 0;
                // Protocol.heartbeat tag=2,无 request / response payload
                NetSender.Send<Protocol.heartbeat>(null, OnHeartbeatRpcResponse);
            }
        }
        /// <summary>
        /// heartbeat 的 RPC response 回调。 
        /// 由 NetSender.Send<T>(req, this) 自动注册到 sessionDict，
        /// dispatcher 收到 type=0, session=K 的包时按 session 找到这里。
        /// </summary>
        private void OnHeartbeatRpcResponse(SprotoTypeBase rpcRsp) {
            // 协议层 decode 出来的实例，类型由 NetSender.sessionDict[session].Response 决定
            Debug.Log("[Net] heartbeat response: " + rpcRsp.GetType().Name);
            var rsp = rpcRsp as SprotoType.heartbeat.response;
            if (rsp == null) {
                Debug.LogWarning("[Net] heartbeat response type mismatch");
                return;
            }

            // game.sproto: response { result 1 : integer }
            if (rsp.HasResult && rsp.result != 2) {
                // server 拒接 / 踢人 / 维护
                Fail(Stage.Online, $"heartbeat reject: result={rsp.result}");
            }

            // 正常路径：可以更新 _lastHeartbeatAckTime / 统计成功率 / 等
            // 什么都不做也行
        }

        // =====================================================================
        // 工具
        // =====================================================================

        private void ChangeStage(Stage next) {
            if (CurrentStage == next) return;
            // I-1 修复: 阶段推进时主动取消上一阶段的 watchdog,避免协程空转
            if (_watchdogs.TryGetValue(CurrentStage, out var oldCo)) {
                StopCoroutine(oldCo);
                _watchdogs.Remove(CurrentStage);
            }
            var prev = CurrentStage;
            CurrentStage = next;
            OnStageChanged?.Invoke(next);
            Debug.Log($"[Net] {prev} -> {next}");
        }

        private void Fail(Stage atStage, string reason) {
            // I-2 修复: idempotency guard — 防止 watchdog / 多重回调重入时重复触发 OnFailed
            if (_failureFired) return;
            _failureFired = true;
            Debug.LogWarning($"[Net] Failed at {atStage}: {reason}");
            CloseLoginSocket();
            NetCore.Disconnect();
            NetCore.ResetState();
            SessionInfo.Inst().Clear();
            ChangeStage(Stage.Disconnected);
            OnFailed?.Invoke(atStage, reason);
        }

        private void StartWatchdog(Stage expected, float seconds) {
            // I-1 修复: 把 watchdog handle 注册到字典,ChangeStage 推进时取消
            if (_watchdogs.TryGetValue(expected, out var old)) StopCoroutine(old);
            _watchdogs[expected] = StartCoroutine(WatchdogTimeout(expected, seconds));
        }

        private IEnumerator WatchdogTimeout(Stage expected, float seconds) {
            yield return new WaitForSeconds(seconds);
            if (CurrentStage == expected) Fail(expected, $"timeout after {seconds}s");
            _watchdogs.Remove(expected);
        }

        // ---------- 同步 readline（coroutine 友好） ----------
        // NetworkStream.Read 是阻塞调用,但 Unity 主线程跑在协程里,
        // 此处用 deadline 手动控制超时,避免永久阻塞游戏循环。
        // 超时 / 远端关闭都返回 null,由调用方判空后 Fail。
        private static byte[] LoginReadLine(NetworkStream stream, int timeoutMs) {
            if (stream == null) return null;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var ms = new MemoryStream(64);
            var oneByte = new byte[1];

            try {
                while (true) {
                    int remaining = (int)Math.Max(0,
                        (deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (remaining == 0) return null;

                    // 设置 read timeout,让 Read 不会无限等
                    stream.ReadTimeout = remaining;
                    int n;
                    try {
                        n = stream.Read(oneByte, 0, 1);
                    } catch (IOException) {
                        return null;   // 远端 closed
                    } catch (SocketException) {
                        return null;
                    }
                    if (n == 0) return null;   // 远端 closed
                    if (oneByte[0] == (byte)'\n') break;
                    ms.WriteByte(oneByte[0]);
                    if (ms.Length > 4096) return null;   // 防御:单行过长
                }
            } catch (Exception) {
                return null;
            }

            var raw = ms.ToArray();
            // 去掉末尾的 \r（skynet 标准是 \n，但也容错 \r\n）
            int end = raw.Length;
            if (end > 0 && raw[end - 1] == (byte)'\r') end--;
            if (end == 0) return new byte[0];
            var line = new byte[end];
            Buffer.BlockCopy(raw, 0, line, 0, end);
            return line;
        }

        private IEnumerator CoReadLineAndThen(NetworkStream stream, Action<byte[]> onLine) {
            byte[] line = LoginReadLine(stream, (int)(GameSettings.NET_RPC_TIMEOUT_SEC * 1000));
            // 注意：协程里调用方依然要再判一次 stage guard,避免阶段已变
            onLine?.Invoke(line);
            yield break;
        }

        // 同步 WriteLine：包内不再开协程,失败立即返回 false,由调用方 Fail
        private static bool WriteLine(NetworkStream stream, string lineWithNewline, int timeoutMs) {
            if (stream == null) return false;
            try {
                var bytes = Encoding.UTF8.GetBytes(lineWithNewline);
                stream.WriteTimeout = timeoutMs;
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                return true;
            } catch (Exception) {
                return false;
            }
        }

        // ---------- token 编码 ----------
        public string EncodeToken(string user, string server, string pass) {
            // Base64Encode 返回 ASCII base64 字符串, 走 <string> 拿 string 即可.
            // ⚠ 不要用 <byte[]>, byte[].ToString() = "System.Byte[]" 会污染 token.
            return CallLuaFunc<string>("Base64Encode", user) + "@" +
                   CallLuaFunc<string>("Base64Encode", server) + ":" +
                   CallLuaFunc<string>("Base64Encode", pass);
        }

        // ---------- login socket 生命周期 ----------
        private void CloseLoginSocket() {
            try { _loginStream?.Close(); } catch (Exception) { }
            try { _loginClient?.Close(); } catch (Exception) { }
            _loginStream = null;
            _loginClient = null;
            _clientKey = null;
            _loginChallenge = null;
            _loginSecret = null;
        }
        void OnDestroy()
        {
            // 资源释放，顺序不能乱
            if (_cryptModule != null)
            {
                _cryptModule.Dispose();
                _cryptModule = null;
            }
            if (_luaEnv != null)
            {
                _luaEnv.Dispose();
                _luaEnv = null;
            }
        }
    }
}
