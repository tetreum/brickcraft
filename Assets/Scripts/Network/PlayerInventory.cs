using System.Collections.Generic;
using Brickcraft.Events;
using Mirror;

namespace Brickcraft.Network
{
    /// <summary>
    /// The inventory of a player. The server owns it and stores it in the database on every change,
    /// the list is synced to its owner only (syncMode Owner), who asks for changes through commands.
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        public readonly SyncList<InventoryItem> items = new SyncList<InventoryItem>();

        private const int MaxAdminAdd = 1000;

        // server only: the database row of the player this inventory belongs to
        private int playerId;

        /// <summary>Server: what it holds changed (anything moved, came or went).</summary>
        public event System.Action ServerChanged;

        public override void OnStartClient() {
            items.Callback += onItemsChanged;
        }

        public override void OnStopClient() {
            items.Callback -= onItemsChanged;
        }

        // -------- server --------

        [Server]
        public void ServerLoad(int playerId, List<InventoryItem> stored) {
            this.playerId = playerId;
            items.Clear();
            foreach (InventoryItem stack in stored) {
                InventoryItem valid = stack;
                valid.health = Inventory.ValidHealth(stack.itemId, stack.health);
                items.Add(valid);
            }
            ServerChanged?.Invoke();
        }

        /// <summary>The armor points of what it wears (see Combat.Armor).</summary>
        [Server]
        public int ServerArmor() {
            int points = 0;
            foreach (InventoryItem stack in items) {
                if (Inventory.IsEquipmentSlot(stack.slot) && Server.items.TryGetValue(stack.itemId, out Item item)) {
                    points += item.armor;
                }
            }
            return points;
        }

        /// <summary>A hit it takes (see Health.ServerReduceDamage): what it wears takes some of it, and wears.</summary>
        [Server]
        public int ServerAbsorb(Combat.DamageInfo damage) {
            int points = ServerArmor();
            if (points == 0 || damage.kind == Combat.DamageInfo.Kind.Script) {
                return damage.amount;
            }
            for (int slot = Inventory.FirstEquipmentSlot; slot <= Inventory.LastEquipmentSlot; slot++) {
                ServerWear(slot);
            }
            return Combat.Armor.Reduce(damage.amount, points);
        }

        /// <summary>
        /// One use of what's in the slot (a hit given with a weapon, taken with armor): items that wear (see
        /// Item.durability) get worn, and break (are gone, its owner is told) once worn out.
        /// </summary>
        [Server]
        public void ServerWear(int slot) {
            int index = Inventory.FindSlot(items, slot);
            if (index == -1 || !Server.items.TryGetValue(items[index].itemId, out Item item) || !item.Wears) {
                return;
            }
            InventoryItem stack = items[index];
            stack.health++;
            if (stack.health >= item.durability) {
                items.RemoveAt(index);
                connectionToClient?.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "Your " + item.name + " broke" });
            } else {
                items[index] = stack;
            }
            save();
        }

        [Server]
        public bool ServerHas(string itemId, int color, int quantity, int health) {
            return Inventory.Has(items, itemId, color, quantity, health);
        }

        /// <summary>Adds them all, or none and false if they don't fit. Colours the item can't have become its default one.</summary>
        [Server]
        public bool ServerAdd(string itemId, int color, int quantity, int health = 0, int slot = 0) {
            if (Server.items.TryGetValue(itemId, out Item item)) {
                color = item.ValidColor(color);
            }
            bool added = Inventory.Add(items, itemId, color, quantity, health, slot);

            if (added) {
                save();
            }
            return added;
        }

        /// <summary>Takes them from the stack in the slot, false if it hasn't enough.</summary>
        [Server]
        public bool ServerRemoveFromSlot(int slot, int quantity) {
            bool removed = Inventory.RemoveFromSlot(items, slot, quantity);

            if (removed) {
                save();
            }
            return removed;
        }

        /// <summary>The stack in the slot takes that colour (one its item can have), false if it can't or the slot is empty.</summary>
        [Server]
        public bool ServerSetColor(int slot, int color) {
            int index = Inventory.FindSlot(items, slot);
            if (index == -1 || !Server.items.TryGetValue(items[index].itemId, out Item item) || !item.AllowsColor(color)) {
                return false;
            }
            InventoryItem stack = items[index];
            stack.color = color;
            items[index] = stack;
            save();
            return true;
        }

        /// <summary>The stack in the slot, null if it's empty.</summary>
        [Server]
        public InventoryItem? ServerGetSlot(int slot) {
            int index = Inventory.FindSlot(items, slot);
            return index == -1 ? (InventoryItem?)null : items[index];
        }

        [Server]
        public bool ServerRemove(string itemId, int color, int quantity, int health = 0) {
            bool removed = Inventory.Remove(items, itemId, color, quantity, health);

            if (removed) {
                save();
            }
            return removed;
        }

        [Server]
        private void save() {
            BrickcraftNetworkManager.Instance.Database.SaveInventory(playerId, items);
            ServerChanged?.Invoke();

            // the host's own client shares these items, refresh its UI too
            if (isLocalPlayer) {
                refreshUI();
            }
        }

        // -------- client to server --------

        [Command]
        public void CmdMoveStack(int fromSlot, int toSlot) {
            if (Inventory.MoveStack(items, fromSlot, toSlot)) {
                save();
            }
        }

        /// <summary>Moves one unit of a stack to another slot (right click while dragging it).</summary>
        [Command]
        public void CmdMoveOne(int fromSlot, int toSlot) {
            if (Inventory.MoveOne(items, fromSlot, toSlot)) {
                save();
            }
        }

        /// <summary>
        /// Moves a stack (or one unit) between this inventory and the storage brick it's looking into, or within
        /// that brick (see BrickStorage, Inventory.MoveBetween).
        /// </summary>
        [Command]
        public void CmdStorageMove(string brickId, bool fromStorage, int fromSlot, bool toStorage, int toSlot, bool one) {
            if (!BrickStorage.IsViewing(connectionToClient, brickId, out Brick storage)) {
                return;
            }
            List<InventoryItem> kept = BrickStorage.Contents(storage);
            int slots = BrickStorage.SlotsOf(storage);
            bool moved;
            if (fromStorage && toStorage) {
                moved = one ? Inventory.MoveOne(kept, fromSlot, toSlot, slots) : Inventory.MoveStack(kept, fromSlot, toSlot, slots);
            } else if (fromStorage) {
                moved = Inventory.MoveBetween(kept, slots, fromSlot, items, Inventory.SlotCount, toSlot, one);
            } else if (toStorage) {
                moved = Inventory.MoveBetween(items, Inventory.SlotCount, fromSlot, kept, slots, toSlot, one);
            } else {
                moved = one ? Inventory.MoveOne(items, fromSlot, toSlot) : Inventory.MoveStack(items, fromSlot, toSlot);
            }
            if (!moved) {
                return;
            }
            if (fromStorage || toStorage) {
                BrickStorage.ServerChanged(storage, kept);
            }
            if (!fromStorage || !toStorage) {
                save();
            }
        }

        /// <summary>Throws some of the stack in a slot in front of the player (see DroppedItems): it can't pick them up again right away.</summary>
        [Command]
        public void CmdDrop(int slot, int quantity) {
            InventoryItem? stack = ServerGetSlot(slot);
            Combat.Health health = GetComponent<Combat.Health>();
            if (!stack.HasValue || quantity < 1 || health.isDead) {
                return;
            }
            quantity = UnityEngine.Mathf.Min(quantity, stack.Value.quantity);
            if (!ServerRemoveFromSlot(slot, quantity)) {
                return;
            }
            UnityEngine.Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();
            DroppedItems.ServerDrop(stack.Value.itemId, stack.Value.color, quantity, stack.Value.health,
                transform.position + forward * 0.6f + UnityEngine.Vector3.up * 0.5f, forward * 4 + UnityEngine.Vector3.up * 2, ThrowPickupDelay);
        }

        /// <summary>Seconds before what a player throws can be picked up (by them too).</summary>
        public const float ThrowPickupDelay = 2;

        /// <summary>Crafts a recipe (see Recipes), putting the result in the given slot.</summary>
        [Command]
        public void CmdCraft(string recipeId, int targetSlot) {
            Recipe recipe = Recipes.Find(recipeId);

            if (recipe == null || !Inventory.IsSlotFree(items, targetSlot)) {
                return;
            }

            foreach (Ingredient ingredient in recipe.ingredients) {
                if (!Inventory.Has(items, ingredient.itemId, Inventory.AnyColor, ingredient.quantity, 0)) {
                    return;
                }
            }
            foreach (Ingredient ingredient in recipe.ingredients) {
                Inventory.Remove(items, ingredient.itemId, Inventory.AnyColor, ingredient.quantity);
            }
            // in the item's default colour
            Inventory.Add(items, recipe.itemId, Server.items[recipe.itemId].color, recipe.quantity, 0, targetSlot);
            save();
        }

        /// <summary>Admins only: adds items out of nothing (the ALL tab of the inventory), in a colour the item can have.</summary>
        [Command]
        public void CmdAdminAdd(string itemId, int color, int quantity) {
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;

            if (player == null || !player.record.IsAdmin || !Server.items.TryGetValue(itemId, out Item item) || quantity < 1 || quantity > MaxAdminAdd) {
                return;
            }
            if (!Inventory.Add(items, itemId, item.ValidColor(color), quantity)) {
                connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "There's no room for " + quantity + " x " + item.name + " in your inventory" });
                return;
            }
            save();
        }

        // -------- client --------

        private void onItemsChanged(SyncList<InventoryItem>.Operation op, int index, InventoryItem oldItem, InventoryItem newItem) {
            if (isLocalPlayer) {
                refreshUI();
            }
        }

        // A change can take several steps (a swap moves two items one after the other, and each step
        // is synced on its own), so the UI is told once per frame, after all of them.
        private bool isUIOutdated;

        private void refreshUI() {
            isUIOutdated = true;
        }

        private void LateUpdate() {
            if (isUIOutdated) {
                isUIOutdated = false;
                EventManager.InventoryChanged.Raise(new InventoryChangedEvent());
            }
        }
    }
}
