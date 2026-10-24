using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.World.Towns;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>Towns: the buildings of StreamingAssets/Buildings, and where the seed puts them (see TownPlanner).</summary>
    public class TownTests
    {
        [SetUp]
        public void SetUp() {
            GameData.Load();
            TownBuildings.Clear();
        }

        [Test]
        public void TheBuildingsAreReadAndSized() {
            List<LDrawImport> buildings = TownBuildings.LoadAll();

            Assert.Greater(buildings.Count, 0, "the buildings folder has models the game knows");
            foreach (LDrawImport building in buildings) {
                Assert.Greater(building.bricks.Count, 0);
                Assert.Greater(building.Size.x, 0);
                Assert.Greater(building.Size.y, 0);
                Assert.Greater(building.Size.z, 0);
            }
        }

        [Test]
        public void TheSameSeedAndRegionGiveTheSameTown() {
            List<LDrawImport> buildings = TownBuildings.LoadAll();
            Vector2Int region = aRegionWithATown(buildings, 12345);
            Assume.That(region.x != int.MinValue, "a region with a town");

            TownPlan plan = TownPlanner.Plan(12345, buildings, region.x, region.y);
            TownPlan again = TownPlanner.Plan(12345, buildings, region.x, region.y);

            Assert.AreEqual(plan.buildings.Count, again.buildings.Count);
            for (int i = 0; i < plan.buildings.Count; i++) {
                Assert.AreEqual(plan.buildings[i].building, again.buildings[i].building, "its building");
                Assert.AreEqual(plan.buildings[i].turns, again.buildings[i].turns, "its turn");
                Assert.AreEqual(plan.buildings[i].corner, again.buildings[i].corner, "its corner");
            }
        }

        [Test]
        public void ATownKeepsToItsRegionAndTheBlockGrid() {
            List<LDrawImport> buildings = TownBuildings.LoadAll();

            for (int rx = -4; rx <= 4; rx++) {
                for (int rz = -4; rz <= 4; rz++) {
                    TownPlan plan = TownPlanner.Plan(99, buildings, rx, rz);
                    if (plan == null) {
                        continue;
                    }
                    Assert.GreaterOrEqual(plan.buildings.Count, 1);
                    Assert.LessOrEqual(plan.buildings.Count, TownPlanner.MaxBuildings);
                    foreach (TownBuilding tb in plan.buildings) {
                        Vector3Int size = buildings[tb.building].SizeTurned(tb.turns);
                        Assert.AreEqual(0, BrickGrid.Mod(tb.corner.x, 2), "x on the block grid");
                        Assert.AreEqual(0, BrickGrid.Mod(tb.corner.z, 2), "z on the block grid");
                        // every chunk it touches is one of the region's
                        int fromX = BrickGrid.FloorDiv(tb.corner.x, BrickGrid.ChunkCells);
                        int fromZ = BrickGrid.FloorDiv(tb.corner.z, BrickGrid.ChunkCells);
                        int toX = BrickGrid.FloorDiv(tb.corner.x + size.x - 1, BrickGrid.ChunkCells);
                        int toZ = BrickGrid.FloorDiv(tb.corner.z + size.z - 1, BrickGrid.ChunkCells);
                        Assert.GreaterOrEqual(fromX, rx * TownPlanner.SpanChunks);
                        Assert.LessOrEqual(toX, rx * TownPlanner.SpanChunks + TownPlanner.SpanChunks - 1);
                        Assert.GreaterOrEqual(fromZ, rz * TownPlanner.SpanChunks);
                        Assert.LessOrEqual(toZ, rz * TownPlanner.SpanChunks + TownPlanner.SpanChunks - 1);
                    }
                }
            }
        }

        [Test]
        public void ASecondTownIsNotTheSameAsTheFirst() {
            List<LDrawImport> buildings = TownBuildings.LoadAll();
            bool differ = false;
            for (int rx = -8; rx <= 8 && !differ; rx++) {
                for (int rz = -8; rz <= 8 && !differ; rz++) {
                    TownPlan first = TownPlanner.Plan(7, buildings, rx, rz);
                    TownPlan second = TownPlanner.Plan(8, buildings, rx, rz);
                    if (first != null && second != null && first.buildings.Count != second.buildings.Count) {
                        differ = true;
                    }
                }
            }
            Assert.IsTrue(differ, "another seed usually makes other towns");
        }

        [Test]
        public void ATownsBricksAreNamedTheSameEveryTime() {
            System.Guid first = TownPlanner.BrickId(1, 2, 3, 4);

            Assert.AreEqual(first, TownPlanner.BrickId(1, 2, 3, 4));
            Assert.AreNotEqual(first, TownPlanner.BrickId(1, 2, 3, 5), "another brick");
            Assert.AreNotEqual(first, TownPlanner.BrickId(1, 2, 4, 4), "another region");
        }

        // a region the seed gives a town to, (int.MinValue, int.MinValue) if none is near
        private static Vector2Int aRegionWithATown(List<LDrawImport> buildings, long seed) {
            for (int rx = -4; rx <= 4; rx++) {
                for (int rz = -4; rz <= 4; rz++) {
                    if (TownPlanner.Plan(seed, buildings, rx, rz) != null) {
                        return new Vector2Int(rx, rz);
                    }
                }
            }
            return new Vector2Int(int.MinValue, int.MinValue);
        }
    }
}