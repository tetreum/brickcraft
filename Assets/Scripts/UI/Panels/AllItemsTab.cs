using System;
using System.Collections.Generic;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The ALL tab of the inventory, for admins: every item of the game, searchable by name or id,
    /// a page at a time. Clicking one adds it to the inventory (the server checks the player is an admin).
    /// </summary>
    public class AllItemsTab : MonoBehaviour
    {
        public const int PageSize = 30;

        public InputField search;
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
                if (isMatch(item, query)) {
                    matches.Add(item);
                }
            }
            matches.Sort((a, b) => a.id.CompareTo(b.id));
            showPage(page);
        }

        // by name (any part, any case) or by exact id
        private static bool isMatch(Item item, string query) {
            if (query.Length == 0) {
                return true;
            }
            return (item.name != null && item.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || item.id.ToString() == query;
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
                entry.name.text = entry.item.name + "\n#" + entry.item.id;
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
                Player.Instance.GetComponent<PlayerInventory>().CmdAdminAdd(entry.item.id, 1);
            }
        }
    }
}
