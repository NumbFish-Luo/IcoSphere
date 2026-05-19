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
            public Vector4 info; // x: terrain id, y: local uv scale, zw: local uv offset
            public Vector4 tint;
            public Vector4 center; // xyz: world center, w: approximate radius
            public Vector4 tangent;
            public Vector4 bitangent;
        }

        private sealed class SphericalRvtPage {
            public int pageId;
            public int pageX;
            public int pageY;
            public int physicalSlice = -1;
            public Vector2 minUv;
            public Vector2 sizeUv;
            public Vector2Int indexOffset;
            public Vector2Int indexSize;
            public bool dirty;
            public bool ready;
            public bool queued;
            public int lastUsedFrame;
        }

        private readonly struct PageCandidate {
            public readonly int PageId;
            public readonly float Score;

            public PageCandidate(int pageId, float score) {
                PageId = pageId;
                Score = score;
            }
        }

        private const int TERRAIN_COUNT = 8;
        private const TerrainType DEFAULT_TERRAIN = TerrainType.Plains;
        private static readonly string[] DEFAULT_TERRAIN_ALBEDO_PATHS = {
            null,
            "Assets/IcoSphere/Textures/Terrain/Sand1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Plains1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Mountain1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Marsh1_d.png",
            "Assets/IcoSphere/Textures/Terrain/Hill3_d.png",
            "Assets/IcoSphere/Textures/Terrain/Dirt1_d.png",
            "Assets/IcoSphere/Textures/Terrain/River1_d.png"
        };

        private static readonly string[] DEFAULT_TERRAIN_HEIGHT_PATHS = {
            "Assets/IcoSphere/Textures/Terrain/Water_h.png",
            "Assets/IcoSphere/Textures/Terrain/Common_h.png",
            "Assets/IcoSphere/Textures/Terrain/Plains1_h.png",
            "Assets/IcoSphere/Textures/Terrain/Mountain1_h.png",
            "Assets/IcoSphere/Textures/Terrain/Marsh1_h.png",
            "Assets/IcoSphere/Textures/Terrain/Hill3_h.png",
            "Assets/IcoSphere/Textures/Terrain/Dirt1_h.png",
            "Assets/IcoSphere/Textures/Terrain/River1_h.png"
        };

        private static readonly string[] DEFAULT_TERRAIN_MASK_PATHS = {
            "Assets/IcoSphere/Textures/Terrain/Water_m.png",
            null,
            "Assets/IcoSphere/Textures/Terrain/Plains1_m.png",
            "Assets/IcoSphere/Textures/Terrain/Mountain1_m.png",
            "Assets/IcoSphere/Textures/Terrain/Marsh1_m.png",
            "Assets/IcoSphere/Textures/Terrain/Hill3_m.png",
            "Assets/IcoSphere/Textures/Terrain/Dirt1_m.png",
            "Assets/IcoSphere/Textures/Terrain/River1_m.png"
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

        [Header("Terrain Layer Data")]
        [SerializeField] private Texture2D[] terrainAlbedoTextures = new Texture2D[TERRAIN_COUNT];
        [SerializeField] private Texture2D[] terrainHeightTextures = new Texture2D[TERRAIN_COUNT];
        [SerializeField] private Texture2D[] terrainMaskTextures = new Texture2D[TERRAIN_COUNT];
        [SerializeField, Range(128, 2048)] private int terrainTextureSize = 1024;
        [SerializeField, Range(0.0f, 0.25f)] private float terrainHeightShadeStrength = 0.08f;
        [SerializeField, Range(0.0f, 1.0f)] private float terrainMaskShadeStrength = 0.55f;
        [SerializeField] private Vector2 terrainRepeat = new(18.0f, 9.0f);
        [SerializeField] private Vector2 perAreaTextureRepeatRange = new(0.75f, 1.15f);

        [Header("Spherical RVT")]
        [SerializeField] private ComputeShader indexCompute;
        [SerializeField] private ComputeShader bakeCompute;
        [SerializeField] private bool enableSphericalRvtSampling = true;
        [SerializeField, Range(64, 512)] private int tileSize = 256;
        [SerializeField, Range(4, 128)] private int pageColumns = 32;
        [SerializeField, Range(2, 64)] private int pageRows = 16;
        [SerializeField, Range(4, 256)] private int physicalTileCount = 64;
        [SerializeField, Range(2, 64)] private int indexTexelsPerPage = 8;
        [SerializeField, Range(0, 16)] private int activePageRadiusX = 3;
        [SerializeField, Range(0, 16)] private int activePageRadiusY = 2;
        [SerializeField, Range(1, 64)] private int pagesToBakePerFrame = 8;

        [Header("Terrain Id Map")]
        [SerializeField, Range(128, 4096)] private int terrainIdMapWidth = 1024;
        [SerializeField, Range(64, 2048)] private int terrainIdMapHeight = 512;

        private IcoSphere target;
        private Material targetMaterial;
        private Camera targetCamera;
        private Vector3[] areaCenters;
        private AreaTerrainData[] areaTerrainData;
        private ComputeBuffer areaTerrainBuffer;
        private Texture2DArray terrainAlbedoArray;
        private Texture2DArray terrainHeightArray;
        private Texture2DArray terrainMaskArray;
        private Texture2D terrainIdMap;
        private float[] terrainIdMapData;
        private RenderTexture indexTexture;
        private RenderTexture rvtAlbedoArray;
        private int[] terrainAreaIdMapData;
        private SphericalRvtPage[] pageTable;
        private int[] physicalSlicePageIds;
        private readonly Queue<int> freePhysicalSlices = new();
        private readonly Queue<int> dirtyPages = new();
        private readonly List<PageCandidate> pageCandidates = new();
        private readonly List<int> wantedPageIds = new();
        private readonly HashSet<int> wantedPageSet = new();
        private int indexKernel = -1;
        private int bakeKernel = -1;
        private int frameIndex;
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
            frameIndex = 0;

            ReleaseRuntimeResources();

#if UNITY_EDITOR
            LoadEditorDefaults();
#endif

            areaCenters = target.GetRawUnsortedAreas();
            if (areaCenters == null || areaCenters.Length == 0 || targetMaterial == null) {
                Debug.LogWarning("SphericalRvtManager: missing IcoSphere area data or material");
                return;
            }

            CreateTerrainTextureArrays();
            CreateAreaTerrainData();
            BuildTerrainIdMap();
            CreateRvtTextures();
            BuildVirtualPages();
            BindStaticMaterialResources();

            initialized = true;
            terrainMapDirty = false;
            rvtReady = false;

            if (CanUseRvtCompute()) {
                indexKernel = indexCompute.FindKernel("Fill");
                bakeKernel = bakeCompute.FindKernel("Bake");
                ClearIndexTexture();
                UpdateWorkingSet();
                BakeQueuedPages(pagesToBakePerFrame);
                RefreshRvtReadyFlag();
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

            ++frameIndex;

            if (terrainMapDirty) {
                BuildTerrainIdMap();
                MarkResidentPagesDirty();
                terrainMapDirty = false;
            }

            if (CanUseRvtCompute()) {
                UpdateWorkingSet();
                BakeQueuedPages(pagesToBakePerFrame);
            }

            RefreshRvtReadyFlag();
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

            areaTerrainData[areaId].info.x = Mathf.Clamp((int)terrainType, 0, TERRAIN_COUNT - 1);
            areaTerrainBuffer.SetData(areaTerrainData, areaId, areaId, 1);
            terrainMapDirty = true;
            return true;
        }

        public void SetAreaTerrains(IReadOnlyList<int> areaIds, TerrainType terrainType) {
            if (areaTerrainData == null || areaTerrainBuffer == null || areaIds == null) {
                return;
            }

            float terrainId = Mathf.Clamp((int)terrainType, 0, TERRAIN_COUNT - 1);
            for (int i = 0; i < areaIds.Count; ++i) {
                int areaId = areaIds[i];
                if (areaId >= 0 && areaId < areaTerrainData.Length) {
                    areaTerrainData[areaId].info.x = terrainId;
                }
            }

            areaTerrainBuffer.SetData(areaTerrainData);
            terrainMapDirty = true;
        }

        private void CreateAreaTerrainData() {
            int n = areaCenters.Length;
            areaTerrainData = new AreaTerrainData[n];
            for (int i = 0; i < n; ++i) {
                Vector3 normal = areaCenters[i].normalized;
                Vector3 center = areaCenters[i] * target.SphereRadius;
                BuildSurfaceBasis(normal, i, out Vector3 tangent, out Vector3 bitangent);

                TerrainType terrainType = PickDefaultTerrain(areaCenters[i], i);
                float repeatMin = Mathf.Max(0.05f, Mathf.Min(perAreaTextureRepeatRange.x, perAreaTextureRepeatRange.y));
                float repeatMax = Mathf.Max(repeatMin, Mathf.Max(perAreaTextureRepeatRange.x, perAreaTextureRepeatRange.y));
                float repeat = Mathf.Lerp(repeatMin, repeatMax, Misc.IntToRandom((uint)i, 313) / 255.0f);
                float areaRadius = EstimateAreaRadius(i, center);
                float uvScale = repeat / Mathf.Max(areaRadius * 2.0f, 0.0001f);

                areaTerrainData[i] = new AreaTerrainData {
                    info = new Vector4((float)terrainType, uvScale, 0.5f, 0.5f),
                    tint = Vector4.one,
                    center = new Vector4(center.x, center.y, center.z, areaRadius),
                    tangent = new Vector4(tangent.x, tangent.y, tangent.z, 0.0f),
                    bitangent = new Vector4(bitangent.x, bitangent.y, bitangent.z, 0.0f)
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
            float moisture = Misc.IntToRandom((uint)areaId, 17) / 255.0f;
            float elevation = Misc.IntToRandom((uint)areaId, 211) / 255.0f;
            uint river = Misc.IntToRandom((uint)areaId, 541);

            if (river < 3 && moisture > 0.58f && latAbs < 0.78f) {
                return TerrainType.River;
            }
            if (latAbs > 0.90f && elevation > 0.62f) {
                return TerrainType.Mountain;
            }
            if (latAbs < 0.38f && moisture < 0.09f) {
                return TerrainType.Sand;
            }
            if (moisture > 0.96f && elevation < 0.34f) {
                return TerrainType.Water;
            }
            if (moisture > 0.92f && elevation < 0.45f) {
                return TerrainType.Marsh;
            }
            if (elevation > 0.96f) {
                return TerrainType.Mountain;
            }
            if (elevation > 0.88f) {
                return TerrainType.Hill;
            }
            if (moisture < 0.055f) {
                return TerrainType.Dirt;
            }
            return TerrainType.Plains;
        }

        private float EstimateAreaRadius(int areaId, Vector3 center) {
            float sum = 0.0f;
            int count = 0;
            for (int i = 0; i < 6; ++i) {
                int neighborId = target.GetNeighborId(areaId, i);
                if (neighborId < 0 || neighborId >= areaCenters.Length || neighborId == areaId) {
                    continue;
                }

                float distance = Vector3.Distance(center, target.GetAreaCenter(neighborId));
                if (distance > 0.00001f) {
                    sum += distance;
                    ++count;
                }
            }

            float neighborDistance = count > 0 ? sum / count : target.SphereRadius * 0.1f;
            return Mathf.Max(neighborDistance * 0.58f, target.SphereRadius * 0.0001f);
        }

        private static void BuildSurfaceBasis(Vector3 normal, int areaId, out Vector3 tangent, out Vector3 bitangent) {
            if (normal.sqrMagnitude < 0.000001f) {
                normal = Vector3.up;
            } else {
                normal.Normalize();
            }

            Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
            tangent = Vector3.Cross(reference, normal);
            if (tangent.sqrMagnitude < 0.000001f) {
                tangent = Vector3.Cross(Vector3.right, normal);
            }
            tangent.Normalize();
            bitangent = Vector3.Cross(normal, tangent).normalized;

            float angle = Misc.IntToRandom((uint)areaId, 719) / 255.0f * 360.0f;
            Quaternion rotation = Quaternion.AngleAxis(angle, normal);
            tangent = rotation * tangent;
            bitangent = rotation * bitangent;
        }

        private void CreateTerrainTextureArrays() {
            terrainTextureSize = Mathf.Max(1, terrainTextureSize);
            terrainAlbedoArray = CreateTerrainTextureArray(
                "SphericalRvtTerrainAlbedoArray",
                terrainAlbedoTextures,
                i => DEFAULT_TERRAIN_COLORS[i],
                false
            );
            terrainHeightArray = CreateTerrainTextureArray(
                "SphericalRvtTerrainHeightArray",
                terrainHeightTextures,
                _ => new Color(0.5f, 0.5f, 0.5f, 1.0f),
                true
            );
            terrainMaskArray = CreateTerrainTextureArray(
                "SphericalRvtTerrainMaskArray",
                terrainMaskTextures,
                _ => Color.white,
                true
            );
        }

        private Texture2DArray CreateTerrainTextureArray(
            string textureName,
            Texture2D[] sourceTextures,
            Func<int, Color> fallbackColor,
            bool linear) {
            Texture2DArray textureArray = new(
                terrainTextureSize,
                terrainTextureSize,
                TERRAIN_COUNT,
                TextureFormat.RGBA32,
                true,
                linear
            ) {
                name = textureName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };

            for (int i = 0; i < TERRAIN_COUNT; ++i) {
                Texture2D source = sourceTextures != null && i < sourceTextures.Length ? sourceTextures[i] : null;
                using TextureCopy copy = TextureCopy.From(source, terrainTextureSize, fallbackColor(i), linear);
                textureArray.SetPixels32(copy.Texture.GetPixels32(), i, 0);
            }
            textureArray.Apply(true, false);
            return textureArray;
        }

        private void BuildTerrainIdMap() {
            int w = Mathf.Max(1, terrainIdMapWidth);
            int h = Mathf.Max(1, terrainIdMapHeight);
            int len = w * h;

            if (terrainAreaIdMapData == null || terrainAreaIdMapData.Length != len) {
                terrainAreaIdMapData = BuildTerrainAreaIdMap(w, h);
            }

            terrainIdMapData = new float[len];
            for (int i = 0; i < len; ++i) {
                int areaId = terrainAreaIdMapData != null && i < terrainAreaIdMapData.Length ? terrainAreaIdMapData[i] : -1;
                terrainIdMapData[i] = areaId >= 0 && areaId < areaTerrainData.Length
                    ? areaTerrainData[areaId].info.x
                    : (float)DEFAULT_TERRAIN;
            }

            UploadTerrainIdMap(w, h);
        }

        private int[] BuildTerrainAreaIdMap(int w, int h) {
            int len = w * h;
            int[] areaIdMap = new int[len];
            if (areaCenters == null || areaCenters.Length == 0) {
                Array.Fill(areaIdMap, -1);
                return areaIdMap;
            }

            int binCols = Mathf.Clamp(w / 2, 32, 1024);
            int binRows = Mathf.Clamp(h / 2, 16, 512);
            BuildAreaCenterBins(binCols, binRows, out int[] binHeads, out int[] binNext);

            for (int y = 0; y < h; ++y) {
                float v = (y + 0.5f) / h;
                float lat = (v - 0.5f) * Mathf.PI;
                float cosLat = Mathf.Abs(Mathf.Cos(lat));
                int radiusX = cosLat < 0.05f
                    ? binCols / 2
                    : Mathf.Clamp(Mathf.CeilToInt(2.0f / Mathf.Max(cosLat, 0.12f)), 2, 32);
                int radiusY = cosLat < 0.05f ? 3 : 2;

                for (int x = 0; x < w; ++x) {
                    float u = (x + 0.5f) / w;
                    Vector3 direction = LonLatUvToDirection(u, v);
                    areaIdMap[y * w + x] = FindNearestAreaIdInBins(
                        direction,
                        u,
                        v,
                        binCols,
                        binRows,
                        binHeads,
                        binNext,
                        radiusX,
                        radiusY
                    );
                }
            }

            return areaIdMap;
        }

        private void BuildAreaCenterBins(int binCols, int binRows, out int[] binHeads, out int[] binNext) {
            int binCount = binCols * binRows;
            binHeads = new int[binCount];
            binNext = new int[areaCenters.Length];
            Array.Fill(binHeads, -1);
            Array.Fill(binNext, -1);

            for (int i = 0; i < areaCenters.Length; ++i) {
                Vector2 uv = WrapLonLatUv(Misc.ToLonLatUv(areaCenters[i]));
                int bx = Mathf.Clamp((int)(uv.x * binCols), 0, binCols - 1);
                int by = Mathf.Clamp((int)(uv.y * binRows), 0, binRows - 1);
                int bin = by * binCols + bx;
                binNext[i] = binHeads[bin];
                binHeads[bin] = i;
            }
        }

        private int FindNearestAreaIdInBins(
            Vector3 direction,
            float u,
            float v,
            int binCols,
            int binRows,
            int[] binHeads,
            int[] binNext,
            int radiusX,
            int radiusY) {
            int centerX = Mathf.Clamp((int)(u * binCols), 0, binCols - 1);
            int centerY = Mathf.Clamp((int)(v * binRows), 0, binRows - 1);
            int bestAreaId = -1;
            float bestDot = -2.0f;

            int minY = Mathf.Max(0, centerY - radiusY);
            int maxY = Mathf.Min(binRows - 1, centerY + radiusY);
            bool scanAllX = radiusX >= binCols / 2;
            int minDx = scanAllX ? 0 : -radiusX;
            int maxDx = scanAllX ? binCols - 1 : radiusX;

            for (int by = minY; by <= maxY; ++by) {
                for (int dx = minDx; dx <= maxDx; ++dx) {
                    int bx = scanAllX ? dx : Mod(centerX + dx, binCols);
                    int areaId = binHeads[by * binCols + bx];
                    while (areaId >= 0) {
                        float d = Vector3.Dot(direction, areaCenters[areaId]);
                        if (d > bestDot) {
                            bestDot = d;
                            bestAreaId = areaId;
                        }
                        areaId = binNext[areaId];
                    }
                }
            }

            return bestAreaId;
        }

        private void UploadTerrainIdMap(int w, int h) {
            if (terrainIdMap != null) {
                DestroyUnityObject(terrainIdMap);
            }

            terrainIdMap = new Texture2D(w, h, TextureFormat.RFloat, false, true) {
                name = "SphericalRvtTerrainIdMap",
                filterMode = FilterMode.Point
            };
            terrainIdMap.wrapModeU = TextureWrapMode.Repeat;
            terrainIdMap.wrapModeV = TextureWrapMode.Clamp;
            terrainIdMap.SetPixelData(terrainIdMapData, 0);
            terrainIdMap.Apply(false, false);
        }

        private void CreateRvtTextures() {
            pageColumns = Mathf.Max(1, pageColumns);
            pageRows = Mathf.Max(1, pageRows);
            int virtualPageCount = pageColumns * pageRows;
            physicalTileCount = Mathf.Clamp(physicalTileCount, 1, virtualPageCount);
            tileSize = Mathf.Max(1, tileSize);
            indexTexelsPerPage = Mathf.Max(1, indexTexelsPerPage);

            int indexWidth = Mathf.Max(1, pageColumns * indexTexelsPerPage);
            int indexHeight = Mathf.Max(1, pageRows * indexTexelsPerPage);

            indexTexture = new RenderTexture(indexWidth, indexHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) {
                name = "SphericalRvtPageTable",
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            indexTexture.Create();

            rvtAlbedoArray = new RenderTexture(tileSize, tileSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                name = "SphericalRvtAlbedoCache",
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = physicalTileCount,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            rvtAlbedoArray.Create();

            physicalSlicePageIds = new int[physicalTileCount];
            freePhysicalSlices.Clear();
            for (int i = 0; i < physicalSlicePageIds.Length; ++i) {
                physicalSlicePageIds[i] = -1;
                freePhysicalSlices.Enqueue(i);
            }
        }

        private void BuildVirtualPages() {
            int pageCount = pageColumns * pageRows;
            pageTable = new SphericalRvtPage[pageCount];
            float sizeX = 1.0f / pageColumns;
            float sizeY = 1.0f / pageRows;

            for (int y = 0; y < pageRows; ++y) {
                for (int x = 0; x < pageColumns; ++x) {
                    int pageId = y * pageColumns + x;
                    pageTable[pageId] = new SphericalRvtPage {
                        pageId = pageId,
                        pageX = x,
                        pageY = y,
                        minUv = new Vector2(x * sizeX, y * sizeY),
                        sizeUv = new Vector2(sizeX, sizeY),
                        indexOffset = new Vector2Int(x * indexTexelsPerPage, y * indexTexelsPerPage),
                        indexSize = new Vector2Int(indexTexelsPerPage, indexTexelsPerPage)
                    };
                }
            }
        }

        private void ClearIndexTexture() {
            if (!CanUseRvtCompute() || pageTable == null) {
                return;
            }

            for (int i = 0; i < pageTable.Length; ++i) {
                WriteIndexPage(pageTable[i], false);
            }
        }

        private void UpdateWorkingSet() {
            if (!CanUseRvtCompute() || pageTable == null || pageTable.Length == 0) {
                return;
            }

            SelectWantedPages(GetCameraVirtualUv());
            for (int i = 0; i < wantedPageIds.Count; ++i) {
                EnsurePageResident(wantedPageIds[i]);
            }
        }

        private Vector2 GetCameraVirtualUv() {
            Vector3 direction = Vector3.forward;
            if (targetCamera != null) {
                Vector3 cameraPos = targetCamera.transform.position;
                direction = cameraPos.sqrMagnitude > 0.000001f ? cameraPos.normalized : targetCamera.transform.forward;
            }

            return WrapLonLatUv(Misc.ToLonLatUv(direction));
        }

        private void SelectWantedPages(Vector2 centerUv) {
            pageCandidates.Clear();
            wantedPageIds.Clear();
            wantedPageSet.Clear();

            int centerX = Mathf.Clamp((int)(centerUv.x * pageColumns), 0, pageColumns - 1);
            int centerY = Mathf.Clamp((int)(centerUv.y * pageRows), 0, pageRows - 1);
            int radiusX = Mathf.Clamp(activePageRadiusX, 0, Mathf.Max(0, pageColumns / 2));
            int radiusY = Mathf.Clamp(activePageRadiusY, 0, Mathf.Max(0, pageRows - 1));

            for (int dy = -radiusY; dy <= radiusY; ++dy) {
                int y = centerY + dy;
                if (y < 0 || y >= pageRows) {
                    continue;
                }

                for (int dx = -radiusX; dx <= radiusX; ++dx) {
                    int x = Mod(centerX + dx, pageColumns);
                    int pageId = y * pageColumns + x;
                    int wrappedDx = WrappedPageDistance(x, centerX, pageColumns);
                    float nx = radiusX > 0 ? wrappedDx / (float)radiusX : 0.0f;
                    float ny = radiusY > 0 ? Mathf.Abs(dy) / (float)radiusY : 0.0f;
                    pageCandidates.Add(new PageCandidate(pageId, nx * nx + ny * ny));
                }
            }

            pageCandidates.Sort((a, b) => a.Score.CompareTo(b.Score));
            int limit = Mathf.Min(physicalTileCount, pageCandidates.Count);
            for (int i = 0; i < limit; ++i) {
                int pageId = pageCandidates[i].PageId;
                if (wantedPageSet.Add(pageId)) {
                    wantedPageIds.Add(pageId);
                }
            }
        }

        private void EnsurePageResident(int pageId) {
            if (pageId < 0 || pageId >= pageTable.Length) {
                return;
            }

            SphericalRvtPage page = pageTable[pageId];
            page.lastUsedFrame = frameIndex;
            if (page.physicalSlice >= 0) {
                return;
            }

            int physicalSlice = AllocatePhysicalSlice();
            if (physicalSlice < 0) {
                return;
            }

            page.physicalSlice = physicalSlice;
            page.ready = false;
            page.dirty = false;
            page.queued = false;
            physicalSlicePageIds[physicalSlice] = pageId;
            MarkPageDirty(page);
        }

        private int AllocatePhysicalSlice() {
            while (freePhysicalSlices.Count > 0) {
                int slice = freePhysicalSlices.Dequeue();
                if (slice >= 0 && slice < physicalSlicePageIds.Length && physicalSlicePageIds[slice] < 0) {
                    return slice;
                }
            }

            int evictPageId = FindEvictionCandidate();
            if (evictPageId < 0) {
                return -1;
            }

            SphericalRvtPage evictPage = pageTable[evictPageId];
            int evictedSlice = evictPage.physicalSlice;
            UnmapPage(evictPage);
            return evictedSlice;
        }

        private int FindEvictionCandidate() {
            int bestPageId = -1;
            int bestFrame = int.MaxValue;
            for (int i = 0; i < pageTable.Length; ++i) {
                SphericalRvtPage page = pageTable[i];
                if (page.physicalSlice < 0 || wantedPageSet.Contains(page.pageId)) {
                    continue;
                }

                if (page.lastUsedFrame < bestFrame) {
                    bestFrame = page.lastUsedFrame;
                    bestPageId = page.pageId;
                }
            }

            if (bestPageId >= 0) {
                return bestPageId;
            }

            for (int i = 0; i < pageTable.Length; ++i) {
                SphericalRvtPage page = pageTable[i];
                if (page.physicalSlice < 0) {
                    continue;
                }

                if (page.lastUsedFrame < bestFrame) {
                    bestFrame = page.lastUsedFrame;
                    bestPageId = page.pageId;
                }
            }

            return bestPageId;
        }

        private void UnmapPage(SphericalRvtPage page) {
            if (page.physicalSlice >= 0 && page.physicalSlice < physicalSlicePageIds.Length) {
                physicalSlicePageIds[page.physicalSlice] = -1;
            }

            WriteIndexPage(page, false);
            page.physicalSlice = -1;
            page.ready = false;
            page.dirty = false;
            page.queued = false;
        }

        private void MarkResidentPagesDirty() {
            if (pageTable == null) {
                return;
            }

            for (int i = 0; i < pageTable.Length; ++i) {
                SphericalRvtPage page = pageTable[i];
                if (page.physicalSlice >= 0) {
                    MarkPageDirty(page);
                }
            }
        }

        private void MarkPageDirty(SphericalRvtPage page) {
            if (page.physicalSlice < 0) {
                return;
            }

            page.dirty = true;
            page.ready = false;
            WriteIndexPage(page, false);
            if (!page.queued) {
                dirtyPages.Enqueue(page.pageId);
                page.queued = true;
            }
        }

        private void BakeQueuedPages(int maxPages) {
            if (!CanUseRvtCompute() || maxPages <= 0) {
                return;
            }

            int bakeCount = Mathf.Min(maxPages, dirtyPages.Count);
            for (int i = 0; i < bakeCount; ++i) {
                int pageId = dirtyPages.Dequeue();
                if (pageId < 0 || pageId >= pageTable.Length) {
                    continue;
                }

                SphericalRvtPage page = pageTable[pageId];
                page.queued = false;
                if (page.physicalSlice < 0 || !page.dirty) {
                    continue;
                }

                BakePage(page);
                page.dirty = false;
                page.ready = true;
                page.lastUsedFrame = frameIndex;
                WriteIndexPage(page, true);
            }
        }

        private void WriteIndexPage(SphericalRvtPage page, bool active) {
            if (!CanUseRvtCompute() || page == null || indexKernel < 0) {
                return;
            }

            float slice = active && page.physicalSlice >= 0 ? page.physicalSlice : -1.0f;
            indexCompute.SetTexture(indexKernel, "_Result", indexTexture);
            indexCompute.SetInts("_Offset", page.indexOffset.x, page.indexOffset.y);
            indexCompute.SetInts("_Size", page.indexSize.x, page.indexSize.y);
            indexCompute.SetVector("_Value", new Vector4(slice, page.minUv.x, page.minUv.y, page.sizeUv.x));
            int groupsX = Mathf.CeilToInt(page.indexSize.x / 8.0f);
            int groupsY = Mathf.CeilToInt(page.indexSize.y / 8.0f);
            indexCompute.Dispatch(indexKernel, groupsX, groupsY, 1);
        }

        private void BakePage(SphericalRvtPage page) {
            bakeCompute.SetTexture(bakeKernel, "_TerrainAlbedoArray", terrainAlbedoArray);
            bakeCompute.SetTexture(bakeKernel, "_TerrainHeightArray", terrainHeightArray);
            bakeCompute.SetTexture(bakeKernel, "_TerrainMaskArray", terrainMaskArray);
            bakeCompute.SetTexture(bakeKernel, "_TerrainIdMap", terrainIdMap);
            bakeCompute.SetTexture(bakeKernel, "_RvtAlbedoArray", rvtAlbedoArray);
            bakeCompute.SetInt("_PhysicalSlice", page.physicalSlice);
            bakeCompute.SetInt("_TileSize", tileSize);
            bakeCompute.SetInt("_TerrainTextureSize", terrainTextureSize);
            bakeCompute.SetInt("_TerrainTextureCount", TERRAIN_COUNT);
            bakeCompute.SetInts("_TerrainIdMapSize", terrainIdMap.width, terrainIdMap.height);
            bakeCompute.SetVector("_PageMin", new Vector4(page.minUv.x, page.minUv.y, 0.0f, 0.0f));
            bakeCompute.SetVector("_PageSize", new Vector4(page.sizeUv.x, page.sizeUv.y, 0.0f, 0.0f));
            bakeCompute.SetVector("_TerrainRepeat", new Vector4(terrainRepeat.x, terrainRepeat.y, 0.0f, 0.0f));
            bakeCompute.SetFloat("_TerrainHeightShadeStrength", terrainHeightShadeStrength);
            bakeCompute.SetFloat("_TerrainMaskShadeStrength", terrainMaskShadeStrength);
            int groups = Mathf.CeilToInt(tileSize / 8.0f);
            bakeCompute.Dispatch(bakeKernel, groups, groups, 1);
        }

        private void BindStaticMaterialResources() {
            if (targetMaterial == null) {
                return;
            }

            targetMaterial.SetBuffer("_AreaTerrainData", areaTerrainBuffer);
            targetMaterial.SetTexture("_TerrainAlbedoArray", terrainAlbedoArray);
            targetMaterial.SetTexture("_TerrainHeightArray", terrainHeightArray);
            targetMaterial.SetTexture("_TerrainMaskArray", terrainMaskArray);
            targetMaterial.SetInt("_TerrainTextureCount", TERRAIN_COUNT);
            targetMaterial.SetFloat("_TerrainHeightShadeStrength", terrainHeightShadeStrength);
            targetMaterial.SetFloat("_TerrainMaskShadeStrength", terrainMaskShadeStrength);
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

            bool hasDirectTerrainFallback = HasTerrainTextureArrays() && areaTerrainBuffer != null;
            bool canSampleRvt = enableSphericalRvtSampling && CanUseRvtCompute();
            targetMaterial.SetFloat("_UseTerrainTextures", hasDirectTerrainFallback ? 1.0f : 0.0f);
            targetMaterial.SetFloat("_UseSphericalRvt", canSampleRvt ? 1.0f : 0.0f);
        }

        private bool HasTerrainTextureArrays() {
            return terrainAlbedoArray != null &&
                   terrainHeightArray != null &&
                   terrainMaskArray != null;
        }

        private bool CanUseRvtCompute() {
            return SystemInfo.supportsComputeShaders &&
                   indexCompute != null &&
                   bakeCompute != null &&
                   HasTerrainTextureArrays() &&
                   terrainIdMap != null &&
                   indexTexture != null &&
                   rvtAlbedoArray != null;
        }

        private void RefreshRvtReadyFlag() {
            rvtReady = CanUseRvtCompute() && dirtyPages.Count == 0 && HasReadyPage();
        }

        private bool HasReadyPage() {
            if (pageTable == null) {
                return false;
            }

            for (int i = 0; i < pageTable.Length; ++i) {
                if (pageTable[i].ready) {
                    return true;
                }
            }
            return false;
        }

        private void ReleaseRuntimeResources() {
            initialized = false;
            rvtReady = false;
            terrainMapDirty = false;
            dirtyPages.Clear();
            freePhysicalSlices.Clear();
            pageCandidates.Clear();
            wantedPageIds.Clear();
            wantedPageSet.Clear();
            pageTable = null;
            physicalSlicePageIds = null;
            indexKernel = -1;
            bakeKernel = -1;

            if (areaTerrainBuffer != null) {
                ComputeBufManager.ScheduleRelease(areaTerrainBuffer);
                areaTerrainBuffer = null;
            }
            if (terrainAlbedoArray != null) {
                DestroyUnityObject(terrainAlbedoArray);
                terrainAlbedoArray = null;
            }
            if (terrainHeightArray != null) {
                DestroyUnityObject(terrainHeightArray);
                terrainHeightArray = null;
            }
            if (terrainMaskArray != null) {
                DestroyUnityObject(terrainMaskArray);
                terrainMaskArray = null;
            }
            if (terrainIdMap != null) {
                DestroyUnityObject(terrainIdMap);
                terrainIdMap = null;
            }
            terrainAreaIdMapData = null;
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
            if (terrainHeightTextures == null || terrainHeightTextures.Length != TERRAIN_COUNT) {
                terrainHeightTextures = new Texture2D[TERRAIN_COUNT];
            }
            if (terrainMaskTextures == null || terrainMaskTextures.Length != TERRAIN_COUNT) {
                terrainMaskTextures = new Texture2D[TERRAIN_COUNT];
            }

            for (int i = 0; i < TERRAIN_COUNT; ++i) {
                LoadDefaultTerrainTexture(ref terrainAlbedoTextures[i], DEFAULT_TERRAIN_ALBEDO_PATHS[i], "_d.png");
                LoadDefaultTerrainTexture(ref terrainHeightTextures[i], DEFAULT_TERRAIN_HEIGHT_PATHS[i], "_h.png");
                LoadDefaultTerrainTexture(ref terrainMaskTextures[i], DEFAULT_TERRAIN_MASK_PATHS[i], "_m.png");
            }
        }

        private static void LoadDefaultTerrainTexture(ref Texture2D slot, string assetPath, string expectedSuffix) {
            string currentPath = slot != null ? AssetDatabase.GetAssetPath(slot) : null;
            bool currentIsWrongKind = !string.IsNullOrEmpty(currentPath) &&
                                      !currentPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase);

            if (slot == null || currentIsWrongKind) {
                slot = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
        }
#endif

        private static Vector2 WrapLonLatUv(Vector2 uv) {
            return new Vector2(Mathf.Repeat(uv.x, 1.0f), Mathf.Clamp01(uv.y));
        }

        private static Vector3 LonLatUvToDirection(float u, float v) {
            float lon = (u * 2.0f - 1.0f) * Mathf.PI;
            float lat = (v - 0.5f) * Mathf.PI;
            float cosLat = Mathf.Cos(lat);
            return new Vector3(Mathf.Cos(lon) * cosLat, Mathf.Sin(lat), Mathf.Sin(lon) * cosLat);
        }

        private static int Mod(int value, int modulus) {
            if (modulus <= 0) {
                return 0;
            }

            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static int WrappedPageDistance(int a, int b, int count) {
            int d = Mathf.Abs(a - b);
            return Mathf.Min(d, count - d);
        }

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

            public static TextureCopy From(Texture2D source, int size, Color fallbackColor, bool linear) {
                Texture2D copy = new(size, size, TextureFormat.RGBA32, false, linear) {
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
                RenderTextureReadWrite readWrite = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
                RenderTexture tmp = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, readWrite);
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
