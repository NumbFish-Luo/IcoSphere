using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace IcoSphere {
    // Runtime Virtual Texture
    public class Rvt : MonoBehaviour {
        // ---- 组件 ----
        [SerializeField] private VirtualCapture virtualCapture;
        [SerializeField] private ComputeShader idxGenerator;

        // ---- 可调参数 ----
        [SerializeField] private int rootTexSize = 1024;
        [SerializeField] private int vtArrCapacity = 512;
        [SerializeField] private Vector3 terrainOffset;

        // ---- 四叉树 ----
        private QuadTree root = null;
        private readonly QuadTreeManager quadTreeManager = new();

        // ---- RT ----
        private RenderTexture rtIdx;
        private RenderTexture rtArrAlbedo;
        private RenderTexture rtArrNormal;

        // ---- Compute Shader ----
        private int kernelMain;

        private void Awake() {
            // 创建索引贴图, 并传入给Compute Shader
            rtIdx = new RenderTexture(rootTexSize, rootTexSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            rtIdx.Create();
            kernelMain = idxGenerator.FindKernel("Main");
            idxGenerator.SetTexture(kernelMain, "_Result", rtIdx);

            int vtTexSize = rootTexSize / 2;
            virtualCapture.Init(vtTexSize);

            // 创建纹理数组rt
            rtArrAlbedo = new RenderTexture(vtTexSize, vtTexSize, 0, RenderTextureFormat.ARGB32) {
                volumeDepth = vtArrCapacity,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            rtArrAlbedo.Create();

            rtArrNormal = new RenderTexture(vtTexSize, vtTexSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                volumeDepth = vtArrCapacity,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            rtArrNormal.Create();

            // 初始化四叉树
            root = quadTreeManager.CreateRoot(rootTexSize, rtArrAlbedo.volumeDepth, OnLoadNodeData);

            Shader.SetGlobalInt("_VT_RootTexSize", rootTexSize);
            Shader.SetGlobalTexture("_VT_AlbedoTex", rtArrAlbedo);
            Shader.SetGlobalTexture("_VT_NormalTex", rtArrNormal);
            Shader.SetGlobalTexture("_VT_IdxTex", rtIdx);
        }

        private void OnDestroy() {
            ReleaseRt(rtIdx);
            ReleaseRt(rtArrAlbedo);
            ReleaseRt(rtArrNormal);
        }

        private void ReleaseRt(RenderTexture rt) {
            if (rt != null) {
                rt.Release();
            }
        }

        private void Update() {
            Profiler.BeginSample("updateAllLeavesState");
            Vector3 p = Camera.main.transform.position;
            Vector2 camPos = new(
                p.x - terrainOffset.x,
                p.z - terrainOffset.z
            );
            quadTreeManager.UpdateNodesState(camPos);
            Profiler.EndSample();
        }

        private void OnLoadNodeData(QuadTree node) {
            Profiler.BeginSample("onLoadNodeData");

            // 获取当前地块的中心坐标和尺寸
            Vector2 center = new(node.x + node.size / 2.0f, node.z + node.size / 2.0f);
            int size = node.size;

            // 调用VirtualCapture渲染该地块的albedo和normal到临时rt
            virtualCapture.VirtualCaptureMrt(center, size, out RenderTexture albedoRT, out RenderTexture normalRT);

            // 将渲染结果复制到纹理数组的对应slice中, 同时复制4个mip级别, 可根据需求调整
            for (int i = 0; i < 4; i++) {
                Graphics.CopyTexture(albedoRT, 0, i, rtArrAlbedo, node.phyTexIdx, i);
                Graphics.CopyTexture(normalRT, 0, i, rtArrNormal, node.phyTexIdx, i);
            }

            // 通过ComputeShader更新索引贴图
            idxGenerator.SetVector("_Val", new Vector4(node.phyTexIdx, node.x, node.z, node.size));
            idxGenerator.Dispatch(kernelMain, size, size, 1); // 传入size * size * 1个线程

            Profiler.EndSample();
        }

#if UNITY_EDITOR
        // 在Scene视图中绘制四叉树节点, 调试用
        private void OnDrawGizmos() {
            if (root == null) {
                return;
            }
            quadTreeManager.OnDrawGizmos(terrainOffset);
        }
#endif
    }
}
