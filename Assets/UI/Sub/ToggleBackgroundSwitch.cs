using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    public class ToggleBackgroundSwitch : MonoBehaviour {
        [Header("背景引用")]
        public GameObject bkOff;
        public GameObject bkOn;

        [Header("颜色目标")]
        public GameObject tex;
        public GameObject icon;

        [Header("面板控制")]
        public GameObject panelGame;

        [Header("激活状态颜色")]
        public Color activeColor = Color.white;

        private Toggle _toggle;
        private bool _targetState;
        private bool _needsUpdate;

        private void Start() {
            _toggle = GetComponent<Toggle>();
            _toggle.graphic = null;
            _targetState = _toggle.isOn;
            _toggle.onValueChanged.AddListener(OnToggleValueChanged);
            // 延迟两帧再应用状态，确保 Canvas 重建完成
            StartCoroutine(ApplyStateDelayed());
        }

        private System.Collections.IEnumerator ApplyStateDelayed() {
            yield return null;
            yield return null;
            ApplyState(_targetState);
        }

        private void OnToggleValueChanged(bool isOn) {
            _targetState = isOn;
            _needsUpdate = true;
        }

        private void LateUpdate() {
            if (_needsUpdate) {
                _needsUpdate = false;
                ApplyState(_targetState);
            }
        }

        private void ApplyState(bool isOn) {
            SetBkActive(isOn);
            SetColors(isOn);
            SetPanelActive(isOn);
        }

        private void SetBkActive(bool isOn) {
            if (bkOff != null) bkOff.SetActive(!isOn);
            if (bkOn != null) bkOn.SetActive(isOn);
        }

        private void SetColors(bool isOn) {
            Color targetColor = isOn ? activeColor : Color.white;
            if (tex != null) {
                var graphic = tex.GetComponent<Graphic>();
                if (graphic != null) graphic.color = targetColor;
            }
            if (icon != null) {
                var graphic = icon.GetComponent<Graphic>();
                if (graphic != null) graphic.color = targetColor;
            }
        }

        private void SetPanelActive(bool isOn) {
            if (panelGame != null) {
                panelGame.SetActive(isOn);
            }
        }

        private void OnDestroy() {
            if (_toggle != null) {
                _toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            }
        }
    }
}