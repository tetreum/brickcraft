using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The player's preferences (see GameSettings), opened over the main menu or the ESC menu, in
    /// tabs: General, Graphics, Controls (see ControlsTab) and Audio. Changes apply right away; Escape
    /// or the close button closes it.
    ///
    /// Graphics: the screen's resolution and display mode (Unity remembers them between runs) and how
    /// far the world is drawn (GameSettings.RenderDistance).
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        public const string PanelName = "SettingsPanel";

        [Header("Tabs")]
        public SegmentedControl tabs;
        /// <summary>What each tab shows, in the order of the tabs.</summary>
        public GameObject[] tabViews;
        /// <summary>The header shows the tab's icon, name and what it's for.</summary>
        public Image headerIcon;
        public Text title;
        public Text subtitle;
        public Sprite[] tabIcons;
        public string[] tabSubtitles = {
            "Adjust your game preferences.",
            "Change how the game is displayed.",
            "Click a key to change it, Esc cancels.",
            "Set the volume of the game.",
        };

        [Header("General")]
        public Dropdown language;
        public Toggle showCoordinates;
        public Toggle crosshair;
        public Toggle tutorialHints;

        [Header("Graphics")]
        public Dropdown resolution;
        public Dropdown displayMode;
        public Slider renderDistance;

        [Header("Audio")]
        public Slider masterVolume;
        public Slider musicVolume;
        public Slider effectsVolume;

        public Button closeButton;

        private static int closedFrame = -1;

        // the resolutions listed, biggest first, and the display modes
        private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
        private static readonly FullScreenMode[] displayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        private static readonly string[] displayModeNames = { "Fullscreen", "Borderless window", "Windowed" };

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

            showCoordinates.onValueChanged.AddListener(on => GameSettings.ShowCoordinates = on);
            crosshair.onValueChanged.AddListener(on => GameSettings.Crosshair = on);
            tutorialHints.onValueChanged.AddListener(on => GameSettings.TutorialHints = on);

            displayMode.ClearOptions();
            displayMode.AddOptions(new List<string>(displayModeNames));
            displayMode.onValueChanged.AddListener(index => Screen.fullScreenMode = displayModes[index]);
            resolution.onValueChanged.AddListener(index => Screen.SetResolution(resolutions[index].x, resolutions[index].y, Screen.fullScreenMode));
            renderDistance.minValue = GameSettings.MinRenderDistance;
            renderDistance.maxValue = GameSettings.MaxRenderDistance;
            renderDistance.wholeNumbers = true;
            renderDistance.onValueChanged.AddListener(value => { GameSettings.RenderDistance = Mathf.RoundToInt(value); showChunks(renderDistance); });

            masterVolume.onValueChanged.AddListener(value => { GameSettings.MasterVolume = value; showPercent(masterVolume); });
            musicVolume.onValueChanged.AddListener(value => { GameSettings.MusicVolume = value; showPercent(musicVolume); });
            effectsVolume.onValueChanged.AddListener(value => { GameSettings.EffectsVolume = value; showPercent(effectsVolume); });

            tabs.Changed += showTab;
            closeButton.onClick.AddListener(close);
        }

        private void showTab(int index) {
            tabs.Select(index);
            for (int i = 0; i < tabViews.Length; i++) {
                tabViews[i].SetActive(i == index);
            }
            headerIcon.sprite = tabIcons[index];
            title.text = tabs.segments[index].GetComponentInChildren<Text>().text;
            subtitle.text = tabSubtitles[index];
        }

        // the slider's "Value" text, next to it
        private static void showPercent(Slider slider) {
            Text value = slider.transform.parent.Find("Value").GetComponent<Text>();
            value.text = Mathf.RoundToInt(slider.value * 100) + "%";
        }

        // and what the server we're on allows, when it's less
        private static void showChunks(Slider slider) {
            int chunks = Mathf.RoundToInt(slider.value);
            int allowed = Network.WorldNetwork.ServerMaxViewDistance;
            string text = chunks + " chunks";
            if (Mirror.NetworkClient.isConnected && allowed > 0 && allowed < chunks) {
                text += "\n(server allows " + allowed + ")";
            }
            slider.transform.parent.Find("Value").GetComponent<Text>().text = text;
        }

        // the screen's resolutions (without their refresh rates), and the window's size if it isn't one of them
        private void loadResolutions() {
            resolutions.Clear();
            foreach (Resolution available in Screen.resolutions) {
                Vector2Int size = new Vector2Int(available.width, available.height);
                if (!resolutions.Contains(size)) {
                    resolutions.Add(size);
                }
            }
            Vector2Int current = new Vector2Int(Screen.width, Screen.height);
            if (!resolutions.Contains(current)) {
                resolutions.Add(current);
            }
            resolutions.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            resolution.ClearOptions();
            resolution.AddOptions(resolutions.ConvertAll(size => size.x + " x " + size.y));
            resolution.SetValueWithoutNotify(resolutions.IndexOf(current));
        }

        private void OnEnable() {
            showTab(0);
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
            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Menu)) {
                close();
            }
        }

        // shows the current values, without changing them
        private void load() {
            language.SetValueWithoutNotify(Mathf.Max(0, System.Array.IndexOf(GameSettings.LanguageCodes, GameSettings.Language)));
            showCoordinates.SetIsOnWithoutNotify(GameSettings.ShowCoordinates);
            crosshair.SetIsOnWithoutNotify(GameSettings.Crosshair);
            tutorialHints.SetIsOnWithoutNotify(GameSettings.TutorialHints);
            loadResolutions();
            displayMode.SetValueWithoutNotify(Mathf.Max(0, System.Array.IndexOf(displayModes, Screen.fullScreenMode == FullScreenMode.MaximizedWindow ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode)));
            renderDistance.SetValueWithoutNotify(GameSettings.RenderDistance);
            showChunks(renderDistance);
            masterVolume.SetValueWithoutNotify(GameSettings.MasterVolume);
            musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
            effectsVolume.SetValueWithoutNotify(GameSettings.EffectsVolume);
            showPercent(masterVolume);
            showPercent(musicVolume);
            showPercent(effectsVolume);

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
