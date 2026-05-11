using NUnit.Framework;
using UnityEngine;

namespace IcoSphere.Tests {
    public sealed class RvtAddressingTests {
        [Test]
        public void TileIdCreatesExpectedChildrenAndParent() {
            RvtTileId root = new(0, 0, 0);

            Assert.AreEqual(new RvtTileId(1, 0, 0), root.GetChild(0));
            Assert.AreEqual(new RvtTileId(1, 1, 0), root.GetChild(1));
            Assert.AreEqual(new RvtTileId(1, 0, 1), root.GetChild(2));
            Assert.AreEqual(new RvtTileId(1, 1, 1), root.GetChild(3));
            Assert.AreEqual(new RvtTileId(2, 2, 3), new RvtTileId(3, 5, 7).GetParent());
        }

        [Test]
        public void TileIdConvertsToNormalizedRect() {
            RvtTileRect rect = new RvtTileId(2, 1, 3).ToRect();

            Assert.That(rect.Min.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(rect.Min.y, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(rect.Size.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(rect.Size.y, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void WrappedDistanceTreatsDateLineAsContinuous() {
            RvtTileRect nearDateLine = new(new Vector2(0.9375f, 0.25f), new Vector2(0.0625f, 0.5f));

            Assert.That(nearDateLine.DistanceSquaredToPointWrapped(new Vector2(0.99f, 0.5f)), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(nearDateLine.DistanceSquaredToPointWrapped(new Vector2(0.02f, 0.5f)), Is.EqualTo(0.0004f).Within(0.0001f));
        }

        [Test]
        public void LonLatMappingMatchesExistingAxisConvention() {
            LonLatRvtAddressMapping mapping = new();

            AssertUv(mapping.WorldToVirtualUv(Vector3.right), 0.5f, 0.5f);
            AssertUv(mapping.WorldToVirtualUv(Vector3.forward), 0.75f, 0.5f);
            AssertUv(mapping.WorldToVirtualUv(Vector3.back), 0.25f, 0.5f);
            AssertUv(mapping.WorldToVirtualUv(Vector3.up), 0.5f, 1.0f);
            AssertUv(mapping.WorldToVirtualUv(Vector3.down), 0.5f, 0.0f);
        }

        private static void AssertUv(Vector2 uv, float expectedU, float expectedV) {
            Assert.That(uv.x, Is.EqualTo(expectedU).Within(0.0001f));
            Assert.That(uv.y, Is.EqualTo(expectedV).Within(0.0001f));
        }
    }
}
