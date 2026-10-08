using System.Collections.Generic;

namespace Brickcraft
{
    /// <summary>A stack of items in an inventory slot, as synced from the server.</summary>
    public struct InventoryItem
    {
        public int slot;
        public int itemId;
        public int quantity;
        public int health;
    }

    /// <summary>
    /// Inventory rules. Only the server changes inventories, clients get the result synced.
    /// Slots go from 1 to <see cref="SlotCount"/>, the last ones being the fast inventory.
    /// </summary>
    public static class Inventory
    {
        public const int SlotCount = 36;
        public const int FirstFastSlot = 28;

        public static int FindStack(IList<InventoryItem> items, int itemId, int health) {
            for (int i = 0; i < items.Count; i++) {
                if (items[i].itemId == itemId && items[i].health == health) {
                    return i;
                }
            }
            return -1;
        }

        public static int FindSlot(IList<InventoryItem> items, int slot) {
            for (int i = 0; i < items.Count; i++) {
                if (items[i].slot == slot) {
                    return i;
                }
            }
            return -1;
        }

        public static bool IsSlotFree(IList<InventoryItem> items, int slot) {
            return slot >= 1 && slot <= SlotCount && FindSlot(items, slot) == -1;
        }

        public static bool Has(IList<InventoryItem> items, int itemId, int quantity, int health) {
            int stack = FindStack(items, itemId, health);

            return stack != -1 && items[stack].quantity >= quantity;
        }

        /// <summary>
        /// Adds to an existing stack, or to a new one in the given slot (or the first free one,
        /// preferring the fast inventory). False if the inventory is full.
        /// </summary>
        public static bool Add(IList<InventoryItem> items, int itemId, int quantity, int health = 0, int slot = 0) {
            int stack = FindStack(items, itemId, health);

            if (stack != -1) {
                InventoryItem item = items[stack];
                item.quantity += quantity;
                items[stack] = item;
                return true;
            }

            if (slot == 0) {
                slot = firstFreeSlot(items);
            }
            if (!IsSlotFree(items, slot)) {
                return false;
            }

            items.Add(new InventoryItem() { slot = slot, itemId = itemId, quantity = quantity, health = health });
            return true;
        }

        /// <summary>Removes items from their stack, false if there aren't enough.</summary>
        public static bool Remove(IList<InventoryItem> items, int itemId, int quantity, int health = 0) {
            int stack = FindStack(items, itemId, health);

            if (stack == -1 || items[stack].quantity < quantity) {
                return false;
            }

            InventoryItem item = items[stack];
            item.quantity -= quantity;

            if (item.quantity > 0) {
                items[stack] = item;
            } else {
                items.RemoveAt(stack);
            }
            return true;
        }

        /// <summary>Moves a stack to another slot, swapping it with whatever is there.</summary>
        public static bool Swap(IList<InventoryItem> items, int slotA, int slotB) {
            if (slotA < 1 || slotA > SlotCount || slotB < 1 || slotB > SlotCount || slotA == slotB) {
                return false;
            }
            int a = FindSlot(items, slotA);
            int b = FindSlot(items, slotB);

            if (a != -1) {
                InventoryItem item = items[a];
                item.slot = slotB;
                items[a] = item;
            }
            if (b != -1) {
                InventoryItem item = items[b];
                item.slot = slotA;
                items[b] = item;
            }
            return a != -1 || b != -1;
        }

        private static int firstFreeSlot(IList<InventoryItem> items) {
            for (int slot = FirstFastSlot; slot <= SlotCount; slot++) {
                if (IsSlotFree(items, slot)) {
                    return slot;
                }
            }
            for (int slot = 1; slot < FirstFastSlot; slot++) {
                if (IsSlotFree(items, slot)) {
                    return slot;
                }
            }
            return 0;
        }
    }
}
