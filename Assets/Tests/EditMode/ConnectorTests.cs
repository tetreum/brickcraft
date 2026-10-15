using Brickcraft.Bricks;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>Where LDraw parts connect besides their studs (Bricks.Connector, LDraw.LDrawConnectors), in their models' space.</summary>
    public class ConnectorTests
    {
        private static readonly float Ldu = Bricks.LDraw.LDrawModelBuilder.Scale;

        [SetUp]
        public void SetUp() {
            GameData.Load();
        }

        private static BrickModel model(string id) {
            BrickModel model = BrickModels.Get(id);
            Assume.That(model, Is.Not.Null, "the part " + id + " in the library");
            return model;
        }

        private static void horizontal(Vector3 axis, string what) {
            Assert.Less(Mathf.Abs(axis.y), 0.01f, what + " lies flat: " + axis);
        }

        [Test]
        public void AnAxleIsAMaleAxleItsLength() {
            Connector[] connectors = model("3705").connectors; // Technic Axle 4
            Assert.AreEqual(1, connectors.Length);
            Connector axle = connectors[0];
            Assert.IsTrue(axle.male);
            Assert.IsTrue(axle.slide, "what's on it slides along it");
            Assert.IsTrue(axle.HasAxleShape);
            Assert.AreEqual(4 * Server.studSize, axle.Length, 0.001f, "4 studs long");
            horizontal(axle.axis, "it");
        }

        [Test]
        public void ATechnicBrickHasAPinHoleAcrossIt() {
            BrickModel brick = model("3700"); // Technic Brick 1x2 with Hole
            Connector hole = System.Array.Find(brick.connectors, c => !c.male);
            Assert.IsNotNull(hole, "its hole");
            Assert.IsFalse(hole.HasAxleShape, "a round pin hole");
            horizontal(hole.axis, "the hole");
            // LDraw's hole is 10 LDU under the brick's top, the brick 24 tall: 14 above its bottom
            Assert.AreEqual(14 * Ldu, hole.Middle.y, 0.01f, "its height");
            Assert.AreEqual(Server.studSize, hole.Length, 0.01f, "through the brick (a stud)");
        }

        [Test]
        public void WheelsHaveTheirHubThroughTheirMiddle() {
            BrickModel rim = model("11208"); // Wheel Rim 10x14: a hub for bars and wheel holder pins
            Assert.AreEqual(1, rim.connectors.Length);
            Connector hub = rim.connectors[0];
            Assert.IsFalse(hub.male);
            Assert.AreEqual(4, hub.sections[0].radius, 0.01f, "bar sized (a technic axle is 6)");
            Bounds mesh = rim.prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.Less(Vector3.Distance(new Vector3(mesh.center.x, mesh.center.y, mesh.center.z), hub.Middle), 0.05f, "through its middle");

            Connector axleHole = System.Array.Find(model("55982").connectors, c => !c.male); // Wheel Rim 14x18 with Axlehole
            Assert.IsNotNull(axleHole);
            Assert.IsTrue(axleHole.HasAxleShape, "an axle hole");

            // a wheel the LDCad library hasn't: a pin hole by the game's rule, through its middle
            Connector guessed = System.Array.Find(model("6582").connectors, c => !c.male); // Wheel Rim 20x33 with 6 Pinholes
            Assert.IsNotNull(guessed);
            Assert.AreEqual(6, guessed.sections[0].radius, 0.01f);
        }

        [Test]
        public void PlainBricksConnectByTheirStudsOnly() {
            Assert.AreEqual(0, model("3001").connectors.Length, "studs and anti-studs are the grid's");
        }
    }
}
