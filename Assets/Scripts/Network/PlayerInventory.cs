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
            items.AddRange(stored);
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
            Inventory.Add(items, recipe.itemId, Server.items[recipe.itemId].color, recipe.quantity, 100, targetSlot);
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
