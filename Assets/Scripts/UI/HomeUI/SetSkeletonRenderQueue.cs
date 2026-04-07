using UnityEngine;
using Spine.Unity;

namespace UI.HomeUI {
    /// <summary>
    /// 设置 SkeletonAnimation 的渲染队列，确保它显示在 UI 之上
    /// </summary>
    public class SetSkeletonRenderQueue : MonoBehaviour {
        [Tooltip("渲染队列值，UI Canvas (Overlay) 约为 3000，设置更高即可覆盖")]
        public int renderQueue = 4000;

        void Start() {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null) {
                renderer.material.renderQueue = renderQueue;
                Debug.Log($"[SetSkeletonRenderQueue] Set renderQueue to {renderQueue} on {gameObject.name}");
            }
        }
    }
}