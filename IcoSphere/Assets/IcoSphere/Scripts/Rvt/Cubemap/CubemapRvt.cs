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
            Shader.SetGlobalTexture("_VT_AlbedoTex", rtArrAlbedo);
            Shader.SetGlobalTexture("_VT_NormalTex", rtArrNormal);
            Shader.SetGlobalTexture("_VT_IdxTex", rtArrIdx);
        }

        private void Update() {
            quadTreeManager.UpdateAllFaces(cam.transform.position);
        }

        private void OnDestroy() {
            ReleaseRt(ref rtArrAlbedo);
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
            Debug.Log($"Load node: f = {node.face}, u = {node.u}, v = {node.v}, s = {node.size}, i = {node.phyTexIdx}");

            // todo: 实现纹理加载逻辑
            // virtualCapture.VirtualCaptureMrt(node, out RenderTexture rtAlbedo, out RenderTexture rtNormal);

            // 将渲染结果复制到纹理数组的对应切片中, 同时复制4个mip级别, 可根据需求调整
            // for (int i = 0; i < 4; ++i) {
            //     Graphics.CopyTexture(rtAlbedo, 0, i, rtArrAlbedo, node.phyTexIdx, i);
            //     Graphics.CopyTexture(rtNormal, 0, i, rtArrNormal, node.phyTexIdx, i);
            // }

            // todo: 通过ComputeShader更新索引贴图
            // ...
            Vector4 val = new(node.phyTexIdx, node.u, node.v, node.size);
            idxGenerator.SetVector("_Val", val);
            idxGenerator.SetInt("_Face", (int)node.face);
            idxGenerator.SetInt("_OffsetU", node.u);
            idxGenerator.SetInt("_OffsetV", node.v);
            idxGenerator.Dispatch(kernelMain, node.size, node.size, 1); // 传入size * size * 1个线程
        }

        private void ReleaseRt(ref RenderTexture rt) {
            if (rt != null) {
                rt.Release();
            }
            rt = null;
        }
    }
}
