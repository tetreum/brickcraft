using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class PlayerPanel : MonoBehaviour
    {
        public static PlayerPanel Instance;
        public Sprite selectedBackgroundSprite;
        public Sprite normalBackgroundSprite;

        /// <summary>The fast inventory (hotbar). Which slot is selected is up to the player, see Player.SelectedSlot.</summary>
        public InventorySlot[] fastInventorySlots;

        void Awake()
        {
            Instance = this;
            Events.EventManager.InventoryChanged.Subscribe(onInventoryChanged);
            Events.EventManager.SelectedSlotChanged.Subscribe(onSelectedSlotChanged);
        }

        private void OnDestroy() {
            Events.EventManager.InventoryChanged.Unsubscribe(onInventoryChanged);
            Events.EventManager.SelectedSlotChanged.Unsubscribe(onSelectedSlotChanged);
        }

        private void onInventoryChanged(Events.InventoryChangedEvent e) {
            reload();
        }

        private void onSelectedSlotChanged(Events.SelectedSlotChangedEvent e) {
            showSelectedSlot(e.slot);
        }

        private void OnEnable() {
            Game.lockMouse();
            reload();
        }

        public void reload() {
            if (Player.Instance == null) {
                return;
            }
            showSelectedSlot(Player.Instance.SelectedSlot);

            Dictionary<int, UserItem> inventory = Player.Instance.getInventoryBySlot();
            InventorySlot slot;
            int slotId;

            for (int i = 0; i < fastInventorySlots.Length; i++) {
                slot = fastInventorySlots[i];
                slotId = int.Parse(slot.originalParent == null ? slot.transform.parent.name : slot.originalParent.name);

                if (!inventory.ContainsKey(slotId)) {
                    slot.setVisible(false);
                    continue;
                }
                slot.setVisible(true);
                slot.GetComponent<RawImage>().texture = inventory[slotId].item.icon;
                slot.quantity.text = inventory[slotId].quantity.ToString();
            }
        }

        private void showSelectedSlot (int selectedSlot) {
            foreach (var slot in fastInventorySlots) {
                slot.transform.parent.GetComponent<Image>().sprite = int.Parse(slot.transform.parent.name) == selectedSlot ? selectedBackgroundSprite : normalBackgroundSprite;
            }
        }
    }
}
