using System.Collections.Generic;
using NUnit.Framework;

namespace Brickcraft.Tests
{
    /// <summary>Inventory rules: stacks of one item, colour and wear, the fast inventory first.</summary>
    public class InventoryTests
    {
        private const int Grey = 72, Red = 4;
        private List<InventoryItem> items;

        [SetUp]
        public void SetUp() {
            GameData.Load();
            items = new List<InventoryItem>();
        }

        private InventoryItem at(int slot) {
            return items[Inventory.FindSlot(items, slot)];
        }

        [Test]
        public void NewStacksGoToTheFastInventoryFirst() {
            Assert.IsTrue(Inventory.Add(items, "stone", Grey, 10));

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(Inventory.FirstFastSlot, items[0].slot);
        }

        [Test]
        public void StacksFillUpToTheirMaxThenOverflow() {
            int max = Inventory.MaxStack("stone");
            Assert.IsTrue(Inventory.Add(items, "stone", Grey, max + 5));

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(max, at(Inventory.FirstFastSlot).quantity);
            Assert.AreEqual(5, at(Inventory.FirstFastSlot + 1).quantity);

            Inventory.Add(items, "stone", Grey, 3);
            Assert.AreEqual(8, at(Inventory.FirstFastSlot + 1).quantity, "fills the stack with room");
        }

        [Test]
        public void ItemsHaveTheirOwnMaxStack() {
            Assert.AreEqual(16, Inventory.MaxStack("bucket"));
            Assert.AreEqual(1, Inventory.MaxStack("water_bucket"));
            Assert.AreEqual(Item.DefaultMaxStack, Inventory.MaxStack("an item that doesn't exist"));
        }

        [Test]
        public void ColoursDontMerge() {
            Inventory.Add(items, "stone", Grey, 1);
            Inventory.Add(items, "stone", Red, 1);

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(2, Inventory.Count(items, "stone", Inventory.AnyColor, 0));
            Assert.AreEqual(1, Inventory.Count(items, "stone", Red, 0));
        }

        [Test]
        public void OnlyItemsThatWearHaveHealth() {
            Assert.AreEqual(0, Inventory.ValidHealth("stone", 100), "old saves had 100 for crafted items");
            Assert.AreEqual(5, Inventory.ValidHealth("pickaxe", 5));

            Inventory.Add(items, "pickaxe", Grey, 1, 0);
            Inventory.Add(items, "pickaxe", Grey, 1, 5);
            Assert.AreEqual(2, items.Count, "worn tools don't stack with new ones");
        }

        [Test]
        public void AddingTooManyAddsNone() {
            int max = Inventory.MaxStack("stone");
            Assert.IsFalse(Inventory.Add(items, "stone", Grey, max * Inventory.SlotCount + 1));
            Assert.AreEqual(0, items.Count);

            Assert.IsTrue(Inventory.Add(items, "stone", Grey, max * Inventory.SlotCount));
            Assert.AreEqual(0, Inventory.Room(items, "stone", Grey, 0));
        }

        [Test]
        public void RemovingTakesTheSmallestStacksFirst() {
            Inventory.Add(items, "stone", Grey, 10, 0, 1);
            Inventory.Add(items, "stone", Grey, 3, 0, 2);

            Assert.IsTrue(Inventory.Remove(items, "stone", Grey, 4));
            Assert.AreEqual(-1, Inventory.FindSlot(items, 2), "the small stack is gone");
            Assert.AreEqual(9, at(1).quantity);

            Assert.IsFalse(Inventory.Remove(items, "stone", Grey, 10), "not enough");
            Assert.AreEqual(9, at(1).quantity, "and nothing removed");
        }

        [Test]
        public void MovingAStackMergesOrSwaps() {
            int max = Inventory.MaxStack("stone");
            Inventory.Add(items, "stone", Grey, max - 2, 0, 1);
            Inventory.Add(items, "stone", Grey, 5, 0, 2);
            Inventory.Add(items, "dirt", Grey, 1, 0, 3);

            Assert.IsTrue(Inventory.MoveStack(items, 2, 1));
            Assert.AreEqual(max, at(1).quantity);
            Assert.AreEqual(3, at(2).quantity, "the rest stays");

            Assert.IsTrue(Inventory.MoveStack(items, 3, 2));
            Assert.AreEqual("dirt", at(2).itemId);
            Assert.AreEqual("stone", at(3).itemId);
        }

        [Test]
        public void MovingOneLeavesTheRest() {
            Inventory.Add(items, "stone", Grey, 5, 0, 1);

            Assert.IsTrue(Inventory.MoveOne(items, 1, 7));
            Assert.AreEqual(4, at(1).quantity);
            Assert.AreEqual(1, at(7).quantity);

            Inventory.Add(items, "dirt", Grey, 1, 0, 8);
            Assert.IsFalse(Inventory.MoveOne(items, 1, 8), "not onto another item");
        }

        [Test]
        public void EquipmentSlotsOnlyTakeArmorWornThere() {
            Assert.IsTrue(Inventory.Fits("stone", 1));
            Assert.IsFalse(Inventory.Fits("stone", Inventory.EquipmentSlot(Item.ArmorSlot.Head)));
            Assert.IsTrue(Inventory.Fits("helmet", Inventory.EquipmentSlot(Item.ArmorSlot.Head)));

            Inventory.Add(items, "stone", Grey, 1, 0, 1);
            Assert.IsFalse(Inventory.Swap(items, 1, Inventory.EquipmentSlot(Item.ArmorSlot.Head)));
        }

        [Test]
        public void ChestsHaveTheirOwnSlots() {
            const int ChestSlots = 4;
            int max = Inventory.MaxStack("stone");
            Assert.IsTrue(Inventory.Add(items, "stone", Grey, 1, 0, 0, ChestSlots));
            Assert.AreEqual(1, items[0].slot, "filled from the first one");
            Assert.IsFalse(Inventory.Add(items, "stone", Grey, max * ChestSlots, 0, 0, ChestSlots));

            List<InventoryItem> player = new List<InventoryItem>();
            Assert.IsTrue(Inventory.MoveBetween(items, ChestSlots, 1, player, Inventory.SlotCount, 5, false));
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual(5, player[0].slot);
        }
    }
}
