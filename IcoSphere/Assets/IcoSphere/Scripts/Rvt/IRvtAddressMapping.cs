using UnityEngine;

namespace IcoSphere {
    public interface IRvtAddressMapping {
        Vector2 WorldToVirtualUv(Vector3 worldPos);
        RvtTileRect GetTileRect(RvtTileId id);
        Vector2 CameraToVirtualUv(Camera camera, Transform globeTransform);
        float EstimateTileError(RvtTileId id, Vector2 focusUv, float cameraDistance);
    }
}
