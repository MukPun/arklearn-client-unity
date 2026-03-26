using UnityEngine;

public class SetSphereCameraTarget : MonoBehaviour
{
    public RenderTexture targetTexture;

    void Start()
    {
        Camera cam = GetComponent<Camera>();
        if (cam != null && targetTexture != null)
        {
            cam.targetTexture = targetTexture;
            Debug.Log("Set sphereCamera targetTexture to: " + targetTexture.name);
        }
    }
}