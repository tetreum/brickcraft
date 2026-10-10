using System;
using System.Collections.Generic;
using Brickcraft.Mods;
using Brickcraft.Network;
using Brickcraft.World;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// What Singleplayer opens: the saved worlds (play or delete them) and a form to create a new one
    /// with its name, seed, difficulty and mods (see Mods.ModDatabase). See World.SavedWorlds.
    /// </summary>
    public class WorldsPanel : MonoBehaviour
    {
        public const string PanelName = "WorldsPanel";

        [Header("Header")]
        public Text title;
        public Text subtitle;
        public Button closeButton;

        [Header("List")]
        public GameObject listView;
        /// <summary>Where the rows go, the content of a ScrollRect.</summary>
        public RectTransform list;
        /// <summary>A world, copied for each: Texts "Name" and "Details", Buttons "Play" and "Delete".</summary>
        public GameObject rowTemplate;
        /// <summary>Shown when there are no worlds yet.</summary>
        public GameObject emptyText;
        public Button newWorldButton;

        [Header("New world")]
        public GameObject createView;
        public InputField nameInput;
        public InputField seedInput;
        public SegmentedControl difficulty;
        public Button backButton;
        public Button createButton;
        /// <summary>What's wrong with the form.</summary>
        public Text error;

        [Header("New world: mods")]
        /// <summary>Where the installed mods go, the content of a ScrollRect.</summary>
        public RectTransform modsList;
        /// <summary>A mod, copied for each: Texts "Name" and "Details", a Toggle "Control".</summary>
        public GameObject modTemplate;
        /// <summary>Shown when no mods are installed.</summary>
        public GameObject noModsText;

        private readonly List<GameObject> rows = new List<GameObject>();
        private readonly List<GameObject> modRows = new List<GameObject>();
        // the toggle of each installed mod, by id
        private readonly Dictionary<string, Toggle> modToggles = new Dictionary<string, Toggle>();
        // the world whose Delete was clicked once: a second click deletes it
        private string confirmingDelete;

        private void Awake() {
            rowTemplate.SetActive(false);
            modTemplate.SetActive(false);
            nameInput.characterLimit = SavedWorlds.MaxNameLength;
            closeButton.onClick.AddListener(close);
            newWorldButton.onClick.AddListener(showCreate);
            backButton.onClick.AddListener(showList);
            createButton.onClick.AddListener(create);
        }

        private void OnEnable() {
            Game.unlockMouse();
            showList();
        }

        private void Update() {
            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Menu)) {
                if (createView.activeSelf) {
                    showList();
                } else {
                    close();
                }
            }
        }

        /// <summary>Opens over the main menu, which stays behind it.</summary>
        public static void Open() {
            Menu.Instance.showPanel(PanelName, false);
            Menu.Instance.getPanel(PanelName).transform.SetAsLastSibling();
        }

        public void close() {
            Menu.Instance.hidePanel(PanelName);
        }

        // -------- list --------

        private void showList() {
            listView.SetActive(true);
            createView.SetActive(false);
            title.text = "Singleplayer";
            subtitle.text = "Pick a world or create a new one.";
            confirmingDelete = null;
            refreshList();
        }

        private void refreshList() {
            // hidden right away, Destroy only happens at the end of the frame
            foreach (GameObject row in rows) {
                row.SetActive(false);
                Destroy(row);
            }
            rows.Clear();

            List<WorldStorage> worlds = SavedWorlds.List();
            emptyText.SetActive(worlds.Count == 0);

            foreach (WorldStorage world in worlds) {
                string saveName = System.IO.Path.GetFileName(world.Folder);
                GameObject row = Instantiate(rowTemplate, list);
                row.name = saveName;
                row.transform.Find("Name").GetComponent<Text>().text = string.IsNullOrEmpty(world.Name) ? saveName : world.Name;
                Text details = row.transform.Find("Details").GetComponent<Text>();
                details.text = world.Difficulty + "   Seed " + world.Seed + "   Played " + timeAgo(world.LastPlayedAt)
                    + (world.Mods.Count > 0 ? "   " + world.Mods.Count + (world.Mods.Count == 1 ? " mod" : " mods") : "");
                Button playButton = row.transform.Find("Play").GetComponent<Button>();
                playButton.onClick.AddListener(() => play(saveName));
                showNote(row, details, noteFor(world));
                // worlds this version can't play (saved by a newer one...) stay listed, to delete them
                playButton.interactable = world.Incompatibility == null;

                Button delete = row.transform.Find("Delete").GetComponent<Button>();
                delete.onClick.AddListener(() => onDelete(saveName, delete));
                row.SetActive(true);
                rows.Add(row);
            }
        }

        private void play(string saveName) {
            Menu.Instance.showPanel("LoadingPanel");
            BrickcraftNetworkManager.GetOrCreate().PlayWorld(saveName);
        }

        // deleting can't be undone: the first click asks, the second deletes
        private void onDelete(string saveName, Button button) {
            if (confirmingDelete != saveName) {
                confirmingDelete = saveName;
                refreshDeleteLabels();
                return;
            }
            try {
                SavedWorlds.Delete(saveName);
            } catch (Exception e) {
                Debug.LogError("Couldn't delete the world " + saveName + ": " + e.Message);
            }
            confirmingDelete = null;
            refreshList();
        }

        private void refreshDeleteLabels() {
            foreach (GameObject row in rows) {
                row.transform.Find("Delete").GetComponentInChildren<Text>().text = row.name == confirmingDelete ? "Sure?" : "Delete";
            }
        }

        // what the player should know before playing it, null if nothing: why it can't be played, the mods
        // that aren't installed (their items are lost while playing without them), or that it'll be upgraded
        private static string noteFor(WorldStorage world) {
            if (world.Incompatibility != null) {
                return "<color=#ff9e94>Can't be played: " + world.Incompatibility + "</color>";
            }
            List<string> notes = new List<string>();
            List<WorldMod> missing = SavedWorlds.MissingMods(world);
            if (missing.Count > 0) {
                // a couple of them, the line ends at the buttons
                List<string> names = missing.ConvertAll(m => m.id + " " + m.version);
                string shown = names.Count <= 2 ? string.Join(", ", names) : names[0] + ", " + names[1] + " and " + (names.Count - 2) + " more";
                notes.Add("<color=#ff9e94>Missing mods: " + shown + "</color>");
            }
            if (world.NeedsUpgrade) {
                notes.Add("<color=#f2d36b>Older save, upgraded when played</color>");
            }
            return notes.Count > 0 ? string.Join("   ", notes) : null;
        }

        // the note goes on a line of its own
        private const float NoteLineHeight = 20;

        private static void showNote(GameObject row, Text details, string note) {
            if (note == null) {
                return;
            }
            details.text += "\n" + note;
            details.horizontalOverflow = HorizontalWrapMode.Overflow;
            ((RectTransform)details.transform).sizeDelta += new Vector2(0, NoteLineHeight);

            // name and details stay centered in the row
            ((RectTransform)row.transform.Find("Name")).anchoredPosition += new Vector2(0, NoteLineHeight / 2);
            ((RectTransform)details.transform).anchoredPosition += new Vector2(0, NoteLineHeight / 2);
        }

        private static string timeAgo(long unixSeconds) {
            TimeSpan ago = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            if (ago.TotalMinutes < 1) {
                return "just now";
            }
            if (ago.TotalHours < 1) {
                return (int)ago.TotalMinutes + " min ago";
            }
            if (ago.TotalDays < 1) {
                return (int)ago.TotalHours + " h ago";
            }
            if (ago.TotalDays < 30) {
                return (int)ago.TotalDays + (ago.TotalDays < 2 ? " day ago" : " days ago");
            }
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("d MMM yyyy");
        }

        // -------- new world --------

        private void showCreate() {
            listView.SetActive(false);
            createView.SetActive(true);
            title.text = "New world";
            subtitle.text = "Leave the seed empty for a random one.";
            nameInput.text = "New world";
            seedInput.text = "";
            difficulty.Select((int)Difficulty.Normal);
            error.text = "";
            refreshMods();
        }

        // every installed mod, off: worlds are played with the ones turned on
        private void refreshMods() {
            foreach (GameObject row in modRows) {
                row.SetActive(false);
                Destroy(row);
            }
            modRows.Clear();
            modToggles.Clear();

            List<ModInfo> mods = ModDatabase.Installed();
            noModsText.SetActive(mods.Count == 0);

            foreach (ModInfo mod in mods) {
                GameObject row = Instantiate(modTemplate, modsList);
                row.name = mod.id;
                Text title = row.transform.Find("Name").GetComponent<Text>();
                title.supportRichText = false; // mods name themselves
                title.text = mod.name + "  " + mod.version;
                Text details = row.transform.Find("Details").GetComponent<Text>();
                details.supportRichText = false;
                details.text = (mod.author.Length > 0 ? "by " + mod.author + "   " : "") + mod.description;

                Toggle toggle = row.transform.Find("Control").GetComponent<Toggle>();
                toggle.isOn = false;
                row.SetActive(true);
                modRows.Add(row);
                modToggles[mod.id] = toggle;
            }
        }

        private void create() {
            string name = nameInput.text.Trim();
            string problem = SavedWorlds.ValidateName(name);
            if (problem != null) {
                error.text = problem;
                return;
            }

            string saveName;
            try {
                List<string> mods = new List<string>();
                foreach (KeyValuePair<string, Toggle> mod in modToggles) {
                    if (mod.Value.isOn) {
                        mods.Add(mod.Key);
                    }
                }
                saveName = SavedWorlds.Create(name, SavedWorlds.ParseSeed(seedInput.text), (Difficulty)Mathf.Max(0, difficulty.Selected), mods);
            } catch (Exception e) {
                error.text = "Couldn't create the world: " + e.Message;
                return;
            }
            play(saveName);
        }
    }
}
