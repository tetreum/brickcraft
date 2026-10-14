using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// What a storage brick keeps (a chest), over the player's inventory, like the inventory panel without its
    /// crafting: stacks are dragged between the two grids and the hotbar (see InventorySlot, SlotGroup). It opens
    /// when the server sends a storage brick's contents (Network.ClientStorage, see Menu) and closes with Escape,
    /// the inventory key, or when the server closes it (too far, broken, locked).
    /// </summary>
    public class StoragePanel : MonoBehaviour
    {
        public const string PanelName = "StoragePanel";

        public Text title;
        /// <summary>The storage grid scrolls when it has more than 27 slots.</summary>
        public ScrollRect scroll;
        /// <summary>The storage brick's slots, 1 to its count (the rest are hidden).</summary>
        public InventorySlot[] storageSlots;
        /// <summary>The player's inventory, slots 1 to 27 (the hotbar has the others).</summary>
        public InventorySlot[] inventorySlots;

        private void OnEnable() {
            ClientStorage.Changed += onStorageChanged;
            Events.EventManager.InventoryChanged.Subscribe(onInventoryChanged);
            reload();
            if (scroll != null) {
                scroll.verticalNormalizedPosition = 1; // from the top
            }
            Game.unlockMouse();
            if (Player.Instance != null) {
                Player.Instance.freeze(Player.FreezeReason.ViewingInventory);
            }
        }

        private void OnDisable() {
            ClientStorage.Changed -= onStorageChanged;
            Events.EventManager.InventoryChanged.Unsubscribe(onInventoryChanged);
            ClientStorage.Close();
            Game.lockMouse();
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.ViewingInventory);
            }
        }

        private void onStorageChanged() {
            if (!ClientStorage.IsOpen) {
                Menu.Instance.hidePanel(PanelName); // the server closed it
                return;
            }
            reload();
        }

        private void onInventoryChanged(Events.InventoryChangedEvent e) {
            reload();
        }

        public void reload() {
            title.text = ClientStorage.Title;
            for (int i = 0; i < storageSlots.Length; i++) {
                int slot = i + 1;
                storageSlots[i].transform.parent.gameObject.SetActive(slot <= ClientStorage.Slots);
                InventoryItem? stack = ClientStorage.InSlot(slot);
                storageSlots[i].show(stack.HasValue && Server.items.TryGetValue(stack.Value.itemId, out Item item) ? item : null,
                    stack.HasValue ? stack.Value.color : 0, stack.HasValue ? stack.Value.quantity : 0, stack.HasValue ? stack.Value.health : 0);
            }
            if (Player.Instance == null) {
                return;
            }
            var inventory = Player.Instance.getInventoryBySlot();
            for (int i = 0; i < inventorySlots.Length; i++) {
                bool has = inventory.TryGetValue(i + 1, out UserItem stack);
                inventorySlots[i].show(has ? stack.item : null, has ? stack.color : 0, has ? stack.quantity : 0, has ? stack.health : 0);
            }
        }
    }
}
