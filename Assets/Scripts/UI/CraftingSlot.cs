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

        private Vector3 initialPos;

        public void OnBeginDrag(PointerEventData eventData) {

            if (originalParent == null) {
                originalParent = transform.parent;
            }

            //GamePanel.isMovingAPanel = true;
            //isDragging = true;
            initialPos = transform.position;
            transform.SetParent(Menu.Instance.transform);
            GetComponent<RectTransform>().SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData) {
            transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData) {
            List<RaycastResult> raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, raycastResults);
            int raycastedItems = raycastResults.Count;

            // try to find an slot
            foreach (RaycastResult raycast in raycastResults) {
                if (raycast.gameObject == gameObject) { // ignore self detection
                    raycastedItems--;
                    continue;
                }

                InventorySlot slot = raycast.gameObject.GetComponent<InventorySlot>();

                if (slot == null) {
                    continue;
                }
                Player.Instance.switchInventorySlots(int.Parse(originalParent.name), int.Parse(raycast.gameObject.transform.parent.name));
                break;
            }

            // he is discarting the item
            if (raycastedItems == 0) {
                setVisible(false);
                return;
            }

            transform.position = initialPos;
            transform.SetParent(originalParent);
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
            } else {
                currentItem = null;
            }

            quantity.gameObject.SetActive(show);
        }
    }
}
