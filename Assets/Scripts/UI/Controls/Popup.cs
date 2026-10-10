using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A window with a title, a text, buttons and optionally a text field (the Prefabs/UI/Popup prefab).
    /// Show it with <see cref="Show"/>; it tells how it was closed: the button clicked (0 is the first,
    /// Enter clicks it) or -1 if it was dismissed (its X or Escape), and the text field's text.
    /// </summary>
    public class Popup : MonoBehaviour
    {
        public Text title;
        public Text text;
        public InputField input;
        /// <summary>Where the buttons go, a horizontal layout.</summary>
        public RectTransform buttons;
        /// <summary>A button, copied for each: an Image and a Text inside. The first one gets primaryColor.</summary>
        public Button buttonTemplate;
        public Button closeButton;
        public Color primaryColor = new Color(0.18f, 0.71f, 0.13f);
        public Color secondaryColor = new Color(0.20f, 0.27f, 0.36f);

        private Action<int, string> onClosed;
        private readonly List<GameObject> buttonObjects = new List<GameObject>();
        private bool isClosed;

        private void Awake() {
            buttonTemplate.gameObject.SetActive(false);
            closeButton.onClick.AddListener(() => Close(-1));
        }

        public void Show(string titleText, string bodyText, IList<string> buttonLabels, bool hasInput, string inputText, string placeholder, Action<int, string> closed) {
            onClosed = closed;
            isClosed = false;
            title.text = titleText ?? "";
            title.gameObject.SetActive(!string.IsNullOrEmpty(titleText));
            text.text = bodyText ?? "";
            text.gameObject.SetActive(!string.IsNullOrEmpty(bodyText));

            input.gameObject.SetActive(hasInput);
            input.text = inputText ?? "";
            ((Text)input.placeholder).text = placeholder ?? "";

            foreach (GameObject old in buttonObjects) {
                Destroy(old);
            }
            buttonObjects.Clear();
            IList<string> labels = buttonLabels != null && buttonLabels.Count > 0 ? buttonLabels : new[] { "OK" };
            for (int i = 0; i < labels.Count; i++) {
                int index = i;
                Button button = Instantiate(buttonTemplate, buttons);
                button.name = "Button " + (i + 1);
                button.GetComponentInChildren<Text>().text = labels[i];
                button.GetComponent<Image>().color = i == 0 ? primaryColor : secondaryColor;
                button.onClick.AddListener(() => Close(index));
                button.gameObject.SetActive(true);
                buttonObjects.Add(button.gameObject);
            }
            gameObject.SetActive(true);

            if (hasInput) {
                input.Select();
                input.ActivateInputField();
            }
        }

        /// <summary>Closes it as if that button was clicked (-1: dismissed).</summary>
        public void Close(int button) {
            if (isClosed) {
                return;
            }
            isClosed = true;
            gameObject.SetActive(false);
            if (onClosed != null) {
                onClosed(button, input.gameObject.activeSelf ? input.text : "");
            }
        }

        private void Update() {
            if (GameInput.GetButtonDown(GameInput.Menu)) {
                Close(-1);
            } else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                Close(0);
            }
        }
    }
}
