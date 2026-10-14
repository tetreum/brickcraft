using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft
{
    /// <summary>
    /// What the local player sees with its eyes under water: everything tinted blue. A screen overlay of its own,
    /// under the game's interface, made the first time it's needed.
    /// </summary>
    public static class UnderwaterView
    {
        private static readonly Color Tint = new Color(0.05f, 0.25f, 0.45f, 0.45f);
        private static GameObject overlay;

        public static void Show(bool show) {
            if (overlay == null) {
                if (!show) {
                    return;
                }
                overlay = create();
            }
            if (overlay.activeSelf != show) {
                overlay.SetActive(show);
            }
        }

        private static GameObject create() {
            GameObject root = new GameObject("UnderwaterView", typeof(Canvas));
            Object.DontDestroyOnLoad(root);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -100; // under the game's interface

            GameObject tint = new GameObject("Tint", typeof(RectTransform), typeof(Image));
            tint.transform.SetParent(root.transform, false);
            RectTransform rect = (RectTransform)tint.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = tint.GetComponent<Image>();
            image.color = Tint;
            image.raycastTarget = false;
            root.SetActive(false);
            return root;
        }
    }
}
