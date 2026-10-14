using System.Collections;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.Npcs;
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
        public IEnumerator HealthIsKeptBetweenSessions() {
            Combat.Health health = TestGame.Player.GetComponent<Combat.Health>();
            Assert.IsTrue(health.ServerDamage(new Combat.DamageInfo() { amount = 7, kind = Combat.DamageInfo.Kind.Script }));
            Assert.AreEqual(health.maxHealth - 7, health.health);
            int hurt = health.health;

            yield return game.Stop();
            yield return game.Start();
            health = TestGame.Player.GetComponent<Combat.Health>();
            Assert.AreEqual(hurt, health.health, "back with the health it left with");

            // left dead: back alive, at the spawn
            Vector3 died = TestGame.Player.transform.position + new Vector3(30, 0, 30);
            TestGame.Player.GetComponent<PlayerNetwork>().ServerTeleport(died);
            Vector3 diedAt = FloatingOrigin.ToAbsolute(died);
            yield return null;
            yield return TestGame.Wait(1); // past the hit's invulnerability
            Assert.IsTrue(health.ServerDamage(new Combat.DamageInfo() { amount = 100, kind = Combat.DamageInfo.Kind.Script }));
            Assert.IsTrue(health.isDead);
            yield return game.Stop();
            yield return game.Start();
            health = TestGame.Player.GetComponent<Combat.Health>();
            Assert.IsFalse(health.isDead);
            Assert.AreEqual(health.maxHealth, health.health);
            Assert.Greater(Vector3.Distance(FloatingOrigin.ToAbsolute(TestGame.Player.transform.position), diedAt), 20, "not where it died");
        }

        [UnityTest]
        public IEnumerator APotionHealsAndIsGone() {
            Combat.Health health = TestGame.Player.GetComponent<Combat.Health>();
            PlayerInventory inventory = TestGame.Player.GetComponent<PlayerInventory>();
            PlayerNetwork network = TestGame.Player.GetComponent<PlayerNetwork>();
            int slot = Player.FirstFastSlot + 5;
            Assert.IsTrue(inventory.ServerAdd("hp_potion", BrickColor.None, 2, 0, slot));

            network.CmdConsume(slot);
            yield return null;
            Assert.AreEqual(2, inventory.ServerGetSlot(slot).Value.quantity, "not used at full health");

            Assert.IsTrue(health.ServerDamage(new Combat.DamageInfo() { amount = 15, kind = Combat.DamageInfo.Kind.Script }));
            network.CmdConsume(slot);
            yield return null;
            Assert.AreEqual(health.maxHealth - 15 + 10, health.health);
            Assert.AreEqual(1, inventory.ServerGetSlot(slot).Value.quantity);

            network.CmdConsume(slot);
            yield return null;
            Assert.AreEqual(health.maxHealth, health.health, "no more than all of it");
            Assert.IsFalse(inventory.ServerGetSlot(slot).HasValue, "the last one is gone");
        }

        [UnityTest]
        public IEnumerator TheColorPickerPaintsBricks() {
            const int Blue = 1, Red = 4;
            BlockType stone = (BlockType)BlockDatabase.IdOf("stone");
            Vector3Int floor = above(3);
            TestGame.Floor(floor, 1, stone);
            Item brickItem = Server.items["brick_2x2"], glassItem = Server.items["glass_2x2"];
            Vector3Int cell = BrickGrid.BlockToCell(floor + Vector3Int.up);
            Brick brick = WorldNetwork.ServerPlaceBrick(brickItem, Red, new BrickPlacement(brickItem.brickModel, cell), Placer.Now(0));
            Brick glass = WorldNetwork.ServerPlaceBrick(glassItem, glassItem.color, new BrickPlacement(glassItem.brickModel, cell + new Vector3Int(0, 3, 0)), Placer.Now(0));

            PlayerInventory inventory = TestGame.Player.GetComponent<PlayerInventory>();
            PlayerNetwork network = TestGame.Player.GetComponent<PlayerNetwork>();
            int slot = Player.FirstFastSlot + 4;
            Assert.IsTrue(inventory.ServerAdd("color_picker", Server.items["color_picker"].color, 1, 0, slot));

            network.CmdSetToolColor(slot, Blue);
            yield return null;
            Assert.AreEqual(Blue, inventory.ServerGetSlot(slot).Value.color, "the picker takes the colour");

            network.CmdPaintBrick(slot, brick.id);
            network.CmdPaintBrick(slot, glass.id);
            yield return null;
            Assert.AreEqual(Blue, brick.color);
            Assert.AreEqual(glassItem.color, glass.color, "glass only takes transparent colours");

            yield return game.Stop();
            yield return game.Start();
            yield return TestGame.WaitUntil(() => BrickGrid.GetBrickAt(cell) != null, 10, "the brick coming back");
            Assert.AreEqual(Blue, BrickGrid.GetBrickAt(cell).color, "painted, saved");
        }

        [UnityTest]
        public IEnumerator ModsWriteInEverybodysChat() {
            System.Collections.Generic.List<Events.ChatLineReceivedEvent> lines = new System.Collections.Generic.List<Events.ChatLineReceivedEvent>();
            System.Action<Events.ChatLineReceivedEvent> received = line => lines.Add(line);
            Events.EventManager.ChatLineReceived.Subscribe(received);
            try {
                MoonSharp.Interpreter.Script script = new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.Preset_SoftSandbox);
                Scripting.LuaApi.Install(script, new Mods.ModInfo() { id = "test_mod", name = "Test mod" });
                script.DoString("chat.say('Hello from a mod') chat.event('A dragon was seen\\nnear spawn')");
                yield return TestGame.WaitUntil(() => lines.Count >= 2, 5, "both lines in the chat");

                Assert.AreEqual(Network.ChatCommands.ServerName, lines[0].sender, "said by the server");
                Assert.AreEqual("Hello from a mod", lines[0].text);
                Assert.IsTrue(lines[1].IsEvent, "an event, like a player joining");
                Assert.AreEqual("A dragon was seen near spawn", lines[1].text, "one line");
            } finally {
                Events.EventManager.ChatLineReceived.Unsubscribe(received);
            }
        }

        // the middle of a block's top, where an NPC stands on it
        private static Vector3 onTop(Vector3Int block) {
            return BrickGrid.CellToWorld(BrickGrid.BlockToCell(block + Vector3Int.up), new Vector3(BrickGrid.StudsPerBlock / 2f, 0.05f, BrickGrid.StudsPerBlock / 2f));
        }

        [UnityTest]
        public IEnumerator NpcsDontWalkIntoTheTerrainsWater() {
            BlockType stone = (BlockType)BlockDatabase.IdOf("stone");
            BlockType water = (BlockType)BlockDatabase.IdOf("still_water");
            // a stone floor two blocks thick, a pool sunk in it: the water has no collider, its bottom has
            Vector3Int floor = above(20);
            TestGame.Floor(floor + Vector3Int.down, 4, stone);
            TestGame.Floor(floor, 4, stone);
            Vector3Int pool = floor + new Vector3Int(1, 0, 0);
            Assert.IsTrue(WorldNetwork.ServerSetBlock(pool, water, Placer.Now(0)));
            Vector3 poolTop = onTop(pool);
            yield return TestGame.WaitUntil(() => Physics.Raycast(poolTop + Vector3.up * 3, Vector3.down, out RaycastHit hit, 6f)
                && hit.point.y < poolTop.y - 0.1f, 10, "the pool's bottom under its water");

            Npc golem = NpcSystem.ServerSpawn(NpcDatabase.Get("golem"), onTop(floor), 0);
            Assert.IsNotNull(golem);
            try {
                yield return null;
                System.Reflection.MethodInfo isWalkable = typeof(Npc).GetMethod("isWalkable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                CapsuleCollider body = golem.GetComponent<CapsuleCollider>();
                Vector3 standing = onTop(floor);
                // its capsule off, as when it checks (see Npc.canWalk)
                body.enabled = false;
                bool intoWater = (bool)isWalkable.Invoke(golem, new object[] { standing, poolTop - standing });
                bool ontoStone = (bool)isWalkable.Invoke(golem, new object[] { standing, onTop(floor - new Vector3Int(1, 0, 0)) - standing });
                body.enabled = true;
                Assert.IsFalse(intoWater, "the water isn't walked into");
                Assert.IsTrue(ontoStone, "the stone is");
            } finally {
                Mirror.NetworkServer.Destroy(golem.gameObject);
            }
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
