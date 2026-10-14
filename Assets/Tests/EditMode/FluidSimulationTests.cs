using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>FluidSimulation on a TestWorld, with the game's water and lava (Minecraft's rules, see the readme).</summary>
    public class FluidSimulationTests
    {
        private BlockType stone, water, lava;
        private TestWorld world;

        [SetUp]
        public void SetUp() {
            stone = GameData.Block("stone");
            water = GameData.Block("water");
            lava = GameData.Block("lava");
            world = new TestWorld();
            world.Floor(0, -20, 20, stone);
        }

        private static int level(BlockDefinition definition) {
            return definition.fluid != null ? definition.fluidLevel : -1;
        }

        [Test]
        public void WaterSpreadsAsADiamondOfSevenOnFlatGround() {
            world.Change(new Vector3Int(0, 1, 0), water);
            world.Run(4);

            for (int x = -9; x <= 9; x++) {
                for (int z = -9; z <= 9; z++) {
                    int distance = Mathf.Abs(x) + Mathf.Abs(z);
                    int expected = distance <= 7 ? distance : -1;
                    Assert.AreEqual(expected, level(world.At(x, 1, z)), "level at " + x + ", " + z);
                }
            }
        }

        [Test]
        public void WaterDriesUpWithoutItsSource() {
            world.Change(new Vector3Int(0, 1, 0), water);
            world.Run(4);
            world.Change(new Vector3Int(0, 1, 0), BlockType.Air);
            world.Run(5);

            for (int x = -9; x <= 9; x++) {
                for (int z = -9; z <= 9; z++) {
                    Assert.IsNull(world.At(x, 1, z).fluid, "water left at " + x + ", " + z);
                }
            }
        }

        [Test]
        public void WaterFlowsOnlyTowardANearbyWayDown() {
            world.Floor(-3, -20, 20, stone); // what it falls onto
            world.SetBlock(new Vector3Int(3, 0, 0), BlockType.Air); // a hole three blocks east
            world.Change(new Vector3Int(0, 1, 0), water);
            world.Run(4);

            Assert.AreEqual(1, level(world.At(1, 1, 0)));
            Assert.AreEqual(2, level(world.At(2, 1, 0)));
            Assert.AreEqual(Fluid.Falling, level(world.At(3, 0, 0)), "it falls into the hole");
            Assert.IsNull(world.At(-1, 1, 0).fluid, "not west");
            Assert.IsNull(world.At(0, 1, 1).fluid, "not north");
            Assert.IsNull(world.At(0, 1, -1).fluid, "not south");
        }

        [Test]
        public void WaterFallsDownFirst() {
            world.Change(new Vector3Int(0, 5, 0), water);
            world.Run(3);

            for (int y = 1; y <= 4; y++) {
                Assert.AreEqual(Fluid.Falling, level(world.At(0, y, 0)), "falling at height " + y);
            }
            Assert.AreEqual(1, level(world.At(1, 1, 0)), "and spreads where it lands");
        }

        [Test]
        public void TwoSourcesMakeAThird() {
            world.Change(new Vector3Int(-1, 1, 0), water);
            world.Change(new Vector3Int(1, 1, 0), water);
            world.Run(2);

            Assert.AreEqual(0, level(world.At(0, 1, 0)));
        }

        [Test]
        public void LavaSpreadsThreeBlocksSlowly() {
            world.Change(new Vector3Int(0, 1, 0), lava);
            world.Run(2);
            Assert.IsNull(world.At(2, 1, 0).fluid, "not two blocks in two seconds");

            world.Run(6);
            Assert.AreEqual(3, level(world.At(3, 1, 0)));
            Assert.IsNull(world.At(4, 1, 0).fluid);
        }

        [Test]
        public void WaterOnALavaSourceMakesObsidian() {
            world.Change(new Vector3Int(0, 1, 0), lava);
            world.Change(new Vector3Int(0, 2, 0), water);
            world.Run(2);

            Assert.AreEqual("obsidian", world.At(0, 1, 0).name);
        }

        [Test]
        public void WaterTouchingFlowingLavaMakesCobblestone() {
            world.Change(new Vector3Int(0, 1, 0), lava);
            world.Run(4);
            world.Change(new Vector3Int(2, 1, 1), water); // beside the lava flowing at 2, 1, 0
            world.Run(2);

            Assert.AreEqual("cobblestone", world.At(2, 1, 0).name);
        }

        [Test]
        public void LavaFallingOntoWaterMakesStone() {
            world.Change(new Vector3Int(0, 1, 0), water);
            world.Run(0.1f);
            world.Change(new Vector3Int(0, 3, 0), lava);
            world.Run(4);

            Assert.AreEqual("stone", world.At(0, 1, 0).name);
        }

        [Test]
        public void BricksThatStayHoldWaterBack() {
            world.bricksThatStay.Add(new Vector3Int(1, 1, 0));
            world.Change(new Vector3Int(0, 1, 0), water);
            world.Run(4);

            Assert.IsNull(world.At(1, 1, 0).fluid);
        }

        [Test]
        public void WaterWashesAwayAndLavaBurns() {
            world.washableBricks.Add(new Vector3Int(2, 1, 0));
            world.washableBricks.Add(new Vector3Int(-2, 1, 0));
            world.Change(new Vector3Int(0, 1, 0), water);
            world.Run(4);
            Assert.IsTrue(world.washed.TryGetValue(new Vector3Int(2, 1, 0), out bool burnt) && !burnt, "water washes it away");

            TestWorld lavaWorld = new TestWorld();
            lavaWorld.Floor(0, -20, 20, stone);
            lavaWorld.washableBricks.Add(new Vector3Int(2, 1, 0));
            lavaWorld.Change(new Vector3Int(0, 1, 0), lava);
            lavaWorld.Run(6);
            Assert.IsTrue(lavaWorld.washed.TryGetValue(new Vector3Int(2, 1, 0), out burnt) && burnt, "lava burns it");
        }

        [Test]
        public void WaterWaitsForAChunkToLoad() {
            world.unloadedChunks.Add(new Vector2Int(1, 0)); // x 16 to 31
            world.Change(new Vector3Int(13, 1, 0), water);
            world.Run(4);
            Assert.AreEqual(2, level(world.At(15, 1, 0)));

            world.unloadedChunks.Clear();
            world.simulation.ChunkLoaded(new Vector2Int(1, 0), new Vector3Int[0]);
            world.Run(4);
            Assert.AreEqual(7, level(world.At(20, 1, 0)), "it goes on into the chunk");
        }

        [Test]
        public void SavedFlowingWaterGoesOnWhenItsChunkLoads() {
            // saved mid-flow: the source and one block of its spread, nothing scheduled
            world.SetBlock(new Vector3Int(0, 1, 0), water);
            BlockType levelOne = (BlockType)GameData.Definition(water).fluid.At(1).id;
            world.SetBlock(new Vector3Int(1, 1, 0), levelOne);

            world.simulation.ChunkLoaded(new Vector2Int(0, 0), new[] { new Vector3Int(1, 1, 0) });
            world.Run(4);

            Assert.AreEqual(7, level(world.At(7, 1, 0)));
            Assert.AreEqual(7, level(world.At(-7, 1, 0)), "the source spreads again once it's looked at");
        }
    }
}
