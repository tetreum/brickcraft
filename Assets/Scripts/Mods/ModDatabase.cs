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

    /// <summary>A mod as players and servers tell each other about it, see ModDatabase.CheckPlayerMods.</summary>
    public struct ModEntry
    {
        public string id;
        public string version;
        /// <summary>See ModInfo.Hash.</summary>
        public string hash;
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

        private string hash;

        /// <summary>
        /// SHA-256 of its files (their paths and contents), so a server can tell whether a player has the
        /// same mod and not just one with the same id and version. Computed the first time it's asked.
        /// </summary>
        public string Hash {
            get {
                if (hash == null) {
                    hash = ModDatabase.HashFolder(folder);
                }
                return hash;
            }
        }

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

        /// <summary>The active mods as sent over the network.</summary>
        public static ModEntry[] ActiveEntries() {
            return active.ConvertAll(m => new ModEntry() { id = m.id, version = m.version, hash = m.Hash }).ToArray();
        }

        /// <summary>The installed mods as sent over the network, so the server can check them.</summary>
        public static ModEntry[] InstalledEntries() {
            return Installed().ConvertAll(m => new ModEntry() { id = m.id, version = m.version, hash = m.Hash }).ToArray();
        }

        /// <summary>
        /// Server: whether a joining player has every active mod, the same version with the same files.
        /// Null if so, otherwise what's missing, to show the player.
        /// </summary>
        public static string CheckPlayerMods(ModEntry[] playerMods) {
            List<string> problems = new List<string>();

            foreach (ModInfo mod in active) {
                ModEntry? theirs = null;
                foreach (ModEntry entry in playerMods ?? new ModEntry[0]) {
                    if (entry.id == mod.id) {
                        theirs = entry;
                    }
                }
                string name = mod.name + " " + mod.version;
                if (!theirs.HasValue) {
                    problems.Add(name + " (you don't have it)");
                } else if (theirs.Value.version != mod.version) {
                    problems.Add(name + " (you have " + theirs.Value.version + ")");
                } else if (theirs.Value.hash != mod.Hash) {
                    problems.Add(name + " (your copy is different)");
                }
            }
            if (problems.Count == 0) {
                return null;
            }
            return "This server needs " + (problems.Count == 1 ? "the mod " : "these mods: ") + string.Join(", ", problems)
                + ". Mods go in the Mods folder next to the game.";
        }

        /// <summary>SHA-256 of the files of a folder: each relative path (with / separators) and contents, sorted by path.</summary>
        public static string HashFolder(string folder) {
            List<string> files = new List<string>(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
            List<string> relative = files.ConvertAll(f => f.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/'));
            int[] order = new int[files.Count];
            for (int i = 0; i < order.Length; i++) {
                order[i] = i;
            }
            Array.Sort(order, (a, b) => string.CompareOrdinal(relative[a], relative[b]));

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create()) {
                byte[] buffer = new byte[81920];
                foreach (int i in order) {
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(relative[i] + "\0");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    using (FileStream stream = File.OpenRead(files[i])) {
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                            sha.TransformBlock(buffer, 0, read, null, 0);
                        }
                    }
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
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
