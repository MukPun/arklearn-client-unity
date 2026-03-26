using UnityEngine;
using UnityEditor;

public class SphereRTEditor {
    [MenuItem("Assets/Create/Render Texture/SphereRT")]
    public static void CreateSphereRT() {
        RenderTexture rt = new RenderTexture(1024, 1024, 0);
        rt.name = "SphereRT";

        string path = "Assets/Resources/Texture/SphereRT.renderTexture";
        AssetDatabase.CreateAsset(rt, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = rt;

        Debug.Log("Created SphereRT at: " + path);
    }
}
