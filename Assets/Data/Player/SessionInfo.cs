using Tools;

namespace Data.Player {
    /// <summary>
    /// 网络会话身份：login 响应下发，game handshake 时携带。
    /// 与本地角色数据 PlayerData 解耦 —— PlayerManager 管「本地存档」，
    /// SessionInfo 管「网络会话凭证」。
    /// </summary>
    public class SessionInfo : Single<SessionInfo> {
        public long   Uid       { get; set; }
        public string   SubId     { get; private set; }
        public byte[] Secret    { get; private set; }
        public string GameHost  { get; private set; }
        public int    GamePort  { get; private set; }

        // Secret 作为「已登录」的判定信号；其他字段是辅助载荷
        public bool   HasSession => Secret.Length > 0;

        public void SaveLoginResponse(long uid, string subid,
                                       string gameHost, int gamePort,
                                       byte[] secret) {
            Uid = uid; SubId = subid;
            GameHost = gameHost; GamePort = gamePort;
            Secret = secret;
        }

        public void Clear() {
            Uid = 0; SubId = null;
            Secret = null;
            GameHost = null; GamePort = 0;
        }
    }
}
