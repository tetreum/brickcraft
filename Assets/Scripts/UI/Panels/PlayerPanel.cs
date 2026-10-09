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

        public GameObject crosshair;
        /// <summary>The player's block coordinates, if the setting is on.</summary>
        public Text coordinates;

        private const float CoordinatesInterval = 0.1f;
        private float nextCoordinatesUpdate;

        void Awake()
        {
            Instance = this;
            Events.EventManager.InventoryChanged.Subscribe(onInventoryChanged);
            Events.EventManager.SelectedSlotChanged.Subscribe(onSelectedSlotChanged);
            Events.EventManager.SettingChanged.Subscribe(onSettingChanged);
            applySettings();
        }

        private void OnDestroy() {
            Events.EventManager.InventoryChanged.Unsubscribe(onInventoryChanged);
            Events.EventManager.SelectedSlotChanged.Unsubscribe(onSelectedSlotChanged);
            Events.EventManager.SettingChanged.Unsubscribe(onSettingChanged);
        }

        private void onSettingChanged(Events.SettingChangedEvent e) {
            applySettings();
        }

        private void applySettings() {
            crosshair.SetActive(GameSettings.Crosshair);
            coordinates.gameObject.SetActive(GameSettings.ShowCoordinates);
        }

        // the player moves all the time, so the coordinates are refreshed a few times per second
        private void Update() {
            if (!coordinates.gameObject.activeSelf || Player.Instance == null || Time.unscaledTime < nextCoordinatesUpdate) {
                return;
            }
            nextCoordinatesUpdate = Time.unscaledTime + CoordinatesInterval;

            Vector3Int block = Bricks.BrickGrid.CellToBlock(Bricks.BrickGrid.WorldToCell(Player.Instance.transform.position));
            coordinates.text = "X: " + block.x + "   Y: " + block.y + "   Z: " + block.z;
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
                ItemColorSwatch.Set(slot.GetComponent<RawImage>(), inventory[slotId].item, inventory[slotId].color);
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
