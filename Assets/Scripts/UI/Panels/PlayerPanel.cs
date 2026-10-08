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
        public UserItem selectedItem {
            get {
                var slots = Player.Instance.getInventoryBySlot();

                if (!slots.ContainsKey(selectedSlot)) {
                    return null;
                }

                return slots[selectedSlot];
            }
        }

        public InventorySlot[] fastInventorySlots;

        private int selectedSlot {
            get {
                return _selectedSlot;
            }
            set {
                _selectedSlot = value;
                updateSelectedItemBackground();
            }
        }
        private int _selectedSlot;
        private int firstSlot = 28;

        void Awake()
        {
            Instance = this;
            selectedSlot = firstSlot; // by default the first slot from fastInventory starting from the left
        }

        private void OnEnable() {
            Game.lockMouse();
            reload();
        }

        public void reload() {
            if (Player.Instance == null) {
                return;
            }

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

        public void switchSelectedItem () {
            selectedSlot++;

            if (selectedSlot < firstSlot || selectedSlot > Player.Instance.inventorySlots) {
                selectedSlot = firstSlot;
            }
        }
        public void selectFastSlot(int slotNumber) {
            int newSlotNumber = firstSlot + slotNumber;

            // has toggled the current slot, wants to unselect
            if (newSlotNumber == selectedSlot) {
                newSlotNumber = -1;
            }

            selectedSlot = newSlotNumber;
        }

        private void updateSelectedItemBackground () {
            foreach (var slot in fastInventorySlots) {
                slot.transform.parent.GetComponent<Image>().sprite = int.Parse(slot.transform.parent.name) == selectedSlot ? selectedBackgroundSprite : normalBackgroundSprite;
            }
        }
    }
}
