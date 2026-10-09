using System;
using System.Collections.Generic;
using Brickcraft.Network;
using Brickcraft.World;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// What Singleplayer opens: the saved worlds (play or delete them) and a form to create a new one
    /// with its name, seed and difficulty. See World.SavedWorlds.
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

        private readonly List<GameObject> rows = new List<GameObject>();
        // the world whose Delete was clicked once: a second click deletes it
        private string confirmingDelete;

        private void Awake() {
            rowTemplate.SetActive(false);
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
                row.transform.Find("Details").GetComponent<Text>().text =
                    world.Difficulty + "   Seed " + world.Seed + "   Played " + timeAgo(world.LastPlayedAt);
                row.transform.Find("Play").GetComponent<Button>().onClick.AddListener(() => play(saveName));

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
                saveName = SavedWorlds.Create(name, SavedWorlds.ParseSeed(seedInput.text), (Difficulty)Mathf.Max(0, difficulty.Selected));
            } catch (Exception e) {
                error.text = "Couldn't create the world: " + e.Message;
                return;
            }
            play(saveName);
        }
    }
}
