using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>A vehicle's own cells (VehicleGrid): what touches, what holds together, and other vehicles' cells in its own.</summary>
    public class VehicleGridTests
    {
        private BrickModel brick2x2;
        private BrickModel brick2x4;

        [SetUp]
        public void SetUp() {
            GameData.Load();
            brick2x2 = Server.items["brick_2x2"].brickModel;
            brick2x4 = BrickModels.Get("3001");
        }

        private static Brick brick(BrickModel model, Vector3Int origin, int rotation = 0) {
            return new Brick() { id = System.Guid.NewGuid().ToString(), placement = new BrickPlacement(model, origin, rotation) };
        }

        [Test]
        public void TurnsAreUnitysQuarterTurns() {
            foreach (Vector3Int v in new[] { Vector3Int.right, new Vector3Int(0, 0, 1), new Vector3Int(3, 2, -5) }) {
                for (int turns = 0; turns < 4; turns++) {
                    Vector3 unity = Quaternion.Euler(0, 90 * turns, 0) * (Vector3)v;
                    Assert.AreEqual(Vector3Int.RoundToInt(unity), VehicleGrid.Turn(v, turns), v + " turned " + turns);
                }
            }
        }

        [Test]
        public void ABrickMappedFillsItsCellsTurned() {
            BrickPlacement placement = new BrickPlacement(brick2x4, new Vector3Int(1, 3, -2), 0);
            Vector3Int offset = new Vector3Int(5, -1, 7);
            for (int turns = 0; turns < 4; turns++) {
                BrickPlacement mapped = VehicleGrid.Map(placement, turns, offset);
                // each cell, a unit box, turned and moved: where its turned box starts
                HashSet<Vector3Int> expected = new HashSet<Vector3Int>();
                foreach (Vector3Int cell in placement.Cells) {
                    expected.Add(Vector3Int.Min(VehicleGrid.Turn(cell, turns), VehicleGrid.Turn(cell + Vector3Int.one, turns)) + offset);
                }
                CollectionAssert.AreEquivalent(expected, new List<Vector3Int>(mapped.Cells), "turned " + turns);
                Assert.AreEqual(turns, mapped.rotation);
            }
        }

        [Test]
        public void TakingTheMiddleBrickSplitsThem() {
            VehicleGrid grid = new VehicleGrid();
            Brick a = brick(brick2x2, new Vector3Int(0, 0, 0)), b = brick(brick2x2, new Vector3Int(2, 0, 0)), c = brick(brick2x2, new Vector3Int(4, 0, 0));
            grid.Register(a);
            grid.Register(b);
            grid.Register(c);
            Assert.AreEqual(1, grid.Components().Count, "a row of three holds together");

            grid.Unregister(b);
            List<List<Brick>> groups = grid.Components();
            Assert.AreEqual(2, groups.Count);
            Assert.IsNull(grid.GetBrickAt(new Vector3Int(2, 0, 0)));
        }

        [Test]
        public void APlacementTouchesTheBricksByItsFaces() {
            VehicleGrid grid = new VehicleGrid();
            Brick a = brick(brick2x2, Vector3Int.zero);
            grid.Register(a);
            Assert.IsTrue(grid.Touching(new BrickPlacement(brick2x2, new Vector3Int(0, 3, 0))).Contains(a), "on top");
            Assert.IsTrue(grid.Touching(new BrickPlacement(brick2x2, new Vector3Int(2, 0, 1))).Contains(a), "beside");
            Assert.AreEqual(0, grid.Touching(new BrickPlacement(brick2x2, new Vector3Int(2, 3, 0))).Count, "only by an edge");
            Assert.IsFalse(grid.IsFree(new BrickPlacement(brick2x2, new Vector3Int(1, 0, 0))));
        }
    }
}
