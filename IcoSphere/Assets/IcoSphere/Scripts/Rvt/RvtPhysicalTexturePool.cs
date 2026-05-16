using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IcoSphere {
    public sealed class RvtPhysicalTexturePool : IDisposable {
        private readonly Queue<int> freeLayers = new();
        private readonly Dictionary<RvtTileId, int> tileLayers = new();
        private Texture2DArray textureArray;

        public RvtPhysicalTexturePool(int tileSize, int layerCount, int paddingPixels) {
            if (tileSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(tileSize), "RVT tile size must be positive.");
            }
            if (layerCount <= 0) {
                throw new ArgumentOutOfRangeException(nameof(layerCount), "RVT layer count must be positive.");
            }
            if (paddingPixels < 0) {
                throw new ArgumentOutOfRangeException(nameof(paddingPixels), "RVT padding must be non-negative.");
            }

            TileSize = tileSize;
            LayerCount = layerCount;
            PaddingPixels = paddingPixels;
            ResetFreeLayers();
        }

        public int TileSize { get; }
        public int LayerCount { get; }
        public int PaddingPixels { get; }
        public int PaddedTileSize => TileSize + PaddingPixels * 2;
        public int ResidentTileCount => tileLayers.Count;
        public int AvailableLayerCount => freeLayers.Count;

        public bool TryAllocate(RvtTileId tileId, out int layer) {
            if (tileLayers.TryGetValue(tileId, out layer)) {
                return true;
            }

            if (freeLayers.Count == 0) {
                layer = -1;
                return false;
            }

            layer = freeLayers.Dequeue();
            tileLayers.Add(tileId, layer);
            return true;
        }

        public bool TryGetLayer(RvtTileId tileId, out int layer) {
            return tileLayers.TryGetValue(tileId, out layer);
        }

        public bool Release(RvtTileId tileId) {
            if (!tileLayers.TryGetValue(tileId, out int layer)) {
                return false;
            }

            tileLayers.Remove(tileId);
            freeLayers.Enqueue(layer);
            return true;
        }

        public void Clear() {
            tileLayers.Clear();
            freeLayers.Clear();
            ResetFreeLayers();
        }

        public Texture2DArray GetOrCreateTextureArray(TextureFormat format = TextureFormat.RGBA32, bool mipChain = false, bool linear = false) {
            if (textureArray != null) {
                return textureArray;
            }

            textureArray = new Texture2DArray(PaddedTileSize, PaddedTileSize, LayerCount, format, mipChain, linear) {
                name = "RVT Physical Texture Array",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1
            };
            return textureArray;
        }

        public void Dispose() {
            if (textureArray == null) {
                return;
            }

            if (Application.isPlaying) {
                Object.Destroy(textureArray);
            } else {
                Object.DestroyImmediate(textureArray);
            }
            textureArray = null;
        }

        private void ResetFreeLayers() {
            for (int layer = 0; layer < LayerCount; layer++) {
                freeLayers.Enqueue(layer);
            }
        }
    }
}
