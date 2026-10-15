using Brickcraft.Bricks;
using NUnit.Framework;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>What connectors fit, and where a connected brick goes (Bricks.BrickJoints).</summary>
    public class JointTests
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

        private static Connector first(string id, bool male) {
            Connector found = System.Array.Find(model(id).connectors, c => c.male == male);
            Assume.That(found, Is.Not.Null, id + "'s " + (male ? "male" : "female") + " connector");
            return found;
        }

        private static int indexOf(BrickModel model, Connector connector) {
            return System.Array.IndexOf(model.connectors, connector);
        }

        [Test]
        public void AxlesAndPinsGoWhereTheyFit() {
            Connector axle = first("3705", true), pinHole = first("3700", false), axleHole = first("55982", false), barHub = first("11208", false);
            Connector pin = System.Array.Find(model("2780").connectors, c => c.male && c.slide); // the whole pin
            Assume.That(pin, Is.Not.Null);

            Assert.IsTrue(BrickJoints.Fits(axle, axleHole, out bool locked), "an axle in an axle hole");
            Assert.IsTrue(locked, "it can't spin there");
            Assert.IsTrue(BrickJoints.Fits(pinHole, axle, out locked), "an axle in a pin hole (either order)");
            Assert.IsFalse(locked, "it spins there");
            Assert.IsFalse(BrickJoints.Fits(axle, barHub), "an axle is too thick for a bar's hub");
            Assert.IsTrue(BrickJoints.Fits(pin, pinHole), "a pin in a pin hole");
            Assert.IsFalse(BrickJoints.Fits(pin, axleHole), "a round pin in an axle hole");
            Assert.IsFalse(BrickJoints.Fits(axle, axle), "two males");

            Assert.AreEqual(3, BrickJoints.MaxSlide(axle, pinHole), "80 LDU through 20: 30 either way, 3 half studs");
        }

        [Test]
        public void AConnectedBrickGoesWhereItsConnectorMeetsItsHolders() {
            BrickModel brick = model("3700"), axleModel = model("3705");
            Connector hole = first("3700", false), axle = axleModel.connectors[0];
            BrickJoint joint = new BrickJoint() { holderConnector = indexOf(brick, hole), connector = 0 };
            Vector3 at = new Vector3(1, 2, 3);
            Quaternion turned = Quaternion.Euler(0, 90, 0);

            BrickJoints.PoseOf(at, turned, brick, axleModel, joint, out Vector3 position, out Quaternion rotation);
            Vector3 holeMiddle = at + turned * hole.Middle, holeAxis = turned * hole.axis;
            Assert.Less(Vector3.Distance(holeMiddle, position + rotation * axle.Middle), 0.0001f, "middle to middle");
            Assert.Greater(Vector3.Dot(holeAxis, rotation * axle.axis), 0.9999f, "along the hole");

            joint.slide = 3;
            BrickJoints.PoseOf(at, turned, brick, axleModel, joint, out position, out rotation);
            Assert.Less(Vector3.Distance(holeMiddle + holeAxis * 30 * Ldu, position + rotation * axle.Middle), 0.0001f, "slid 30 LDU");

            joint.slide = 0;
            joint.spin = 1;
            BrickJoints.PoseOf(at, turned, brick, axleModel, joint, out position, out Quaternion spun);
            Assert.AreEqual(90, Vector3.Angle(rotation * axle.side, spun * axle.side), 0.01f, "a quarter turn around it");
            Assert.Greater(Vector3.Dot(holeAxis, spun * axle.axis), 0.9999f, "still along it");

            joint.spin = 0;
            joint.flip = true;
            BrickJoints.PoseOf(at, turned, brick, axleModel, joint, out position, out Quaternion flipped);
            Assert.Less(Vector3.Dot(holeAxis, flipped * axle.axis), -0.9999f, "the other way along it");
            Assert.Less(Vector3.Distance(holeMiddle, position + flipped * axle.Middle), 0.0001f, "still middle to middle");
        }

        [Test]
        public void ConnectorsLineUpOnOneLineOverlapping() {
            Vector3 x = Vector3.right;
            Assert.IsTrue(BrickJoints.LineUp(Vector3.zero, x, 2, new Vector3(1, 0, 0), x, 2));
            Assert.IsTrue(BrickJoints.LineUp(Vector3.zero, x, 2, new Vector3(3, 0, 0), -x, 2), "facing each other");
            Assert.IsFalse(BrickJoints.LineUp(Vector3.zero, x, 2, new Vector3(3, 0, 0), x, 2), "end to end, apart");
            Assert.IsFalse(BrickJoints.LineUp(Vector3.zero, x, 2, new Vector3(1, 0.5f, 0), x, 2), "side by side");
            Assert.IsFalse(BrickJoints.LineUp(Vector3.zero, x, 2, new Vector3(1, 0, 0), Vector3.up, 2), "across");
        }
    }
}
