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
