using System;
using System.IO;
using UnityEngine;

namespace Data.Player {
    /// <summary>
    /// JSON 文件存档 —— 玩家数据版本号 + 全量快照。
    ///
    /// 路径: Application.persistentDataPath/userdata/{playerName}.json
    /// 路径中 playerName 必须用 base64 防特殊字符(用户名可能含中文 / @ / : 等)。
    ///
    /// 文件格式:
    /// {
    ///   "version": 12,
    ///   "snapshot": { level, exp, reason, charList[], squad[], desktopChar, items[], permissions[] }
    /// }
    ///
    /// 设计原则:
    /// - JSON 而不是 ScriptableObject:ScriptableObject 不适合做网络数据 sink(见设计讨论)
    /// - 单文件每账号:避免单文件全账号,登录时 IO 范围可控
    /// - 写失败不抛异常:存档是辅助,不能因为写盘失败让游戏崩溃
    /// </summary>
    public static class LocalCache {
        [Serializable]
        public class Snapshot {
            public int version;
            public int level;
            public int exp;
            public int reason;
            public string desktopChar;
            public CharSnap[] charList;
            public string[] squad;
            public ItemSnap[] items;
            public string[] permissions;
        }

        [Serializable]
        public class CharSnap {
            public string id;
            public int elite;
            public int level;
            public int exp;
            public int trust;
        }

        [Serializable]
        public class ItemSnap {
            public int id;
            public int amount;
        }

        [Serializable]
        private class Envelope {
            public int version;
            public string snapshot;   // Snapshot 再做一次 base64-编码防 JsonUtility 转义坑
        }

        private static string CacheDir =>
            Path.Combine(Application.persistentDataPath, "userdata");

        private static string EncodeName(string name) {
            // 用户名安全编码 —— 任何输入都变合法文件名
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name))
                .Replace('/', '_').Replace('+', '-');
        }

        private static string PathFor(string playerName) {
            return Path.Combine(CacheDir, EncodeName(playerName) + ".json");
        }

        /// <summary>读版本号,文件不存在或损坏返回 -1。</summary>
        public static int ReadVersion(string playerName) {
            try {
                if (!File.Exists(PathFor(playerName))) return -1;
                var env = JsonUtility.FromJson<Envelope>(File.ReadAllText(PathFor(playerName)));
                return env?.version ?? -1;
            } catch (Exception e) {
                Debug.LogWarning($"[LocalCache] ReadVersion failed for {playerName}: {e.Message}");
                return -1;
            }
        }

        /// <summary>读快照,文件不存在/损坏返回 null。</summary>
        public static Snapshot ReadSnapshot(string playerName) {
            try {
                if (!File.Exists(PathFor(playerName))) return null;
                var env = JsonUtility.FromJson<Envelope>(File.ReadAllText(PathFor(playerName)));
                if (env == null || string.IsNullOrEmpty(env.snapshot)) return null;
                byte[] bytes = Convert.FromBase64String(env.snapshot);
                return JsonUtility.FromJson<Snapshot>(System.Text.Encoding.UTF8.GetString(bytes));
            } catch (Exception e) {
                Debug.LogWarning($"[LocalCache] ReadSnapshot failed for {playerName}: {e.Message}");
                return null;
            }
        }

        /// <summary>原子写:先写 .tmp,再 File.Replace。</summary>
        public static void Write(string playerName, int version, Snapshot snap) {
            try {
                Directory.CreateDirectory(CacheDir);
                string path = PathFor(playerName);
                string snapJson = JsonUtility.ToJson(snap);
                var env = new Envelope {
                    version = version,
                    snapshot = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(snapJson))
                };
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(env));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            } catch (Exception e) {
                Debug.LogWarning($"[LocalCache] Write failed for {playerName}: {e.Message}");
            }
        }

        /// <summary>删除某玩家存档(用于登出/切号)。</summary>
        public static void Delete(string playerName) {
            try {
                string path = PathFor(playerName);
                if (File.Exists(path)) File.Delete(path);
            } catch (Exception e) {
                Debug.LogWarning($"[LocalCache] Delete failed for {playerName}: {e.Message}");
            }
        }
    }
}
