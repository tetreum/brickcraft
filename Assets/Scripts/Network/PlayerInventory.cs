using System.Collections.Generic;
using Brickcraft.UI;
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

        /// <summary>False if the inventory is full.</summary>
        [Server]
        public bool ServerAdd(int itemId, int quantity, int health = 0, int slot = 0) {
            bool added = Inventory.Add(items, itemId, quantity, health, slot);

            if (added) {
                save();
            }
            return added;
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
        public void CmdSwapSlots(int slotA, int slotB) {
            if (Inventory.Swap(items, slotA, slotB)) {
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

        // -------- client --------

        private void onItemsChanged(SyncList<InventoryItem>.Operation op, int index, InventoryItem oldItem, InventoryItem newItem) {
            if (isLocalPlayer) {
                refreshUI();
            }
        }

        private void refreshUI() {
            if (PlayerPanel.Instance != null) {
                PlayerPanel.Instance.reload();
            }
            if (InventoryPanel.Instance != null && InventoryPanel.Instance.gameObject.activeInHierarchy) {
                InventoryPanel.Instance.reload();
            }
        }
    }
}
