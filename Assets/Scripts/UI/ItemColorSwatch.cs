using Brickcraft.Bricks;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A small square in a corner of an item's icon with the colour of its stack, for items that can
    /// have several (their icon only shows their default colour). Created the first time it's needed.
    /// </summary>
    public static class ItemColorSwatch
    {
        private const string Name = "ColorSwatch";
        private const float Size = 14;

        /// <summary>Shows the colour of the item on the icon, or hides it (item null, or an item of a single colour).</summary>
        public static void Set(Graphic icon, Item item, int color) {
            BrickColor brickColor = item != null && item.IsColorable ? BrickColorPalette.Get(color) : null;
            Transform existing = icon.transform.Find(Name);

            if (brickColor == null) {
                if (existing != null) {
                    existing.gameObject.SetActive(false);
                }
                return;
            }
            Image swatch = existing != null ? existing.GetComponent<Image>() : create(icon.transform);
            Color32 shown = brickColor.Color32;
            shown.a = brickColor.isTransparent ? (byte)160 : (byte)255;
            swatch.color = shown;
            swatch.gameObject.SetActive(true);
        }

        private static Image create(Transform icon) {
            GameObject swatch = new GameObject(Name, typeof(RectTransform), typeof(Image), typeof(Outline));
            swatch.transform.SetParent(icon, false);

            RectTransform rect = (RectTransform)swatch.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); // top left, the quantity is at the bottom
            rect.anchoredPosition = new Vector2(3, -3);
            rect.sizeDelta = new Vector2(Size, Size);

            Image image = swatch.GetComponent<Image>();
            image.raycastTarget = false; // drags go to the slot
            swatch.GetComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);
            return image;
        }
    }
}
