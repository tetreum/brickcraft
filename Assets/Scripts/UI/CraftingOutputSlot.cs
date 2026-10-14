using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    public class CraftingOutputSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Text quantity;
        [HideInInspector]
        public Transform originalParent;
        [HideInInspector]
        public Recipe currentRecipe;

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
            ItemTooltip.Hide();
            transform.SetParent(Menu.Instance.transform);
            GetComponent<RectTransform>().SetAsLastSibling();
        }

        // what it crafts, in a tooltip (see ItemTooltip)
        public void OnPointerEnter(PointerEventData eventData) {
            if (!eventData.dragging && currentRecipe != null && GetComponent<UnityEngine.UI.RawImage>().color.a > 0
                    && Server.items.TryGetValue(currentRecipe.itemId, out Item item)) {
                ItemTooltip.Show(item, 0);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            ItemTooltip.Hide();
        }

        private void OnDisable() {
            ItemTooltip.Hide();
        }

        public void OnDrag(PointerEventData eventData) {
            if (isDragging && eventData.button == PointerEventData.InputButton.Left) {
                transform.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData) {
            if (!isDragging || eventData.button != PointerEventData.InputButton.Left) {
                return;
            }
            isDragging = false;

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

            InventorySlot.backInSlot(transform, originalParent);
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
