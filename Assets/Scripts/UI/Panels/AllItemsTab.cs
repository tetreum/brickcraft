using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The ALL tab of the inventory, for admins: every item of the game, searchable by name or id,
    /// a page at a time. Clicking one adds it to the inventory (the server checks the player is an admin).
    /// The colour picker next to the search chooses the colour they're given in: then only items that
    /// can have it are listed. Without one, each item comes in its default colour.
    /// </summary>
    public class AllItemsTab : MonoBehaviour
    {
        public const int PageSize = 30;

        public InputField search;
        public ColorPicker colorPicker;
        public RectTransform results;
        /// <summary>An entry, copied for every result: a Button with a RawImage "Icon" and a Text "Name" inside.</summary>
        public GameObject entryTemplate;
        public Button previousPage;
        public Button nextPage;
        public Text pageText;

        private class Entry
        {
            public GameObject gameObject;
            public RawImage icon;
            public Text name;
            public Item item;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Item> matches = new List<Item>();
        private int page;
        // the picked colour, BrickColor.None for each item's default one
        private int color {
            get { return colorPicker.Selected; }
        }

        private void Awake() {
            entryTemplate.SetActive(false);

            for (int i = 0; i < PageSize; i++) {
                GameObject copy = Instantiate(entryTemplate, results);
                copy.name = "Entry " + (i + 1);

                Entry entry = new Entry() {
                    gameObject = copy,
                    icon = copy.transform.Find("Icon").GetComponent<RawImage>(),
                    name = copy.transform.Find("Name").GetComponent<Text>(),
                };
                entry.name.supportRichText = false; // names come from mods
                copy.GetComponent<Button>().onClick.AddListener(() => take(entry));
                entries.Add(entry);
            }

            search.onValueChanged.AddListener(text => {
                page = 0;
                refresh();
            });
            colorPicker.ColorChanged += picked => {
                page = 0;
                refresh();
            };
            previousPage.onClick.AddListener(() => showPage(page - 1));
            nextPage.onClick.AddListener(() => showPage(page + 1));
        }

        private void OnEnable() {
            refresh();
        }

        private void refresh() {
            string query = search.text.Trim();
            matches.Clear();

            foreach (Item item in Server.items.Values) {
                if (isMatch(item, query) && (color == BrickColor.None || item.AllowsColor(color))) {
                    matches.Add(item);
                }
            }
            matches.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            showPage(page);
        }

        // by name or id, any part, any case
        private static bool isMatch(Item item, string query) {
            if (query.Length == 0) {
                return true;
            }
            return (item.name != null && item.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || item.id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void showPage(int newPage) {
            int pages = Math.Max(1, (matches.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(newPage, 0, pages - 1);

            for (int i = 0; i < entries.Count; i++) {
                int index = page * PageSize + i;
                Entry entry = entries[i];

                if (index >= matches.Count) {
                    entry.item = null;
                    entry.gameObject.SetActive(false);
                    continue;
                }
                entry.item = matches[index];
                entry.icon.texture = entry.item.icon;
                ItemColorSwatch.Set(entry.icon, entry.item, colorOf(entry.item));
                entry.name.text = entry.item.name + "\n" + entry.item.id;
                entry.gameObject.SetActive(true);
            }

            pageText.text = matches.Count == 0
                ? "No items found"
                : "Page " + (page + 1) + " / " + pages + "  (" + matches.Count + " items)";
            previousPage.interactable = page > 0;
            nextPage.interactable = page < pages - 1;
        }

        private void take(Entry entry) {
            if (entry.item != null && Player.Instance != null) {
                Player.Instance.GetComponent<PlayerInventory>().CmdAdminAdd(entry.item.id, colorOf(entry.item), 1);
            }
        }

        private int colorOf(Item item) {
            return color == BrickColor.None ? item.color : color;
        }
    }
}
