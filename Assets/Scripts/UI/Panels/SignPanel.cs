using Brickcraft.Bricks;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// Writing on a sign (see Bricks.SignText): opened by the server when the player places one, or clicks one it can
    /// write on. A box of up to SignText.MaxLines lines and MaxLength characters, Save (the server cleans and keeps it)
    /// and Cancel (Esc: it stays as it was). Made the first time it's needed.
    /// </summary>
    public class SignPanel : MonoBehaviour
    {
        private static SignPanel window;

        private string brickId;
        private bool isOpen;
        private Text title, hint, saveLabel, cancelLabel;
        private InputField field;

        /// <summary>Opens it to write on that sign, with what it says now.</summary>
        public static void Open(string brickId, string text) {
            if (window == null) {
                if (Menu.Instance == null) {
                    return;
                }
                window = create(Menu.Instance.transform);
            }
            window.brickId = brickId;
            window.transform.SetAsLastSibling();
            window.gameObject.SetActive(true);
            window.relabel();
            window.field.text = text ?? "";
            window.field.ActivateInputField();
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

        /// <summary>Whether it's open, and the sign it writes on.</summary>
        public static string Writing {
            get { return window != null && window.isOpen ? window.brickId : null; }
        }

        /// <summary>Saves what's typed on the sign (the server checks it can and cleans it), and closes.</summary>
        public static void Save(string text) {
            if (window == null || !window.isOpen) {
                return;
            }
            if (Player.Instance != null) {
                Player.Instance.network.CmdSetSignText(window.brickId, text);
            }
            Close();
        }

        private void Update() {
            if (GameInput.KeyDown(KeyCode.Escape)) {
                Close();
            }
        }

        private void OnEnable() {
            EventManager.LanguageChanged.Subscribe(onLanguageChanged);
        }

        private void OnDisable() {
            EventManager.LanguageChanged.Unsubscribe(onLanguageChanged);
        }

        private void onLanguageChanged(LanguageChangedEvent e) {
            relabel();
        }

        private void relabel() {
            title.text = Loc.Get(Loc.UI, "sign.title");
            hint.text = Loc.Get(Loc.UI, "sign.hint", SignText.MaxLines, SignText.MaxLength);
            saveLabel.text = Loc.Get(Loc.UI, "sign.save");
            cancelLabel.text = Loc.Get(Loc.UI, "sign.cancel");
        }

        // at most MaxLines lines while typing (the server cuts them too)
        private void onTyped(string text) {
            string[] lines = text.Split('\n');
            if (lines.Length > SignText.MaxLines) {
                field.text = string.Join("\n", lines, 0, SignText.MaxLines);
                field.caretPosition = field.text.Length;
            }
        }

        private static SignPanel create(Transform root) {
            Font font = CodeUi.Font;
            // over the game, which shows dimmed behind it
            GameObject cover = new GameObject("SignPanel", typeof(RectTransform), typeof(Image));
            cover.transform.SetParent(root, false);
            RectTransform coverRect = (RectTransform)cover.transform;
            coverRect.anchorMin = Vector2.zero;
            coverRect.anchorMax = Vector2.one;
            coverRect.offsetMin = coverRect.offsetMax = Vector2.zero;
            cover.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);

            GameObject box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            box.transform.SetParent(cover.transform, false);
            RectTransform boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(720, 0);
            box.GetComponent<Image>().color = CodeUi.Background;
            VerticalLayoutGroup layout = box.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 22, 26);
            layout.spacing = 14;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            box.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            SignPanel made = cover.AddComponent<SignPanel>();
            made.title = CodeUi.Label(box.transform, font, "", 32, FontStyle.Bold, Color.white);
            made.hint = CodeUi.Label(box.transform, font, "", 18, FontStyle.Normal, CodeUi.HintColor);

            // the text, as many lines as a sign has, centred like on it
            made.field = CodeUi.Field(box.transform, font, "");
            made.field.lineType = InputField.LineType.MultiLineNewline;
            made.field.characterLimit = SignText.MaxLength;
            made.field.textComponent.alignment = TextAnchor.MiddleCenter;
            made.field.textComponent.fontSize = 24;
            made.field.GetComponent<LayoutElement>().preferredHeight = 34 * SignText.MaxLines;
            made.field.onValueChanged.AddListener(made.onTyped);

            GameObject buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttons.transform.SetParent(box.transform, false);
            HorizontalLayoutGroup row = buttons.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            GameObject cancel = CodeUi.Clickable(buttons.transform, font, "", CodeUi.ButtonColor, 0, Close);
            GameObject save = CodeUi.Clickable(buttons.transform, font, "", CodeUi.ActionColor, 0, () => Save(made.field.text));
            foreach (GameObject button in new[] { cancel, save }) {
                button.GetComponent<LayoutElement>().preferredHeight = 50;
                button.GetComponentInChildren<Text>().fontSize = 21;
            }
            made.cancelLabel = cancel.GetComponentInChildren<Text>();
            made.saveLabel = save.GetComponentInChildren<Text>();
            return made;
        }
    }
}
