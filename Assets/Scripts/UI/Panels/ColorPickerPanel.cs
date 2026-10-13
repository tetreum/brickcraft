using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The color picker's window (see ColorPickerTool): a swatch for every colour of the palette, the color picker's own
    /// outlined; hovering one names it, clicking one makes it the color picker's. The dig button on a brick or terrain block
    /// out of the window picks its colour instead, to use one already used. Made the first time it's needed.
    /// </summary>
    public class ColorPickerPanel : MonoBehaviour
    {
        private static ColorPickerPanel window;
        private static readonly Color OutlineColor = new Color(1, 1, 1, 0.9f);

        private Text title;
        private readonly Dictionary<int, Outline> outlines = new Dictionary<int, Outline>();
        private int slot;
        private bool isOpen;

        /// <summary>Opens the window, for the color picker in that slot.</summary>
        public static void Open(int slot) {
            if (window == null) {
                if (Menu.Instance == null) {
                    return;
                }
                window = create(Menu.Instance.transform);
            }
            window.slot = slot;
            window.transform.SetAsLastSibling();
            window.gameObject.SetActive(true);
            window.showCurrent();
            if (!window.isOpen) {
                window.isOpen = true;
                DialogMode.Enter();
            }
        }

        public static void Close() {
            if (window == null || !window.isOpen) {
                return;
            }
            window.isOpen = false;
            window.gameObject.SetActive(false);
            DialogMode.Exit();
        }

        private void Update() {
            if (GameInput.KeyDown(KeyCode.Escape)) {
                Close();
            } else if (GameInput.GetButtonDown(GameInput.Dig) && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) {
                int color = ColorPickerTool.ColorUnderPointer();
                if (color != BrickColor.None) {
                    pick(color);
                }
            }
        }

        private void OnDestroy() {
            if (isOpen) {
                isOpen = false;
                DialogMode.Exit();
            }
        }

        // the color picker's colour outlined and named
        private void showCurrent() {
            int current = ColorPickerTool.HeldColor;
            foreach (KeyValuePair<int, Outline> entry in outlines) {
                entry.Value.enabled = entry.Key == current;
            }
            title.text = nameOf(current);
        }

        private static string nameOf(int colorId) {
            BrickColor color = BrickColorPalette.Get(colorId);
            return color != null ? color.name : "Pick a colour";
        }

        private void pick(int colorId) {
            ColorPickerTool.Pick(slot, colorId);
            Close();
        }

        // transparent colours show they are
        private static Color shownColor(BrickColor color) {
            Color32 shown = color.Color32;
            shown.a = color.isTransparent ? (byte)140 : (byte)255;
            return shown;
        }

        private static ColorPickerPanel create(Transform root) {
            Font font = CodeUi.Font;
            GameObject panel = new GameObject("ColorPickerPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            // on the right: the bricks in the middle stay in sight to pick from
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-24, 0);
            rect.sizeDelta = new Vector2(560, 460);
            panel.GetComponent<Image>().color = CodeUi.Background;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ColorPickerPanel picker = panel.AddComponent<ColorPickerPanel>();
            CodeUi.Label(panel.transform, font, "COLOR PICKER", 20, FontStyle.Bold, Color.white);
            picker.title = CodeUi.Label(panel.transform, font, "", 15, FontStyle.Normal, CodeUi.TextColor);

            // the swatches, scrolling
            GameObject scroll = new GameObject("Colors", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D), typeof(LayoutElement));
            scroll.transform.SetParent(panel.transform, false);
            scroll.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scroll.transform, false);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
            GridLayoutGroup cells = content.GetComponent<GridLayoutGroup>();
            cells.padding = new RectOffset(8, 8, 8, 8);
            cells.cellSize = new Vector2(34, 34);
            cells.spacing = new Vector2(6, 6);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = (RectTransform)scroll.transform;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30;

            List<BrickColor> colors = new List<BrickColor>(BrickColorPalette.All);
            colors.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (BrickColor color in colors) {
                BrickColor shown = color;
                GameObject swatch = new GameObject(color.name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
                swatch.transform.SetParent(contentRect, false);
                swatch.GetComponent<Image>().color = shownColor(color);
                swatch.GetComponent<Button>().onClick.AddListener(() => picker.pick(shown.id));
                Outline outline = swatch.GetComponent<Outline>();
                outline.effectColor = OutlineColor;
                outline.effectDistance = new Vector2(3, -3);
                outline.enabled = false;
                picker.outlines[color.id] = outline;
                PointerHover hover = swatch.AddComponent<PointerHover>();
                hover.entered = () => picker.title.text = shown.name + "  (" + shown.id + ")";
                hover.exited = () => picker.title.text = nameOf(ColorPickerTool.HeldColor);
            }

            GameObject close = CodeUi.Clickable(panel.transform, font, "Close", CodeUi.ButtonColor, 0, Close);
            close.GetComponent<LayoutElement>().preferredHeight = 36;
            panel.SetActive(false);
            return picker;
        }
    }
}
