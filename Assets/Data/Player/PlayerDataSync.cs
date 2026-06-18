using System;
using System.Collections;
using Sproto;
using SprotoType;
using UnityEngine;

namespace Data.Player {
    /// <summary>
    /// 把 skynet 服务端的玩家数据同步到本地 PlayerData。
    ///
    /// 触发时机:NetworkManager.OnGameHandshakeResponseHandler 解析出 uid + dataVersion 后
    /// 调用 PlayerDataSync.FetchAndApply(uid, serverVersion)。
    ///
    /// 流程:
    ///   1. 拿当前 PlayerManager.Inst().Get() 的本地 PlayerData(可能为 null —— 资源未加载 / 首次启动)
    ///   2. 读 LocalCache 版本号
    ///   3. 三态决策:
    ///        a) 本地缓存命中且 version == serverVersion → ApplySnapshot,直接触发 PlayerReady
    ///        b) 本地缓存命中但 version 不一致 → 跳过本地,发起 getPlayerData RPC
    ///        c) 本地无缓存 → 发起 getPlayerData RPC
    ///   4. RPC 成功后 ApplyServerData + LocalCache.Write + 触发 PlayerReady
    ///   5. RPC 失败 → 触发 PlayerReadyFailed(订阅者决定是否回退到本地快照)
    ///
    /// 注意:本类是非 MonoBehaviour 静态工具。协程由调用方(NetworkManager)驱动。
    /// </summary>
    public static class PlayerDataSync {
        /// <summary>完全同步完成(数据已可用),参数是 server 返回的 version。</summary>
        public static event Action<int> OnPlayerReady;
        /// <summary>同步失败,参数是错误原因。订阅者可决定回退到本地快照或报错 UI。</summary>
        public static event Action<string> OnPlayerReadyFailed;

        /// <summary>
        /// 清空所有 OnPlayerReady / OnPlayerReadyFailed 订阅。
        /// 只能在事件声明者内部把 event 字段置 null;外部调用此方法达到同样效果。
        /// NetworkManager 在每次 BeginLogin 重新挂订阅前调用,避免多次登录时 handler 堆叠。
        /// </summary>
        public static void Reset() {
            OnPlayerReady = null;
            OnPlayerReadyFailed = null;
        }

        /// <summary>
        /// 同步入口。返回 IEnumerator 给 NetworkManager 协程驱动。
        /// 失败时**不抛异常**,只触发 OnPlayerReadyFailed —— 让上层决定 UI 行为。
        /// </summary>
        public static IEnumerator FetchAndApply(long uid, int serverVersion) {
            PlayerData local = PlayerManager.Inst().Get();

            // 1. 本地无 PlayerData:这种场景是 PlayerManager 没加载资产,跳过 cache,直接拉
            string localKey = local?.GetName();
            int localVersion = !string.IsNullOrEmpty(localKey)
                ? LocalCache.ReadVersion(localKey)
                : -1;

            if (local != null && localVersion == serverVersion && serverVersion > 0) {
                // 2a. 本地缓存命中 + 版本一致:跳过 RPC
                var snap = LocalCache.ReadSnapshot(localKey);
                if (snap != null) {
                    local.ApplySnapshot(snap);
                    Debug.Log($"[Sync] 本地缓存 v{serverVersion} 命中,跳过 RPC");
                    OnPlayerReady?.Invoke(serverVersion);
                    yield break;
                }
                // snapshot 损坏,fallback 到 RPC
                Debug.LogWarning($"[Sync] 本地版本匹配但 snapshot 损坏,fallback RPC");
            }

            // 2b/2c. 拉全量
            bool done = false;
            string failReason = null;
            int respVersion = 0;

            NetSender.Send<Protocol.getPlayerData>(
                new SprotoType.getPlayerData.request {
                    uid = uid,
                    version = localVersion
                },
                rsp => {
                    var r = rsp as SprotoType.getPlayerData.response;
                    if (r == null || !r.HasResult || r.result != 1) {
                        failReason = $"server reject: result={r?.result}";
                    } else {
                        respVersion = (int)r.version;
                        try {
                            // PlayerManager 当前必须有 PlayerData(资源预填)。
                            // 没有的话临时 new 一个 —— 服务端覆盖完后用户可以登录进游戏
                            PlayerData target = local
                                ?? UnityEngine.ScriptableObject.CreateInstance<PlayerData>();
                            target.ApplyServerData(r);
                            PlayerManager.Inst().SetActive(target);  // Step 1 已暴露
                            LocalCache.Write(target.GetName() ?? $"uid_{uid}",
                                (int)r.version, target.ToSnapshot());
                        } catch (Exception e) {
                            failReason = $"apply failed: {e.Message}";
                        }
                    }
                    done = true;
                });

            // 协程等待 RPC 完成
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!done) {
                if (Time.realtimeSinceStartup > deadline) {
                    failReason = "RPC timeout 5s";
                    break;
                }
                yield return null;
            }

            if (failReason != null) {
                Debug.LogError($"[Sync] {failReason}");
                OnPlayerReadyFailed?.Invoke(failReason);
            } else {
                Debug.Log($"[Sync] server v{respVersion} applied");
                OnPlayerReady?.Invoke(respVersion);
            }
        }
    }
}
