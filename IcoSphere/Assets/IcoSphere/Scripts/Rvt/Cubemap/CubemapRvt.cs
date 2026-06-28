using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

namespace IcoSphere {
    public class CubemapRvt : MonoBehaviour {
        // ---- 组件 ----
        [SerializeField] private Camera cam;
        [SerializeField] private CubemapVirtualCapture virtualCapture;
        [SerializeField] private ComputeShader idxGenerator;

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

        // ---- 带多层切片的RT, 6个面混用 ----
        private RenderTexture rtArrIdx = null;
        private RenderTexture rtArrAlbedo = null;
        private RenderTexture rtArrNormal = null;

        // ---- Compute Shader ----
        private int kernelMain;

        // ---- Unity生命周期函数 ----
        private void Awake() {
            // 创建索引贴图, 并传入给Compute Shader
            rtArrIdx = new(rootTexSize, rootTexSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                volumeDepth = 6, // 6层切片
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            rtArrIdx.Create();
            kernelMain = idxGenerator.FindKernel("Main");
            idxGenerator.SetTexture(kernelMain, "_Result", rtArrIdx);

            // 初始化虚拟相机
            int vtTexSize = rootTexSize / 2;
            virtualCapture.Init(vtTexSize);

            // 创建纹理数组rt
            rtArrAlbedo = new(vtTexSize, vtTexSize, 0, RenderTextureFormat.ARGB32) {
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
            quadTreeManager = new(rootTexSize, radius, vtArrCapacity, OnLoadNodeData);

            // 初始化Shader全局参数
            Shader.SetGlobalInt("_VT_RootTexSize", rootTexSize);
            Shader.SetGlobalTexture("_VT_ArrIdx", rtArrIdx);
            Shader.SetGlobalTexture("_VT_ArrAlbedo", rtArrAlbedo);
            Shader.SetGlobalTexture("_VT_ArrNormal", rtArrNormal);

            // 兼容旧版Shader方案, C#的变量名暂且不变, 只是给Shader添加全局参数
            Shader.SetGlobalTexture("_VT_ArrDiffuse", rtArrAlbedo);
            Shader.SetGlobalTexture("_VT_ArrHeight", rtArrNormal);
        }

        private void Update() {
            quadTreeManager.UpdateAllFaces(cam.transform.position);
        }

        private void OnDestroy() {
            ReleaseRt(ref rtArrIdx);
            ReleaseRt(ref rtArrAlbedo);
            ReleaseRt(ref rtArrNormal);
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

        // ---- 私有成员函数 ----
        private void OnLoadNodeData(CubemapQuadTree node) {
            int idx = node.phyTexIdx;
            int u = node.u;
            int v = node.v;
            int s = node.size;
            int f = (int)node.face;

            // 实现纹理加载逻辑. 暂时使用旧版方案, 即使用diffuse和height, 而不是更加现代的albedo和normal
            virtualCapture.VirtualCaptureMrt_Old(node, out RenderTexture rtDiffuse, out RenderTexture rtHeight);

            // 将渲染结果复制到纹理数组的对应切片中, 同时复制4个mip级别, 可根据需求调整
            for (int i = 0; i < 4; ++i) {
                Graphics.CopyTexture(rtDiffuse, 0, i, rtArrAlbedo, idx, i);
                Graphics.CopyTexture(rtHeight, 0, i, rtArrNormal, idx, i);
            }

            // 通过ComputeShader更新索引贴图
            Vector4 val = new(idx, u, v, s);
            idxGenerator.SetVector("_Val", val);
            idxGenerator.SetInt("_Face", f);
            idxGenerator.SetInt("_OffsetU", u);
            idxGenerator.SetInt("_OffsetV", v);
            idxGenerator.Dispatch(kernelMain, s, s, 1); // 传入(s * s * 1)个线程
        }

        private void ReleaseRt(ref RenderTexture rt) {
            if (rt != null) {
                rt.Release();
            }
            rt = null;
        }
    }
}
