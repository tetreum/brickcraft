using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class PlayerPanel : MonoBehaviour
    {
        public static PlayerPanel Instance;
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

        private int selectedSlot = 28; // by default the first slot from fastInventory starting from the left

        void Awake()
        {
            Instance = this;
        }

        private void OnEnable() {
            reload();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                switchSelectedItem();
            }
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

            // update brick previewer
            if (selectedItem != null && selectedItem.item.type == Item.Type.Brick) {
                updateBrickPreviewer();
            }
        }

        public void updateBrickPreviewer() {
            BrickModel selectedBrickModel = Server.brickModels[selectedItem.item.id];

            if (BrickCollisionDetector.Instance != null) {
                if (BrickCollisionDetector.Instance.currentBrickType == selectedBrickModel.type) {
                    return;
                }
                Destroy(BrickCollisionDetector.Instance.gameObject);
            }

            GameObject brickPreviewer = Instantiate(Server.brickPrefabs[selectedBrickModel.type.ToString()]);
            brickPreviewer.AddComponent<BrickCollisionDetector>();
        }

        void switchSelectedItem () {

        }
    }
}
