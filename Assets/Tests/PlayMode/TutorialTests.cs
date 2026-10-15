using System.Collections;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.Tutorials;
using Brickcraft.Vehicles;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>The tutorials (Tutorials.Tutorial) played: their flat world, what the player gets, the guide's steps.</summary>
    [Timeout(120000)]
    public class TutorialTests
    {
        private TestGame game;

        [UnitySetUp]
        public IEnumerator SetUp() {
            game = new TestGame();
            // the test's world is a tutorial's, like SavedWorlds.CreateTutorial makes them
            WorldStorage.Create(game.SaveFolder, "Tutorial test", 1, Difficulty.Peaceful, false, flat: true, tutorial: FirstCar.Id, npcsOn: false);
            yield return game.Start();
            yield return TestGame.StandStill();
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            VehicleControls.Override = null;
            yield return game.Delete();
        }

        private static Item item(string id) {
            Assume.That(Server.items.ContainsKey(id), id + " in the catalogue");
            return Server.items[id];
        }

        private static int indexOf(BrickModel model, System.Func<Connector, bool> which) {
            for (int i = 0; i < model.connectors.Length; i++) {
                if (which(model.connectors[i])) {
                    return i;
                }
            }
            Assert.Fail(model.id + " has no connector like that");
            return -1;
        }

        // the first of those joints that it can be held with, connected; null if none
        private static Brick connect(Brick holder, Item part, IEnumerable<BrickJoint> joints, System.Func<Brick, bool> good = null) {
            foreach (BrickJoint joint in joints) {
                if (BrickJoints.WhyNot(holder, part.brickModel, joint) != null) {
                    continue;
                }
                Brick brick = VehicleSystem.ServerConnect(part, part.color, holder, joint);
                if (good == null || good(brick)) {
                    return brick;
                }
                // destroyed at the end of the frame: out of the way of the next one now
                GameObject removed = brick.gameObject;
                VehicleSystem.ServerRemove(brick);
                removed.SetActive(false);
            }
            return null;
        }

        private static IEnumerator stepIs(int step, string what) {
            yield return null;
            Assert.AreEqual(step, UI.TutorialGuide.Step, what);
        }

        [UnityTest]
        public IEnumerator TheFirstCarIsBuiltStepByStepAndDrives() {
            Assert.IsTrue(WorldNetwork.Flat, "a flat world");
            Vector3Int under = TestGame.PlayerBlock;
            under.y = World.CustomGenerator.CustomChunkGenerator.FlatGrassHeight;
            Assert.AreEqual(BlockType.Grass, TestGame.Block(under), "grass");
            Assert.AreEqual(BlockType.Air, TestGame.Block(under + Vector3Int.up), "nothing on it");
            Assert.AreEqual(BlockType.Grass, TestGame.Block(under + new Vector3Int(7, 0, -5)), "the same everywhere");
            PlayerInventory inventory = TestGame.Player.GetComponent<PlayerInventory>();
            for (int i = 0; i < FirstCar.Tutorial.items.Length; i++) {
                InventoryItem? there = inventory.ServerGetSlot(Player.FirstFastSlot + i);
                Assert.IsTrue(there.HasValue, "the parts in the hotbar");
                Assert.AreEqual(FirstCar.Tutorial.items[i].itemId, there.Value.itemId);
            }
            Assert.IsFalse(System.Linq.Enumerable.Any(inventory.items, stack => stack.itemId == "dirt"), "not the usual starter items");
            yield return stepIs(0, "the guide on its first step");

            Item wheelItem = item(FirstCar.Wheel), axleItem = item(FirstCar.Axle), beamItem = item(FirstCar.Beam), topItem = item(FirstCar.Top), seatItem = item(FirstCar.Seat);
            int hub = indexOf(wheelItem.brickModel, c => !c.male && c.HasAxleShape);

            // a wheel on the ground
            Vector3Int ground = TestGame.PlayerBlock + new Vector3Int(12, 0, 0);
            Vehicle car = VehicleSystem.ServerCreate(wheelItem, wheelItem.color, new BrickPlacement(wheelItem.brickModel, BrickGrid.BlockToCell(ground)));
            Brick first = new List<Brick>(car.grid.Bricks)[0];
            yield return stepIs(1, "a wheel: the axle next");

            // an axle in its hub
            int along = BrickJoints.MaxSlide(wheelItem.brickModel.connectors[hub], axleItem.brickModel.connectors[0]);
            Brick axle = connect(first, axleItem, new[] {
                new BrickJoint() { holderConnector = hub, connector = 0, slide = along },
                new BrickJoint() { holderConnector = hub, connector = 0, slide = -along },
            });
            Assert.IsNotNull(axle, "an axle in the wheel");
            yield return stepIs(2, "the technic brick next");

            // the technic brick on it by an end hole, studs up
            int firstHole = -1, lastHole = -1;
            for (int i = 0; i < beamItem.brickModel.connectors.Length; i++) {
                Connector c = beamItem.brickModel.connectors[i];
                if (c.male || c.HasAxleShape) {
                    continue;
                }
                if (firstHole < 0 || c.Middle.x < beamItem.brickModel.connectors[firstHole].Middle.x) { firstHole = i; }
                if (lastHole < 0 || c.Middle.x > beamItem.brickModel.connectors[lastHole].Middle.x) { lastHole = i; }
            }
            List<BrickJoint> onAxle = new List<BrickJoint>();
            // anywhere along it (where the player isn't in the way)
            int slides = BrickJoints.MaxSlide(axleItem.brickModel.connectors[0], beamItem.brickModel.connectors[firstHole]);
            for (int slide = 0; slide <= slides; slide++) {
                foreach (int way in new[] { slide, -slide }) {
                    for (int spin = 0; spin < 4; spin++) {
                        foreach (bool flip in new[] { false, true }) {
                            onAxle.Add(new BrickJoint() { holderConnector = 0, connector = firstHole, slide = way, spin = spin, flip = flip });
                        }
                    }
                }
            }
            Brick beam = connect(axle, beamItem, onAxle, b => FirstCar.Tutorial.steps[2].isDone());
            Assert.IsNotNull(beam, "the technic brick on the axle, studs up");
            yield return stepIs(3, "the second axle next");

            Brick axle2 = connect(beam, axleItem, new[] { new BrickJoint() { holderConnector = lastHole, connector = 0 } });
            Assert.IsNotNull(axle2, "an axle through its other end hole");
            yield return stepIs(4, "the wheels next");

            // a wheel on each free axle end
            foreach (Brick rod in new[] { axle, axle2 }) {
                int max = BrickJoints.MaxSlide(rod.model.connectors[0], wheelItem.brickModel.connectors[hub]);
                foreach (int slide in new[] { max, -max }) {
                    connect(rod, wheelItem, new[] { new BrickJoint() { holderConnector = 0, connector = hub, slide = slide, flip = slide < 0 } });
                }
            }
            Assert.AreEqual(4, FirstCar.Count(FirstCar.Wheel), "four wheels");
            yield return stepIs(5, "the brick on top next");

            int height = beamItem.brickModel.heightInPlates;
            Brick top = connect(beam, topItem, new[] { BrickJoint.OnStuds(new Vector3Int(0, height, 0), 0), BrickJoint.OnStuds(new Vector3Int(0, height, 0), 1) });
            Assert.IsNotNull(top, "a brick on its studs");
            yield return stepIs(6, "the seat next");

            // the seat on it, facing the way it goes
            List<BrickJoint> onTop = new List<BrickJoint>();
            for (int rotation = 0; rotation < 4; rotation++) {
                for (int x = -1; x < 8; x++) {
                    for (int z = -1; z < 8; z++) {
                        onTop.Add(BrickJoint.OnStuds(new Vector3Int(x, topItem.brickModel.heightInPlates, z), rotation));
                    }
                }
            }
            Brick seat = connect(top, seatItem, onTop, s => FirstCar.Tutorial.steps[6].isDone());
            Assert.IsNotNull(seat, "a seat on the brick, facing along it");
            yield return stepIs(7, "sitting next");

            Assert.IsTrue(VehicleSystem.ServerSit(TestGame.Player.GetComponent<PlayerNetwork>(), seat));
            yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == 8, 5, "driving next");
            Assert.AreEqual(4, car.Drive.WheelCount, "it rolls on its four wheels");

            VehicleControls.Override = new VehicleInput() { throttle = 1 };
            yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == 9, 30, FirstCar.DriveDistance + " metres driven");
            VehicleControls.Override = null;
            VehicleSystem.ServerStandUp(TestGame.Player.GetComponent<PlayerNetwork>(), Vector3.zero, Vector3.zero);
        }

        [UnityTest]
        public IEnumerator TutorialWorldsAreMadeNewAndArentListed() {
            yield return null;
            const string id = "zz_test_tutorial";
            string saveName = SavedWorlds.CreateTutorial(id, "A tutorial");
            try {
                string folder = System.IO.Path.Combine(SavedWorlds.Folder, saveName);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "left.txt"), "from before");
                Assert.AreEqual(saveName, SavedWorlds.CreateTutorial(id, "A tutorial"), "the same folder");
                Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(folder, "left.txt")), "made new");
                WorldStorage info = WorldStorage.ReadInfo(folder);
                Assert.IsTrue(info.Flat);
                Assert.AreEqual(id, info.Tutorial);
                Assert.IsFalse(info.NpcsOn, "no NPCs");
                Assert.IsFalse(SavedWorlds.List().Exists(w => w.Tutorial == id), "not among the worlds to play");
            } finally {
                SavedWorlds.Delete(saveName);
            }
        }
    }
}
