using System;

namespace IcoSphere {
    public readonly struct RvtTileId : IEquatable<RvtTileId> {
        public readonly int level;
        public readonly int x;
        public readonly int y;

        public RvtTileId(int level, int x, int y) {
            if (level < 0) {
                throw new ArgumentOutOfRangeException(nameof(level), "RVT tile level must be non-negative.");
            }

            int width = 1 << level;
            if (x < 0 || x >= width) {
                throw new ArgumentOutOfRangeException(nameof(x), "RVT tile x is outside the level range.");
            }
            if (y < 0 || y >= width) {
                throw new ArgumentOutOfRangeException(nameof(y), "RVT tile y is outside the level range.");
            }

            this.level = level;
            this.x = x;
            this.y = y;
        }

        public RvtTileId GetChild(int childIndex) {
            if (childIndex < 0 || childIndex > 3) {
                throw new ArgumentOutOfRangeException(nameof(childIndex), "RVT child index must be in [0, 3].");
            }

            int childX = x * 2 + (childIndex & 1);
            int childY = y * 2 + ((childIndex >> 1) & 1);
            return new RvtTileId(level + 1, childX, childY);
        }

        public RvtTileId GetParent() {
            if (level == 0) {
                return this;
            }

            return new RvtTileId(level - 1, x >> 1, y >> 1);
        }

        public RvtTileRect ToRect() {
            float inv = 1.0f / (1 << level);
            return new RvtTileRect(new UnityEngine.Vector2(x * inv, y * inv), new UnityEngine.Vector2(inv, inv));
        }

        public bool Equals(RvtTileId other) {
            return level == other.level && x == other.x && y == other.y;
        }

        public override bool Equals(object obj) {
            return obj is RvtTileId other && Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 31 + level;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                return hash;
            }
        }

        public override string ToString() {
            return $"RvtTileId(level:{level}, x:{x}, y:{y})";
        }

        public static bool operator ==(RvtTileId left, RvtTileId right) {
            return left.Equals(right);
        }

        public static bool operator !=(RvtTileId left, RvtTileId right) {
            return !left.Equals(right);
        }
    }
}
