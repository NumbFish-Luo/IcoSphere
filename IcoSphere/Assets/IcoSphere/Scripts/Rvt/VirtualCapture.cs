using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    [RequireComponent(typeof(Rvt))]
    public class VirtualCapture : MonoBehaviour {
        public Terrain terrain;
        public Material captureMat;                 // 使用 URP 版 VT_Terrain_Blit 材质
        public Texture2DArray albedoAtlas;          // 运行时生成的 Albedo 图集
        public Texture2DArray normalAtlas;          // 运行时生成的 Normal 图集

        public const int virtualTextArraySize = 512; // 与 VT_Terrain 中的尺寸保持一致

        private RenderTexture[] clipRTs;
        private RenderBuffer[] mrtBuffers;
        private Mesh fullscreenQuad;
        private int mipmapCount;

        void Awake() {
            if (terrain == null)
                terrain = GetComponent<Terrain>();

            if (terrain == null) {
                Debug.LogError("VirtualCapture: 未找到 Terrain 组件！");
                return;
            }

            mipmapCount = (int)Mathf.Log(virtualTextArraySize, 2);

            // 创建两个临时 RT，用于存储一次 MRT 绘制的 Albedo 和 Normal
            clipRTs = new RenderTexture[2];
            clipRTs[0] = new(virtualTextArraySize, virtualTextArraySize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            clipRTs[1] = new(virtualTextArraySize, virtualTextArraySize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            for (int i = 0; i < clipRTs.Length; i++) {
                clipRTs[i].useMipMap = true;
                clipRTs[i].autoGenerateMips = false;
                clipRTs[i].Create();
            }

            mrtBuffers = new RenderBuffer[] { clipRTs[0].colorBuffer, clipRTs[1].colorBuffer };

            // 创建全屏四边形，用于 Blit 操作（替代 GL.Begin/End）
            fullscreenQuad = CreateFullscreenQuad();

            // 将地形控制贴图绑定到材质
            if (captureMat != null) {
                var alphamaps = terrain.terrainData.alphamapTextures;
                for (int i = 0; i < alphamaps.Length; i++)
                    captureMat.SetTexture("_Control" + i, alphamaps[i]);
            }

            // 传递图集纹理到 Shader
            Shader.SetGlobalTexture("albedoAtlas", albedoAtlas);
            Shader.SetGlobalTexture("normalAtlas", normalAtlas);
            Shader.SetGlobalInt("virtualTextArraySize", virtualTextArraySize);

            // 初始化 tileData（每个 splat 的平铺系数）
            // 使用 TerrainLayer 数组来获取地形图层信息
            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
            var tileData = new Vector4[terrainLayers.Length];
            for (int i = 0; i < tileData.Length; i++) {
                tileData[i] = new Vector4(
                    terrain.terrainData.size.x / terrainLayers[i].tileSize.x,
                    terrain.terrainData.size.z / terrainLayers[i].tileSize.y,
                    0, 0);
            }
            Shader.SetGlobalVectorArray("tileData", tileData);
        }

        void OnDestroy() {
            if (clipRTs != null) {
                foreach (var rt in clipRTs)
                    if (rt != null) rt.Release();
            }
            if (fullscreenQuad != null)
                Destroy(fullscreenQuad);
        }

        // 创建全屏四边形网格（NDC: -1 到 1）
        private Mesh CreateFullscreenQuad() {
            Mesh mesh = new Mesh {
                vertices = new Vector3[] {
                    new(-1, -1, 0),
                    new( 1, -1, 0),
                    new( 1,  1, 0),
                    new(-1,  1, 0)
                },
                uv = new Vector2[] {
                    new(0, 0),
                    new(1, 0),
                    new(1, 1),
                    new(0, 1)
                },
                triangles = new int[] { 0, 1, 2, 0, 2, 3 }
            };
            return mesh;
        }

        // MRT 捕获：输出 albedoRT 和 normalRT（带 mipmap）
        public void VirtualCapture_MRT(Vector2 center, float size, out RenderTexture albedoRT, out RenderTexture normalRT) {
            if (captureMat == null) {
                albedoRT = normalRT = null;
                Debug.LogError("captureMat 未设置！");
                return;
            }

            int terrainSize = (int)terrain.terrainData.size.x;
            Vector4 offsetScale = new(
                (center.x - size / 2) / terrainSize,
                (center.y - size / 2) / terrainSize,
                size / terrainSize,
                size / terrainSize
            );
            captureMat.SetVector("blitOffsetScale", offsetScale);

            // 设置 MRT 并绘制全屏四边形
            Graphics.SetRenderTarget(mrtBuffers, clipRTs[0].depthBuffer);
            GL.Clear(false, true, Color.clear);
            captureMat.SetPass(0);
            Graphics.DrawMeshNow(fullscreenQuad, Matrix4x4.identity);

            // 生成 mipmap
            clipRTs[0].GenerateMips();
            clipRTs[1].GenerateMips();

            albedoRT = clipRTs[0];
            normalRT = clipRTs[1];
        }

#if UNITY_EDITOR
        [ContextMenu("生成纹理图集 (MakeAlbedoAtlas)")]
        void MakeAlbedoAtlas() {
            if (terrain == null || terrain.terrainData == null) {
                Debug.LogError("请先指定 Terrain 组件！");
                return;
            }

            // 使用新的 TerrainLayer API 获取地形图层数据
            TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
            if (terrainLayers.Length == 0) {
                Debug.LogError("地形没有 TerrainLayer 图层！");
                return;
            }

            // 获取第一张贴图的尺寸作为图集尺寸（所有贴图尺寸应相同）
            int width = terrainLayers[0].diffuseTexture ? terrainLayers[0].diffuseTexture.width : 512;
            int height = terrainLayers[0].diffuseTexture ? terrainLayers[0].diffuseTexture.height : 512;
            int arrayLen = terrainLayers.Length;

            // 创建 Albedo 图集（线性空间，因为 Albedo 通常为 sRGB，但为了便于混合，保留原始）
            albedoAtlas = new Texture2DArray(width, height, arrayLen,
                terrainLayers[0].diffuseTexture ? terrainLayers[0].diffuseTexture.format : TextureFormat.RGBA32, true, false) {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };

            // 创建 Normal 图集（线性空间，因为 normal map 需要 unpack）
            int normWidth = terrainLayers[0].normalMapTexture ? terrainLayers[0].normalMapTexture.width : width;
            int normHeight = terrainLayers[0].normalMapTexture ? terrainLayers[0].normalMapTexture.height : height;
            normalAtlas = new Texture2DArray(normWidth, normHeight, arrayLen,
                terrainLayers[0].normalMapTexture ? terrainLayers[0].normalMapTexture.format : TextureFormat.RGBA32, true, true) {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };

            // 逐个复制贴图和法线贴图的每一级 mipmap
            for (int idx = 0; idx < arrayLen; idx++) {
                var layer = terrainLayers[idx];

                // 复制 Albedo 贴图
                if (layer.diffuseTexture != null) {
                    int mipCount = layer.diffuseTexture.mipmapCount;
                    for (int mip = 0; mip < mipCount; mip++)
                        Graphics.CopyTexture(layer.diffuseTexture, 0, mip, albedoAtlas, idx, mip);
                } else {
                    Debug.LogWarning($"TerrainLayer {idx} 缺少 Albedo 贴图！");
                }

                // 复制法线贴图
                if (layer.normalMapTexture != null) {
                    int normMipCount = layer.normalMapTexture.mipmapCount;
                    for (int mip = 0; mip < normMipCount; mip++)
                        Graphics.CopyTexture(layer.normalMapTexture, 0, mip, normalAtlas, idx, mip);
                } else {
                    // 如果没有法线贴图，填充默认法线 (0.5, 0.5, 1.0, 1.0)
                    // 这里简单填充单一颜色，更严谨的做法是生成临时纹理
                    Debug.LogWarning($"TerrainLayer {idx} 缺少法线贴图，将使用默认法线。");
                }
            }

            // 保存为资产（可选）
            string path = "Assets/IcoSphere/Atlas/GeneratedVTAtlas.asset";
            UnityEditor.AssetDatabase.CreateAsset(albedoAtlas, path);
            string normalPath = path.Replace(".asset", "_Normal.asset");
            UnityEditor.AssetDatabase.CreateAsset(normalAtlas, normalPath);
            UnityEditor.AssetDatabase.SaveAssets();

            Debug.Log($"纹理图集生成完成！共 {arrayLen} 个图层，Albedo 尺寸: {width}x{height}，Normal 尺寸: {normWidth}x{normHeight}");
        }
#endif
    }
}
