using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The item shown in an inventory slot (its parent, named after the slot number). Dragging it onto
    /// another slot swaps them, onto a crafting slot uses it there. Right clicking a slot while dragging
    /// moves one unit there; after that, letting go puts the rest back. Slots in a storage grid (see
    /// SlotGroup) are a storage brick's: moves to, from or within them go through
    /// Network.PlayerInventory.CmdStorageMove. The pointer over it shows its item's tooltip (see ItemTooltip).
    /// </summary>
    public class InventorySlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Text quantity;
        [HideInInspector]
        public Transform originalParent;

        private bool isDragging;
        private bool hasSplit;

        private int sourceSlot {
            get { return int.Parse(originalParent.name); }
        }

        private bool sourceIsStorage {
            get { return SlotGroup.IsStorage(originalParent); }
        }

        // a stack (or one unit) from where it's dragged to that cell, whichever grids they're in
        private void move(Transform cell, bool one) {
            bool toStorage = SlotGroup.IsStorage(cell);
            int to = int.Parse(cell.name);
            if (!sourceIsStorage && !toStorage) {
                if (one) {
                    Player.Instance.moveOneInventoryItem(sourceSlot, to);
                } else {
                    Player.Instance.moveInventoryStack(sourceSlot, to);
                }
                return;
            }
            if (Network.ClientStorage.IsOpen) {
                Player.Instance.GetComponent<Network.PlayerInventory>().CmdStorageMove(Network.ClientStorage.BrickId, sourceIsStorage, sourceSlot, toStorage, to, one);
            }
        }

        // only the left button drags: Unity starts drags with every button, and a right click (to split)
        // moving a bit would start a second one
        public void OnBeginDrag(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left || isDragging) {
                return;
            }
            if (originalParent == null) {
                originalParent = transform.parent;
            }
            isDragging = true;
            hasSplit = false;
            ItemTooltip.Hide();
            transform.SetParent(Menu.Instance.transform);
            GetComponent<RectTransform> ().SetAsLastSibling ();
        }

        public void OnDrag(PointerEventData eventData) {
            if (isDragging && eventData.button == PointerEventData.InputButton.Left) {
                transform.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData) {
            // not dragging: the drag already ended, its last unit was split off
            if (!isDragging || eventData.button != PointerEventData.InputButton.Left) {
                return;
            }
            Component target;
            Transform cell = findCell(eventData.position, out target);

            // let go over the world (no interface under the pointer): the stack is thrown
            if (cell == null && !hasSplit && !sourceIsStorage && quantityIn(sourceSlot, false) > 0 && !isOverInterface(eventData.position)) {
                Player.Instance.GetComponent<Network.PlayerInventory>().CmdDrop(sourceSlot, quantityIn(sourceSlot, false));
            }
            // nothing is moved if the slot got emptied meanwhile, or units were split off (the rest goes back)
            if (cell != null && !hasSplit && quantityIn(sourceSlot, sourceIsStorage) > 0) {
                if (target is InventorySlot) {
                    move(cell, false);
                } else if (target is CraftingSlot && !sourceIsStorage) {
                    ((CraftingSlot)target).setCurrentItem(sourceSlot);
                }
            }
            stopDragging();
        }

        // right click while dragging: one unit goes to the slot under the pointer
        private void Update() {
            if (!isDragging) {
                return;
            }
            if (!GameInput.KeyDown(KeyCode.Mouse1)) {
                return;
            }

            Component target;
            Transform cell = findCell(GameInput.PointerPosition, out target);
            if (cell == null || !(target is InventorySlot) || (int.Parse(cell.name) == sourceSlot && SlotGroup.IsStorage(cell) == sourceIsStorage)) {
                return;
            }

            int left = quantityIn(sourceSlot, sourceIsStorage);
            if (left == 0) {
                return;
            }
            move(cell, true);
            hasSplit = true;

            // that was the last one, there's nothing left to hold
            if (left == 1) {
                stopDragging();
            }
        }

        private void stopDragging() {
            isDragging = false;
            backInSlot(transform, originalParent);
        }

        /// <summary>Puts a slot's item back in its cell, filling it but its 5px margin.</summary>
        public static void backInSlot(Transform item, Transform cell) {
            RectTransform rect = (RectTransform)item;
            rect.SetParent(cell, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(5, 5);
            rect.offsetMax = new Vector2(-5, -5);
        }

        /// <summary>
        /// The slot under a screen position: the cell holding an InventorySlot or CraftingSlot, wherever in
        /// it the pointer is (its item, quantity, border or margin). Null if there's none.
        /// </summary>
        private Transform findCell(Vector2 position, out Component target) {
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);

            foreach (RaycastResult hit in hits) {
                // what's being dragged is under the pointer too
                if (hit.gameObject.transform.IsChildOf(transform)) {
                    continue;
                }
                for (Transform t = hit.gameObject.transform; t != null; t = t.parent) {
                    target = slotIn(t);
                    if (target != null) {
                        return t;
                    }
                }
            }
            target = null;
            return null;
        }

        // the slot shown by a cell: a child with an InventorySlot or CraftingSlot (cells are named after their slot)
        private Component slotIn(Transform cell) {
            int number;
            if (!int.TryParse(cell.name, out number) || cell.GetComponent<InventorySlot>() != null || cell.GetComponent<CraftingSlot>() != null) {
                return null;
            }
            foreach (Transform child in cell) {
                if (child == transform) {
                    continue;
                }
                Component slot = child.GetComponent<InventorySlot>();
                if (slot == null) {
                    slot = child.GetComponent<CraftingSlot>();
                }
                if (slot != null) {
                    return slot;
                }
            }
            return null;
        }

        // any interface under that point, besides what's being dragged
        private bool isOverInterface(Vector2 position) {
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
            foreach (RaycastResult hit in hits) {
                if (!hit.gameObject.transform.IsChildOf(transform)) {
                    return true;
                }
            }
            return false;
        }

        // the stack's item, health and colour in that slot, null if it's empty
        private static Item itemIn(int slot, bool storage, out int health, out int color) {
            health = 0;
            color = Bricks.BrickColor.None;
            if (storage) {
                InventoryItem? stack = Network.ClientStorage.InSlot(slot);
                if (!stack.HasValue || !Server.items.TryGetValue(stack.Value.itemId, out Item kept)) {
                    return null;
                }
                health = stack.Value.health;
                color = stack.Value.color;
                return kept;
            }
            if (Player.Instance == null || !Player.Instance.getInventoryBySlot().TryGetValue(slot, out UserItem item)) {
                return null;
            }
            health = item.health;
            color = item.color;
            return item.item;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            Transform cell = originalParent != null ? originalParent : transform.parent;
            if (isDragging || eventData.dragging || !int.TryParse(cell.name, out int slot)) {
                return;
            }
            Item item = itemIn(slot, SlotGroup.IsStorage(cell), out int health, out int color);
            if (item != null) {
                ItemTooltip.Show(item, health, color);
            } else {
                ItemTooltip.Hide();
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            ItemTooltip.Hide();
        }

        // its panel closed under the pointer
        private void OnDisable() {
            ItemTooltip.Hide();
        }

        private static int quantityIn(int slot, bool storage) {
            if (storage) {
                InventoryItem? stack = Network.ClientStorage.InSlot(slot);
                return stack.HasValue ? stack.Value.quantity : 0;
            }
            UserItem item;
            return Player.Instance.getInventoryBySlot().TryGetValue(slot, out item) ? item.quantity : 0;
        }

        public void setVisible (bool show) {
            RawImage rawImage = GetComponent<RawImage>();
            Color currColor = rawImage.color;
            currColor.a = show ? 1f : 0f;
            rawImage.color = currColor;

            quantity.gameObject.SetActive(show);
            if (!show) {
                ItemColorSwatch.Set(rawImage, null, 0);
                ItemDurabilityBar.Set(rawImage, null, 0);
            }
        }

        /// <summary>Shows a stack (item null for none): its icon, colour, how many (not for items that don't stack) and how worn.</summary>
        public void show (Item item, int color, int count, int health) {
            if (item == null) {
                setVisible(false);
                return;
            }
            setVisible(true);
            RawImage icon = GetComponent<RawImage>();
            icon.texture = item.icon;
            ItemColorSwatch.Set(icon, item, color);
            ItemDurabilityBar.Set(icon, item, health);
            quantity.text = item.maxStack == 1 && count == 1 ? "" : count.ToString();
        }
    }
}
