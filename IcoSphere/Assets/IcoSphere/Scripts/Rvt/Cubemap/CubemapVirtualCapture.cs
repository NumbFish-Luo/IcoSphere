using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace IcoSphere {
    public class CubemapVirtualCapture : MonoBehaviour {
        // ---- 可调参数 ----
        [SerializeField] private string atlasSrcPath = "Assets/IcoSphere/Textures/Terrain";
        [SerializeField] private string atlasDstPath = "Assets/IcoSphere/Atlas";
        [SerializeField] private string atlasTexSuffix = ".png";
        [SerializeField] private int atlasTexSize = 1024;
        [SerializeField] private Material matBlit;
        [SerializeField] private Texture2DArray atlasDiffuse;
        [SerializeField] private Texture2DArray atlasHeight;
        [SerializeField] private Texture2DArray atlasMix;

        // ---- 私有成员变量 ----
        // 数值大小等同于需要mrt一次生成的贴图数量大小, 旧版是Diffuse + Height + Mix = 3, 新版是Albedo + Normal = 2
        private RenderTexture[] rts = new RenderTexture[3];
        private RenderBuffer[] bufs = new RenderBuffer[3];

        // ---- 内部类 ----
        private class ImgPathGroup {
            public string dPath;
            public string hPath;
            public string mPath;
        }

        // ---- Unity生命周期函数 ----
        private void OnDestroy() {
            for (int i = 0; i < 2; ++i) {
                if (rts[i] != null) {
                    rts[i].Release();
                    rts[i] = null;
                }
            }
        }

        // ---- 公有成员函数 ----
        public void Init(int vtTexSize) {
            // 这里使用旧版的Diffuse + Height + Mix组合
            // 如果使用更加现代的Albedo + Normal组合, 则需要Normal是RenderTextureReadWrite.Linear
            for (int i = 0; i < 3; ++i) {
                rts[i] = new(vtTexSize, vtTexSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                    useMipMap = true,
                    autoGenerateMips = false
                };
                rts[i].Create();
                bufs[i] = rts[i].colorBuffer;
            }

            // 地形相关Shader数据
            // todo: 地形图集, 包含多种类型地形的多种类型贴图, 例如泥土贴图 Dirt1_d (diffuse), Dirt1_h (height), Dirt1_m (mix)
            // todo: 地形混合alpha贴图, 刷地形时控制各个地形的混合值, 或者直接使用 xxx_m (mix) 计算获得混合值
            // 暂时无需做法线贴图, 后续再添加法线

            // 全局Shader参数设置
            Shader.SetGlobalInt("_VT_TexSize", vtTexSize);
        }

        // 这里使用旧版的Diffuse + Height + Mix组合, 后面需要改成现代的Albedo + Normal组合
        public void VirtualCaptureMrt(CubemapQuadTree node, out RenderTexture rtDiffuse, out RenderTexture rtHeight, out RenderTexture rtMix) {
            rtDiffuse = null;
            rtHeight = null;
            rtMix = null;
        }

        // ---- 私有成员函数 ----
#if UNITY_EDITOR
        // 在这里制作图集, 实为多层切片的Texture2DArray, 需要贴图为RGBA 32bit格式
        // 将指定文件夹内的所有贴图, 按_d (diffuse), _h (height), _m (mix)后缀生成对应图集
        // 也就是共3个图集, 分别命名为AtlasDiffuse, AtlasHeight, AtlasMix
        // 例外: 有一张贴图Common_h是默认贴图，直接当每个图集的第一张贴图 (切片0位置)
        // 贴图默认读取文件夹: atlasSrcPath = "Assets/IcoSphere/Textures/Terrain"
        // 图集默认保存文件夹: atlasDstPath = "Assets/IcoSphere/Atlas"
        [ContextMenu("生成图集")]
        private void MakeAtlas() {
            // 默认贴图, 记得进行缩放
            const string COMMON = "Common_h";
            Texture2D defaultTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{atlasSrcPath}/{COMMON}{atlasTexSuffix}");
            defaultTex = ResizeTexture(defaultTex, atlasTexSize, atlasTexSize);

            // 收集源文件夹下所有贴图
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { atlasSrcPath });
            Dictionary<string, ImgPathGroup> groups = new();
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);

                // 只处理带_d, _h, _m后缀的贴图，且跳过Common_h默认贴图
                if (fileName == COMMON) {
                    continue;
                }

                string suffix = null;
                if (fileName.EndsWith("_d")) {
                    suffix = "d";
                } else if (fileName.EndsWith("_h")) {
                    suffix = "h";
                } else if (fileName.EndsWith("_m")) {
                    suffix = "m";
                } else {
                    continue;
                }

                string baseName = fileName[..^2];
                if (!groups.ContainsKey(baseName)) {
                    groups[baseName] = new();
                }

                ImgPathGroup g = groups[baseName];
                switch (suffix) {
                case "d": g.dPath = path; break;
                case "h": g.hPath = path; break;
                case "m": g.mPath = path; break;
                }
                groups[baseName] = g;
            }

            // 构建3个图集的贴图列表, 均以Common_h作为第0张
            List<Texture2D> diffuseList = new() { defaultTex };
            List<Texture2D> heightList = new() { defaultTex };
            List<Texture2D> mixList = new() { defaultTex };

            // 按键名排序，保证生成顺序稳定
            foreach (KeyValuePair<string, ImgPathGroup> kv in groups.OrderBy(g => g.Key)) {
                if (!string.IsNullOrEmpty(kv.Value.dPath)) {
                    diffuseList.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(kv.Value.dPath));
                } else {
                    diffuseList.Add(defaultTex);
                }
                if (!string.IsNullOrEmpty(kv.Value.hPath)) {
                    heightList.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(kv.Value.hPath));
                } else {
                    heightList.Add(defaultTex);
                }
                if (!string.IsNullOrEmpty(kv.Value.mPath)) {
                    mixList.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(kv.Value.mPath));
                } else {
                    mixList.Add(defaultTex);
                }
            }

            // 生成并保存图集资产
            CreateAndSaveTexArr(diffuseList, $"{atlasDstPath}/AtlasDiffuse.asset", "AtlasDiffuse");
            CreateAndSaveTexArr(heightList, $"{atlasDstPath}/AtlasHeight.asset", "AtlasHeight");
            CreateAndSaveTexArr(mixList, $"{atlasDstPath}/AtlasMix.asset", "AtlasMix");

            AssetDatabase.Refresh();
            Debug.Log("图集生成完成！");
        }

        // 从贴图列表生成 Texture2DArray 并保存为 .asset 文件
        private void CreateAndSaveTexArr(List<Texture2D> texs, string savePath, string arrName) {
            // 尺寸是固定的, 如果贴图尺寸不同则需要进行缩放处理
            int w = atlasTexSize;
            int h = atlasTexSize;
            for (int i = 0; i < texs.Count; i++) {
                if (texs[i].width != w || texs[i].height != h) {
                    texs[i] = ResizeTexture(texs[i], w, h);
                }
            }

            // 拷贝贴图
            Texture2DArray texArr = new(w, h, texs.Count, texs[0].format, true) { name = arrName };
            for (int i = 0; i < texs.Count; ++i) {
                try {
                    Graphics.CopyTexture(texs[i], 0, 0, 0, 0, w, h, texArr, i, 0, 0, 0);
                } catch (System.Exception e) {
                    Debug.LogError($"拷贝贴图 {texs[i].name} 到 {arrName} 时出错：{e.Message}");
                    return;
                }
            }
            AssetDatabase.CreateAsset(texArr, savePath);
        }

        // 使用RT高质量缩放贴图
        private Texture2D ResizeTexture(Texture2D src, int w, int h) {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D result = new(w, h, TextureFormat.ARGB32, false);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }
#endif
    }
}
