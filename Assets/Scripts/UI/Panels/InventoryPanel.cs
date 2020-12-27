using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class InventoryPanel : MonoBehaviour
    {
        public static InventoryPanel Instance;
        public InventorySlot[] inventorySlots;
        private void Awake() {
            Instance = this;
        }

        private void OnEnable() {
            reload();
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
    }
}
