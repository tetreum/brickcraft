using System.Collections;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>Technic in a game: parts connected off the grid by their connectors (Bricks.BrickJoints).</summary>
    [Timeout(120000)]
    public class TechnicTests
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

        private static Item item(string id) {
            Assume.That(Server.items.ContainsKey(id), id + " in the catalogue");
            return Server.items[id];
        }

        private static int indexOf(BrickModel model, bool male, System.Func<Connector, bool> also = null) {
            for (int i = 0; i < model.connectors.Length; i++) {
                if (model.connectors[i].male == male && (also == null || also(model.connectors[i]))) {
                    return i;
                }
            }
            Assert.Fail(model.id + " has no " + (male ? "male" : "female") + " connector like that");
            return -1;
        }

        // a technic brick in the air above the player
        private static Brick technicBrick() {
            Item brickItem = item("part_3700");
            Vector3Int block = TestGame.PlayerBlock + new Vector3Int(0, 8, 0);
            Brick brick = WorldNetwork.ServerPlaceBrick(brickItem, brickItem.color, new BrickPlacement(brickItem.brickModel, BrickGrid.BlockToCell(block)), Placer.Now(0));
            Assert.IsNotNull(brick);
            return brick;
        }

        [UnityTest]
        public IEnumerator AnAxleGoesThroughATechnicBrickAndAWheelOnItsEnd() {
            Brick holder = technicBrick();
            Item axleItem = item("part_3706"), wheelItem = item("part_55982"); // Axle 6: room for a wheel on its end
            BrickJoint throughHole = new BrickJoint() { holderConnector = indexOf(holder.model, false), connector = 0 };
            Assert.IsNull(BrickJoints.WhyNot(holder, axleItem.brickModel, throughHole), "the axle fits through its hole");
            Brick axle = Server.Instance.spawnConnected(axleItem, axleItem.color, holder, throughHole);
            Assert.IsNotNull(axle);
            yield return null;

            Connector hole = holder.model.connectors[throughHole.holderConnector], rod = axle.model.connectors[0];
            Transform h = holder.gameObject.transform, a = axle.gameObject.transform;
            Assert.Less(Vector3.Distance(h.TransformPoint(hole.Middle), a.TransformPoint(rod.Middle)), 0.001f, "middle to middle");
            Assert.Greater(Mathf.Abs(Vector3.Dot(h.TransformDirection(hole.axis), a.TransformDirection(rod.axis))), 0.999f, "along the hole");
            Assert.AreNotEqual(axle, BrickGrid.GetBrickAt(BrickGrid.WorldToCell(a.TransformPoint(rod.End))), "no cells of its own");

            // a wheel at one end of the axle, its axle hole on it
            int max = BrickJoints.MaxSlide(rod, wheelItem.brickModel.connectors[indexOf(wheelItem.brickModel, false, c => c.HasAxleShape)]);
            BrickJoint onEnd = new BrickJoint() { holderConnector = 0, connector = indexOf(wheelItem.brickModel, false, c => c.HasAxleShape), slide = max };
            Assert.IsNull(BrickJoints.WhyNot(axle, wheelItem.brickModel, onEnd), "the wheel fits on the axle's end");
            Brick wheel = Server.Instance.spawnConnected(wheelItem, wheelItem.color, axle, onEnd);
            Assert.IsNotNull(wheel);
            yield return null;
            Assert.IsTrue(BrickJoints.ConnectsTo(wheel.model, wheel.gameObject.transform.position, wheel.gameObject.transform.rotation, axle), "on it");

            // the same wheel where the brick is: in the way
            BrickJoint onBrick = onEnd;
            onBrick.slide = 0;
            Assert.IsNotNull(BrickJoints.WhyNot(axle, wheelItem.brickModel, onBrick), "not into the brick");

            // the brick goes, and what it held with it
            Server.Instance.removeBrick(holder);
            Assert.IsFalse(Server.bricks.ContainsKey(axle.id), "the axle went with it");
            Assert.IsFalse(Server.bricks.ContainsKey(wheel.id), "and the wheel");
        }

        [UnityTest]
        public IEnumerator ConnectedBricksAreSavedHeldAsTheyWere() {
            Brick holder = technicBrick();
            Item axleItem = item("part_3706"), wheelItem = item("part_55982");
            int wheelHole = indexOf(wheelItem.brickModel, false, c => c.HasAxleShape);
            Brick axle = WorldNetwork.ServerConnectBrick(axleItem, axleItem.color, holder, new BrickJoint() { holderConnector = indexOf(holder.model, false), connector = 0, spin = 1 }, Placer.Now(0));
            yield return null;
            BrickJoint onEnd = new BrickJoint() { holderConnector = 0, connector = wheelHole, slide = BrickJoints.MaxSlide(axle.model.connectors[0], wheelItem.brickModel.connectors[wheelHole]), flip = true };
            Assert.IsNull(BrickJoints.WhyNot(axle, wheelItem.brickModel, onEnd));
            Brick wheel = WorldNetwork.ServerConnectBrick(wheelItem, 4, axle, onEnd, Placer.Now(0));
            yield return null;
            string axleId = axle.id, wheelId = wheel.id;
            Vector3 wheelAt = FloatingOrigin.ToAbsolute(wheel.gameObject.transform.position);
            Quaternion wheelTurned = wheel.gameObject.transform.rotation;

            yield return game.Stop();
            yield return game.Start();
            yield return TestGame.WaitUntil(() => Server.bricks.ContainsKey(wheelId), 10, "the wheel back");
            Brick back = Server.bricks[wheelId];
            Assert.AreEqual(axleId, back.attachedTo, "on its axle");
            Assert.AreEqual(onEnd, back.joint.Value, "held as it was");
            Assert.AreEqual(4, back.color);
            Assert.Less(Vector3.Distance(wheelAt, FloatingOrigin.ToAbsolute(back.gameObject.transform.position)), 0.001f, "where it was");
            Assert.Less(Quaternion.Angle(wheelTurned, back.gameObject.transform.rotation), 0.01f, "turned as it was");
            Assert.AreEqual(1, Server.bricks[axleId].joint.Value.spin, "the axle too");
        }

        [UnityTest]
        public IEnumerator AVehicleKeepsItsConnectedParts() {
            Item brickItem = item("part_3700"), axleItem = item("part_3706"), wheelItem = item("part_55982");
            Vector3Int cell = BrickGrid.BlockToCell(TestGame.PlayerBlock + new Vector3Int(0, 8, 0));
            Vehicles.Vehicle vehicle = Vehicles.VehicleSystem.ServerCreate(brickItem, brickItem.color, new BrickPlacement(brickItem.brickModel, cell));
            yield return null;
            Brick holder = vehicle.grid.GetBrickAt(Vector3Int.zero);
            BrickJoint through = new BrickJoint() { holderConnector = indexOf(holder.model, false), connector = 0 };
            Assert.IsNull(BrickJoints.WhyNot(holder, axleItem.brickModel, through));
            Brick axle = Vehicles.VehicleSystem.ServerConnect(axleItem, axleItem.color, holder, through);
            int wheelHole = indexOf(wheelItem.brickModel, false, c => c.HasAxleShape);
            BrickJoint onEnd = new BrickJoint() { holderConnector = 0, connector = wheelHole, slide = BrickJoints.MaxSlide(axle.model.connectors[0], wheelItem.brickModel.connectors[wheelHole]) };
            Brick wheel = Vehicles.VehicleSystem.ServerConnect(wheelItem, wheelItem.color, axle, onEnd);
            Assert.IsNotNull(wheel);
            Assert.AreSame(vehicle, wheel.vehicle, "the vehicle's");
            Assert.AreSame(vehicle.transform, wheel.gameObject.transform.parent, "moving with it");
            Assert.AreEqual(3, vehicle.grid.Count);
            Assert.AreEqual(3, vehicle.parts.Count, "its parts, for the clients");
            Assert.AreEqual(holder.id, vehicle.parts[1].holder);
            Assert.AreEqual(1, vehicle.grid.Components().Count, "one piece");
            string wheelId = wheel.id, id = vehicle.vehicleId;
            yield return TestGame.WaitUntil(() => Npcs.NpcSystem.IsLoaded(vehicle.transform.position), 30, "its chunk loaded");

            yield return game.Stop();
            yield return game.Start();
            Vehicles.Vehicle reloaded = null;
            yield return TestGame.WaitUntil(() => {
                foreach (Vehicles.Vehicle v in Vehicles.Vehicle.All) {
                    if (v.vehicleId == id) {
                        reloaded = v;
                    }
                }
                return reloaded != null;
            }, 10, "the vehicle back");
            Assert.AreEqual(3, reloaded.grid.Count, "with its axle and wheel");
            Assert.IsTrue(Server.bricks.TryGetValue(wheelId, out Brick wheelBack) && wheelBack.joint.HasValue, "the wheel connected");
            Assert.AreEqual(onEnd, wheelBack.joint.Value);

            // the brick holding the axle goes: the vehicle with it
            Vehicles.VehicleSystem.ServerRemove(reloaded.grid.GetBrickAt(Vector3Int.zero));
            yield return null;
            Assert.IsFalse(Server.bricks.ContainsKey(wheelId), "and what it held");
        }

        [UnityTest]
        public IEnumerator APlayerConnectsAnAxleAndBricksStayOutOfItsWay() {
            Player player = Player.Instance;
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            yield return TestGame.Wait(1); // on the ground
            // a technic brick in front of the player, within reach
            Item brickItem = item("part_3700"), axleItem = item("part_3706");
            Transform eye = Camera.main.transform;
            Vector3Int cell = BrickGrid.WorldToCell(eye.position + eye.forward * 2.5f + Vector3.up);
            Brick holder = WorldNetwork.ServerPlaceBrick(brickItem, brickItem.color, new BrickPlacement(brickItem.brickModel, cell), Placer.Now(0));
            yield return null;

            // aimed at its hole: the hole, and how far along it
            int hole = indexOf(holder.model, false);
            Transform h = holder.gameObject.transform;
            Vector3 middle = h.TransformPoint(holder.model.connectors[hole].Middle), along = h.TransformDirection(holder.model.connectors[hole].axis);
            System.Collections.Generic.List<int> fitting = new System.Collections.Generic.List<int>();
            Vector3 across = Vector3.Cross(along, Vector3.up).normalized;
            Assert.AreEqual(hole, BrickJoints.NearestFitting(holder, axleItem.brickModel, middle + across * 2, middle - across * 0.3f, 0.15f, fitting, out Vector3 nearest),
                "looked at from the side, through the brick");
            Assert.Less(Vector3.Distance(middle, nearest), 0.01f, "at its middle");
            Assert.AreEqual(1, fitting.Count, "the axle's one connector fits it");
            Assert.AreEqual(hole, BrickJoints.NearestFitting(holder, axleItem.brickModel, middle + along * 2, middle, 0.15f, fitting, out nearest), "looked at end on");
            Assert.AreEqual(2, BrickJoints.SlideToward(holder, hole, axleItem.brickModel.connectors[0], middle + along * 0.4f), "40 cm along: 2 half studs");
            Assert.AreEqual(-1, BrickJoints.NearestFitting(holder, axleItem.brickModel, middle + Vector3.up * 2 + across, middle + Vector3.up * 2 - across, 0.15f, fitting, out nearest),
                "too far from it");

            int slot = Player.FirstFastSlot + 4;
            Assert.IsTrue(inventory.ServerAdd(axleItem.id, axleItem.color, 1, 0, slot));
            player.network.CmdConnectBrick(slot, holder.id, hole, 0, 2, 0, false);
            yield return null;
            Assert.AreEqual(1, Server.connectedTo(holder).Count, "the axle went through it");
            Assert.IsFalse(inventory.ServerGetSlot(slot).HasValue, "the axle was used");
            Brick axle = Server.connectedTo(holder)[0];

            // a brick where the axle sticks out: not there
            Item brick2x2 = Server.items["brick_2x2"];
            Vector3 end = axle.gameObject.transform.TransformPoint(axle.model.connectors[0].End);
            Vector3Int there = BrickGrid.WorldToCell(end - along * 0.1f);
            BrickPlacement onIt = new BrickPlacement(brick2x2.brickModel, there - new Vector3Int(1, 1, 1));
            Assert.IsTrue(BrickGrid.IsFree(onIt), "free cells: the axle takes none");
            Assert.IsTrue(BrickJoints.OverlapsConnected(onIt.WorldBounds), "but it's there");
            Assert.IsTrue(inventory.ServerAdd(brick2x2.id, brick2x2.color, 1, 0, slot));
            player.network.CmdPlaceBrick(slot, onIt.origin, 0);
            yield return null;
            Assert.IsTrue(inventory.ServerGetSlot(slot).HasValue, "not placed into the axle");
        }

        [UnityTest]
        public IEnumerator WhatDoesntFitDoesntGo() {
            Brick holder = technicBrick();
            Item barWheel = item("part_11208");
            yield return null;
            BrickJoint joint = new BrickJoint() { holderConnector = indexOf(holder.model, false), connector = indexOf(barWheel.brickModel, false) };
            Assert.IsNotNull(BrickJoints.WhyNot(holder, barWheel.brickModel, joint), "a hole into a hole");
            Item axleItem = item("part_3705");
            joint = new BrickJoint() { holderConnector = indexOf(holder.model, false), connector = 0, slide = 4 };
            Assert.IsNotNull(BrickJoints.WhyNot(holder, axleItem.brickModel, joint), "slid out of it");
        }
    }
}
