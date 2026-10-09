using System.Collections.Generic;

namespace Brickcraft
{
    /// <summary>A stack of items in an inventory slot, as synced from the server.</summary>
    public struct InventoryItem
    {
        public int slot;
        public string itemId;
        /// <summary>Its colour (see BrickColorPalette), only stacks of the same colour merge.</summary>
        public int color;
        public int quantity;
        public int health;
    }

    /// <summary>
    /// Inventory rules. Only the server changes inventories, clients get the result synced.
    /// Slots go from 1 to <see cref="SlotCount"/>, the last ones being the fast inventory. An item
    /// can be in several stacks, each holding up to its Item.maxStack. Stacks are of one item, colour
    /// and health.
    /// </summary>
    public static class Inventory
    {
        public const int SlotCount = 36;
        public const int FirstFastSlot = 28;

        /// <summary>Counting or removing: any colour of the item (recipes don't care about colours).</summary>
        public const int AnyColor = int.MinValue;

        /// <summary>How many of the item fit in one slot.</summary>
        public static int MaxStack(string itemId) {
            Item item;
            return Server.items.TryGetValue(itemId, out item) ? item.maxStack : Item.DefaultMaxStack;
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

        private static bool isSame(InventoryItem stack, string itemId, int color, int health) {
            return stack.itemId == itemId && (color == AnyColor || stack.color == color) && stack.health == health;
        }

        private static bool isSame(InventoryItem stack, InventoryItem other) {
            return isSame(stack, other.itemId, other.color, other.health);
        }

        /// <summary>How many there are, in all their stacks.</summary>
        public static int Count(IList<InventoryItem> items, string itemId, int color, int health) {
            int count = 0;
            foreach (InventoryItem stack in items) {
                if (isSame(stack, itemId, color, health)) {
                    count += stack.quantity;
                }
            }
            return count;
        }

        public static bool Has(IList<InventoryItem> items, string itemId, int color, int quantity, int health) {
            return Count(items, itemId, color, health) >= quantity;
        }

        /// <summary>How many more fit: what's missing in their stacks, plus whole empty slots.</summary>
        public static int Room(IList<InventoryItem> items, string itemId, int color, int health) {
            int max = MaxStack(itemId);
            int room = 0;
            foreach (InventoryItem stack in items) {
                if (isSame(stack, itemId, color, health) && stack.quantity < max) {
                    room += max - stack.quantity;
                }
            }
            for (int slot = 1; slot <= SlotCount; slot++) {
                if (FindSlot(items, slot) == -1) {
                    room += max;
                }
            }
            return room;
        }

        /// <summary>
        /// Adds them all or none: first filling their stacks, then new ones in free slots (preferring the
        /// fast inventory). With a slot, all of them go there (empty or with room). False if they don't fit.
        /// </summary>
        public static bool Add(IList<InventoryItem> items, string itemId, int color, int quantity, int health = 0, int slot = 0) {
            int max = MaxStack(itemId);

            if (slot != 0) {
                int stack = FindSlot(items, slot);
                if (stack == -1) {
                    if (slot < 1 || slot > SlotCount || quantity > max) {
                        return false;
                    }
                    items.Add(new InventoryItem() { slot = slot, itemId = itemId, color = color, quantity = quantity, health = health });
                    return true;
                }
                InventoryItem existing = items[stack];
                if (!isSame(existing, itemId, color, health) || existing.quantity + quantity > max) {
                    return false;
                }
                existing.quantity += quantity;
                items[stack] = existing;
                return true;
            }

            if (Room(items, itemId, color, health) < quantity) {
                return false;
            }
            for (int i = 0; i < items.Count && quantity > 0; i++) {
                InventoryItem stack = items[i];
                if (isSame(stack, itemId, color, health) && stack.quantity < max) {
                    int added = System.Math.Min(max - stack.quantity, quantity);
                    stack.quantity += added;
                    items[i] = stack;
                    quantity -= added;
                }
            }
            while (quantity > 0) {
                int added = System.Math.Min(max, quantity);
                items.Add(new InventoryItem() { slot = firstFreeSlot(items), itemId = itemId, color = color, quantity = added, health = health });
                quantity -= added;
            }
            return true;
        }

        /// <summary>Removes them from their stacks (the smallest first), false (and nothing removed) if there aren't enough.</summary>
        public static bool Remove(IList<InventoryItem> items, string itemId, int color, int quantity, int health = 0) {
            if (!Has(items, itemId, color, quantity, health)) {
                return false;
            }
            while (quantity > 0) {
                int smallest = -1;
                for (int i = 0; i < items.Count; i++) {
                    if (isSame(items[i], itemId, color, health) && (smallest == -1 || items[i].quantity < items[smallest].quantity)) {
                        smallest = i;
                    }
                }
                quantity -= takeFrom(items, smallest, quantity);
            }
            return true;
        }

        /// <summary>Removes them from the stack in the slot, false (and nothing removed) if it hasn't enough.</summary>
        public static bool RemoveFromSlot(IList<InventoryItem> items, int slot, int quantity) {
            int stack = FindSlot(items, slot);
            if (stack == -1 || items[stack].quantity < quantity) {
                return false;
            }
            takeFrom(items, stack, quantity);
            return true;
        }

        // takes up to quantity from the stack at index, removing it when empty; returns how many it took
        private static int takeFrom(IList<InventoryItem> items, int index, int quantity) {
            InventoryItem stack = items[index];
            int taken = System.Math.Min(stack.quantity, quantity);
            stack.quantity -= taken;
            if (stack.quantity > 0) {
                items[index] = stack;
            } else {
                items.RemoveAt(index);
            }
            return taken;
        }

        /// <summary>
        /// Moves the stack in <paramref name="from"/> to <paramref name="to"/>: onto the same item it merges,
        /// as much as fits (the rest stays), otherwise they swap. False if nothing changed.
        /// </summary>
        public static bool MoveStack(IList<InventoryItem> items, int from, int to) {
            int source = FindSlot(items, from);
            int target = FindSlot(items, to);

            if (source == -1 || target == -1 || !isSame(items[target], items[source])) {
                return Swap(items, from, to);
            }
            if (from == to) {
                return false;
            }
            InventoryItem into = items[target];
            int moved = System.Math.Min(MaxStack(into.itemId) - into.quantity, items[source].quantity);
            if (moved <= 0) {
                return false; // already full
            }
            into.quantity += moved;
            items[target] = into;
            takeFrom(items, source, moved);
            return true;
        }

        /// <summary>
        /// Moves one unit of the stack in <paramref name="from"/> to <paramref name="to"/>, which has to be
        /// empty or hold the same item with room for it. False if it couldn't.
        /// </summary>
        public static bool MoveOne(IList<InventoryItem> items, int from, int to) {
            if (from == to || to < 1 || to > SlotCount) {
                return false;
            }
            int source = FindSlot(items, from);
            if (source == -1) {
                return false;
            }
            InventoryItem moved = items[source];
            int target = FindSlot(items, to);

            if (target == -1) {
                items.Add(new InventoryItem() { slot = to, itemId = moved.itemId, color = moved.color, quantity = 1, health = moved.health });
            } else if (isSame(items[target], moved) && items[target].quantity < MaxStack(moved.itemId)) {
                InventoryItem stack = items[target];
                stack.quantity++;
                items[target] = stack;
            } else {
                return false;
            }

            takeFrom(items, FindSlot(items, from), 1);
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
