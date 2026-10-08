using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class InventoryPanel : MonoBehaviour
    {
        public static InventoryPanel Instance;
        public InventorySlot[] inventorySlots;
        public CraftingSlot[] craftingSlots;
        public CraftingOutputSlot craftingOutputSlot;
        private void Awake() {
            Instance = this;
        }

        private void OnEnable() {
            resetCraftingSlots();
            reload();
            Game.unlockMouse();
            Player.Instance.freeze(Player.FreezeReason.ViewingInventory);
        }

        private void OnDisable() {
            Game.lockMouse();
            Player.Instance.unFreeze(Player.FreezeReason.ViewingInventory);
        }

        public void reload () {
            Dictionary<int, UserItem> inventory = Player.Instance.getInventoryBySlot();
            InventorySlot slot;
            int slotId;

            for (int i = 0; i < inventorySlots.Length; i++) {
                slotId = (i + 1);
                slot = inventorySlots[i];
            
                if (!inventory.ContainsKey(slotId)) {
                    slot.setVisible(false);
                    continue;
                }
                slot.setVisible(true);
                slot.GetComponent<RawImage>().texture = inventory[slotId].item.icon;
                slot.quantity.text = inventory[slotId].quantity.ToString();
            }

            foreach (CraftingSlot craftingSlot in craftingSlots) {
                if (craftingSlot.currentItem == null) {
                    continue;
                }
                // material got consumed
                if (!inventory.ContainsKey(craftingSlot.currentItem.slot)) {
                    craftingSlot.setVisible(false);

                    // he cannot longer craft then
                    craftingOutputSlot.setVisible(false);
                    continue;
                }
                // to update it's quantitty
                if (craftingSlot.quantity.text != inventory[craftingSlot.currentItem.slot].quantity.ToString()) {
                    craftingSlot.setCurrentItem(craftingSlot.currentItem.slot);

                    // we do not longer know if has enough materials, so lets refresh the possible craft
                    showPossibleCrafting();
                }
            }
        }

        void resetCraftingSlots() {
            foreach (CraftingSlot slot in craftingSlots) {
                slot.setVisible(false);
            }

            craftingOutputSlot.setVisible(false);
        }

        public void showPossibleCrafting () {
            int filledSlots = 0;
            bool found = false;

            foreach (CraftingSlot slot in craftingSlots) {
                if (slot.currentItem != null) {
                    filledSlots++;
                }
            }

            foreach (Recipe recipe in Game.Instance.craftingRecipes) {
                // we can skip those recipes that dont match the amount of items
                if (filledSlots != recipe.ingredients.Length) {
                    continue;
                }

                foreach (Ingredient ingredient in recipe.ingredients) {
                    CraftingSlot slot = craftingSlots[ingredient.slot - 1];

                    if (slot.currentItem == null || 
                        ingredient.itemId != slot.currentItem.id || 
                        slot.currentItem.quantity < ingredient.quantity) {
                        found = false;
                        break;
                    }
                    found = true;
                }
                
                if (found) {
                    craftingOutputSlot.currentRecipe = recipe;
                    craftingOutputSlot.setVisible(true);
                    return;
                }
            }

            craftingOutputSlot.setVisible(false);
        }

        // the server crafts it, the inventory refreshes once it's synced back
        public void craftRecipe (Recipe recipe, int slotId) {
            int recipeIndex = System.Array.IndexOf(Game.Instance.craftingRecipes, recipe);

            Player.Instance.GetComponent<Network.PlayerInventory>().CmdCraft(recipeIndex, slotId);
        }
    }
}
