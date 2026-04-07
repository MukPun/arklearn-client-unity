using UnityEngine;
using UnityEngine.InputSystem;

namespace Scripts.Game {
    public static class InputHelper {
        public static Vector3 GetMousePosition() {
            return Mouse.current.position.ReadValue();
        }

        public static bool MouseOnScreen() {
            Vector2 pos = Mouse.current.position.ReadValue();
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            return pos.x <= screenWidth && pos.x >= 0 && pos.y <= screenHeight && pos.y >= 0;
        }
    }
}