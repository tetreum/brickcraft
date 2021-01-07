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
        private void Awake() {
            Instance = this;
        }

        private void OnEnable() {
            resetCraftingSlots();
            reload();
            Game.unlockMouse();
        }

        private void OnDisable() {
            Game.lockMouse();
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
        }

        void resetCraftingSlots() {
            foreach (CraftingSlot slot in craftingSlots) {
                slot.setVisible(false);
            }
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
                    Debug.Log(recipe.itemId + " matches");
                    break;
                }
            }

            
        }
    }
}
