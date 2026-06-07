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
            // Defensive: NetCore connect 回调极少情况下可能被触发两次（library quirk / 网络异常），
            // 用 stage guard 保证 idempotent，避免重复发包 + 启动重复 watchdog
            if (CurrentStage != Stage.LoginConnecting) return;
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
            // Defensive: 同 OnLoginSocketConnected,防双回调
            if (CurrentStage != Stage.GameConnecting) return;
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
