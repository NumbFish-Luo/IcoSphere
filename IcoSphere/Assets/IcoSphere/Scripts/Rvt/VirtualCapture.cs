using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    [RequireComponent(typeof(Rvt))]
    public class VirtualCapture : MonoBehaviour {
        public Terrain terrain;
        public Material blitMat;
        public Texture2DArray albedoAtlas;
        public Texture2DArray normalAtlas;
        public const int vtArrSize = 512;
        private RenderTexture[] clipRTs;
        private RenderBuffer[] mrtBufs;

        void Awake() {
            // 创建两个临时rt, 用于存储一次mrt绘制的Albedo和Normal
            clipRTs = new RenderTexture[2];
            clipRTs[0] = new(vtArrSize, vtArrSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            clipRTs[1] = new(vtArrSize, vtArrSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            for (int i = 0; i < clipRTs.Length; ++i) {
                clipRTs[i].useMipMap = true;
                clipRTs[i].autoGenerateMips = false;
                clipRTs[i].Create();
            }

            // 准备mrtBufs
            mrtBufs = new RenderBuffer[] { clipRTs[0].colorBuffer, clipRTs[1].colorBuffer };

            // 将地形控制贴图绑定到材质
            if (blitMat != null) {
                Texture2D[] alphaMaps = terrain.terrainData.alphamapTextures;
                for (int i = 0; i < alphaMaps.Length; ++i) {
                    blitMat.SetTexture("_Ctrl" + i, alphaMaps[i]);
                }
            }

            // 传递图集纹理到Shader
            Shader.SetGlobalTexture("_VT_AlbedoAtlas", albedoAtlas);
            Shader.SetGlobalTexture("_VT_NormalAtlas", normalAtlas);
            Shader.SetGlobalInt("_VT_ArrSize", vtArrSize);

            // 初始化tileData (每个splat的平铺系数)
            // 使用TerrainLayer数组来获取地形图层信息
            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
            Vector4[] tileData = new Vector4[terrainLayers.Length];
            float x = terrain.terrainData.size.x;
            float z = terrain.terrainData.size.z;
            for (int i = 0; i < tileData.Length; i++) {
                tileData[i] = new Vector4(
                    x / terrainLayers[i].tileSize.x,
                    z / terrainLayers[i].tileSize.y,
                    0, 0);
            }
            Shader.SetGlobalVectorArray("_VT_TileData", tileData);
        }

        void OnDestroy() {
            if (clipRTs != null) {
                foreach (RenderTexture rt in clipRTs) {
                    if (rt != null) {
                        rt.Release();
                    }
                }
            }
        }

        // mrt (Multiple Render Targets) 捕获: 输出albedoRT和normalRT, 带mipmap
        public void VirtualCaptureMrt(Vector2 center, float size, out RenderTexture rtAlbedo, out RenderTexture rtNormal) {
            int terrainSize = (int)terrain.terrainData.size.x;
            blitMat.SetVector("_VT_BlitOffsetScale", new(
                (center.x - size / 2) / terrainSize,
                (center.y - size / 2) / terrainSize,
                size / terrainSize,
                size / terrainSize
            ));

            // 设置mrt, 并绘制全屏四边形
            RenderTexture rtOld = RenderTexture.active;

            Graphics.SetRenderTarget(mrtBufs, clipRTs[0].depthBuffer);
            blitMat.SetPass(0);

            GL.Clear(false, true, Color.clear);

            GL.PushMatrix();
            GL.LoadOrtho();

            // Render the full screen quad manually
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(0.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(1.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(1.0f, 1.0f, 0.1f);
            GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(0.0f, 1.0f, 0.1f);
            GL.End();

            GL.PopMatrix();

            RenderTexture.active = rtOld;

            rtAlbedo = clipRTs[0];
            rtNormal = clipRTs[1];

            // 生成mipmap
            rtAlbedo.GenerateMips();
            rtNormal.GenerateMips();
        }

#if UNITY_EDITOR
        [ContextMenu("生成纹理图集")]
        void MakeAlbedoAtlas() {
            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
            int w = terrainLayers[0].diffuseTexture ? terrainLayers[0].diffuseTexture.width : 512;
            int h = terrainLayers[0].diffuseTexture ? terrainLayers[0].diffuseTexture.height : 512;
            int n = terrainLayers.Length;

            // 创建Albedo图集, 线性空间, 因为Albedo通常为sRGB, 但为了便于混合, 保留原始
            albedoAtlas = new Texture2DArray(w, h, n, terrainLayers[0].diffuseTexture ? terrainLayers[0].diffuseTexture.format : TextureFormat.RGBA32, true, false) {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };

            // 创建Normal图集, 线性空间, normal需要unpack
            int wn = terrainLayers[0].normalMapTexture ? terrainLayers[0].normalMapTexture.width : w;
            int hn = terrainLayers[0].normalMapTexture ? terrainLayers[0].normalMapTexture.height : h;
            normalAtlas = new Texture2DArray(wn, hn, n, terrainLayers[0].normalMapTexture ? terrainLayers[0].normalMapTexture.format : TextureFormat.RGBA32, true, true) {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };

            // 逐个复制贴图和法线贴图的每一级mipmap
            for (int i = 0; i < n; ++i) {
                TerrainLayer layer = terrainLayers[i];

                // 复制Albedo
                if (layer.diffuseTexture != null) {
                    int mipCount = layer.diffuseTexture.mipmapCount;
                    for (int mip = 0; mip < mipCount; ++mip)
                        Graphics.CopyTexture(layer.diffuseTexture, 0, mip, albedoAtlas, i, mip);
                } else {
                    Debug.LogWarning($"TerrainLayer {i} 缺少 Albedo 贴图！");
                }

                // 复制法线贴图
                if (layer.normalMapTexture != null) {
                    int normMipCount = layer.normalMapTexture.mipmapCount;
                    for (int mip = 0; mip < normMipCount; mip++)
                        Graphics.CopyTexture(layer.normalMapTexture, 0, mip, normalAtlas, i, mip);
                } else {
                    // 如果没有法线贴图，填充默认法线 (0.5, 0.5, 1.0, 1.0)
                    // 这里简单填充单一颜色，更严谨的做法是生成临时纹理
                    Debug.LogWarning($"TerrainLayer {i} 缺少法线贴图，将使用默认法线。");
                }
            }

            // 保存为资产 (测试用)
            bool testSave = true;
            if (testSave) {
                string path = "Assets/IcoSphere/Atlas/GeneratedVTAtlas.asset";
                UnityEditor.AssetDatabase.CreateAsset(albedoAtlas, path);
                string normalPath = path.Replace(".asset", "_Normal.asset");
                UnityEditor.AssetDatabase.CreateAsset(normalAtlas, normalPath);
                UnityEditor.AssetDatabase.SaveAssets();
            }

            Debug.Log($"纹理图集生成完成！共 {n} 个图层，Albedo 尺寸: {w}x{h}，Normal 尺寸: {wn}x{hn}");
        }
#endif
    }
}
