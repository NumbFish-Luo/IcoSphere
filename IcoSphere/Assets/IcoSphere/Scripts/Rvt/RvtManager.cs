using System;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public sealed class RvtManager : MonoBehaviour {
        private static readonly int UseRvtId = Shader.PropertyToID("_UseRvt");
        private static readonly int RvtIndexTexId = Shader.PropertyToID("_RvtIndexTex");
        private static readonly int RvtAlbedoTexArrayId = Shader.PropertyToID("_RvtAlbedoTexArray");
        private static readonly int RvtIndexMaxLevelId = Shader.PropertyToID("_RvtIndexMaxLevel");
        private static readonly int RvtTileSizeId = Shader.PropertyToID("_RvtTileSize");
        private static readonly int RvtPaddedTileSizeId = Shader.PropertyToID("_RvtPaddedTileSize");
        private static readonly int RvtPaddingPixelsId = Shader.PropertyToID("_RvtPaddingPixels");
        private static readonly int RvtGeneratedMipCountId = Shader.PropertyToID("_RvtGeneratedMipCount");
        private static readonly int RvtMipBiasId = Shader.PropertyToID("_RvtMipBias");

        [SerializeField] private Camera sourceCamera;
        [SerializeField] private Transform globeTransform;
        [SerializeField] private Material targetMaterial;
        [SerializeField] private bool enableRvt = true;
        [SerializeField] private bool updateEveryFrame = true;
        [SerializeField, Min(1)] private int tileSize = 128;
        [SerializeField, Min(1)] private int layerCount = 16;
        [SerializeField, Min(0)] private int paddingPixels = 4;
        [SerializeField, Min(0)] private int maxResidentLevel = 4;
        [SerializeField, Min(0)] private int indexMaxLevel = 6;
        [SerializeField, Min(0.001f)] private float globeRadius = 1.0f;
        [SerializeField] private float mipBias = 0.0f;

        private readonly HashSet<RvtTileId> residentTiles = new();
        private readonly HashSet<RvtTileId> wantedSet = new();
        private readonly List<RvtTileId> wantedTiles = new();
        private readonly List<RvtTileId> residentScratch = new();

        private IRvtAddressMapping addressMapping;
        private RvtPhysicalTexturePool physicalPool;
        private IRvtTileSource tileSource;
        private Texture2D indexTexture;
        private Color[] indexPixels;
        private Color32[] sourcePixels;
        private Color32[] paddedPixels;

        public Texture2D IndexTexture => indexTexture;
        public Texture2DArray PhysicalTextureArray => physicalPool?.GetOrCreateTextureArray();

        public static List<RvtTileId> BuildWantedTiles(Vector2 focusUv, int maxLevel) {
            if (maxLevel < 0) {
                throw new ArgumentOutOfRangeException(nameof(maxLevel), "RVT max level must be non-negative.");
            }

            List<RvtTileId> results = new(maxLevel + 1);
            float u = Mathf.Repeat(focusUv.x, 1.0f);
            float v = Mathf.Clamp01(focusUv.y);
            for (int level = 0; level <= maxLevel; level++) {
                int dimension = 1 << level;
                int x = Mathf.Clamp(Mathf.FloorToInt(u * dimension), 0, dimension - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(v * dimension), 0, dimension - 1);
                results.Add(new RvtTileId(level, x, y));
            }

            return results;
        }

        private void OnEnable() {
            EnsureInitialized();
            Refresh();
        }

        private void Update() {
            if (updateEveryFrame) {
                Refresh();
            }
        }

        private void OnDestroy() {
            if (physicalPool != null) {
                physicalPool.Dispose();
                physicalPool = null;
            }

            if (indexTexture != null) {
                if (Application.isPlaying) {
                    Destroy(indexTexture);
                } else {
                    DestroyImmediate(indexTexture);
                }
                indexTexture = null;
            }
        }

        public void Refresh() {
            EnsureInitialized();
            Vector2 focusUv = ResolveFocusUv();
            int residentMaxLevel = Mathf.Min(maxResidentLevel, indexMaxLevel);
            wantedTiles.Clear();
            wantedTiles.AddRange(BuildWantedTiles(focusUv, residentMaxLevel));

            bool uploaded = UpdateResidency();
            if (uploaded) {
                physicalPool.GetOrCreateTextureArray().Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }

            RewriteIndexTexture();
            BindMaterial();
        }

        private void EnsureInitialized() {
            addressMapping ??= new LonLatRvtAddressMapping();
            tileSource ??= new DebugRvtTileSource(tileSize);
            physicalPool ??= new RvtPhysicalTexturePool(tileSize, layerCount, paddingPixels);
            EnsureIndexTexture();
        }

        private bool UpdateResidency() {
            wantedSet.Clear();
            foreach (RvtTileId tileId in wantedTiles) {
                wantedSet.Add(tileId);
            }

            residentScratch.Clear();
            residentScratch.AddRange(residentTiles);
            foreach (RvtTileId residentTile in residentScratch) {
                if (wantedSet.Contains(residentTile)) {
                    continue;
                }

                physicalPool.Release(residentTile);
                residentTiles.Remove(residentTile);
            }

            bool uploadedAny = false;
            foreach (RvtTileId tileId in wantedTiles) {
                if (physicalPool.TryGetLayer(tileId, out _)) {
                    continue;
                }

                if (!physicalPool.TryAllocate(tileId, out int layer)) {
                    continue;
                }

                residentTiles.Add(tileId);
                uploadedAny |= UploadTile(tileId, layer);
            }

            return uploadedAny;
        }

        private bool UploadTile(RvtTileId tileId, int layer) {
            sourcePixels ??= new Color32[tileSize * tileSize];
            if (!tileSource.TryLoadTile(tileId, sourcePixels)) {
                return false;
            }

            int paddedSize = physicalPool.PaddedTileSize;
            int expectedLength = paddedSize * paddedSize;
            if (paddedPixels == null || paddedPixels.Length != expectedLength) {
                paddedPixels = new Color32[expectedLength];
            }

            for (int y = 0; y < paddedSize; y++) {
                int srcY = Mathf.Clamp(y - paddingPixels, 0, tileSize - 1);
                for (int x = 0; x < paddedSize; x++) {
                    int srcX = Mathf.Clamp(x - paddingPixels, 0, tileSize - 1);
                    paddedPixels[y * paddedSize + x] = sourcePixels[srcY * tileSize + srcX];
                }
            }

            physicalPool.GetOrCreateTextureArray().SetPixels32(paddedPixels, layer, 0);
            return true;
        }

        private void RewriteIndexTexture() {
            EnsureIndexTexture();
            RvtIndexPayload invalid = RvtIndexPayload.Invalid;
            Color invalidColor = invalid.ToVector4();
            for (int i = 0; i < indexPixels.Length; i++) {
                indexPixels[i] = invalidColor;
            }

            foreach (RvtTileId tileId in wantedTiles) {
                if (!physicalPool.TryGetLayer(tileId, out int layer)) {
                    continue;
                }

                WritePayload(RvtIndexPayload.FromTile(tileId, layer, indexMaxLevel));
            }

            indexTexture.SetPixels(indexPixels);
            indexTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private void WritePayload(RvtIndexPayload payload) {
            int resolution = 1 << indexMaxLevel;
            Color color = payload.ToVector4();
            int maxY = Mathf.Min(payload.OriginY + payload.Size, resolution);
            int maxX = Mathf.Min(payload.OriginX + payload.Size, resolution);
            for (int y = payload.OriginY; y < maxY; y++) {
                int rowOffset = y * resolution;
                for (int x = payload.OriginX; x < maxX; x++) {
                    indexPixels[rowOffset + x] = color;
                }
            }
        }

        private void EnsureIndexTexture() {
            indexMaxLevel = Mathf.Max(indexMaxLevel, maxResidentLevel);
            int resolution = 1 << indexMaxLevel;
            if (indexTexture != null && indexTexture.width == resolution && indexTexture.height == resolution) {
                if (indexPixels == null || indexPixels.Length != resolution * resolution) {
                    indexPixels = new Color[resolution * resolution];
                }
                return;
            }

            if (indexTexture != null) {
                if (Application.isPlaying) {
                    Destroy(indexTexture);
                } else {
                    DestroyImmediate(indexTexture);
                }
            }

            indexTexture = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, mipChain: false, linear: true) {
                name = "RVT Index Texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            indexPixels = new Color[resolution * resolution];
        }

        private Vector2 ResolveFocusUv() {
            Camera cam = sourceCamera != null ? sourceCamera : Camera.main;
            if (cam == null) {
                return new Vector2(0.5f, 0.5f);
            }

            if (TryGetViewportSphereHit(cam, out Vector3 localSurfacePoint)) {
                return addressMapping.WorldToVirtualUv(localSurfacePoint);
            }

            return addressMapping.CameraToVirtualUv(cam, globeTransform);
        }

        private bool TryGetViewportSphereHit(Camera cam, out Vector3 localSurfacePoint) {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0.0f));
            Vector3 localOrigin = globeTransform == null ? ray.origin : globeTransform.InverseTransformPoint(ray.origin);
            Vector3 localDirection = globeTransform == null
                ? ray.direction.normalized
                : globeTransform.InverseTransformDirection(ray.direction).normalized;

            float b = Vector3.Dot(localOrigin, localDirection);
            float c = Vector3.Dot(localOrigin, localOrigin) - globeRadius * globeRadius;
            float discriminant = b * b - c;
            if (discriminant < 0.0f) {
                localSurfacePoint = default;
                return false;
            }

            float sqrt = Mathf.Sqrt(discriminant);
            float t = -b - sqrt;
            if (t < 0.0f) {
                t = -b + sqrt;
            }
            if (t < 0.0f) {
                localSurfacePoint = default;
                return false;
            }

            localSurfacePoint = localOrigin + localDirection * t;
            return true;
        }

        private void BindMaterial() {
            if (targetMaterial == null) {
                return;
            }

            targetMaterial.SetFloat(UseRvtId, enableRvt ? 1.0f : 0.0f);
            targetMaterial.SetTexture(RvtIndexTexId, indexTexture);
            targetMaterial.SetTexture(RvtAlbedoTexArrayId, physicalPool.GetOrCreateTextureArray());
            targetMaterial.SetFloat(RvtIndexMaxLevelId, indexMaxLevel);
            targetMaterial.SetFloat(RvtTileSizeId, tileSize);
            targetMaterial.SetFloat(RvtPaddedTileSizeId, physicalPool.PaddedTileSize);
            targetMaterial.SetFloat(RvtPaddingPixelsId, paddingPixels);
            targetMaterial.SetFloat(RvtGeneratedMipCountId, 1.0f);
            targetMaterial.SetFloat(RvtMipBiasId, mipBias);
        }
    }
}
