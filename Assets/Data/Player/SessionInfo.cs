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
