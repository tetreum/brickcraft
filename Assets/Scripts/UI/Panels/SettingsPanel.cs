using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The player's preferences (see GameSettings), opened over the main menu or the ESC menu.
    /// Changes apply right away; Escape or the close button closes it.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public const string PanelName = "SettingsPanel";

        public Dropdown language;
        public SegmentedControl difficulty;
        public Toggle autoSave;
        public Toggle showCoordinates;
        public Toggle crosshair;
        public Toggle tutorialHints;
        public Button closeButton;

        private static int closedFrame = -1;

        /// <summary>Closed this frame, so the Escape that closed it does nothing else.</summary>
        public static bool WasClosedThisFrame {
            get { return closedFrame == Time.frameCount; }
        }

        /// <summary>Opens the settings over the panels shown, which stay behind it.</summary>
        public static void Open() {
            Menu.Instance.showPanel(PanelName, false);
            Menu.Instance.getPanel(PanelName).transform.SetAsLastSibling();
        }

        public static bool IsOpen {
            get { return Menu.Instance != null && Menu.Instance.getPanel(PanelName).activeSelf; }
        }

        private void Awake() {
            List<string> names = new List<string>(GameSettings.LanguageNames);
            language.ClearOptions();
            language.AddOptions(names);
            language.onValueChanged.AddListener(index => GameSettings.Language = GameSettings.LanguageCodes[index]);

            difficulty.Changed += index => GameSettings.Difficulty = (Difficulty)index;
            autoSave.onValueChanged.AddListener(on => GameSettings.AutoSave = on);
            showCoordinates.onValueChanged.AddListener(on => GameSettings.ShowCoordinates = on);
            crosshair.onValueChanged.AddListener(on => GameSettings.Crosshair = on);
            tutorialHints.onValueChanged.AddListener(on => GameSettings.TutorialHints = on);
            closeButton.onClick.AddListener(close);
        }

        private void OnEnable() {
            load();
            Game.unlockMouse();

            if (Player.Instance != null) {
                Player.Instance.freeze(Player.FreezeReason.Settings);
            }
        }

        private void OnDisable() {
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.Settings);
            }
        }

        private void Update() {
            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Menu, KeyCode.Escape)) {
                close();
            }
        }

        // shows the current values, without changing them
        private void load() {
            language.SetValueWithoutNotify(Mathf.Max(0, System.Array.IndexOf(GameSettings.LanguageCodes, GameSettings.Language)));
            difficulty.Select((int)GameSettings.Difficulty);
            autoSave.SetIsOnWithoutNotify(GameSettings.AutoSave);
            showCoordinates.SetIsOnWithoutNotify(GameSettings.ShowCoordinates);
            crosshair.SetIsOnWithoutNotify(GameSettings.Crosshair);
            tutorialHints.SetIsOnWithoutNotify(GameSettings.TutorialHints);

            foreach (SwitchToggle toggle in GetComponentsInChildren<SwitchToggle>(true)) {
                toggle.Snap();
            }
        }

        public void close() {
            closedFrame = Time.frameCount;
            Menu.Instance.hidePanel(PanelName);
        }
    }
}
