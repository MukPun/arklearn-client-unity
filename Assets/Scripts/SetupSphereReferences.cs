using UnityEngine;
using UnityEngine.UI;
using UI.Sub;

public class SetupSphereReferences : MonoBehaviour {
    [ContextMenu("Setup Sphere")]
    public void Setup() {
        LoginUI loginUI = GetComponent<LoginUI>();
        if (loginUI == null) {
            Debug.LogError("LoginUI not found!");
            return;
        }

        // 加载RenderTexture
        RenderTexture rt = Resources.Load<RenderTexture>("Texture/SphereRT");
        if (rt == null) {
            Debug.LogError("Failed to load SphereRT!");
            return;
        }

        // 创建sphere UI对象
        GameObject sphereObj = new GameObject("sphere");
        sphereObj.transform.parent = loginUI.transform;

        RectTransform rect = sphereObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(800, 800);

        RawImage rawImage = sphereObj.AddComponent<RawImage>();
        rawImage.texture = rt;

        // 设置sphereCamera的Target Texture
        Camera sphereCam = loginUI.sphereCamera;
        if (sphereCam != null) {
            sphereCam.targetTexture = rt;
        }

        // 赋值给LoginUI的字段
        loginUI.sphere = rect;

        Debug.Log("Setup complete!");
    }
}
