using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// Picks one of the brick colours (BrickColorPalette, from colors.csv), or none. A button showing the
    /// chosen colour that opens a popup with a swatch per colour; hovering one shows its name.
    /// </summary>
    public class ColorPicker : MonoBehaviour
    {
        [Header("Button")]
        public Button button;
        public Image swatch;
        public Text label;

        [Header("Popup")]
        /// <summary>Covers what's behind the popup, clicking it closes the popup.</summary>
        public Button popup;
        public Text title;
        public Button noneButton;
        /// <summary>Where the swatches go, the content of a ScrollRect.</summary>
        public RectTransform swatches;
        /// <summary>A colour, copied for each: a Button whose Image gets the colour, with an Image "Selected" inside.</summary>
        public GameObject swatchTemplate;

        /// <summary>What the button says when no colour is chosen.</summary>
        public string noneText = "Default colour";

        /// <summary>The chosen colour, BrickColor.None when none.</summary>
        public int Selected { get; private set; } = BrickColor.None;

        /// <summary>Raised when a colour is picked, with its id (BrickColor.None for none).</summary>
        public event Action<int> ColorChanged;

        private readonly Dictionary<int, GameObject> selectedMarks = new Dictionary<int, GameObject>();
        private bool isBuilt;

        private void Awake() {
            swatchTemplate.SetActive(false);
            popup.gameObject.SetActive(false);
            button.onClick.AddListener(togglePopup);
            popup.onClick.AddListener(closePopup);
            noneButton.onClick.AddListener(() => Select(BrickColor.None, true));
            addHover(noneButton.gameObject, () => noneText);
            showSelected();
        }

        private void OnDisable() {
            closePopup();
        }

        /// <summary>Chooses a colour (BrickColor.None for none), telling the listeners if notify.</summary>
        public void Select(int colorId, bool notify = false) {
            if (colorId != BrickColor.None && BrickColorPalette.Get(colorId) == null) {
                colorId = BrickColor.None;
            }
            closePopup();
            if (colorId == Selected) {
                return;
            }
            Selected = colorId;
            showSelected();

            if (notify && ColorChanged != null) {
                ColorChanged(colorId);
            }
        }

        private void togglePopup() {
            if (popup.gameObject.activeSelf) {
                closePopup();
                return;
            }
            build();
            popup.gameObject.SetActive(true);
            popup.transform.SetAsLastSibling(); // over the rest of the panel
            title.text = nameOf(Selected);
        }

        private void closePopup() {
            if (popup != null) {
                popup.gameObject.SetActive(false);
            }
        }

        // the swatches are made the first time the popup opens
        private void build() {
            if (isBuilt) {
                return;
            }
            isBuilt = true;

            List<BrickColor> colors = new List<BrickColor>(BrickColorPalette.All);
            colors.Sort((a, b) => a.id.CompareTo(b.id));

            foreach (BrickColor color in colors) {
                BrickColor picked = color;
                GameObject copy = Instantiate(swatchTemplate, swatches);
                copy.name = color.id.ToString();
                copy.GetComponent<Image>().color = shownColor(color);
                copy.GetComponent<Button>().onClick.AddListener(() => Select(picked.id, true));
                addHover(copy, () => picked.name + "  (" + picked.id + ")");
                selectedMarks[color.id] = copy.transform.Find("Selected").gameObject;
                copy.SetActive(true);
            }
            showSelected();
        }

        private void showSelected() {
            BrickColor color = BrickColorPalette.Get(Selected);
            swatch.gameObject.SetActive(color != null);
            if (color != null) {
                swatch.color = shownColor(color);
            }
            label.text = nameOf(Selected);

            foreach (KeyValuePair<int, GameObject> mark in selectedMarks) {
                mark.Value.SetActive(mark.Key == Selected);
            }
        }

        private string nameOf(int colorId) {
            BrickColor color = BrickColorPalette.Get(colorId);
            return color != null ? color.name : noneText;
        }

        // see-through colours look see-through
        private static Color shownColor(BrickColor color) {
            Color32 shown = color.Color32;
            shown.a = color.isTransparent ? (byte)140 : (byte)255;
            return shown;
        }

        // hovering shows its name in the title, leaving shows the chosen one again
        private void addHover(GameObject target, Func<string> text) {
            PointerHover hover = target.AddComponent<PointerHover>();
            hover.entered = () => title.text = text();
            hover.exited = () => title.text = nameOf(Selected);
        }
    }
}
