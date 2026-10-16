using Brickcraft.Vehicles;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>What parts are on a vehicle (Item.VehicleRole, Item.Mass) and the vehicle items: the seat and the wiring tool.</summary>
    public class VehiclePartTests
    {
        [SetUp]
        public void SetUp() {
            GameData.Load();
        }

        private static Item firstTagged(string tag) {
            foreach (Item item in Server.items.Values) {
                if (item.tags.Contains(tag) && item.id.StartsWith("part_")) {
                    return item;
                }
            }
            return null;
        }

        [Test]
        public void TheSeatIsASeatAndTheWiringToolATool() {
            Item seat = Server.items["seat"];
            Assert.AreEqual(VehicleRole.Seat, seat.VehicleRole);
            Assert.AreEqual("4079", seat.brickModelId);
            Assert.IsNotNull(seat.brickModel, "the minifig seat is in the LDraw library");

            Item tool = Server.items["wiring_tool"];
            Assert.IsTrue(tool.IsWiringTool);
            Assert.AreEqual(Item.Type.Tool, tool.type);
            Assert.AreEqual("6246a", tool.BricklinkPart, "the screwdriver, LDraw's 55298");
            Assert.IsNotNull(tool.brickModel);
            Assert.AreEqual(VehicleRole.None, tool.VehicleRole);
        }

        [Test]
        public void LDrawsCategoriesGiveTheirPartsRoles() {
            Assert.AreEqual(VehicleRole.Wheel, firstTagged("wheel").VehicleRole);
            Assert.AreEqual(VehicleRole.Propeller, firstTagged("propeller").VehicleRole);
            Assert.AreEqual(VehicleRole.Wing, firstTagged("wing").VehicleRole);
            Assert.AreEqual(VehicleRole.None, Server.items["brick_2x2"].VehicleRole, "the body");
        }

        [Test]
        public void InfoJsonNamesRoles() {
            Assert.AreEqual(VehicleRole.Wheel, VehicleParts.Parse("wheel"));
            Assert.AreEqual(VehicleRole.Rudder, VehicleParts.Parse(" Rudder "));
            Assert.AreEqual(VehicleRole.None, VehicleParts.Parse("hovercraft"));
            Assert.AreEqual(VehicleRole.None, VehicleParts.Parse(null));
        }

        [Test]
        public void PartsWeighByTheirSize() {
            Item brick = Server.items["brick_2x2"];
            BrickModel model = brick.brickModel;
            Assert.AreEqual(model.width * model.depth * model.heightInPlates * VehicleParts.MassPerCell, brick.Mass, 0.0001f);
            Assert.Greater(brick.Mass, Server.items["plate_1x1_green"].Mass, "a 2x2 brick outweighs a 1x1 plate");
        }
    }
}
