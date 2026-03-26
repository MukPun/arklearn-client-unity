using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace UI {
    public class ToggleController : Toggle {

        /// <summary>
        /// 初始化开关状态
        /// </summary>
        public void Initialization(bool value) {
            isOn = value;
        }

        /// <summary>
        /// 获取当前开关状态
        /// </summary>
        public bool GetValue() {
            return isOn;
        }
    }
}
