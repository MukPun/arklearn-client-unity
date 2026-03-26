using UnityEngine;

public class SetupWireframeCore : MonoBehaviour {
    public Transform wireframeCore;

    void Start() {
        SphereWireframe wireframe = GetComponent<SphereWireframe>();
        if (wireframe != null && wireframeCore != null) {
            // SphereWireframe创建完子对象后，延迟调用此方法重新设置父对象
            Invoke("ReParentLineRenderers", 0.1f);
        }
    }

    void ReParentLineRenderers() {
        SphereWireframe wireframe = GetComponent<SphereWireframe>();
        if (wireframe == null) return;

        // 获取所有LineRenderer子对象
        LineRenderer[] renderers = GetComponentsInChildren<LineRenderer>();
        foreach (LineRenderer lr in renderers) {
            if (lr.gameObject != wireframeCore.gameObject) {
                lr.transform.SetParent(wireframeCore);
                lr.transform.localPosition = Vector3.zero;
                lr.transform.localRotation = Quaternion.identity;
            }
        }

        Debug.Log("ReParent complete: " + renderers.Length + " LineRenderers");
    }
}
