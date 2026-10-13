using System.Collections.Generic;
using Brickcraft.Bricks;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>Models that don't fill their box (BrickFootprint): a corner brick leaves its notch free, turned or not.</summary>
    public class BrickFootprintTests
    {
        private BrickModel corner, oneByOne;

        [SetUp]
        public void SetUp() {
            GameData.Load();
            BrickGrid.Clear();
            corner = BrickModels.Get("2357"); // Brick 2x2 Corner
            oneByOne = BrickModels.Get("3005");
        }

        [TearDown]
        public void TearDown() {
            BrickGrid.Clear();
        }

        // the corner brick's free column, in its own axes
        private Vector2Int freeColumn() {
            for (int x = 0; x < corner.width; x++) {
                for (int z = 0; z < corner.depth; z++) {
                    if (!corner.Fills(x, z)) {
                        return new Vector2Int(x, z);
                    }
                }
            }
            Assert.Fail("the corner brick fills all its columns");
            return default;
        }

        [Test]
        public void ACornerBrickFillsThreeColumnsOfFour() {
            Assert.AreEqual(2, corner.width);
            Assert.AreEqual(2, corner.depth);
            Assert.IsNotNull(corner.footprint);
            int filled = 0;
            foreach (bool column in corner.footprint) {
                filled += column ? 1 : 0;
            }
            Assert.AreEqual(3, filled);
            Assert.IsNull(BrickModels.Get("3001").footprint, "a 2x4 brick fills its box");
        }

        [Test]
        public void TheFreeColumnTurnsWithTheBrick() {
            Vector2Int free = freeColumn();
            // where the free column's middle is, from the brick's pivot (game units)
            Vector3 local = new Vector3((free.x + 0.5f - corner.width / 2f) * Server.studSize, 0.1f, (free.y + 0.5f - corner.depth / 2f) * Server.studSize);
            for (int rotation = 0; rotation < 4; rotation++) {
                BrickPlacement placement = new BrickPlacement(corner, new Vector3Int(10, 0, -6), rotation);
                Vector3Int notch = BrickGrid.WorldToCell(placement.Position + placement.Rotation * local);
                List<Vector3Int> cells = new List<Vector3Int>(placement.Cells);

                Assert.AreEqual(3 * corner.heightInPlates, cells.Count, "turned " + rotation);
                CollectionAssert.DoesNotContain(cells, notch, "the notch, turned " + rotation);
            }
        }

        [Test]
        public void AOneByOneGoesInTheNotch() {
            BrickPlacement placement = new BrickPlacement(corner, Vector3Int.zero, 1);
            BrickGrid.Register(new Brick() { id = "corner", placement = placement });

            int fits = 0;
            for (int x = 0; x < 2; x++) {
                for (int z = 0; z < 2; z++) {
                    bool isFree = BrickGrid.IsFree(new BrickPlacement(oneByOne, new Vector3Int(x, 0, z)));
                    Assert.AreEqual(!placement.FillsColumn(x, z), isFree, "column " + x + ", " + z);
                    fits += isFree ? 1 : 0;
                }
            }
            Assert.AreEqual(1, fits);
        }

        [Test]
        public void ItsCollidersLeaveTheNotchOpen() {
            float area = 0;
            foreach (BoxCollider box in corner.prefab.GetComponents<BoxCollider>()) {
                area += box.size.x * box.size.z;
            }
            Assert.AreEqual(3 * Server.studSize * Server.studSize, area, 0.0001f);
        }
    }
}
