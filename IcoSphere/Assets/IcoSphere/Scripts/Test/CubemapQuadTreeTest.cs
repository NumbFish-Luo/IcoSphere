using UnityEngine;

namespace IcoSphere {
    public class CubemapQuadTreeTest : MonoBehaviour {
        [Header("Planet Settings")]
        public float planetRadius = 1000f;
        public int rootTextureSize = 1024;
        public int textureArrayCapacity = 512;

        [Header("Debug Visualization")]
        public CubemapFace selectedFace = CubemapFace.R; // 选择要显示的面
        public bool showAllFaces = false; // 勾选则显示所有面, 忽略selectedFace

        private CubemapQuadTreeManager quadTreeManager;

        private void Awake() {
            quadTreeManager = new CubemapQuadTreeManager(
                rootTextureSize,
                planetRadius,
                textureArrayCapacity,
                OnLoadNodeData
            );
        }

        private void Update() {
            Vector3 cameraPos = Camera.main.transform.position;
            quadTreeManager.UpdateAllFaces(cameraPos);
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
