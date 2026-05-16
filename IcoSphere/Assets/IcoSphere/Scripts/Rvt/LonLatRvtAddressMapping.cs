using UnityEngine;

namespace IcoSphere {
    public sealed class LonLatRvtAddressMapping : IRvtAddressMapping {
        public Vector2 WorldToVirtualUv(Vector3 worldPos) {
            Vector3 p = worldPos.normalized;
            Vector2 lonLat = new(Mathf.Atan2(p.z, p.x), Mathf.Asin(p.y));
            lonLat /= Mathf.PI;
            return new Vector2((lonLat.x + 1.0f) * 0.5f, lonLat.y + 0.5f);
        }

        public RvtTileRect GetTileRect(RvtTileId id) {
            return id.ToRect();
        }

        public Vector2 CameraToVirtualUv(Camera camera, Transform globeTransform) {
            Vector3 localPos = globeTransform == null
                ? camera.transform.position
                : globeTransform.InverseTransformPoint(camera.transform.position);
            return WorldToVirtualUv(localPos.normalized);
        }

        public float EstimateTileError(RvtTileId id, Vector2 focusUv, float cameraDistance) {
            float distance = Mathf.Sqrt(id.ToRect().DistanceSquaredToPointWrapped(focusUv));
            float tileSize = 1.0f / (1 << id.level);
            return distance * Mathf.Max(cameraDistance, 1.0f) / tileSize;
        }
    }
}
