using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    // 构建一个纯粹的四叉树测试代码
    public class QuadTreeTest : MonoBehaviour {
        [SerializeField] private int rootSize = 1024;
        [SerializeField] private int arrSize = 256 + 128;

        private QuadTreeManager quadTreeManager = new();
        private QuadTree root = null;

        private void Awake() {
            root = quadTreeManager.CreateRoot(rootSize, arrSize, OnLoadNodeData);
        }

        private void Update() {
            Vector3 p = Camera.main.transform.position;
            Vector2 camPos = new(p.x, p.z);
            quadTreeManager.UpdateNodesState(camPos);
        }

        private void OnLoadNodeData(QuadTree node) {
            Debug.Log($"{node.x}, {node.z}, {node.size}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            quadTreeManager.OnDrawGizmos(Vector3.zero);
        }
#endif
    }
}
