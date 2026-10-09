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
        public bool ServerHas(int itemId, int quantity, int health) {
            return Inventory.Has(items, itemId, quantity, health);
        }

        /// <summary>Adds them all, or none and false if they don't fit.</summary>
        [Server]
        public bool ServerAdd(int itemId, int quantity, int health = 0, int slot = 0) {
            bool added = Inventory.Add(items, itemId, quantity, health, slot);

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
        public bool ServerRemove(int itemId, int quantity, int health = 0) {
            bool removed = Inventory.Remove(items, itemId, quantity, health);

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

        /// <summary>Crafts one of Game.craftingRecipes, putting the result in the given slot.</summary>
        [Command]
        public void CmdCraft(int recipeIndex, int targetSlot) {
            Recipe[] recipes = Game.Instance.craftingRecipes;

            if (recipeIndex < 0 || recipeIndex >= recipes.Length || !Inventory.IsSlotFree(items, targetSlot)) {
                return;
            }
            Recipe recipe = recipes[recipeIndex];

            foreach (Ingredient ingredient in recipe.ingredients) {
                if (!Inventory.Has(items, ingredient.itemId, ingredient.quantity, 0)) {
                    return;
                }
            }
            foreach (Ingredient ingredient in recipe.ingredients) {
                Inventory.Remove(items, ingredient.itemId, ingredient.quantity);
            }
            Inventory.Add(items, recipe.itemId, recipe.quantity, 100, targetSlot);
            save();
        }

        /// <summary>Admins only: adds items out of nothing (the ALL tab of the inventory).</summary>
        [Command]
        public void CmdAdminAdd(int itemId, int quantity) {
            ConnectedPlayer player = connectionToClient.authenticationData as ConnectedPlayer;

            if (player == null || !player.record.IsAdmin || !Server.items.TryGetValue(itemId, out Item item) || quantity < 1 || quantity > MaxAdminAdd) {
                return;
            }
            if (!Inventory.Add(items, itemId, quantity)) {
                connectionToClient.Send(new ChatMessage() { sender = ChatCommands.ServerName, text = "There's no room for " + quantity + " x " + item.name + " in your inventory" });
                return;
            }
            save();
            UnityEngine.Debug.Log(player.record.Name + " took " + quantity + " x " + item.name + " (" + itemId + ") from the ALL tab");
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
