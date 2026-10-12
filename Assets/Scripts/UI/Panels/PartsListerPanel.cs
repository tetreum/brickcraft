using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The parts lister's panel on the right (see PartsListerView): how to move around, the box's size, a − and +
    /// for each of its faces, the list's name (needed: it names its files) and the buttons that save the parts list
    /// or cancel. Made the first time it's needed.
    /// </summary>
    public class PartsListerPanel : MonoBehaviour
    {
        private static PartsListerPanel instance;
        private static readonly Color Background = new Color(0.086f, 0.125f, 0.173f, 0.95f);
        private static readonly Color ButtonColor = new Color(0.16f, 0.22f, 0.3f);
        private static readonly Color SaveColor = new Color(0.18f, 0.71f, 0.13f);
        private static readonly Color TextColor = new Color(0.85f, 0.89f, 0.94f);

        private Text size;
        private InputField listName;
        private Button save;

        public static void Show() {
            if (instance == null) {
                if (Menu.Instance == null) {
                    return;
                }
                instance = create(Menu.Instance.transform);
            }
            instance.transform.SetAsLastSibling();
            instance.gameObject.SetActive(true);
            instance.listName.text = "";
            instance.refresh();
        }

        public static void Hide() {
            if (instance != null) {
                instance.gameObject.SetActive(false);
            }
        }

        private void OnEnable() {
            PartsListerView.BoxChanged += refresh;
        }

        private void OnDisable() {
            PartsListerView.BoxChanged -= refresh;
        }

        private void refresh() {
            PartsListerView view = PartsListerView.Current;
            if (view != null) {
                Vector3Int cells = view.Size;
                size.text = cells.x + " x " + cells.z + " studs, " + cells.y + " plates high";
            }
            save.interactable = !string.IsNullOrWhiteSpace(listName.text);
        }

        private static PartsListerPanel create(Transform root) {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject panel = new GameObject("PartsListerPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-24, 0);
            rect.sizeDelta = new Vector2(300, 0);
            panel.GetComponent<Image>().color = Background;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            PartsListerPanel parts = panel.AddComponent<PartsListerPanel>();
            text(panel.transform, font, "PARTS LIST", 20, FontStyle.Bold, Color.white);
            text(panel.transform, font,
                "Right mouse: look around\nMove keys, jump, Ctrl: fly\nWheel or drag on a face: resize it (Shift: x4)",
                14, FontStyle.Normal, new Color(0.65f, 0.71f, 0.79f));
            parts.size = text(panel.transform, font, "", 15, FontStyle.Normal, TextColor);

            for (int face = 0; face < PartsListerView.FaceNames.Length; face++) {
                int which = face;
                GameObject row = new GameObject(PartsListerView.FaceNames[face], typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(panel.transform, false);
                row.GetComponent<LayoutElement>().preferredHeight = 30;
                HorizontalLayoutGroup line = row.GetComponent<HorizontalLayoutGroup>();
                line.spacing = 6;
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = false;
                line.childForceExpandHeight = true;
                Text label = text(row.transform, font, PartsListerView.FaceNames[face], 15, FontStyle.Normal, TextColor);
                label.alignment = TextAnchor.MiddleLeft;
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                button(row.transform, font, "−", ButtonColor, 40, () => grow(which, -1));
                button(row.transform, font, "+", ButtonColor, 40, () => grow(which, 1));
            }

            parts.listName = input(panel.transform, font, "Name of the list (required)");
            parts.listName.characterLimit = Network.PartsLister.MaxNameLength;
            parts.listName.onValueChanged.AddListener(value => parts.refresh());
            GameObject saveButton = button(panel.transform, font, "Save parts list", SaveColor, 0, () => {
                if (PartsListerView.Current != null) {
                    PartsListerView.Current.Save(parts.listName.text);
                }
            });
            saveButton.GetComponent<LayoutElement>().preferredHeight = 40;
            parts.save = saveButton.GetComponent<Button>();
            button(panel.transform, font, "Cancel", ButtonColor, 0, () => {
                if (PartsListerView.Current != null) {
                    PartsListerView.Current.Cancel();
                }
            }).GetComponent<LayoutElement>().preferredHeight = 36;
            panel.SetActive(false);
            return parts;
        }

        // the panel's buttons: Shift moves it 4 at a time, like the wheel
        private static void grow(int face, int steps) {
            if (PartsListerView.Current == null) {
                return;
            }
            bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            PartsListerView.Current.Grow(face, fast ? steps * 4 : steps);
        }

        private static Text text(Transform parent, Font font, string value, int size, FontStyle style, Color color) {
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
        private static InputField input(Transform parent, Font font, string hint) {
            GameObject go = new GameObject("Name", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f);
            go.GetComponent<LayoutElement>().preferredHeight = 34;
            Text text = PartsListerPanel.text(go.transform, font, "", 16, FontStyle.Normal, Color.white);
            text.supportRichText = false;
            Text placeholder = PartsListerPanel.text(go.transform, font, hint, 15, FontStyle.Italic, new Color(0.55f, 0.6f, 0.68f));
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

        private static GameObject button(Transform parent, Font font, string label, Color color, float width, UnityAction onClick) {
            GameObject go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            Button clickable = go.GetComponent<Button>();
            clickable.onClick.AddListener(onClick);
            // disabled (saving without a name), it shows
            ColorBlock colors = clickable.colors;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
            clickable.colors = colors;
            if (width > 0) {
                go.GetComponent<LayoutElement>().preferredWidth = width;
            }
            Text text = PartsListerPanel.text(go.transform, font, label, 16, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go;
        }
    }
}
