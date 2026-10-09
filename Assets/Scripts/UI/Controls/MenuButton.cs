using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A big menu button (Prefabs/UI/MenuButton) that looks like a brick seen from above: a rounded
    /// plate with a darker bottom edge, a row of studs on each side, an icon and a label. Set the label,
    /// icon and variant on the instance, and the click on its Button.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class MenuButton : MonoBehaviour
    {
        public enum Variant
        {
            /// <summary>The main action, in green.</summary>
            Primary,
            Secondary,
        }

        [Header("Content")]
        public string label = "Button";
        public Sprite icon;
        public Variant variant = Variant.Secondary;

        [Header("Parts")]
        public Image edge;
        public Image face;
        public Image border;
        public Image iconImage;
        public Text labelText;
        public Image[] studs;
        public Image[] studRings;

        // the Button tints the face, darker at rest than hovered, so faces are drawn that much brighter
        private const float RestTint = 0.88f;

        private struct Palette
        {
            public Color face, edge, border, stud, studRing;
        }

        private static readonly Palette Primary = new Palette() {
            face = new Color(0.33f, 0.74f, 0.13f),
            edge = new Color(0.17f, 0.46f, 0.06f),
            border = new Color(0.64f, 0.92f, 0.40f, 0.9f),
            stud = new Color(0.20f, 0.55f, 0.07f),
            studRing = new Color(0.70f, 0.95f, 0.50f, 0.8f),
        };

        private static readonly Palette Secondary = new Palette() {
            face = new Color(0.15f, 0.20f, 0.27f),
            edge = new Color(0.06f, 0.09f, 0.13f),
            border = new Color(0.33f, 0.41f, 0.52f, 0.9f),
            stud = new Color(0.08f, 0.11f, 0.16f),
            studRing = new Color(0.30f, 0.37f, 0.47f),
        };

        private void Awake() {
            apply();
        }

        private void OnValidate() {
            apply();
        }

        /// <summary>Changes the label from code.</summary>
        public void SetLabel(string text) {
            label = text;
            apply();
        }

        private void apply() {
            if (face == null || labelText == null) {
                return; // parts not assigned yet
            }
            bool isPrimary = variant == Variant.Primary;
            Palette palette = isPrimary ? Primary : Secondary;

            edge.color = palette.edge;
            face.color = new Color(
                Mathf.Min(1, palette.face.r / RestTint),
                Mathf.Min(1, palette.face.g / RestTint),
                Mathf.Min(1, palette.face.b / RestTint));
            border.color = palette.border;
            foreach (Image stud in studs) {
                stud.color = palette.stud;
            }
            foreach (Image ring in studRings) {
                ring.color = palette.studRing;
            }

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;

            labelText.text = label;
            labelText.resizeTextMaxSize = isPrimary ? 36 : 30;
        }
    }
}
