using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class CubemapRvt : MonoBehaviour {
        // ---- 组件 ----
        [SerializeField] private Camera cam;
        [SerializeField] private CubemapVirtualCapture virtualCapture;

        // ---- 可调参数 ----
        [SerializeField] private float radius = 1000.0f;
        [SerializeField] private int rootTexSize = 1024;
        [SerializeField] private int vtArrCapacity = 512;

        // ---- Debug ----
        [Header("Debug")]
        [SerializeField] private bool showAllFaces = true; // 勾选则显示所有面, 忽略selectedFace
        [SerializeField] private CubemapFace selectedFace = CubemapFace.R; // 选择要显示的面

        // ---- 四叉树 ----
        private CubemapQuadTreeManager quadTreeManager = null;

        // ---- 6面带多层切片的RT ----
        private RenderTexture[] rtArrAlbedoAllFaces = new RenderTexture[6];

        // ---- Compute Shader ----
        // ....

        // ---- Unity生命周期函数 ----
        private void Awake() {
            quadTreeManager = new(rootTexSize, radius, vtArrCapacity, OnLoadNodeData);

            int vtTexSize = rootTexSize / 2;
            virtualCapture.Init(vtTexSize);

            for (int face = 0; face < 6; ++face) {
                RenderTexture rtArrAlbedoOneFace = new(vtTexSize, vtTexSize, 0, RenderTextureFormat.ARGB32) {
                    volumeDepth = vtArrCapacity,
                    wrapMode = TextureWrapMode.Clamp,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                    useMipMap = true,
                    autoGenerateMips = false
                };
                rtArrAlbedoOneFace.Create();
                rtArrAlbedoAllFaces[face] = rtArrAlbedoOneFace;
            }
        }

        private void Update() {
            quadTreeManager.UpdateAllFaces(cam.transform.position);
        }

        private void OnDestroy() {
            // ...
        }

        // ---- 私有函数 ----
        private void OnLoadNodeData(CubemapQuadTree node) {
            // todo: 实现纹理加载逻辑
            Debug.Log($"Load node: face={node.face}, u={node.u}, v={node.v}, size={node.size}, texIdx={node.phyTexIdx}");
        }

        private void ReleaseRt(RenderTexture rt) {
            if (rt != null) {
                rt.Release();
            }
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
