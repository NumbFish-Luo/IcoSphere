using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace IcoSphere {
    // Runtime Virtual Texture
    public class Rvt : MonoBehaviour {
        private QuadTree root = null;
        private QuadTreeManager quadTreeManager = new();
        private RenderTexture clipRTAlbedoArray;
        private RenderTexture clipRTNormalArray;
        private VirtualCapture virtualCapture;

        // -------------------- 可调参数 --------------------
        public int rootSize = 1024;
        public Vector3 terrainOffset;
        public ComputeShader indexGenerator;
        public RenderTexture indexRT;

        void Start() {
            virtualCapture = GetComponent<VirtualCapture>();
            if (virtualCapture == null) {
                Debug.LogError("VT_Terrain 需要 VirtualCapture 组件！");
                return;
            }

            // 创建索引贴图
            indexRT = new RenderTexture(rootSize, rootSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            indexRT.Create();

            // 将 Compute Shader 的 Result 纹理绑定
            if (indexGenerator != null)
                indexGenerator.SetTexture(0, "Result", indexRT);
            else
                Debug.LogError("请为 VT_Terrain 指定 indexGenerator (Compute Shader)！");

            // 创建用于存储 VT 纹理数组的 RenderTexture（作为物理图集）
            int arraySize = 256 + 128;  // 与 VirtualCapture 中保持一致，或通过参数暴露
            clipRTAlbedoArray = new RenderTexture(VirtualCapture.virtualTextArraySize, VirtualCapture.virtualTextArraySize, 0, RenderTextureFormat.ARGB32) {
                volumeDepth = arraySize,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            clipRTAlbedoArray.Create();

            clipRTNormalArray = new RenderTexture(VirtualCapture.virtualTextArraySize, VirtualCapture.virtualTextArraySize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                volumeDepth = arraySize,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            clipRTNormalArray.Create();

            // 初始化四叉树
            root = quadTreeManager.CreateRoot(rootSize, clipRTAlbedoArray.volumeDepth, OnLoadNodeData);

            // 将全局纹理传递给 Shader (URP)
            Shader.SetGlobalTexture("_VT_AlbedoTex", clipRTAlbedoArray);
            Shader.SetGlobalTexture("_VT_NormalTex", clipRTNormalArray);
            Shader.SetGlobalTexture("_VT_IndexTex", indexRT);
            Shader.SetGlobalInt("VT_RootSize", rootSize);
        }

        void OnDestroy() {
            if (indexRT != null) indexRT.Release();
            if (clipRTAlbedoArray != null) clipRTAlbedoArray.Release();
            if (clipRTNormalArray != null) clipRTNormalArray.Release();
        }

        void Update() {
            if (Camera.main == null) return;
            Vector2 camPos = new(
                Camera.main.transform.position.x - terrainOffset.x,
                Camera.main.transform.position.z - terrainOffset.z
            );
            Profiler.BeginSample("updateAllLeavesState");
            quadTreeManager.UpdateNodesState(camPos);
            Profiler.EndSample();
        }

        private void OnLoadNodeData(QuadTree node) {
            Profiler.BeginSample("onLoadNodeData");

            // 获取当前地块的中心坐标和尺寸
            Vector2 center = new(node.x + node.size / 2.0f, node.z + node.size / 2.0f);
            int size = node.size;

            // 调用 VirtualCapture 渲染该地块的 albedo 和 normal 到临时 RT
            virtualCapture.VirtualCapture_MRT(center, size, out RenderTexture albedoRT, out RenderTexture normalRT);

            // 将渲染结果复制到纹理数组的对应 slice 中（同时复制 4 个 mip 级别，可根据需求调整）
            for (int i = 0; i < 4; i++) {
                Graphics.CopyTexture(albedoRT, 0, i, clipRTAlbedoArray, node.phyTexIdx, i);
                Graphics.CopyTexture(normalRT, 0, i, clipRTNormalArray, node.phyTexIdx, i);
            }

            // 更新索引贴图（通过 Compute Shader）
            if (indexGenerator != null) {
                indexGenerator.SetVector("value", new Vector4(node.phyTexIdx, node.x, node.z, node.size));
                indexGenerator.SetInt("offsetX", node.x);
                indexGenerator.SetInt("offsetZ", node.z);
                indexGenerator.Dispatch(0, size, size, 1);
                Debug.Log($"Dispatch: {node.phyTexIdx}, {node.x}, {node.z}, {node.size}");
            }

            Profiler.EndSample();
        }

#if UNITY_EDITOR
        // 辅助：在 Scene 视图中绘制四叉树节点（调试用）
        void OnDrawGizmos() {
            if (root == null) {
                return;
            }
            quadTreeManager.OnDrawGizmos(terrainOffset);
        }
#endif
    }
}
