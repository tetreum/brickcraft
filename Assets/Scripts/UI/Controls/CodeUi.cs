using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>The pieces and colours of the panels built in code (PartsListerPanel, ModelImporterPanel).</summary>
    public static class CodeUi
    {
        public static readonly Color Background = new Color(0.086f, 0.125f, 0.173f, 0.95f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.22f, 0.3f);
        public static readonly Color ActionColor = new Color(0.18f, 0.71f, 0.13f);
        public static readonly Color TextColor = new Color(0.85f, 0.89f, 0.94f);
        public static readonly Color HintColor = new Color(0.65f, 0.71f, 0.79f);

        public static Font Font {
            get { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        }

        public static Text Label(Transform parent, Font font, string value, int size, FontStyle style, Color color) {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.text = value;
            label.raycastTarget = false;
            return label;
        }

        // a text field with its hint while empty (typing in it, the game's keys are text, see GameInput.IsTyping)
        public static InputField Field(Transform parent, Font font, string hint) {
            GameObject go = new GameObject("Name", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f);
            go.GetComponent<LayoutElement>().preferredHeight = 34;
            Text text = Label(go.transform, font, "", 16, FontStyle.Normal, Color.white);
            text.supportRichText = false;
            Text placeholder = Label(go.transform, font, hint, 15, FontStyle.Italic, new Color(0.55f, 0.6f, 0.68f));
            foreach (Text label in new[] { text, placeholder }) {
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform rect = (RectTransform)label.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8, 2);
                rect.offsetMax = new Vector2(-8, -2);
            }
            InputField field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        public static GameObject Clickable(Transform parent, Font font, string label, Color color, float width, UnityAction onClick) {
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            Button clickable = go.GetComponent<Button>();
            clickable.onClick.AddListener(onClick);
            // disabled, it shows
            ColorBlock colors = clickable.colors;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
            clickable.colors = colors;
            if (width > 0) {
                go.GetComponent<LayoutElement>().preferredWidth = width;
            }
            Text text = Label(go.transform, font, label, 16, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go;
        }
    }
}
