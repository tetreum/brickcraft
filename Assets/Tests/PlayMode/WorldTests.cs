using System.Collections;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>The game running: a new world loads, holds its player, keeps what's built in it and floods.</summary>
    [Timeout(120000)]
    public class WorldTests
    {
        private TestGame game;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            yield return game.Start();
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            yield return game.Delete();
        }

        // a block high above the player, in the air whatever the terrain
        private static Vector3Int above(int height) {
            Vector3Int block = TestGame.PlayerBlock;
            return new Vector3Int(block.x, Mathf.Min(block.y + height, Chunk.NumSlices * Chunk.SliceHeight - 10), block.z);
        }

        [UnityTest]
        public IEnumerator ThePlayerStandsOnTheSpawnArea() {
            Vector3 start = TestGame.Player.transform.position;
            yield return TestGame.Wait(3);
            Vector3 now = TestGame.Player.transform.position;

            Assert.Less(start.y - now.y, 2f, "it didn't fall through the ground");
            Assert.IsTrue(Physics.Raycast(now, Vector3.down, out RaycastHit hit, 5f), "the ground under it has colliders");
            Assert.IsTrue(WorldBehaviour.Instance.IsWalkable(WorldBehaviour.ChunkAt(now)));
        }

        [UnityTest]
        public IEnumerator BlocksAndBricksAreSavedAndComeBack() {
            BlockType stone = (BlockType)BlockDatabase.IdOf("stone");
            Vector3Int floor = above(20);
            TestGame.Floor(floor, 1, stone);
            Vector3Int dug = floor + new Vector3Int(1, 0, 1);
            Assert.IsTrue(WorldNetwork.ServerSetBlock(dug, BlockType.Air, null));

            Item item = Server.items["brick_2x2"];
            BrickModel model = BrickModels.Get(item.brickModelId);
            Vector3Int cell = BrickGrid.BlockToCell(floor + Vector3Int.up);
            Brick brick = WorldNetwork.ServerPlaceBrick(item, 4, new BrickPlacement(model, cell), Placer.Now(0));
            Assert.AreSame(brick, BrickGrid.GetBrickAt(cell));

            yield return game.Stop();
            yield return game.Start();

            Assert.AreEqual(stone, TestGame.Block(floor));
            Assert.AreEqual(BlockType.Air, TestGame.Block(dug));
            yield return TestGame.WaitUntil(() => BrickGrid.GetBrickAt(cell) != null, 10, "the brick coming back");
            Brick loaded = BrickGrid.GetBrickAt(cell);
            Assert.AreEqual(brick.id, loaded.id);
            Assert.AreEqual("brick_2x2", loaded.itemId);
            Assert.AreEqual(4, loaded.color);
        }

        [UnityTest]
        public IEnumerator WaterPouredOnAFloorSpreads() {
            BlockType stone = (BlockType)BlockDatabase.IdOf("stone");
            BlockType water = (BlockType)BlockDatabase.IdOf("water");
            Vector3Int floor = above(20);
            TestGame.Floor(floor, 9, stone);

            Vector3Int source = floor + Vector3Int.up;
            Assert.IsTrue(WorldNetwork.ServerSetBlock(source, water, Placer.Now(0)));
            yield return TestGame.WaitUntil(() => BlockDatabase.Get(TestGame.Block(source + new Vector3Int(0, 0, 7))).fluid != null, 10, "the water reaching 7 blocks");

            BlockDefinition edge = BlockDatabase.Get(TestGame.Block(source + new Vector3Int(3, 0, 4)));
            Assert.AreEqual(7, edge.fluidLevel);
            Assert.IsNull(BlockDatabase.Get(TestGame.Block(source + new Vector3Int(8, 0, 0))).fluid, "no further");

            Assert.IsTrue(WorldNetwork.ServerSetBlock(source, BlockType.Air, null));
            yield return TestGame.WaitUntil(() => BlockDatabase.Get(TestGame.Block(source + new Vector3Int(0, 0, 7))).fluid == null, 10, "the water drying up");
        }
    }
}
