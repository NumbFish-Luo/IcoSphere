using UnityEngine;

namespace IcoSphere {
    public readonly struct RvtTileRect {
        public readonly Vector2 Min;
        public readonly Vector2 Size;

        public RvtTileRect(Vector2 min, Vector2 size) {
            Min = min;
            Size = size;
        }

        public Vector2 Max => Min + Size;

        public float DistanceSquaredToPointWrapped(Vector2 point) {
            float best = DistanceSquaredToPoint(point, Min);
            best = Mathf.Min(best, DistanceSquaredToPoint(point, Min + Vector2.left));
            best = Mathf.Min(best, DistanceSquaredToPoint(point, Min + Vector2.right));
            return best;
        }

        private float DistanceSquaredToPoint(Vector2 point, Vector2 rectMin) {
            Vector2 rectMax = rectMin + Size;
            float dx = DistanceToInterval(point.x, rectMin.x, rectMax.x);
            float dy = DistanceToInterval(point.y, rectMin.y, rectMax.y);
            return dx * dx + dy * dy;
        }

        private static float DistanceToInterval(float value, float min, float max) {
            if (value < min) {
                return min - value;
            }
            if (value > max) {
                return value - max;
            }
            return 0.0f;
        }
    }
}
