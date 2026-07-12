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
        [SerializeField] private int atlasTexSize = 512;
        [SerializeField] private int vtArrCapacity = 512;

        // ---- Debug ----
        [Header("Debug")]
        [SerializeField] private bool showAllFaces = true; // 勾选则显示所有面, 忽略selectedFace
        [SerializeField] private CubemapFace selectedFace = CubemapFace.R; // 选择要显示的面

        // ---- 四叉树 ----
        private CubemapQuadTreeManager quadTreeManager = null;

        // ---- 带多层切片的RT, 6个面混用 ----
        private RenderTexture rtArrIdx = null;
        private RenderTexture rtArrDiffuse = null;
        private RenderTexture rtArrHeight = null;
        private RenderTexture rtArrMix = null;

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
            virtualCapture.Init(atlasTexSize);

            // 创建纹理数组rt
            // 这里使用旧版的Diffuse + Height + Mix组合
            // 如果使用更加现代的Albedo + Normal组合, 则需要Normal是RenderTextureReadWrite.Linear
            NewRtArr(ref rtArrDiffuse, atlasTexSize, RenderTextureReadWrite.sRGB);
            NewRtArr(ref rtArrHeight, atlasTexSize, RenderTextureReadWrite.sRGB);
            NewRtArr(ref rtArrMix, atlasTexSize, RenderTextureReadWrite.sRGB);

            // 初始化四叉树
            quadTreeManager = new(rootTexSize, radius, vtArrCapacity, OnLoadNodeData);

            // 初始化Shader全局参数
            Shader.SetGlobalInt("_VT_RootTexSize", rootTexSize);
            Shader.SetGlobalInt("_VT_AtlasTexSize", atlasTexSize);
            Shader.SetGlobalTexture("_VT_ArrIdx", rtArrIdx);

            // 现代的Albedo + Normal组合
            // Shader.SetGlobalTexture("_VT_ArrAlbedo", rtArrAlbedo);
            // Shader.SetGlobalTexture("_VT_ArrNormal", rtArrNormal);

            // 这里使用旧版的Diffuse + Height + Mix组合
            Shader.SetGlobalTexture("_VT_ArrDiffuse", rtArrDiffuse);
            Shader.SetGlobalTexture("_VT_ArrHeight", rtArrHeight);
            Shader.SetGlobalTexture("_VT_ArrMix", rtArrMix);
        }

        private void Update() {
            quadTreeManager.UpdateAllFaces(cam.transform.position);
        }

        private void OnDestroy() {
            ReleaseRt(ref rtArrIdx);
            ReleaseRt(ref rtArrDiffuse);
            ReleaseRt(ref rtArrHeight);
            ReleaseRt(ref rtArrMix);
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
        private void NewRtArr(ref RenderTexture rtArr, int size, RenderTextureReadWrite rw) {
            rtArr = new(size, size, 0, RenderTextureFormat.ARGB32, rw) {
                volumeDepth = vtArrCapacity,
                wrapMode = TextureWrapMode.Clamp,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
                useMipMap = true,
                autoGenerateMips = false
            };
            rtArr.Create();
        }

        private void OnLoadNodeData(CubemapQuadTree node) {
            int idx = node.phyTexIdx;
            int u = node.u;
            int v = node.v;
            int s = node.size;
            int f = (int)node.face;

            // 实现纹理加载逻辑. 暂时使用旧版方案
            virtualCapture.VirtualCaptureMrt(node, out RenderTexture rtDiffuse, out RenderTexture rtHeight, out RenderTexture rtMix);

            // 将渲染结果复制到纹理数组的对应切片中, 同时复制4个mip级别, 可根据需求调整
            for (int mip = 0; mip < 4; ++mip) {
                Graphics.CopyTexture(rtDiffuse, 0, mip, rtArrDiffuse, idx, mip);
                Graphics.CopyTexture(rtHeight, 0, mip, rtArrHeight, idx, mip);
                Graphics.CopyTexture(rtMix, 0, mip, rtArrMix, idx, mip);
            }

            // 通过ComputeShader更新索引贴图
            Vector4 val = new(idx, u, v, s);
            idxGenerator.SetVector("_Val", val);
            idxGenerator.SetInt("_Face", f);
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
