using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IcoSphere {
    public class SphericalRvtManager : MonoBehaviour {
        [StructLayout(LayoutKind.Sequential)]
        public struct AreaTerrainData {
            // x: terrain id, y: uv repeat, z/w: uv offset
            public Vector4 info;
            public Vector4 tint;
        }

        private struct RvtPage {
            public int slice;
            public int pageX;
            public int pageY;
            public Vector2 minUv;
            public Vector2 sizeUv;
            public Vector2Int indexOffset;
            public Vector2Int indexSize;
        }

        private const int TERRAIN_COUNT = 8;
        private const TerrainType DEFAULT_TERRAIN = TerrainType.Plains;
        private static readonly string[] DEFAULT_TERRAIN_ALBEDO_PATHS = {
            "Assets/IcoSphere/Textures/Terrain/Water_m.png",
            "Assets/IcoSphere/Textures/Terrain/Sand1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Plains1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Mountain1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Marsh1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Hill3_d.png",
            "Assets/IcoSphere/Textures/Terrain/Dirt1_d.png",
            "Assets/IcoSphere/Textures/Terrain/River1_d.png"
        };

        private static readonly Color[] DEFAULT_TERRAIN_COLORS = {
            new(0.08f, 0.22f, 0.36f, 1.0f),
            new(0.78f, 0.68f, 0.44f, 1.0f),
            new(0.28f, 0.56f, 0.25f, 1.0f),
            new(0.38f, 0.38f, 0.36f, 1.0f),
            new(0.22f, 0.38f, 0.28f, 1.0f),
            new(0.42f, 0.48f, 0.26f, 1.0f),
            new(0.45f, 0.31f, 0.20f, 1.0f),
            new(0.05f, 0.28f, 0.42f, 1.0f)
        };

        [Header("Source Terrain Textures")]
        [SerializeField] private Texture2D[] terrainAlbedoTextures = new Texture2D[TERRAIN_COUNT];
        [SerializeField, Range(128, 2048)] private int terrainTextureSize = 1024;
        [SerializeField] private Vector2 terrainRepeat = new(72.0f, 36.0f);

        [Header("Spherical RVT")]
        [SerializeField] private ComputeShader indexCompute;
        [SerializeField] private ComputeShader bakeCompute;
        [SerializeField, Range(64, 512)] private int tileSize = 256;
        [SerializeField, Range(2, 64)] private int pageColumns = 16;
        [SerializeField, Range(1, 32)] private int pageRows = 8;
        [SerializeField, Range(4, 128)] private int indexTexelsPerPage = 32;
        [SerializeField, Range(1, 64)] private int pagesToBakePerFrame = 8;

        [Header("Terrain Id Map")]
        [SerializeField, Range(128, 4096)] private int terrainIdMapWidth = 1024;
        [SerializeField, Range(64, 2048)] private int terrainIdMapHeight = 512;
        [SerializeField, Range(0, 64)] private int terrainIdMapDilatePasses = 12;

        private IcoSphere target;
        private Material targetMaterial;
        private Camera targetCamera;
        private Vector3[] areaCenters;
        private AreaTerrainData[] areaTerrainData;
        private ComputeBuffer areaTerrainBuffer;
        private Texture2DArray terrainAlbedoArray;
        private Texture2D terrainIdMap;
        private float[] terrainIdMapData;
        private RenderTexture indexTexture;
        private RenderTexture rvtAlbedoArray;
        private readonly List<RvtPage> pages = new();
        private readonly Queue<int> dirtyPages = new();
        private int indexKernel = -1;
        private int bakeKernel = -1;
        private bool initialized;
        private bool terrainMapDirty;
        private bool rvtReady;

        public bool IsInitialized => initialized;
        public bool IsRvtReady => rvtReady;

        public static SphericalRvtManager FindOrCreate(IcoSphere target) {
            SphericalRvtManager manager = target.GetComponent<SphericalRvtManager>();
            if (manager != null) {
                return manager;
            }

            manager = FindAnyObjectByType<SphericalRvtManager>();
            if (manager != null) {
                return manager;
            }

            return target.gameObject.AddComponent<SphericalRvtManager>();
        }

        public void Initialize(IcoSphere icoSphere, Material material, Camera cam) {
            target = icoSphere;
            targetMaterial = material;
            targetCamera = cam;

            ReleaseRuntimeResources();

#if UNITY_EDITOR
            LoadEditorDefaults();
#endif

            areaCenters = target.GetRawUnsortedAreas();
            if (areaCenters == null || areaCenters.Length == 0 || targetMaterial == null) {
                Debug.LogWarning("SphericalRvtManager: missing IcoSphere area data or material");
                return;
            }

            CreateAreaTerrainData();
            CreateTerrainAlbedoArray();
            BuildTerrainIdMap();
            CreateRvtTextures();
            BuildPages();
            BindStaticMaterialResources();

            initialized = true;
            rvtReady = false;
            terrainMapDirty = false;

            if (CanUseRvtCompute()) {
                indexKernel = indexCompute.FindKernel("Fill");
                bakeKernel = bakeCompute.FindKernel("Bake");
                FillIndexTexture();
                EnqueueAllPages();
            } else {
                Debug.LogWarning("SphericalRvtManager: compute resources unavailable; using direct terrain texture sampling fallback");
            }

            BindMaterialRuntimeState();
        }

        public void Tick(Camera cam) {
            if (!initialized) {
                return;
            }

            if (cam != null) {
                targetCamera = cam;
            }

            if (terrainMapDirty) {
                BuildTerrainIdMap();
                EnqueueAllPages();
                rvtReady = false;
                terrainMapDirty = false;
            }

            if (CanUseRvtCompute()) {
                int bakeCount = Mathf.Min(pagesToBakePerFrame, dirtyPages.Count);
                for (int i = 0; i < bakeCount; ++i) {
                    BakePage(dirtyPages.Dequeue());
                }

                if (dirtyPages.Count == 0) {
                    rvtReady = true;
                }
            }

            BindMaterialRuntimeState();
        }

        public TerrainType GetAreaTerrain(int areaId) {
            if (areaTerrainData == null || areaId < 0 || areaId >= areaTerrainData.Length) {
                return DEFAULT_TERRAIN;
            }

            return (TerrainType)Mathf.RoundToInt(areaTerrainData[areaId].info.x);
        }

        public bool SetAreaTerrain(int areaId, TerrainType terrainType) {
            if (areaTerrainData == null || areaTerrainBuffer == null || areaId < 0 || areaId >= areaTerrainData.Length) {
                return false;
            }

            areaTerrainData[areaId].info.x = (uint)terrainType;
            areaTerrainBuffer.SetData(areaTerrainData, areaId, areaId, 1);
            terrainMapDirty = true;
            return true;
        }

        public void SetAreaTerrains(IReadOnlyList<int> areaIds, TerrainType terrainType) {
            if (areaTerrainData == null || areaTerrainBuffer == null || areaIds == null) {
                return;
            }

            for (int i = 0; i < areaIds.Count; ++i) {
                int areaId = areaIds[i];
                if (areaId >= 0 && areaId < areaTerrainData.Length) {
                    areaTerrainData[areaId].info.x = (uint)terrainType;
                }
            }

            areaTerrainBuffer.SetData(areaTerrainData);
            terrainMapDirty = true;
        }

        private void CreateAreaTerrainData() {
            int n = areaCenters.Length;
            areaTerrainData = new AreaTerrainData[n];
            for (int i = 0; i < n; ++i) {
                TerrainType terrainType = PickDefaultTerrain(areaCenters[i], i);
                float repeat = Mathf.Lerp(18.0f, 46.0f, Misc.IntToRandom((uint)i, 313) / 255.0f);
                Vector2 offset = new(
                    Misc.IntToRandom((uint)i, 97) / 255.0f,
                    Misc.IntToRandom((uint)i, 173) / 255.0f
                );

                areaTerrainData[i] = new AreaTerrainData {
                    info = new Vector4((uint)terrainType, repeat, offset.x, offset.y),
                    tint = Vector4.one
                };
            }

            int stride = Marshal.SizeOf(typeof(AreaTerrainData));
            areaTerrainBuffer = ComputeBufManager.NewBuf(n, stride);
            if (areaTerrainBuffer == null) {
                Debug.LogWarning("SphericalRvtManager: failed to allocate area terrain buffer");
                return;
            }
            areaTerrainBuffer.SetData(areaTerrainData);
        }

        private TerrainType PickDefaultTerrain(Vector3 rawAreaCenter, int areaId) {
            float latAbs = Mathf.Abs(Mathf.Asin(Mathf.Clamp(rawAreaCenter.normalized.y, -1.0f, 1.0f)) / (Mathf.PI * 0.5f));
            uint moisture = Misc.IntToRandom((uint)areaId, 17);
            uint elevation = Misc.IntToRandom((uint)areaId, 211);
            uint river = Misc.IntToRandom((uint)areaId, 541);

            if (river < 8) {
                return TerrainType.River;
            }
            if (latAbs > 0.86f) {
                return TerrainType.Mountain;
            }
            if (moisture < 34) {
                return TerrainType.Sand;
            }
            if (moisture > 222) {
                return TerrainType.Marsh;
            }
            if (elevation > 226) {
                return TerrainType.Mountain;
            }
            if (elevation > 174) {
                return TerrainType.Hill;
            }
            if (moisture < 78) {
                return TerrainType.Dirt;
            }
            if (moisture > 202 && elevation < 72) {
                return TerrainType.Water;
            }
            return TerrainType.Plains;
        }

        private void CreateTerrainAlbedoArray() {
            terrainTextureSize = Mathf.Max(1, terrainTextureSize);
            terrainAlbedoArray = new Texture2DArray(
                terrainTextureSize,
                terrainTextureSize,
                TERRAIN_COUNT,
                TextureFormat.RGBA32,
                true,
                false
            ) {
                name = "SphericalRvtTerrainAlbedoArray",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };

            for (int i = 0; i < TERRAIN_COUNT; ++i) {
                Texture2D source = i < terrainAlbedoTextures.Length ? terrainAlbedoTextures[i] : null;
                using TextureCopy copy = TextureCopy.From(source, terrainTextureSize, DEFAULT_TERRAIN_COLORS[i]);
                terrainAlbedoArray.SetPixels32(copy.Texture.GetPixels32(), i, 0);
            }
            terrainAlbedoArray.Apply(true, false);
        }

        private void BuildTerrainIdMap() {
            int w = Mathf.Max(1, terrainIdMapWidth);
            int h = Mathf.Max(1, terrainIdMapHeight);
            int len = w * h;
            terrainIdMapData = new float[len];
            for (int i = 0; i < len; ++i) {
                terrainIdMapData[i] = -1.0f;
            }

            for (int i = 0; i < areaCenters.Length; ++i) {
                Vector2 uv = Misc.ToLonLatUv(areaCenters[i]);
                int x = Mathf.Clamp((int)(uv.x * w), 0, w - 1);
                int y = Mathf.Clamp((int)(uv.y * h), 0, h - 1);
                terrainIdMapData[y * w + x] = areaTerrainData[i].info.x;
            }

            DilateTerrainIdMap(w, h);
            UploadTerrainIdMap(w, h);
        }

        private void DilateTerrainIdMap(int w, int h) {
            float[] next = new float[terrainIdMapData.Length];
            for (int pass = 0; pass < terrainIdMapDilatePasses; ++pass) {
                Array.Copy(terrainIdMapData, next, terrainIdMapData.Length);
                int filled = 0;

                for (int y = 0; y < h; ++y) {
                    for (int x = 0; x < w; ++x) {
                        int idx = y * w + x;
                        if (terrainIdMapData[idx] >= 0.0f) {
                            continue;
                        }

                        float neighbor = FindNeighborTerrainId(x, y, w, h);
                        if (neighbor >= 0.0f) {
                            next[idx] = neighbor;
                            ++filled;
                        }
                    }
                }

                (terrainIdMapData, next) = (next, terrainIdMapData);
                if (filled == 0) {
                    break;
                }
            }

            float fallback = (uint)DEFAULT_TERRAIN;
            for (int i = 0; i < terrainIdMapData.Length; ++i) {
                if (terrainIdMapData[i] < 0.0f) {
                    terrainIdMapData[i] = fallback;
                }
            }
        }

        private float FindNeighborTerrainId(int x, int y, int w, int h) {
            for (int dy = -1; dy <= 1; ++dy) {
                int yy = Mathf.Clamp(y + dy, 0, h - 1);
                for (int dx = -1; dx <= 1; ++dx) {
                    if (dx == 0 && dy == 0) {
                        continue;
                    }

                    int xx = (x + dx + w) % w;
                    float value = terrainIdMapData[yy * w + xx];
                    if (value >= 0.0f) {
                        return value;
                    }
                }
            }
            return -1.0f;
        }

        private void UploadTerrainIdMap(int w, int h) {
            if (terrainIdMap != null) {
                DestroyUnityObject(terrainIdMap);
            }

            terrainIdMap = new Texture2D(w, h, TextureFormat.RFloat, false, true) {
                name = "SphericalRvtTerrainIdMap",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            terrainIdMap.SetPixelData(terrainIdMapData, 0);
            terrainIdMap.Apply(false, false);
        }

        private void CreateRvtTextures() {
            int pageCount = Mathf.Max(1, pageColumns * pageRows);
            int indexWidth = Mathf.Max(1, pageColumns * indexTexelsPerPage);
            int indexHeight = Mathf.Max(1, pageRows * indexTexelsPerPage);

            indexTexture = new RenderTexture(indexWidth, indexHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) {
                name = "SphericalRvtIndex",
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            indexTexture.Create();

            rvtAlbedoArray = new RenderTexture(tileSize, tileSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                name = "SphericalRvtAlbedoArray",
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = pageCount,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rvtAlbedoArray.Create();
        }

        private void BuildPages() {
            pages.Clear();
            float sizeX = 1.0f / pageColumns;
            float sizeY = 1.0f / pageRows;
            for (int y = 0; y < pageRows; ++y) {
                for (int x = 0; x < pageColumns; ++x) {
                    int slice = y * pageColumns + x;
                    pages.Add(new RvtPage {
                        slice = slice,
                        pageX = x,
                        pageY = y,
                        minUv = new Vector2(x * sizeX, y * sizeY),
                        sizeUv = new Vector2(sizeX, sizeY),
                        indexOffset = new Vector2Int(x * indexTexelsPerPage, y * indexTexelsPerPage),
                        indexSize = new Vector2Int(indexTexelsPerPage, indexTexelsPerPage)
                    });
                }
            }
        }

        private void FillIndexTexture() {
            if (!CanUseRvtCompute()) {
                return;
            }

            indexCompute.SetTexture(indexKernel, "_Result", indexTexture);
            foreach (RvtPage page in pages) {
                indexCompute.SetInts("_Offset", page.indexOffset.x, page.indexOffset.y);
                indexCompute.SetInts("_Size", page.indexSize.x, page.indexSize.y);
                indexCompute.SetVector("_Value", new Vector4(page.slice, page.minUv.x, page.minUv.y, page.sizeUv.x));
                int groupsX = Mathf.CeilToInt(page.indexSize.x / 8.0f);
                int groupsY = Mathf.CeilToInt(page.indexSize.y / 8.0f);
                indexCompute.Dispatch(indexKernel, groupsX, groupsY, 1);
            }
        }

        private void EnqueueAllPages() {
            dirtyPages.Clear();
            for (int i = 0; i < pages.Count; ++i) {
                dirtyPages.Enqueue(i);
            }
        }

        private void BakePage(int pageIndex) {
            if (!CanUseRvtCompute() || pageIndex < 0 || pageIndex >= pages.Count) {
                return;
            }

            RvtPage page = pages[pageIndex];
            bakeCompute.SetTexture(bakeKernel, "_TerrainAlbedoArray", terrainAlbedoArray);
            bakeCompute.SetTexture(bakeKernel, "_TerrainIdMap", terrainIdMap);
            bakeCompute.SetTexture(bakeKernel, "_RvtAlbedoArray", rvtAlbedoArray);
            bakeCompute.SetInt("_Slice", page.slice);
            bakeCompute.SetInt("_TileSize", tileSize);
            bakeCompute.SetInt("_TerrainTextureSize", terrainTextureSize);
            bakeCompute.SetInt("_TerrainTextureCount", TERRAIN_COUNT);
            bakeCompute.SetInts("_TerrainIdMapSize", terrainIdMap.width, terrainIdMap.height);
            bakeCompute.SetVector("_PageMin", new Vector4(page.minUv.x, page.minUv.y, 0.0f, 0.0f));
            bakeCompute.SetVector("_PageSize", new Vector4(page.sizeUv.x, page.sizeUv.y, 0.0f, 0.0f));
            bakeCompute.SetVector("_TerrainRepeat", new Vector4(terrainRepeat.x, terrainRepeat.y, 0.0f, 0.0f));
            int groups = Mathf.CeilToInt(tileSize / 8.0f);
            bakeCompute.Dispatch(bakeKernel, groups, groups, 1);
        }

        private void BindStaticMaterialResources() {
            if (targetMaterial == null) {
                return;
            }

            targetMaterial.SetBuffer("_AreaTerrainData", areaTerrainBuffer);
            targetMaterial.SetTexture("_TerrainAlbedoArray", terrainAlbedoArray);
            targetMaterial.SetInt("_TerrainTextureCount", TERRAIN_COUNT);
            targetMaterial.SetFloat("_TerrainDirectRepeat", 1.0f);
            targetMaterial.SetVector("_TerrainGlobalRepeat", new Vector4(terrainRepeat.x, terrainRepeat.y, 0.0f, 0.0f));

            if (indexTexture != null && rvtAlbedoArray != null) {
                targetMaterial.SetTexture("_SphericalRvtIndexTex", indexTexture);
                targetMaterial.SetTexture("_SphericalRvtAlbedoArray", rvtAlbedoArray);
                targetMaterial.SetFloat("_SphericalRvtPageSizeY", 1.0f / Mathf.Max(1, pageRows));
            }
        }

        private void BindMaterialRuntimeState() {
            if (targetMaterial == null) {
                return;
            }

            targetMaterial.SetFloat("_UseTerrainTextures", terrainAlbedoArray != null && areaTerrainBuffer != null ? 1.0f : 0.0f);
            targetMaterial.SetFloat("_UseSphericalRvt", rvtReady && CanUseRvtCompute() ? 1.0f : 0.0f);
        }

        private bool CanUseRvtCompute() {
            return SystemInfo.supportsComputeShaders &&
                   indexCompute != null &&
                   bakeCompute != null &&
                   terrainAlbedoArray != null &&
                   terrainIdMap != null &&
                   indexTexture != null &&
                   rvtAlbedoArray != null;
        }

        private void ReleaseRuntimeResources() {
            initialized = false;
            rvtReady = false;
            terrainMapDirty = false;
            dirtyPages.Clear();
            pages.Clear();

            if (areaTerrainBuffer != null) {
                ComputeBufManager.ScheduleRelease(areaTerrainBuffer);
                areaTerrainBuffer = null;
            }
            if (terrainAlbedoArray != null) {
                DestroyUnityObject(terrainAlbedoArray);
                terrainAlbedoArray = null;
            }
            if (terrainIdMap != null) {
                DestroyUnityObject(terrainIdMap);
                terrainIdMap = null;
            }
            if (indexTexture != null) {
                indexTexture.Release();
                DestroyUnityObject(indexTexture);
                indexTexture = null;
            }
            if (rvtAlbedoArray != null) {
                rvtAlbedoArray.Release();
                DestroyUnityObject(rvtAlbedoArray);
                rvtAlbedoArray = null;
            }
        }

        private void OnDestroy() {
            ReleaseRuntimeResources();
        }

#if UNITY_EDITOR
        private void LoadEditorDefaults() {
            if (indexCompute == null) {
                indexCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/IcoSphere/Shaders/SphericalRvtIndex.compute");
            }
            if (bakeCompute == null) {
                bakeCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/IcoSphere/Shaders/SphericalRvtBake.compute");
            }

            if (terrainAlbedoTextures == null || terrainAlbedoTextures.Length != TERRAIN_COUNT) {
                terrainAlbedoTextures = new Texture2D[TERRAIN_COUNT];
            }

            for (int i = 0; i < TERRAIN_COUNT; ++i) {
                if (terrainAlbedoTextures[i] == null) {
                    terrainAlbedoTextures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(DEFAULT_TERRAIN_ALBEDO_PATHS[i]);
                }
            }
        }
#endif

        private static void DestroyUnityObject(UnityEngine.Object obj) {
            if (obj == null) {
                return;
            }

            if (Application.isPlaying) {
                Destroy(obj);
            } else {
                DestroyImmediate(obj);
            }
        }

        private sealed class TextureCopy : IDisposable {
            public Texture2D Texture { get; }

            private TextureCopy(Texture2D texture) {
                Texture = texture;
            }

            public static TextureCopy From(Texture2D source, int size, Color fallbackColor) {
                Texture2D copy = new(size, size, TextureFormat.RGBA32, false, false) {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear
                };

                if (source == null) {
                    Color32 c = fallbackColor;
                    Color32[] pixels = new Color32[size * size];
                    for (int i = 0; i < pixels.Length; ++i) {
                        pixels[i] = c;
                    }
                    copy.SetPixels32(pixels);
                    copy.Apply(false, false);
                    return new TextureCopy(copy);
                }

                RenderTexture oldActive = RenderTexture.active;
                RenderTexture tmp = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source, tmp);
                RenderTexture.active = tmp;
                copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                copy.Apply(false, false);
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(tmp);
                return new TextureCopy(copy);
            }

            public void Dispose() {
                DestroyUnityObject(Texture);
            }
        }
    }
}
