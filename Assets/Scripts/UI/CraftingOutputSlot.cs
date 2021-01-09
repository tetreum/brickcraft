using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class CraftingOutputSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Text quantity;
        [HideInInspector]
        public Transform originalParent;
        [HideInInspector]
        public Recipe currentRecipe;

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

            // try to find an slot
            foreach (RaycastResult raycast in raycastResults) {
                if (raycast.gameObject == gameObject) { // ignore self detection
                    continue;
                }

                InventorySlot slot = raycast.gameObject.GetComponent<InventorySlot>();

                if (slot == null) {
                    continue;
                }

                int slotId = int.Parse(raycast.gameObject.transform.parent.name);

                if (!Player.Instance.isInventorySlotAvailable(slotId)) {
                    continue;
                }

                InventoryPanel.Instance.craftRecipe(currentRecipe, slotId);
                break;
            }

            transform.position = initialPos;
            transform.SetParent(originalParent);
        }

        public void setVisible(bool show) {
            RawImage rawImage = GetComponent<RawImage>();
            Color currColor = rawImage.color;
            currColor.a = show ? 1f : 0f;
            rawImage.color = currColor;

            if (show) {
                quantity.text = currentRecipe.quantity.ToString();
                rawImage.texture = currentRecipe.item.icon;
            } else {
                currentRecipe = null;
            }

            quantity.gameObject.SetActive(show);
        }
    }
}
