using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class VirtualCapture : MonoBehaviour {
        private Material captureMat;
        public Shader captureShader;
        public TerrainData terrainData;
        public Texture2DArray albedoAtlas;
        public Texture2DArray normalAtlas;
        public RenderTexture[] clipRTs;
        private RenderBuffer[] mrtRB = new RenderBuffer[2];
        public int mipmapCount;
        public const int virtualTextArraySize = 512;

        void Awake() {
            mipmapCount = (int)Mathf.Log(virtualTextArraySize, 2);
            clipRTs = new RenderTexture[2];
            for (int i = 0; i < clipRTs.Length; i++) {
                clipRTs[i] = new(
                    width: virtualTextArraySize,
                    height: virtualTextArraySize,
                    depth: 16,
                    format: RenderTextureFormat.ARGB32,
                    readWrite: i == 0 ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear
                ) {
                    useMipMap = true,
                    autoGenerateMips = false
                };
                clipRTs[i].Create();
            }

            captureMat = new Material(captureShader);
            for (int k = 0; k < terrainData.alphamapTextures.Length; k++) {
                captureMat.SetTexture("_Control" + k, terrainData.alphamapTextures[k]);
            }

            // splatPrototypes已过时, 现在改为最新写法terrainLayers
            // 获取TerrainLayer数组
            TerrainLayer[] terrainLayers = terrainData.terrainLayers;
            if (terrainLayers == null || terrainLayers.Length == 0) {
                Debug.LogWarning("Terrain has no layers.");
                return; // 或者根据业务逻辑处理
            }

            var tileData = new Vector4[terrainLayers.Length];
            for (int i = 0; i < tileData.Length; i++) {
                // 注意: TerrainLayer.tileSize是Vector2, 用法与原splatPrototypes一致
                tileData[i] = new Vector4(
                    terrainData.size.x / terrainLayers[i].tileSize.x,
                    terrainData.size.z / terrainLayers[i].tileSize.y,
                    0, 0
                );
            }

            Shader.SetGlobalTexture("albedoAtlas", albedoAtlas);
            Shader.SetGlobalTexture("normalAtlas", normalAtlas);
            Shader.SetGlobalVectorArray("tileData", tileData);
            Shader.SetGlobalInt("virtualTextArraySize", virtualTextArraySize);

            // mrt mode
            mrtRB = new RenderBuffer[] { clipRTs[0].colorBuffer, clipRTs[1].colorBuffer };
        }

        void OnDestroy() {
            if (clipRTs != null) {
                for (int i = 0; i < clipRTs.Length; i++) {
                    clipRTs[i].Release();
                }
            }
        }

        // QuadTree分配了索引之后, 我们可以根据节点所在的位置和size, 去加载这块混合后的贴图, 并拷贝到Texture2DArray对应的index里
        // 这里说的加载并不是真的加载, 如果是SVT (Streaming Virtural Texture) 那就是硬盘加载, 我们做RVT (Runtime Virtual Texture) 这里其实是实时创建
        // 为了流程描述统一, 特意说成加载
        // 这里实时创建有2种方式, 第一种是放个相机去拍, 这种简单也能对格子贴画, 路面等自动支持, 但是性能不好
        // 因为渲染流程要走一遍, 相机要对地形mesh各种处理, 这些都是我们不需要的
        // 所以我这里采用性能更高的blit方式, 缺点是做路面与贴花时需要再开发功能支持
        // MRT (Multiple Render Targets): 允许在一次渲染过程中输出多个渲染结果
        public void VirtualCapture_MRT(Vector2 center, float size, out RenderTexture albedoRT, out RenderTexture normalRT) {
            int terrainSize = (int)terrainData.size.x;
            Shader.SetGlobalVector(
                "blitOffsetScale",
                new Vector4(
                    (center.x - size / 2) / terrainSize,
                    (center.y - size / 2) / terrainSize,
                    (size) / terrainSize,
                    (size) / terrainSize
                )
            );
            RenderTexture oldRT = RenderTexture.active;
            Graphics.SetRenderTarget(mrtRB, clipRTs[0].depthBuffer);

            GL.Clear(false, true, Color.clear);

            GL.PushMatrix();
            GL.LoadOrtho();

            captureMat.SetPass(0); // Pass 0 outputs 2 render textures.

            // Render the full screen quad manually.
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0.0f, 0.0f); GL.Vertex3(0.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 0.0f); GL.Vertex3(1.0f, 0.0f, 0.1f);
            GL.TexCoord2(1.0f, 1.0f); GL.Vertex3(1.0f, 1.0f, 0.1f);
            GL.TexCoord2(0.0f, 1.0f); GL.Vertex3(0.0f, 1.0f, 0.1f);
            GL.End();

            GL.PopMatrix();

            RenderTexture.active = oldRT;
            albedoRT = clipRTs[0];
            normalRT = clipRTs[1];
            albedoRT.GenerateMips();
            normalRT.GenerateMips();
        }

#if UNITY_EDITOR
        // splatPrototypes已过时, 现在改为最新写法terrainLayers
        [ContextMenu("MakeAlbedoAtlas")]
        private void MakeAlbedoAtlas() {
            // 获取TerrainData组件
            TerrainData terrainData = GetComponent<Terrain>().terrainData;

            // 1. 使用terrainLayers替代splatPrototypes
            TerrainLayer[] terrainLayers = terrainData.terrainLayers;
            if (terrainLayers == null || terrainLayers.Length == 0) {
                Debug.LogWarning("No terrain layers found!");
                return;
            }

            // 使用第一个图层的贴图尺寸作为标准
            int width = terrainLayers[0].diffuseTexture.width;
            int height = terrainLayers[0].diffuseTexture.height;

            // 法线贴图的宽高 (如果没有法线贴图, 可以使用0, 这里我们使用和漫反射贴图同样的尺寸)
            int normalWidth = terrainLayers[0].normalMapTexture != null ? terrainLayers[0].normalMapTexture.width : width;
            int normalHeight = terrainLayers[0].normalMapTexture != null ? terrainLayers[0].normalMapTexture.height : height;

            // 2. 初始化纹理数组
            // 注意: Texture2DArray的构造参数可能需要调整, 具体取决于你的Unity版本和需求
            albedoAtlas = new Texture2DArray(width, height, terrainLayers.Length, terrainLayers[0].diffuseTexture.format, true, false);
            // 目前对于法线贴图, 可能需要设置为线性空间格式, 且通常不需要mipmap
            normalAtlas = new Texture2DArray(normalWidth, normalHeight, terrainLayers.Length, TextureFormat.RGBA32, true, true);

            // 3. 循环遍历所有地形图层
            for (int i = 0; i < terrainLayers.Length; i++) {
                // 获取当前图层的漫反射贴图和法线贴图
                Texture2D diffuseTex = terrainLayers[i].diffuseTexture;
                Texture2D normalTex = terrainLayers[i].normalMapTexture;

                if (diffuseTex == null) continue; // 跳过没有漫反射贴图的图层

                // 复制漫反射贴图的所有mipmap级别
                for (int mip = 0; mip < diffuseTex.mipmapCount; mip++) {
                    Graphics.CopyTexture(diffuseTex, 0, mip, albedoAtlas, i, mip);
                }

                // 复制法线贴图的所有mipmap级别 (如果存在)
                if (normalTex != null) {
                    for (int mip = 0; mip < normalTex.mipmapCount; mip++) {
                        Graphics.CopyTexture(normalTex, 0, mip, normalAtlas, i, mip);
                    }
                }
            }
        }
#endif
    }
}
