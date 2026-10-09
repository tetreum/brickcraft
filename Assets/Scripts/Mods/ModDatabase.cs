using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft.Mods
{
    /// <summary>The "index.json" of a mod: what players see of it.</summary>
    public class ModIndex
    {
        public string name;
        public string author;
        public string version;
        public string description;
    }

    /// <summary>A mod installed in the Mods folder.</summary>
    public class ModInfo
    {
        /// <summary>Its folder's name, a slug without ":" (see Slugs). Its items are "[id]:[item]".</summary>
        public string id;
        public string name;
        public string author;
        public string version;
        public string description;
        public string folder;

        /// <summary>Folder with its items, see ItemDatabase.</summary>
        public string ItemsFolder {
            get { return Path.Combine(folder, ModDatabase.ItemsFolder); }
        }
    }

    /// <summary>
    /// Mods, folders in Mods/ next to the game ([Game]_Data/../Mods, the project's folder in the editor):
    ///   index.json   { "name", "author", "version", "description" }
    ///   items/       its items, one folder each like StreamingAssets/Items (see ItemDatabase)
    ///
    /// Each world has its own list of mods (chosen when it's created, see WorldStorage.Mods), which are
    /// activated when it's played: their items are loaded besides the game's own ones.
    /// </summary>
    public static class ModDatabase
    {
        public const string IndexFile = "index.json";
        public const string ItemsFolder = "items";

        public static string Folder {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods")); }
        }

        private static readonly List<ModInfo> active = new List<ModInfo>();

        /// <summary>The mods of the game being played, their items are loaded.</summary>
        public static IList<ModInfo> Active {
            get { return active.AsReadOnly(); }
        }

        /// <summary>The installed mods (read again each time, they can change while the game runs), by id.</summary>
        public static List<ModInfo> Installed() {
            List<ModInfo> mods = new List<ModInfo>();
            if (!Directory.Exists(Folder)) {
                return mods;
            }
            string[] folders = Directory.GetDirectories(Folder);
            Array.Sort(folders, StringComparer.Ordinal);

            foreach (string folder in folders) {
                ModInfo mod = read(folder);
                if (mod != null) {
                    mods.Add(mod);
                }
            }
            return mods;
        }

        /// <summary>The installed mod with that id, null if it isn't installed.</summary>
        public static ModInfo Find(string id) {
            foreach (ModInfo mod in Installed()) {
                if (mod.id == id) {
                    return mod;
                }
            }
            return null;
        }

        /// <summary>
        /// Plays with these mods: loads the game's items and theirs. Returns the ids of the ones that
        /// aren't installed (their items are unknown, their blocks air).
        /// </summary>
        public static List<string> Activate(IEnumerable<string> ids) {
            List<ModInfo> installed = Installed();
            List<string> missing = new List<string>();
            active.Clear();

            foreach (string id in ids) {
                ModInfo mod = installed.Find(m => m.id == id);
                if (mod == null) {
                    missing.Add(id);
                    Debug.LogWarning("The mod " + id + " isn't installed in " + Folder + ", its items are unknown");
                } else if (!active.Contains(mod)) {
                    active.Add(mod);
                }
            }
            ItemDatabase.Load(active);

            if (active.Count > 0) {
                Debug.Log("Mods: " + string.Join(", ", active.ConvertAll(m => m.id + " " + m.version)));
            }
            return missing;
        }

        private static ModInfo read(string folder) {
            string id = Path.GetFileName(folder);
            string file = Path.Combine(folder, IndexFile);

            if (!File.Exists(file)) {
                Debug.LogWarning("The mod folder " + folder + " has no " + IndexFile + ", skipping it");
                return null;
            }
            if (!Slugs.IsValid(id) || id.Contains(":")) {
                Debug.LogError("The mod folder " + folder + " isn't a valid mod id, mod folders are named with lowercase letters, digits and _");
                return null;
            }
            ModIndex index;
            try {
                index = JsonConvert.DeserializeObject<ModIndex>(File.ReadAllText(file));
            } catch (Exception e) {
                Debug.LogError("Invalid " + file + ": " + e.Message);
                return null;
            }
            if (index == null || string.IsNullOrWhiteSpace(index.name) || string.IsNullOrWhiteSpace(index.version)) {
                Debug.LogError(file + " needs a name and a version");
                return null;
            }
            return new ModInfo() {
                id = id,
                name = index.name.Trim(),
                author = index.author != null ? index.author.Trim() : "",
                version = index.version.Trim(),
                description = index.description != null ? index.description.Trim() : "",
                folder = folder,
            };
        }
    }
}
