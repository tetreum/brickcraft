using System.Collections;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.Tutorials;
using Brickcraft.Vehicles;
using Brickcraft.World;
using Brickcraft.World.CustomGenerator;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>
    /// The tutorials (Tutorials.Tutorial) played: their world, what the player gets, the guide's steps as the vehicle is
    /// built the way a player would (what the placer would send), and the vehicle going at the end.
    /// </summary>
    [Timeout(180000)]
    public class TutorialTests
    {
        private TestGame game;

        [SetUp]
        public void SetUp() {
            game = new TestGame();
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            VehicleControls.Override = null;
            yield return game.Delete();
        }

        // its world, like SavedWorlds.CreateTutorial makes them, played
        private IEnumerator play(Tutorial tutorial) {
            WorldStorage.Create(game.SaveFolder, "Tutorial test", 1, Difficulty.Peaceful, false, terrain: tutorial.terrain, tutorial: tutorial.id, npcsOn: false);
            yield return game.Start();
            yield return TestGame.StandStill();
            Assert.AreEqual(tutorial.terrain, WorldNetwork.Terrain);
            PlayerInventory inventory = TestGame.Player.GetComponent<PlayerInventory>();
            for (int i = 0; i < tutorial.items.Length; i++) {
                InventoryItem? there = inventory.ServerGetSlot(Player.FirstFastSlot + i);
                Assert.IsTrue(there.HasValue, "its parts in the hotbar");
                Assert.AreEqual(tutorial.items[i].itemId, there.Value.itemId);
            }
            Assert.IsFalse(System.Linq.Enumerable.Any(inventory.items, stack => stack.itemId == "dirt"), "not the usual starter items");
            yield return stepIs(0, "the guide on its first step");
            // the house ones have no vehicle
            if (tutorial != FirstHouse.Tutorial && tutorial != FirstExport.Tutorial) {
                Assert.IsNotNull(demo(), "a finished one to look at");
                Assert.IsNull(TutorialChecks.Built(), "it isn't the one being built");
            }
        }

        // the tutorial's finished vehicle, shown
        private static Vehicle demo() {
            return System.Linq.Enumerable.FirstOrDefault(Vehicle.All, TutorialChecks.IsDemo);
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

        // gone now (destroyed at the end of the frame): out of the way of the next try
        private static void takeBack(Brick brick) {
            GameObject removed = brick.gameObject;
            VehicleSystem.ServerRemove(brick);
            removed.SetActive(false);
        }

        // the first of those joints that it can be held with (and is good), connected; null if none
        private static Brick connect(Brick holder, Item part, IEnumerable<BrickJoint> joints, System.Func<Brick, bool> good = null) {
            foreach (BrickJoint joint in joints) {
                if (BrickJoints.WhyNot(holder, part.brickModel, joint) != null) {
                    continue;
                }
                Brick brick = VehicleSystem.ServerConnect(part, part.color, holder, joint);
                if (good == null || good(brick)) {
                    return brick;
                }
                takeBack(brick);
            }
            return null;
        }

        // on a vehicle's cells, the first of those placements that's free and touches it (and is good); null if none
        private static Brick place(Vehicle vehicle, Item part, IEnumerable<BrickPlacement> placements, System.Func<Brick, bool> good = null) {
            foreach (BrickPlacement placement in placements) {
                if (!vehicle.grid.IsFree(placement) || vehicle.grid.Touching(placement).Count == 0 || VehicleSystem.OverlapsOthers(vehicle, placement)) {
                    continue;
                }
                Brick brick = VehicleSystem.ServerPlace(vehicle, part, part.color, placement);
                if (good == null || good(brick)) {
                    return brick;
                }
                takeBack(brick);
            }
            return null;
        }

        // on a brick's studs, the first of those on its grid where it goes (and is good)
        private static Brick onStuds(Brick holder, Item part, IEnumerable<Vector3Int> origins, System.Func<Brick, bool> good = null) {
            List<BrickJoint> joints = new List<BrickJoint>();
            foreach (Vector3Int origin in origins) {
                for (int rotation = 0; rotation < 4; rotation++) {
                    joints.Add(BrickJoint.OnStuds(origin, rotation));
                }
            }
            return connect(holder, part, joints, good);
        }

        // a row of cells on top of a brick's grid, around its middle first
        private static IEnumerable<Vector3Int> around(int x, int z, int y, int reach) {
            for (int distance = 0; distance <= reach; distance++) {
                for (int dx = -distance; dx <= distance; dx++) {
                    for (int dz = -distance; dz <= distance; dz++) {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == distance) {
                            yield return new Vector3Int(x + dx, y, z + dz);
                        }
                    }
                }
            }
        }

        private static IEnumerator stepIs(int step, string what) {
            yield return null;
            Assert.AreEqual(step, UI.TutorialGuide.Step, what);
        }

        // a car's chassis the technic way, step by step (the car's and the plane's first 6): a wheel on the ground, an
        // axle in it, a technic brick on the axle (studs up), an axle through its other end, three more wheels, a brick
        // on top. Returns the brick on top.
        private static IEnumerator chassis(Tutorial tutorial, string wheelId, string axleId, string beamId, string topId, System.Action<Brick> built) {
            Item wheelItem = item(wheelId), axleItem = item(axleId), beamItem = item(beamId), topItem = item(topId);
            int hub = indexOf(wheelItem.brickModel, c => !c.male && c.HasAxleShape);

            Vector3Int ground = TestGame.PlayerBlock + new Vector3Int(12, 0, 0);
            Vehicle car = VehicleSystem.ServerCreate(wheelItem, wheelItem.color, new BrickPlacement(wheelItem.brickModel, BrickGrid.BlockToCell(ground)));
            Brick first = new List<Brick>(car.grid.Bricks)[0];
            yield return stepIs(1, "a wheel: the axle next");

            int along = BrickJoints.MaxSlide(wheelItem.brickModel.connectors[hub], axleItem.brickModel.connectors[0]);
            Brick axle = connect(first, axleItem, new[] {
                new BrickJoint() { holderConnector = hub, connector = 0, slide = along },
                new BrickJoint() { holderConnector = hub, connector = 0, slide = -along },
            });
            Assert.IsNotNull(axle, "an axle in the wheel");
            yield return stepIs(2, "the technic brick next");

            int firstHole = -1, lastHole = -1;
            for (int i = 0; i < beamItem.brickModel.connectors.Length; i++) {
                Connector c = beamItem.brickModel.connectors[i];
                if (c.male || c.HasAxleShape) {
                    continue;
                }
                if (firstHole < 0 || c.Middle.x < beamItem.brickModel.connectors[firstHole].Middle.x) { firstHole = i; }
                if (lastHole < 0 || c.Middle.x > beamItem.brickModel.connectors[lastHole].Middle.x) { lastHole = i; }
            }
            // anywhere along it (where the player isn't in the way), studs up
            List<BrickJoint> onAxle = new List<BrickJoint>();
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
            Brick beam = connect(axle, beamItem, onAxle, b => tutorial.steps[2].isDone());
            Assert.IsNotNull(beam, "the technic brick on the axle, studs up");
            yield return stepIs(3, "the second axle next");

            Brick axle2 = connect(beam, axleItem, new[] { new BrickJoint() { holderConnector = lastHole, connector = 0 } });
            Assert.IsNotNull(axle2, "an axle through its other end hole");
            yield return stepIs(4, "the wheels next");

            foreach (Brick rod in new[] { axle, axle2 }) {
                int max = BrickJoints.MaxSlide(rod.model.connectors[0], wheelItem.brickModel.connectors[hub]);
                foreach (int slide in new[] { max, -max }) {
                    connect(rod, wheelItem, new[] { new BrickJoint() { holderConnector = 0, connector = hub, slide = slide, flip = slide < 0 } });
                }
            }
            Assert.AreEqual(4, TutorialChecks.Count(wheelId), "four wheels");
            yield return stepIs(5, "the brick on top next");

            int height = beamItem.brickModel.heightInPlates;
            Brick top = connect(beam, topItem, new[] { BrickJoint.OnStuds(new Vector3Int(0, height, 0), 0), BrickJoint.OnStuds(new Vector3Int(0, height, 0), 1) });
            Assert.IsNotNull(top, "a brick on its studs");
            yield return stepIs(6, "what goes on it next");
            built(top);
        }

        private static IEnumerator sitAndGo(Brick seat, int lastStep, float seconds, string what) {
            Assert.IsTrue(VehicleSystem.ServerSit(TestGame.Player.GetComponent<PlayerNetwork>(), seat));
            yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == lastStep, 5, "the last step next");
            VehicleControls.Override = new VehicleInput() { throttle = 1 };
            Vehicle vehicle = seat.vehicle;
            Vector3 from = FloatingOrigin.ToAbsolute(vehicle.transform.position);
            string trace = "";
            float end = Time.time + seconds;
            while (UI.TutorialGuide.Step != lastStep + 1) {
                if (Time.time > end) {
                    Assert.Fail(what + " didn't happen: " + trace);
                }
                yield return TestGame.Wait(1);
                Vector3 at = FloatingOrigin.ToAbsolute(vehicle.transform.position);
                trace += "[" + (at - from).ToString("F1") + " v=" + vehicle.Body.velocity.magnitude.ToString("F1") + " up=" + vehicle.transform.up.ToString("F2") + " sim=" + vehicle.IsSimulated + "]";
            }
            // and it keeps going, the right way up
            yield return TestGame.Wait(3);
            Assert.Greater(vehicle.transform.up.y, 0.8f, "still the right way up: " + trace);
            VehicleControls.Override = null;
            VehicleSystem.ServerStandUp(TestGame.Player.GetComponent<PlayerNetwork>(), Vector3.zero, Vector3.zero);
        }

        [UnityTest]
        public IEnumerator TheFirstHouseIsBuiltAndSleptIn() {
            yield return play(FirstHouse.Tutorial);
            Assert.IsEmpty(TutorialChecks.Placed(FirstHouse.Frame), "the finished house isn't the player's");
            Assert.AreEqual(1, System.Linq.Enumerable.Count(Server.bricks.Values, b => b.itemId == FirstHouse.Door), "the finished house: its door");
            Assert.AreEqual(1, System.Linq.Enumerable.Count(Server.bricks.Values, b => b.itemId == FirstHouse.Lid), "and its chest's lid");

            // the player's house, in front of it: step by step to the night
            Vector3Int block = TestGame.PlayerBlock;
            Vector3Int corner = BrickGrid.BlockToCell(new Vector3Int(block.x - 3, CustomChunkGenerator.FlatGrassHeight + 1, block.z + 4));
            Assert.IsTrue(FirstHouse.Build(corner, false), "the house built");
            yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == 9, 5, "the house finished: sleeping next");
            Assert.AreEqual(36, FirstHouse.WallBricks(), "its walls: 18 bricks 1x6 and 18 1x4");
            yield return TestGame.WaitUntil(() => !WorldTime.IsDay, 3, "night falling");

            // in the bed till morning
            Brick bed = TutorialChecks.Placed(FirstHouse.Bed)[0];
            PlayerNetwork network = TestGame.Player.GetComponent<PlayerNetwork>();
            network.CmdSleep(bed.id);
            yield return TestGame.WaitUntil(() => !string.IsNullOrEmpty(network.sleepingIn), 3, "asleep");
            yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == 10, 20, "the night slept through");
            Assert.IsTrue(WorldTime.IsDay, "morning");
        }

        [UnityTest]
        public IEnumerator TheFirstModelIsListedForRealLife() {
            yield return play(FirstExport.Tutorial);
            // the house, all the player's own (the parts lister lists only what it placed)
            List<Brick> house = new List<Brick>(Server.bricks.Values);
            Assert.Greater(house.Count, 300, "the house built");
            int playerId = WorldNetwork.ServerPlacerOf(house[0]).playerId;
            Assert.AreNotEqual(0, playerId, "placed by the player");
            Assert.IsTrue(house.TrueForAll(b => WorldNetwork.ServerPlacerOf(b).playerId == playerId), "all of it");

            // the parts lister (in hand from the start) used on the house's nearest brick
            Vector3 eye = Camera.main.transform.position;
            Brick nearest = house[0];
            foreach (Brick brick in house) {
                if (Vector3.Distance(eye, brick.WorldBounds.center) < Vector3.Distance(eye, nearest.WorldBounds.center)) {
                    nearest = brick;
                }
            }
            TestGame.Player.GetComponent<PartsLister>().CmdStartOnBrick(Player.FirstFastSlot, nearest.id);
            yield return TestGame.WaitUntil(() => PartsListerView.IsOpen, 5, "its box around the house");
            yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == 1, 3, "saving next");

            // saved: done, its files written
            string name = "zz_tutorial_test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            string folder = System.IO.Path.Combine(PartsLister.Folder, name);
            try {
                PartsListerView.Current.Save(name);
                yield return TestGame.WaitUntil(() => UI.TutorialGuide.Step == FirstExport.Tutorial.steps.Length, 10, "saved: all done");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(folder, name + ".xml")), "its BrickLink wanted list");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(folder, name + ".ldr")), "and its LDraw model");
            } finally {
                if (System.IO.Directory.Exists(folder)) {
                    System.IO.Directory.Delete(folder, true);
                }
            }
        }

        [UnityTest]
        public IEnumerator TheFirstCarIsBuiltStepByStepAndDrives() {
            yield return play(FirstCar.Tutorial);
            Assert.AreEqual(4, TutorialChecks.Parts(demo(), FirstCar.Wheel).Count, "the finished car: its wheels");
            Assert.IsTrue(TutorialChecks.Parts(demo(), FirstCar.Seat).Exists(s => TutorialChecks.FacesAcross(s, FirstCar.Axle)), "and its seat");
            Vector3Int under = TestGame.PlayerBlock;
            under.y = CustomChunkGenerator.FlatGrassHeight;
            Assert.AreEqual(BlockType.Grass, TestGame.Block(under), "grass");
            Assert.AreEqual(BlockType.Air, TestGame.Block(under + Vector3Int.up), "nothing on it");
            Assert.AreEqual(BlockType.Grass, TestGame.Block(under + new Vector3Int(7, 0, 5)), "the same everywhere");

            Brick top = null;
            yield return chassis(FirstCar.Tutorial, FirstCar.Wheel, FirstCar.Axle, FirstCar.Beam, FirstCar.Top, built => top = built);
            Item topItem = item(FirstCar.Top);
            Brick seat = onStuds(top, item(FirstCar.Seat), around(3, 0, topItem.brickModel.heightInPlates, 4), s => FirstCar.Tutorial.steps[6].isDone());
            Assert.IsNotNull(seat, "a seat on the brick, facing along it");
            yield return stepIs(7, "sitting next");
            // its drive takes in its new parts at the next physics step
            Vehicle car = seat.vehicle;
            yield return TestGame.WaitUntil(() => car.Drive.WheelCount == 4, 2, "it rolls on its four wheels");
            yield return sitAndGo(seat, 8, 30, FirstCar.DriveDistance + " metres driven");
        }

        [UnityTest]
        public IEnumerator TheFirstPlaneIsBuiltStepByStepAndTakesOff() {
            yield return play(FirstPlane.Tutorial);
            Assert.AreEqual(4, TutorialChecks.Parts(demo(), FirstPlane.Wheel).Count, "the finished plane: its wheels");
            Assert.AreEqual(1, TutorialChecks.Parts(demo(), FirstPlane.Wing).Count, "its wing");
            Assert.IsTrue(TutorialChecks.Parts(demo(), FirstPlane.Propeller).Exists(FirstPlane.IsGoodPropeller), "its propeller");
            Assert.IsTrue(TutorialChecks.Parts(demo(), FirstPlane.Seat).Exists(s => TutorialChecks.FacesAcross(s, FirstPlane.Axle)), "and its seat");
            Brick top = null;
            yield return chassis(FirstPlane.Tutorial, FirstPlane.Wheel, FirstPlane.Axle, FirstPlane.Beam, FirstPlane.Top, built => top = built);

            // the wing on the brick, its middle over the brick's
            Item topItem = item(FirstPlane.Top), wingItem = item(FirstPlane.Wing);
            Vector3Int topSize = BrickPlacement.SizeFor(topItem.brickModel, 0);
            Brick wing = onStuds(top, wingItem, around(topSize.x / 2 - wingItem.brickModel.width / 2, topSize.z / 2 - wingItem.brickModel.depth / 2,
                topItem.brickModel.heightInPlates, 6));
            Assert.IsNotNull(wing, "the wing on the brick");
            yield return stepIs(7, "the propeller next");

            // the propeller on its edge, pushing the way it goes; the seat facing it
            int on = wingItem.brickModel.heightInPlates, size = wingItem.brickModel.width;
            // near its middle (its weight and push where the plane's are), pushing the way it goes
            List<Vector3Int> edges = new List<Vector3Int>(around(size / 2 - 3, size / 2 - 1, on, 6));
            Brick propeller = onStuds(wing, item(FirstPlane.Propeller), edges, p => FirstPlane.Tutorial.steps[7].isDone());
            Assert.IsNotNull(propeller, "a propeller on the wing's edge, pushing ahead");
            yield return stepIs(8, "the seat next");
            Brick seat = onStuds(wing, item(FirstPlane.Seat), around(size / 2 - 1, size / 2 - 1, on, 4), s => FirstPlane.Tutorial.steps[8].isDone());
            Assert.IsNotNull(seat, "a seat on the wing, facing ahead");
            yield return stepIs(9, "sitting next");
            // its drive takes in its new parts at the next physics step
            Vehicle plane = seat.vehicle;
            yield return TestGame.WaitUntil(() => plane.Drive.WingCount == 1 && plane.Drive.PropellerCount == 1, 2, "a wing and a propeller that work");
            yield return sitAndGo(seat, 10, 40, FirstPlane.FlyHeight + " metres up");
        }

        [UnityTest]
        public IEnumerator TheFirstBoatIsStartedOnTheWaterAndSails() {
            yield return play(FirstBoat.Tutorial);
            Assert.IsTrue(TutorialChecks.Parts(demo(), FirstBoat.Seat).Exists(FirstBoat.IsOnEvenRaft), "the finished boat: its raft");
            Assert.IsTrue(TutorialChecks.Parts(demo(), FirstBoat.Propeller).Exists(FirstBoat.PushesFromBehind), "and its propeller");
            Vector3Int player = TestGame.PlayerBlock;
            int surface = CustomChunkGenerator.FlatGrassHeight;
            Assert.AreEqual(BlockType.Still_Water, TestGame.Block(new Vector3Int(player.x, surface, CustomChunkGenerator.CoastSeaZ + 2)), "a sea ahead");
            Assert.AreEqual(BlockType.Sand, TestGame.Block(new Vector3Int(player.x, surface, CustomChunkGenerator.CoastSeaZ - 1)), "past a beach");

            Assert.AreEqual(BlockType.Sand, TestGame.Block(new Vector3Int(player.x, surface - 1, CustomChunkGenerator.CoastSeaZ)), "shallow by the shore");
            Assert.AreEqual(BlockType.Still_Water, TestGame.Block(new Vector3Int(player.x, surface - 4, CustomChunkGenerator.CoastSeaZ + 8)), "deep further out");

            // in the shallows it wades (it walks out up the beach's step), it doesn't swim
            PlayerNetwork network = TestGame.Player.GetComponent<PlayerNetwork>();
            Vector3 shallows = BrickGrid.CellToWorld(BrickGrid.BlockToCell(new Vector3Int(player.x, surface, CustomChunkGenerator.CoastSeaZ + 1)));
            network.ServerTeleport(shallows + new Vector3(0.4f, 1.2f, 0.4f));
            yield return TestGame.Wait(1);
            Assert.AreEqual(FirstPersonController.WaterDepth.Feet, Player.Instance.firstPersonController.Depth, "wading in the shallows");

            // on the beach, the seat in hand, looking at the water: the preview on its surface
            Vector3 shore = BrickGrid.CellToWorld(BrickGrid.BlockToCell(new Vector3Int(player.x, surface + 1, CustomChunkGenerator.CoastSeaZ - 1)));
            network.ServerTeleport(shore + new Vector3(0.4f, 1.2f, 0.4f)); // its position is its middle
            yield return TestGame.Wait(1);
            Assert.Less(Vector3.Distance(TestGame.Player.transform.position, shore), 3, "on the beach");
            if (Player.Instance.SelectedSlot != Player.FirstFastSlot) {
                Player.Instance.selectFastSlot(0); // the seat (selecting it again would empty the hand)
            }
            Transform eye = Camera.main.transform;
            Vector3 water = BrickGrid.CellToWorld(BrickGrid.BlockToCell(new Vector3Int(player.x, surface + 1, CustomChunkGenerator.CoastSeaZ + 2))) + new Vector3(0.4f, 0, 0.4f);
            Vector3 toward = (water - eye.position).normalized;
            Player.Instance.transform.rotation = Quaternion.LookRotation(new Vector3(toward.x, 0, toward.z).normalized);
            eye.localRotation = Quaternion.Euler(-Mathf.Asin(toward.y) * Mathf.Rad2Deg, 0, 0);
            Player.Instance.firstPersonController.ResetLook();
            BrickPlacer placer = Object.FindObjectOfType<BrickPlacer>();
            System.Reflection.FieldInfo targetField = typeof(BrickPlacer).GetField("target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            yield return TestGame.WaitUntil(() => ((BrickPlacement?)targetField.GetValue(placer)).HasValue, 5, "a preview over the water");
            BrickPlacement onWater = ((BrickPlacement?)targetField.GetValue(placer)).Value;
            Assert.AreEqual(BrickGrid.BlockToCell(new Vector3Int(0, surface + 1, 0)).y, onWater.origin.y, "on the water's surface");
            Assert.GreaterOrEqual(BrickGrid.CellToBlock(onWater.origin).z, CustomChunkGenerator.CoastSeaZ, "over the sea");

            // placed, facing out to sea (R turns it): it starts the boat
            System.Reflection.FieldInfo rotationField = typeof(BrickPlacer).GetField("rotation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            PlayerInventory inventory = TestGame.Player.GetComponent<PlayerInventory>();
            Brick seat = null;
            for (int rotation = 0; rotation < 4 && seat == null; rotation++) {
                rotationField.SetValue(placer, rotation);
                yield return null;
                onWater = ((BrickPlacement?)targetField.GetValue(placer)).Value;
                network.CmdCreateVehicle(Player.FirstFastSlot, onWater.origin, (byte)onWater.rotation);
                yield return null;
                Assert.AreEqual(1, TutorialChecks.Count(FirstBoat.Seat), "a seat on the water");
                seat = TutorialChecks.Parts(FirstBoat.Seat)[0];
                if (seat.vehicle.transform.TransformDirection(TutorialChecks.Facing(seat)).z < 0.9f) {
                    takeBack(seat);
                    inventory.ServerAdd(FirstBoat.Seat, Server.items[FirstBoat.Seat].color, 1, 0, Player.FirstFastSlot);
                    seat = null;
                    yield return null;
                }
            }
            Assert.IsNotNull(seat, "a seat facing out to sea");
            yield return stepIs(1, "a seat on the water: the raft next");
            Vehicle boat = seat.vehicle;

            // a raft of bricks around its base, one against each of its sides
            Item raftItem = item(FirstBoat.Raft);
            Vector3Int o = seat.placement.origin, seatSize = seat.placement.Size;
            for (int side = 0; side < 4; side++) {
                List<BrickPlacement> against = new List<BrickPlacement>();
                for (int rotation = 0; rotation < 2; rotation++) {
                    Vector3Int s = BrickPlacement.SizeFor(raftItem.brickModel, rotation);
                    int middleX = o.x + (seatSize.x - s.x) / 2, middleZ = o.z + (seatSize.z - s.z) / 2;
                    Vector3Int at = side == 0 ? new Vector3Int(o.x - s.x, o.y, middleZ) : side == 1 ? new Vector3Int(o.x + seatSize.x, o.y, middleZ)
                        : side == 2 ? new Vector3Int(middleX, o.y, o.z - s.z) : new Vector3Int(middleX, o.y, o.z + seatSize.z);
                    against.Add(new BrickPlacement(raftItem.brickModel, at, rotation));
                }
                Assert.IsNotNull(place(boat, raftItem, against), "a raft brick against side " + side);
            }
            yield return stepIs(2, "the propeller next");

            // the propeller behind it
            Item propellerItem = item(FirstBoat.Propeller);
            List<BrickPlacement> behind = new List<BrickPlacement>();
            foreach (Vector3Int at in around(seat.placement.origin.x, seat.placement.origin.z, seat.placement.origin.y, 8)) {
                for (int rotation = 0; rotation < 4; rotation++) {
                    behind.Add(new BrickPlacement(propellerItem.brickModel, at, rotation));
                }
            }
            Assert.IsNotNull(place(boat, propellerItem, behind, p => FirstBoat.Tutorial.steps[2].isDone()), "a propeller behind the seat, pushing it");
            yield return stepIs(3, "sitting next");

            yield return sitAndGo(seat, 4, 30, FirstBoat.SailDistance + " metres sailed");
        }

        [UnityTest]
        public IEnumerator ATutorialsWorldGoesWhenItsLeft() {
            yield return play(FirstCar.Tutorial);
            Assert.IsTrue(System.IO.Directory.Exists(game.SaveFolder));
            yield return game.Stop();
            yield return TestGame.WaitUntil(() => !System.IO.Directory.Exists(game.SaveFolder), 6, "its world deleted");
        }

        [UnityTest]
        public IEnumerator TutorialWorldsAreMadeNewAndArentListed() {
            yield return null;
            const string id = "zz_test_tutorial";
            string saveName = SavedWorlds.CreateTutorial(id, "A tutorial", TerrainKind.Coast);
            try {
                string folder = System.IO.Path.Combine(SavedWorlds.Folder, saveName);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "left.txt"), "from before");
                Assert.AreEqual(saveName, SavedWorlds.CreateTutorial(id, "A tutorial", TerrainKind.Coast), "the same folder");
                Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(folder, "left.txt")), "made new");
                WorldStorage info = WorldStorage.ReadInfo(folder);
                Assert.AreEqual(TerrainKind.Coast, info.Terrain);
                Assert.AreEqual(id, info.Tutorial);
                Assert.IsFalse(info.NpcsOn, "no NPCs");
                Assert.IsFalse(SavedWorlds.List().Exists(w => w.Tutorial == id), "not among the worlds to play");
            } finally {
                SavedWorlds.Delete(saveName);
            }
        }
    }
}
