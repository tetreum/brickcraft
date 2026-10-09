using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Brickcraft.UI
{
    /// <summary>
    /// Calls back when the pointer enters or leaves the object. Unlike an EventTrigger it handles
    /// nothing else, so scrolling and dragging still reach the ScrollRect around it.
    /// </summary>
    public class PointerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action entered;
        public Action exited;

        public void OnPointerEnter(PointerEventData eventData) {
            if (entered != null) {
                entered();
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (exited != null) {
                exited();
            }
        }
    }
}
