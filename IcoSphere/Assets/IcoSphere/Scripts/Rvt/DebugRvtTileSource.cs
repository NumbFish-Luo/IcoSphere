using System;
using UnityEngine;

namespace IcoSphere {
    public sealed class DebugRvtTileSource : IRvtTileSource {
        public DebugRvtTileSource(int tileSize) {
            if (tileSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(tileSize), "RVT debug tile size must be positive.");
            }

            TileSize = tileSize;
        }

        public int TileSize { get; }

        public bool TryLoadTile(RvtTileId tileId, Color32[] pixels) {
            if (pixels == null) {
                throw new ArgumentNullException(nameof(pixels));
            }
            int expectedLength = TileSize * TileSize;
            if (pixels.Length < expectedLength) {
                throw new ArgumentException("Pixel buffer is smaller than the RVT tile.", nameof(pixels));
            }

            Color32 baseColor = GetDebugColor(tileId);
            int bandSize = Mathf.Max(1, TileSize / 4);
            for (int y = 0; y < TileSize; y++) {
                for (int x = 0; x < TileSize; x++) {
                    int checker = ((x / bandSize) + (y / bandSize)) & 1;
                    byte factor = checker == 0 ? (byte)255 : (byte)190;
                    pixels[y * TileSize + x] = Scale(baseColor, factor);
                }
            }

            return true;
        }

        public Color32 GetDebugColor(RvtTileId tileId) {
            uint hash = Hash(tileId);
            return new Color32(
                (byte)(64 + hash % 160),
                (byte)(64 + (hash >> 8) % 160),
                (byte)(64 + (hash >> 16) % 160),
                255);
        }

        private static uint Hash(RvtTileId tileId) {
            unchecked {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)tileId.level) * 16777619u;
                hash = (hash ^ (uint)tileId.x) * 16777619u;
                hash = (hash ^ (uint)tileId.y) * 16777619u;
                return hash;
            }
        }

        private static Color32 Scale(Color32 color, byte factor) {
            return new Color32(
                (byte)(color.r * factor / 255),
                (byte)(color.g * factor / 255),
                (byte)(color.b * factor / 255),
                color.a);
        }
    }
}
