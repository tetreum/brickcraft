using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The item shown in an inventory slot (its parent, named after the slot number). Dragging it onto
    /// another slot swaps them, onto a crafting slot uses it there. Right clicking a slot while dragging
    /// moves one unit there; after that, letting go puts the rest back.
    /// </summary>
    public class InventorySlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Text quantity;
        [HideInInspector]
        public Transform originalParent;

        private bool isDragging;
        private bool hasSplit;

        private int sourceSlot {
            get { return int.Parse(originalParent.name); }
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

            // nothing is moved if the slot got emptied meanwhile, or units were split off (the rest goes back)
            if (cell != null && !hasSplit && hasItem(sourceSlot)) {
                if (target is InventorySlot) {
                    Player.Instance.moveInventoryStack(sourceSlot, int.Parse(cell.name));
                } else if (target is CraftingSlot) {
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
            BaseInput input = EventSystem.current != null && EventSystem.current.currentInputModule != null
                ? EventSystem.current.currentInputModule.input
                : null;
            if (input == null || !input.GetMouseButtonDown(1)) {
                return;
            }

            Component target;
            Transform cell = findCell(input.mousePosition, out target);
            if (cell == null || !(target is InventorySlot) || int.Parse(cell.name) == sourceSlot) {
                return;
            }

            int left = quantityIn(sourceSlot);
            if (left == 0) {
                return;
            }
            Player.Instance.moveOneInventoryItem(sourceSlot, int.Parse(cell.name));
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

        private static int quantityIn(int slot) {
            UserItem item;
            return Player.Instance.getInventoryBySlot().TryGetValue(slot, out item) ? item.quantity : 0;
        }

        private static bool hasItem(int slot) {
            return quantityIn(slot) > 0;
        }

        public void setVisible (bool show) {
            RawImage rawImage = GetComponent<RawImage>();
            Color currColor = rawImage.color;
            currColor.a = show ? 1f : 0f;
            rawImage.color = currColor;

            quantity.gameObject.SetActive(show);
        }
    }
}
