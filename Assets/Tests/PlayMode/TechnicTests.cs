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
