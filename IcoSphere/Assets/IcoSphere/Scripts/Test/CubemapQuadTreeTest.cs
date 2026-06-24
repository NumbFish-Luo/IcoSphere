using UnityEngine;

namespace IcoSphere {
    public class CubemapQuadTreeTest : MonoBehaviour {
        [Header("Planet Settings")]
        [SerializeField] private Camera cam;
        [SerializeField] private float radius = 1000.0f;
        [SerializeField] private int rootSize = 1024;
        [SerializeField] private int texArrCapacity = 512;

        [Header("Debug Visualization")]
        [SerializeField] private CubemapFace selectedFace = CubemapFace.R; // 选择要显示的面
        [SerializeField] private bool showAllFaces = true; // 勾选则显示所有面, 忽略selectedFace

        private CubemapQuadTreeManager quadTreeManager = null;

        private void Awake() {
            quadTreeManager = new CubemapQuadTreeManager(
                rootSize,
                radius,
                texArrCapacity,
                OnLoadNodeData
            );
        }

        private void Update() {
            quadTreeManager.UpdateAllFaces(cam.transform.position);
        }

        private void OnLoadNodeData(CubemapQuadTree node) {
            // todo: 实现纹理加载逻辑
            Debug.Log($"Load node: face={node.face}, u={node.u}, v={node.v}, size={node.size}, texIdx={node.phyTexIdx}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (quadTreeManager != null) {
                if (showAllFaces) {
                    quadTreeManager.OnDrawGizmos(); // 显示所有面
                } else {
                    quadTreeManager.OnDrawGizmos((int)selectedFace); // 只显示选中的面
                }
            }
        }
#endif
    }
}
