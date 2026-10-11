using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A bar along the bottom of a worn item's icon (see Item.durability): how many uses it has left, from green
    /// to red. New items and items that don't wear show none. Created the first time it's needed.
    /// </summary>
    public static class ItemDurabilityBar
    {
        private const string Name = "DurabilityBar";
        private const float Height = 5;

        /// <summary>Shows how worn the stack is (its health, uses taken), or hides it (item null, new or not wearing).</summary>
        public static void Set(Graphic icon, Item item, int health) {
            Transform existing = icon.transform.Find(Name);
            if (item == null || !item.Wears || health <= 0) {
                if (existing != null) {
                    existing.gameObject.SetActive(false);
                }
                return;
            }
            RectTransform bar = existing != null ? (RectTransform)existing : create(icon.transform);
            float left = Mathf.Clamp01(1 - health / (float)item.durability);
            RectTransform fill = (RectTransform)bar.GetChild(0);
            fill.anchorMax = new Vector2(left, 1);
            fill.GetComponent<Image>().color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.85f, 0.2f), left);
            bar.gameObject.SetActive(true);
        }

        private static RectTransform create(Transform icon) {
            GameObject bar = new GameObject(Name, typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(icon, false);
            RectTransform rect = (RectTransform)bar.transform;
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.offsetMin = new Vector2(4, 3);
            rect.offsetMax = new Vector2(-4, 3 + Height);
            Image background = bar.GetComponent<Image>();
            background.color = new Color(0, 0, 0, 0.7f);
            background.raycastTarget = false; // drags go to the slot

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(rect, false);
            RectTransform fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1, 1);
            fillRect.offsetMax = new Vector2(-1, -1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fill.GetComponent<Image>().raycastTarget = false;
            return rect;
        }
    }
}
