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
            // 与 NetworkManager (DontDestroyOnLoad) 对齐生命周期,
            // 否则场景切换会导致 NetTestUI 死亡,事件订阅变悬空引用
            var debugUiGo = new GameObject("NetworkTestUI");
            debugUiGo.AddComponent<Scripts.Debug.NetworkTestUI>();    // ⭐ 新增:调试入口
            DontDestroyOnLoad(debugUiGo);
#endif
            Destroy(gameObject);
        }
    }
}