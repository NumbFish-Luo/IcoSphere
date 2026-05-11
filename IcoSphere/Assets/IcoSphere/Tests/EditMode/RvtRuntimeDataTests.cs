using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere.Tests {
    public sealed class RvtRuntimeDataTests {
        [Test]
        public void PhysicalTexturePoolAllocatesAndReusesReleasedLayers() {
            RvtPhysicalTexturePool pool = new(tileSize: 128, layerCount: 2, paddingPixels: 4);
            RvtTileId first = new(1, 0, 0);
            RvtTileId second = new(1, 1, 0);
            RvtTileId third = new(1, 0, 1);

            Assert.IsTrue(pool.TryAllocate(first, out int firstLayer));
            Assert.IsTrue(pool.TryAllocate(second, out int secondLayer));
            Assert.AreEqual(0, firstLayer);
            Assert.AreEqual(1, secondLayer);
            Assert.IsFalse(pool.TryAllocate(third, out _));

            Assert.IsTrue(pool.Release(first));
            Assert.IsTrue(pool.TryAllocate(third, out int reusedLayer));
            Assert.AreEqual(firstLayer, reusedLayer);
            Assert.IsTrue(pool.TryGetLayer(third, out int storedLayer));
            Assert.AreEqual(reusedLayer, storedLayer);
        }

        [Test]
        public void IndexPayloadScalesTileToFinestIndexGrid() {
            RvtIndexPayload payload = RvtIndexPayload.FromTile(new RvtTileId(2, 1, 3), layer: 7, maxLevel: 6);

            Assert.AreEqual(7, payload.Layer);
            Assert.AreEqual(16, payload.OriginX);
            Assert.AreEqual(48, payload.OriginY);
            Assert.AreEqual(16, payload.Size);
            Assert.AreEqual(new Vector4(7, 16, 48, 16), payload.ToVector4());
        }

        [Test]
        public void DebugTileSourceFillsDeterministicPixels() {
            DebugRvtTileSource source = new(tileSize: 4);
            RvtTileId tile = new(2, 3, 1);
            Color32[] first = new Color32[16];
            Color32[] second = new Color32[16];

            Assert.IsTrue(source.TryLoadTile(tile, first));
            Assert.IsTrue(source.TryLoadTile(tile, second));

            CollectionAssert.AreEqual(first, second);
            Assert.AreNotEqual(source.GetDebugColor(tile), source.GetDebugColor(new RvtTileId(2, 0, 1)));
        }

        [Test]
        public void ManagerBuildsRootToFocusedTileChain() {
            List<RvtTileId> tiles = RvtManager.BuildWantedTiles(new Vector2(0.6f, 0.2f), maxLevel: 3);

            CollectionAssert.AreEqual(
                new[] {
                    new RvtTileId(0, 0, 0),
                    new RvtTileId(1, 1, 0),
                    new RvtTileId(2, 2, 0),
                    new RvtTileId(3, 4, 1)
                },
                tiles);
        }

        [Test]
        public void ManagerTileSelectionWrapsLongitudeAndClampsLatitude() {
            List<RvtTileId> tiles = RvtManager.BuildWantedTiles(new Vector2(1.1f, 1.2f), maxLevel: 1);

            CollectionAssert.AreEqual(
                new[] {
                    new RvtTileId(0, 0, 0),
                    new RvtTileId(1, 0, 1)
                },
                tiles);
        }
    }
}
