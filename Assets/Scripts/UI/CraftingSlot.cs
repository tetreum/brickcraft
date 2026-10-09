using Brickcraft.Bricks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class CraftingSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Text quantity;
        [HideInInspector]
        public Transform originalParent;
        [HideInInspector]
        public UserItem currentItem;

        private bool isDragging;

        // only the left button drags (see InventorySlot)
        public void OnBeginDrag(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left || isDragging) {
                return;
            }
            if (originalParent == null) {
                originalParent = transform.parent;
            }
            isDragging = true;
            transform.SetParent(Menu.Instance.transform);
            GetComponent<RectTransform>().SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData) {
            if (isDragging && eventData.button == PointerEventData.InputButton.Left) {
                transform.position = eventData.position;
            }
        }

        // an ingredient only points at an inventory stack: dragging it out of its cell takes it off the grid
        public void OnEndDrag(PointerEventData eventData) {
            if (!isDragging || eventData.button != PointerEventData.InputButton.Left) {
                return;
            }
            isDragging = false;

            List<RaycastResult> raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, raycastResults);

            bool backOnItsCell = false;
            foreach (RaycastResult raycast in raycastResults) {
                if (!raycast.gameObject.transform.IsChildOf(transform) && raycast.gameObject.transform.IsChildOf(originalParent)) {
                    backOnItsCell = true;
                }
            }
            InventorySlot.backInSlot(transform, originalParent);

            if (!backOnItsCell) {
                setVisible(false);
                InventoryPanel.Instance.showPossibleCrafting();
            }
        }

        public void setCurrentItem (int slotId) {
            UserItem userItem = null;

            foreach (UserItem item in Player.Instance.getInventory()) {
                if (item.slot == slotId) {
                    userItem = item;
                    break;
                }
            }

            if (userItem == null) {
                return;
            }

            currentItem = userItem;
            
            setVisible(true);

            InventoryPanel.Instance.showPossibleCrafting();
        }

        public void setVisible(bool show) {
            RawImage rawImage = GetComponent<RawImage>();
            Color currColor = rawImage.color;
            currColor.a = show ? 1f : 0f;
            rawImage.color = currColor;

            if (show) {
                quantity.text = currentItem.quantity.ToString();
                rawImage.texture = currentItem.item.icon;
                ItemColorSwatch.Set(rawImage, currentItem.item, currentItem.color);
            } else {
                currentItem = null;
                ItemColorSwatch.Set(rawImage, null, 0);
            }

            quantity.gameObject.SetActive(show);
        }
    }
}
