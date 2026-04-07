using UnityEngine;

namespace UI.Sub {
    public class HomeUIHelper : MonoBehaviour {
        private void Start() {
            Transform setting = transform.Find("setting");
            setting?.GetComponent<UnityEngine.UI.Button>()?.onClick.AddListener(ShowSettingUI);
        }

        public void ShowSettingUI() {
            UIManager.Inst().Show("SettingUI");
        }
    }
}