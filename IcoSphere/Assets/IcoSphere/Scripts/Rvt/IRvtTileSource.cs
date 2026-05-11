using UnityEngine;

namespace IcoSphere {
    public interface IRvtTileSource {
        int TileSize { get; }
        bool TryLoadTile(RvtTileId tileId, Color32[] pixels);
    }
}
