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
        /// <summary>How worn it is: uses taken out of its item's durability (0 new, and always for items that don't wear).</summary>
        public int health;
    }

    /// <summary>
    /// Inventory rules. Only the server changes inventories, clients get the result synced.
    /// Slots go from 1 to <see cref="SlotCount"/>, the last ones being the fast inventory. An item
    /// can be in several stacks, each holding up to its Item.maxStack. Stacks are of one item, colour
    /// and health. Other containers (a chest's slots, see Network.BrickStorage) follow the same rules with
    /// their own number of slots (slots), filled from the first one.
    ///
    /// Past a player's slots come its equipment slots (what it wears, see Item.ArmorSlot): only stacks moved there
    /// by hand go in them, of armor for that slot. Adding, counting and removing items leave them alone.
    /// </summary>
    public static class Inventory
    {
        public const int SlotCount = 36;
        public const int FirstFastSlot = 28;

        /// <summary>The first equipment slot (the head's), then chest, legs, feet and the left hand's (see EquipmentSlot).</summary>
        public const int FirstEquipmentSlot = SlotCount + 1;
        public const int LastEquipmentSlot = SlotCount + 5;

        /// <summary>The player's slot where armor for that part is worn.</summary>
        public static int EquipmentSlot(Item.ArmorSlot armorSlot) {
            return SlotCount + (int)armorSlot;
        }

        public static bool IsEquipmentSlot(int slot) {
            return slot >= FirstEquipmentSlot && slot <= LastEquipmentSlot;
        }

        /// <summary>Whether that item can be in that slot of a player's inventory: equipment slots only take armor worn there.</summary>
        public static bool Fits(string itemId, int slot) {
            if (!IsEquipmentSlot(slot)) {
                return true;
            }
            return Server.items.TryGetValue(itemId, out Item item) && item.type == Item.Type.Armor && EquipmentSlot(item.armorSlot) == slot;
        }

        // the last slot stacks can be moved to: a player's go on to its equipment
        private static int lastSlot(int slots) {
            return slots == SlotCount ? LastEquipmentSlot : slots;
        }

        // whether the stack can be in that slot (the container's, or a player's equipment)
        private static bool fits(InventoryItem stack, int slot, int slots) {
            return slots != SlotCount || Fits(stack.itemId, slot);
        }

        /// <summary>A stack's health for that item: items that don't wear are never worn (old saves had 100 for crafted ones).</summary>
        public static int ValidHealth(string itemId, int health) {
            return Server.items.TryGetValue(itemId, out Item item) && item.Wears ? System.Math.Max(0, health) : 0;
        }

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

        public static bool IsSlotFree(IList<InventoryItem> items, int slot, int slots = SlotCount) {
            return slot >= 1 && slot <= slots && FindSlot(items, slot) == -1;
        }

        private static bool isSame(InventoryItem stack, string itemId, int color, int health) {
            return stack.itemId == itemId && (color == AnyColor || stack.color == color) && stack.health == health;
        }

        private static bool isSame(InventoryItem stack, InventoryItem other) {
            return isSame(stack, other.itemId, other.color, other.health);
        }

        /// <summary>How many there are, in all their stacks (not what's worn).</summary>
        public static int Count(IList<InventoryItem> items, string itemId, int color, int health) {
            int count = 0;
            foreach (InventoryItem stack in items) {
                if (isSame(stack, itemId, color, health) && !IsEquipmentSlot(stack.slot)) {
                    count += stack.quantity;
                }
            }
            return count;
        }

        public static bool Has(IList<InventoryItem> items, string itemId, int color, int quantity, int health) {
            return Count(items, itemId, color, health) >= quantity;
        }

        /// <summary>How many more fit: what's missing in their stacks, plus whole empty slots.</summary>
        public static int Room(IList<InventoryItem> items, string itemId, int color, int health, int slots = SlotCount) {
            int max = MaxStack(itemId);
            int room = 0;
            foreach (InventoryItem stack in items) {
                if (isSame(stack, itemId, color, health) && stack.quantity < max && stack.slot <= slots) {
                    room += max - stack.quantity;
                }
            }
            for (int slot = 1; slot <= slots; slot++) {
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
        public static bool Add(IList<InventoryItem> items, string itemId, int color, int quantity, int health = 0, int slot = 0, int slots = SlotCount) {
            int max = MaxStack(itemId);
            health = ValidHealth(itemId, health);

            if (slot != 0) {
                int stack = FindSlot(items, slot);
                if (stack == -1) {
                    if (slot < 1 || slot > slots || quantity > max) {
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

            if (Room(items, itemId, color, health, slots) < quantity) {
                return false;
            }
            for (int i = 0; i < items.Count && quantity > 0; i++) {
                InventoryItem stack = items[i];
                if (isSame(stack, itemId, color, health) && stack.quantity < max && stack.slot <= slots) {
                    int added = System.Math.Min(max - stack.quantity, quantity);
                    stack.quantity += added;
                    items[i] = stack;
                    quantity -= added;
                }
            }
            while (quantity > 0) {
                int added = System.Math.Min(max, quantity);
                items.Add(new InventoryItem() { slot = firstFreeSlot(items, slots), itemId = itemId, color = color, quantity = added, health = health });
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
                    if (isSame(items[i], itemId, color, health) && !IsEquipmentSlot(items[i].slot) && (smallest == -1 || items[i].quantity < items[smallest].quantity)) {
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
        public static bool MoveStack(IList<InventoryItem> items, int from, int to, int slots = SlotCount) {
            int source = FindSlot(items, from);
            int target = FindSlot(items, to);

            if (source == -1 || target == -1 || !isSame(items[target], items[source])) {
                return Swap(items, from, to, slots);
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
        public static bool MoveOne(IList<InventoryItem> items, int from, int to, int slots = SlotCount) {
            if (from == to || to < 1 || to > lastSlot(slots)) {
                return false;
            }
            int source = FindSlot(items, from);
            if (source == -1 || !fits(items[source], to, slots)) {
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

        /// <summary>Moves a stack to another slot, swapping it with whatever is there (if each fits where it goes).</summary>
        public static bool Swap(IList<InventoryItem> items, int slotA, int slotB, int slots = SlotCount) {
            int last = lastSlot(slots);
            if (slotA < 1 || slotA > last || slotB < 1 || slotB > last || slotA == slotB) {
                return false;
            }
            int a = FindSlot(items, slotA);
            int b = FindSlot(items, slotB);
            if ((a != -1 && !fits(items[a], slotB, slots)) || (b != -1 && !fits(items[b], slotA, slots))) {
                return false;
            }

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

        /// <summary>
        /// Moves the stack in a slot of one container to a slot of another (a player's inventory and a chest):
        /// whole (onto the same item it merges as much as fits, onto another they swap) or one unit (onto an
        /// empty slot or the same item with room). False if nothing changed.
        /// </summary>
        public static bool MoveBetween(IList<InventoryItem> source, int sourceSlots, int from, IList<InventoryItem> target, int targetSlots, int to, bool one) {
            int s = FindSlot(source, from);
            if (s == -1 || from < 1 || from > sourceSlots || to < 1 || to > targetSlots) {
                return false;
            }
            InventoryItem moving = source[s];
            int t = FindSlot(target, to);
            int max = MaxStack(moving.itemId);
            if (t != -1 && isSame(target[t], moving)) {
                InventoryItem into = target[t];
                int moved = System.Math.Min(max - into.quantity, one ? 1 : moving.quantity);
                if (moved <= 0) {
                    return false;
                }
                into.quantity += moved;
                target[t] = into;
                takeFrom(source, s, moved);
                return true;
            }
            if (one) {
                if (t != -1) {
                    return false;
                }
                target.Add(new InventoryItem() { slot = to, itemId = moving.itemId, color = moving.color, quantity = 1, health = moving.health });
                takeFrom(source, s, 1);
                return true;
            }
            // whole: to an empty slot, or swapping with what's there
            source.RemoveAt(s);
            if (t != -1) {
                InventoryItem back = target[t];
                target.RemoveAt(t);
                back.slot = from;
                source.Add(back);
            }
            moving.slot = to;
            target.Add(moving);
            return true;
        }

        private static int firstFreeSlot(IList<InventoryItem> items, int slots = SlotCount) {
            if (slots != SlotCount) {
                for (int slot = 1; slot <= slots; slot++) {
                    if (IsSlotFree(items, slot, slots)) {
                        return slot;
                    }
                }
                return 0;
            }
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
