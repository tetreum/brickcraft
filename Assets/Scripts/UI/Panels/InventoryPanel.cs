using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The player's inventory (slots 1 to 27, the hotbar has the rest), what it wears (its equipment slots, see
    /// Inventory.EquipmentSlot: armor is dragged in and out of them) and crafting.
    /// </summary>
    public class InventoryPanel : MonoBehaviour
    {
        public static InventoryPanel Instance;
        public InventorySlot[] inventorySlots;
        /// <summary>What's worn: head, chest, legs, feet and left hand (their cells are named after their slots, 37 to 41).</summary>
        public InventorySlot[] equipmentSlots;

        // the name of an equipment cell's caption (Head...), shown while it's empty
        public const string EquipmentLabel = "Label";
        public CraftingSlot[] craftingSlots;
        public CraftingOutputSlot craftingOutputSlot;

        [Header("Tabs")]
        public Button inventoryTab;
        public Button allTab;
        /// <summary>What the inventory tab shows: the inventory grid and the crafting area.</summary>
        public GameObject[] inventoryViews;
        /// <summary>What the ALL tab (admins only) shows, see AllItemsTab.</summary>
        public GameObject allItemsView;

        // like the settings' tabs: the selected one in green
        private static readonly Color SelectedTab = new Color(0.18f, 0.71f, 0.13f);
        private static readonly Color UnselectedTab = new Color(1f, 1f, 1f, 0f);

        // the tab shown last: it's the one shown again when the panel opens
        private bool showingAll;

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
            showTab(showingAll && isLocalPlayerAdmin());
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
            showingAll = all;
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

            // the player is already gone when leaving the game with the inventory open
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.ViewingInventory);
            }
        }

        public void reload () {
            Dictionary<int, UserItem> inventory = Player.Instance.getInventoryBySlot();

            for (int i = 0; i < inventorySlots.Length; i++) {
                show(inventorySlots[i], inventory, i + 1);
            }
            for (int i = 0; i < equipmentSlots.Length; i++) {
                bool worn = show(equipmentSlots[i], inventory, Inventory.FirstEquipmentSlot + i);
                Transform cell = equipmentSlots[i].originalParent != null ? equipmentSlots[i].originalParent : equipmentSlots[i].transform.parent;
                Transform label = cell.Find(EquipmentLabel);
                if (label != null) {
                    label.gameObject.SetActive(!worn);
                }
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

        // the stack in that slot, false if there's none
        private static bool show(InventorySlot slot, Dictionary<int, UserItem> inventory, int slotId) {
            UserItem stack;
            if (!inventory.TryGetValue(slotId, out stack)) {
                slot.setVisible(false);
                return false;
            }
            slot.show(stack.item, stack.color, stack.quantity, stack.health);
            return true;
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

            foreach (Recipe recipe in Recipes.All) {
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
            Player.Instance.GetComponent<Network.PlayerInventory>().CmdCraft(recipe.id, slotId);
        }
    }
}
