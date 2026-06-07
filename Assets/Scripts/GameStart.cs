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