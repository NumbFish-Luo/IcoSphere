using System;
using UnityEngine;

namespace IcoSphere {
    public readonly struct RvtIndexPayload : IEquatable<RvtIndexPayload> {
        public static readonly RvtIndexPayload Invalid = new(-1, 0, 0, 0);

        public RvtIndexPayload(int layer, int originX, int originY, int size) {
            if (layer < -1) {
                throw new ArgumentOutOfRangeException(nameof(layer), "RVT index layer must be -1 or non-negative.");
            }
            if (originX < 0) {
                throw new ArgumentOutOfRangeException(nameof(originX), "RVT index origin x must be non-negative.");
            }
            if (originY < 0) {
                throw new ArgumentOutOfRangeException(nameof(originY), "RVT index origin y must be non-negative.");
            }
            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size), "RVT index size must be non-negative.");
            }

            Layer = layer;
            OriginX = originX;
            OriginY = originY;
            Size = size;
        }

        public int Layer { get; }
        public int OriginX { get; }
        public int OriginY { get; }
        public int Size { get; }
        public bool IsValid => Layer >= 0;

        public static RvtIndexPayload FromTile(RvtTileId tileId, int layer, int maxLevel) {
            if (maxLevel < tileId.level) {
                throw new ArgumentOutOfRangeException(nameof(maxLevel), "RVT index max level must cover the tile level.");
            }

            int scale = 1 << (maxLevel - tileId.level);
            return new RvtIndexPayload(layer, tileId.x * scale, tileId.y * scale, scale);
        }

        public Vector4 ToVector4() {
            return new Vector4(Layer, OriginX, OriginY, Size);
        }

        public bool Equals(RvtIndexPayload other) {
            return Layer == other.Layer && OriginX == other.OriginX && OriginY == other.OriginY && Size == other.Size;
        }

        public override bool Equals(object obj) {
            return obj is RvtIndexPayload other && Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 31 + Layer;
                hash = hash * 31 + OriginX;
                hash = hash * 31 + OriginY;
                hash = hash * 31 + Size;
                return hash;
            }
        }

        public static bool operator ==(RvtIndexPayload left, RvtIndexPayload right) {
            return left.Equals(right);
        }

        public static bool operator !=(RvtIndexPayload left, RvtIndexPayload right) {
            return !left.Equals(right);
        }
    }
}
