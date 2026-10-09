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

        [Header("Tabs")]
        public Button inventoryTab;
        public Button allTab;
        /// <summary>What the inventory tab shows: the inventory grid and the crafting area.</summary>
        public GameObject[] inventoryViews;
        /// <summary>What the ALL tab (admins only) shows, see AllItemsTab.</summary>
        public GameObject allItemsView;

        private static readonly Color SelectedTab = Color.white;
        private static readonly Color UnselectedTab = new Color(0.75f, 0.75f, 0.75f, 1f);

        private void Awake() {
            Instance = this;
            inventoryTab.onClick.AddListener(showInventory);
            allTab.onClick.AddListener(showAll);
        }

        private void OnEnable() {
            Events.EventManager.InventoryChanged.Subscribe(onInventoryChanged);
            Events.EventManager.PlayerRoleChanged.Subscribe(onRoleChanged);

            resetCraftingSlots();
            reload();
            showInventory();
            Game.unlockMouse();
            Player.Instance.freeze(Player.FreezeReason.ViewingInventory);
        }

        private void onInventoryChanged(Events.InventoryChangedEvent e) {
            reload();
        }

        // the role can change while it's open: the ALL tab comes and goes with admin
        private void onRoleChanged(Events.PlayerRoleChangedEvent e) {
            if (!e.isLocalPlayer) {
                return;
            }
            if (e.newRole != Network.PlayerRoles.Admin && allItemsView.activeSelf) {
                showInventory();
            } else {
                allTab.gameObject.SetActive(e.newRole == Network.PlayerRoles.Admin);
            }
        }

        private static bool isLocalPlayerAdmin() {
            return Player.Instance != null && Player.Instance.network.role == Network.PlayerRoles.Admin;
        }

        public void showInventory() {
            showTab(false);
        }

        public void showAll() {
            if (isLocalPlayerAdmin()) {
                showTab(true);
            }
        }

        private void showTab(bool all) {
            foreach (GameObject view in inventoryViews) {
                view.SetActive(!all);
            }
            allItemsView.SetActive(all);
            allTab.gameObject.SetActive(isLocalPlayerAdmin());

            inventoryTab.GetComponent<Image>().color = all ? UnselectedTab : SelectedTab;
            allTab.GetComponent<Image>().color = all ? SelectedTab : UnselectedTab;
        }

        private void OnDisable() {
            Events.EventManager.InventoryChanged.Unsubscribe(onInventoryChanged);
            Events.EventManager.PlayerRoleChanged.Unsubscribe(onRoleChanged);

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
