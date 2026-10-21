using System;
using System.Collections;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Network;
using Brickcraft.Npcs;
using Brickcraft.World;
using Brickcraft.World.Towns;
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
        public IEnumerator NpcsCanBeTurnedOffAndItsSaved() {
            Mirror.NetworkConnectionToClient admin = TestGame.Player.GetComponent<PlayerNetwork>().connectionToClient;
            Assert.IsTrue(WorldNetwork.NpcsOn, "on in a new world");
            string kind = null;
            foreach (NpcInfo info in NpcDatabase.All) {
                kind = kind ?? info.id;
            }
            ChatCommands.Run(admin, "/spawnnpc " + kind);
            yield return null;
            Assert.Greater(NpcSystem.All.Count, 0, "one spawned");

            ChatCommands.Run(admin, "/npcs off");
            yield return null;
            Assert.IsFalse(WorldNetwork.NpcsOn);
            foreach (Npc npc in NpcSystem.All) {
                Assert.IsTrue(npc == null || npc.IsPet, "only pets stay: " + (npc != null ? npc.name : ""));
            }

            yield return game.Stop();
            yield return game.Start();
            Assert.IsFalse(WorldNetwork.NpcsOn, "still off, saved");
            ChatCommands.Run(TestGame.Player.GetComponent<PlayerNetwork>().connectionToClient, "/npcs on");
            Assert.IsTrue(WorldNetwork.NpcsOn);
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
            // dirt: wild golems appear on stone, one could be in the way
            BlockType dirt = (BlockType)BlockDatabase.IdOf("dirt");
            BlockType water = (BlockType)BlockDatabase.IdOf("still_water");
            // a floor two blocks thick, a pool sunk in it: the water has no collider, its bottom has
            Vector3Int floor = above(20);
            TestGame.Floor(floor + Vector3Int.down, 4, dirt);
            TestGame.Floor(floor, 4, dirt);
            Vector3Int pool = floor + new Vector3Int(1, 0, 0);
            Assert.IsTrue(WorldNetwork.ServerSetBlock(pool, water, Placer.Now(0)));
            Vector3 poolTop = onTop(pool);
            yield return TestGame.WaitUntil(() => Physics.Raycast(poolTop + Vector3.up * 3, Vector3.down, out RaycastHit hit, 6f)
                && hit.point.y < poolTop.y - 0.1f, 10, "the pool's bottom under its water");

            // NPCs only go where the world is marked walkable, which comes a moment after it loads
            Vector3 dirtTop = onTop(floor - new Vector3Int(1, 0, 0));
            yield return TestGame.WaitUntil(() => NpcSystem.IsLoaded(poolTop) && NpcSystem.IsLoaded(dirtTop), 10, "the floor walkable to NPCs");

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
                bool ontoDirt = (bool)isWalkable.Invoke(golem, new object[] { standing, dirtTop - standing });
                body.enabled = true;
                Assert.IsFalse(intoWater, "the water isn't walked into");
                Assert.IsTrue(ontoDirt, "the dirt is");
            } finally {
                Mirror.NetworkServer.Destroy(golem.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator TheServerTellsEachPlayerInItsLanguage() {
            string languageKey = "settings." + GameSettings.LanguageSetting;
            string saved = PlayerPrefs.HasKey(languageKey) ? PlayerPrefs.GetString(languageKey) : null;
            System.Collections.Generic.List<Events.ChatLineReceivedEvent> lines = new System.Collections.Generic.List<Events.ChatLineReceivedEvent>();
            System.Action<Events.ChatLineReceivedEvent> received = line => lines.Add(line);
            Events.EventManager.ChatLineReceived.Subscribe(received);
            try {
                GameSettings.Language = "es";
                // the host is the save's admin: a command's answer comes back as a key, worded here
                ChatCommands.Run(Mirror.NetworkServer.localConnection, "/pvp");
                yield return TestGame.WaitUntil(() => lines.Count > 0, 5, "the answer");
                Assert.AreEqual(ChatCommands.ServerName, lines[0].sender);
                StringAssert.StartsWith("El PvP está ", lines[0].text);

                GameSettings.Language = "en";
                ChatCommands.Run(Mirror.NetworkServer.localConnection, "/pvp");
                yield return TestGame.WaitUntil(() => lines.Count > 1, 5, "the second answer");
                StringAssert.StartsWith("PvP is ", lines[1].text);
            } finally {
                Events.EventManager.ChatLineReceived.Unsubscribe(received);
                GameSettings.Language = saved ?? GameSettings.SystemLanguage;
                if (saved == null) {
                    PlayerPrefs.DeleteKey(languageKey);
                    PlayerPrefs.Save();
                }
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

        [UnityTest]
        public IEnumerator TownsAreBuiltWhereTheSeedPutsThem() {
            long seed = WorldNetwork.Seed;
            List<LDrawImport> buildings = TownBuildings.All;
            Assume.That(buildings.Count, Is.GreaterThan(0), "buildings to build with");

            Vector2Int spawnChunk = WorldBehaviour.ChunkAt(TestGame.Player.transform.position);
            int spawnRegionX = BrickGrid.FloorDiv(spawnChunk.x, TownPlanner.SpanChunks);
            int spawnRegionZ = BrickGrid.FloorDiv(spawnChunk.y, TownPlanner.SpanChunks);

            bool built = false;
            int towns = 0;

            for (int dx = -1; dx <= 1 && !built; dx++) {
                for (int dz = -1; dz <= 1 && !built; dz++) {
                    int regionX = spawnRegionX + dx, regionZ = spawnRegionZ + dz;
                    TownPlan plan = TownPlanner.Plan(seed, buildings, regionX, regionZ);
                    if (plan == null) {
                        continue;
                    }
                    towns++;

                    // go to the town's middle: the server generates what it stands on
                    int centerChunkX = regionX * TownPlanner.SpanChunks + TownPlanner.SpanChunks / 2;
                    int centerChunkZ = regionZ * TownPlanner.SpanChunks + TownPlanner.SpanChunks / 2;
                    Vector3Int block = new Vector3Int(centerChunkX * 16 + 8, 100, centerChunkZ * 16 + 8);
                    Teleport(BrickGrid.CellToWorld(BrickGrid.BlockToCell(block)));

                    List<Vector2Int> needed = TownPlacer.NeededChunks(plan, buildings);
                    yield return WaitFor(() => allGenerated(needed), 30);
                    TownPlacer.PlaceRegion(regionX, regionZ);
                    built = WorldNetwork.ServerHasGeneratedBricks(needed);
                }
            }

            Assume.That(towns, Is.GreaterThan(0), "a town near the spawn");
            Assert.IsTrue(built, "a town's bricks appeared once its ground was there");
        }

        [UnityTest]
        public IEnumerator AdminsCanAskWhereTheClosestThingIs() {
            string languageKey = "settings." + GameSettings.LanguageSetting;
            string saved = PlayerPrefs.HasKey(languageKey) ? PlayerPrefs.GetString(languageKey) : null;
            List<Events.ChatLineReceivedEvent> lines = new List<Events.ChatLineReceivedEvent>();
            Action<Events.ChatLineReceivedEvent> received = line => lines.Add(line);
            Events.EventManager.ChatLineReceived.Subscribe(received);
            try {
                GameSettings.Language = "en";
                ChatCommands.Run(Mirror.NetworkServer.localConnection, "/closest desert");
                yield return TestGame.WaitUntil(() => lines.Count > 0, 10, "the answer to /closest desert");
                Assert.AreEqual(ChatCommands.ServerName, lines[0].sender);
                StringAssert.Contains("Desert", lines[0].text);

                lines.Clear();
                ChatCommands.Run(Mirror.NetworkServer.localConnection, "/closest nonsense");
                yield return TestGame.WaitUntil(() => lines.Count > 0, 10, "the usage of /closest");
                StringAssert.Contains("/closest", lines[0].text);
            } finally {
                Events.EventManager.ChatLineReceived.Unsubscribe(received);
                GameSettings.Language = saved ?? GameSettings.SystemLanguage;
                if (saved == null) {
                    PlayerPrefs.DeleteKey(languageKey);
                    PlayerPrefs.Save();
                }
            }
        }

        [UnityTest]
        public IEnumerator CoordinatesShowWhereThePlayerIs() {
            UI.PlayerPanel panel = UI.PlayerPanel.Instance;
            Assume.That(panel, Is.Not.Null, "the player panel on the canvas");
            bool saved = GameSettings.ShowCoordinates;
            try {
                yield return TestGame.StandStill();

                GameSettings.ShowCoordinates = true;
                yield return null;
                Assert.IsTrue(panel.coordinates.gameObject.activeSelf, "shown when the setting is on");

                Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(TestGame.Player.transform.position));
                string expected = "X: " + block.x + "   Y: " + block.y + "   Z: " + block.z;
                yield return TestGame.WaitUntil(() => panel.coordinates.text == expected, 5, "the coordinates of the player");

                GameSettings.ShowCoordinates = false;
                yield return null;
                Assert.IsFalse(panel.coordinates.gameObject.activeSelf, "hidden when the setting is off");
            } finally {
                GameSettings.ShowCoordinates = saved;
            }
        }

        [UnityTest]
        public IEnumerator TheClosestTownIsOneThatStands() {
            long seed = WorldNetwork.Seed;
            List<LDrawImport> buildings = TownBuildings.All;
            Assume.That(buildings.Count, Is.GreaterThan(0), "buildings to build with");

            Vector2Int spawnChunk = WorldBehaviour.ChunkAt(TestGame.Player.transform.position);
            int spawnRegionX = BrickGrid.FloorDiv(spawnChunk.x, TownPlanner.SpanChunks);
            int spawnRegionZ = BrickGrid.FloorDiv(spawnChunk.y, TownPlanner.SpanChunks);

            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    int regionX = spawnRegionX + dx, regionZ = spawnRegionZ + dz;
                    TownPlan plan = TownPlanner.Plan(seed, buildings, regionX, regionZ);
                    if (plan == null) {
                        continue; // no town planned here
                    }
                    // go to its middle: the server generates what it stands on, and builds it
                    int centerChunkX = regionX * TownPlanner.SpanChunks + TownPlanner.SpanChunks / 2;
                    int centerChunkZ = regionZ * TownPlanner.SpanChunks + TownPlanner.SpanChunks / 2;
                    Vector3Int middle = new Vector3Int(centerChunkX * 16 + 8, 100, centerChunkZ * 16 + 8);
                    Teleport(BrickGrid.CellToWorld(BrickGrid.BlockToCell(middle)));

                    List<Vector2Int> needed = TownPlacer.NeededChunks(plan, buildings);
                    yield return WaitFor(() => allGenerated(needed), 30);
                    yield return null;

                    Vector3Int at = BrickGrid.CellToBlock(BrickGrid.WorldToCell(TestGame.Player.transform.position));
                    if (Closest.Find("town", at, out Vector3Int town)) {
                        Assert.Greater(town.y, 0, "the town's height");
                        long ax = town.x - at.x, az = town.z - at.z;
                        Assert.Less((long)Mathf.Sqrt(ax * ax + az * az), 300, "near where it was looked for");
                        yield break;
                    }
                }
            }
            Assert.Ignore("no town near the spawn to check");
        }

        // moves the player (server) and keeps it from dying of the trip
        private static void Teleport(Vector3 position) {
            Combat.Health health = TestGame.Player.GetComponent<Combat.Health>();
            health.health = health.maxHealth;
            TestGame.Player.GetComponent<PlayerNetwork>().ServerTeleport(position);
        }

        private static bool allGenerated(List<Vector2Int> chunks) {
            foreach (Vector2Int coords in chunks) {
                if (!WorldBehaviour.Instance.IsGenerated(coords)) {
                    return false;
                }
            }
            return true;
        }

        // waits for the condition, without failing (the caller decides what a timeout means)
        private static IEnumerator WaitFor(Func<bool> condition, float seconds) {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) {
                yield return null;
            }
        }
    }
}
