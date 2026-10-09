using System;
using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The Controls tab of the settings: every action of GameInput.Bindings, grouped by category.
    /// Clicking an action's key waits for the next key or mouse button and maps the action to it;
    /// Escape cancels.
    /// </summary>
    public class ControlsTab : MonoBehaviour
    {
        /// <summary>Where the rows go, the content of a ScrollRect.</summary>
        public RectTransform list;
        /// <summary>A category title, copied for each: a Text.</summary>
        public GameObject categoryTemplate;
        /// <summary>An action, copied for each: a Text "Label" and a Button "Key" with a Text inside.</summary>
        public GameObject rowTemplate;
        public Button resetButton;
        /// <summary>Tells what happened, like a key being used twice.</summary>
        public Text status;

        public Color keyColor = new Color(0.086f, 0.125f, 0.173f);
        public Color waitingColor = new Color(0.18f, 0.71f, 0.13f);

        private class Row
        {
            public GameInput.Binding binding;
            public Image keyImage;
            public Text keyText;
        }

        private readonly List<Row> rows = new List<Row>();
        private Row waiting;
        private int waitingSince;

        // what a player can press: keyboard keys and mouse buttons (not joysticks)
        private static KeyCode[] pressableKeys;

        private void Awake() {
            categoryTemplate.SetActive(false);
            rowTemplate.SetActive(false);

            string category = null;
            foreach (GameInput.Binding binding in GameInput.Bindings) {
                if (binding.category != category) {
                    category = binding.category;
                    GameObject title = Instantiate(categoryTemplate, list);
                    title.name = category;
                    title.GetComponent<Text>().text = category;
                    title.SetActive(true);
                }

                GameObject go = Instantiate(rowTemplate, list);
                go.name = binding.button;
                go.transform.Find("Label").GetComponent<Text>().text = binding.label;
                Transform key = go.transform.Find("Key");

                Row row = new Row() {
                    binding = binding,
                    keyImage = key.GetComponent<Image>(),
                    keyText = key.GetComponentInChildren<Text>(),
                };
                key.GetComponent<Button>().onClick.AddListener(() => startWaiting(row));
                go.SetActive(true);
                rows.Add(row);
            }

            resetButton.onClick.AddListener(() => {
                stopWaiting();
                GameInput.ResetAll();
                status.text = "All keys are back to their defaults.";
            });
        }

        private void OnEnable() {
            EventManager.SettingChanged.Subscribe(onSettingChanged);
            status.text = "";
            refresh();

            // from the top, whatever was seen last time
            Canvas.ForceUpdateCanvases();
            list.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
        }

        private void OnDisable() {
            EventManager.SettingChanged.Unsubscribe(onSettingChanged);
            stopWaiting();
        }

        private void onSettingChanged(SettingChangedEvent e) {
            refresh();
        }

        private void refresh() {
            foreach (Row row in rows) {
                row.keyText.text = keysText(row.binding.button);
                row.keyImage.color = row == waiting ? waitingColor : keyColor;
            }
        }

        private static string keysText(string button) {
            return string.Join(" / ", Array.ConvertAll(GameInput.GetKeys(button), GameInput.KeyName));
        }

        private void startWaiting(Row row) {
            stopWaiting();
            waiting = row;
            waitingSince = Time.frameCount;
            // the key pressed now picks the action's key, it's no shortcut
            GameInput.SetTyping(true);
            // the clicked button stays selected, and the UI's Submit (Enter, Space) would click it
            // again instead of the key reaching us
            if (EventSystem.current != null) {
                EventSystem.current.SetSelectedGameObject(null);
            }
            row.keyText.text = "Press a key...";
            row.keyImage.color = waitingColor;
            status.text = "";
        }

        private void stopWaiting() {
            if (waiting == null) {
                return;
            }
            waiting = null;
            GameInput.SetTyping(false);
            refresh();
        }

        private void Update() {
            // the click that started waiting doesn't count
            if (waiting == null || Time.frameCount == waitingSince) {
                return;
            }
            if (pressableKeys == null) {
                pressableKeys = findPressableKeys();
            }
            foreach (KeyCode key in pressableKeys) {
                if (!Input.GetKeyDown(key)) {
                    continue;
                }
                Row row = waiting;
                stopWaiting();

                if (key != KeyCode.Escape) {
                    GameInput.Remap(row.binding.button, key);
                    status.text = sharedWith(row.binding, key);
                }
                return;
            }
        }

        // tells if other actions use the key too, it's allowed (like Sprint and Place on any stud)
        private static string sharedWith(GameInput.Binding binding, KeyCode key) {
            List<string> others = new List<string>();
            foreach (GameInput.Binding other in GameInput.Bindings) {
                if (other != binding && Array.IndexOf(GameInput.GetKeys(other.button), key) >= 0) {
                    others.Add(other.label);
                }
            }
            return others.Count == 0 ? "" : GameInput.KeyName(key) + " is also used by " + string.Join(", ", others.ToArray()) + ".";
        }

        private static KeyCode[] findPressableKeys() {
            List<KeyCode> keys = new List<KeyCode>();
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode))) {
                if (key != KeyCode.None && key < KeyCode.JoystickButton0) {
                    keys.Add(key);
                }
            }
            return keys.ToArray();
        }
    }
}
