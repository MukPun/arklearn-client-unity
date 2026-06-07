using Tools;

namespace Settings {
    public class GameSettings {
        public const string CHAR_META_PATH = @"Meta/Char";
        
        public const string MONSTER_META_PATH = @"Meta/Monster";
        
        public const string DUNGEON_META_PATH = @"Meta/Dungeon";

        public const string ITEM_META_PATH = @"Meta/Item";

        public const string UI_PREFAB_PATH = @"Prefab/UI";

        public static bool GAME_KEEP_SPEED = true;
        
        public static bool GAME_PERFORMANCE = false;
        
        public static int GAME_PROFILED_SCREEN = 0;
        
        public static bool SOUND_SOUND_EFFECT_ENABLE = true;
        
        public static int SOUND_SOUND_EFFECT_VALUE = 100;
        
        public static bool SOUND_MUSIC_ENABLE = true;
        
        public static int SOUND_MUSIC_VALUE = 100;
        
        public static bool SOUND_VOICE_ENABLE = true;
        
        public static int SOUND_VOICE_VALUE = 100;

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