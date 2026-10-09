using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// The worlds saved on this computer, each in its own folder of &lt;persistentDataPath&gt;/saves
    /// (see WorldStorage): listing, creating and deleting them.
    /// </summary>
    public static class SavedWorlds
    {
        public const int MaxNameLength = 32;

        public static string Folder {
            get { return Path.Combine(Application.persistentDataPath, "saves"); }
        }

        /// <summary>The saved worlds (just their headers), the last played first.</summary>
        public static List<WorldStorage> List() {
            List<WorldStorage> worlds = new List<WorldStorage>();

            if (Directory.Exists(Folder)) {
                foreach (string folder in Directory.GetDirectories(Folder)) {
                    WorldStorage world = WorldStorage.ReadInfo(folder);
                    if (world != null) {
                        worlds.Add(world);
                    }
                }
            }
            worlds.Sort((a, b) => b.LastPlayedAt.CompareTo(a.LastPlayedAt));
            return worlds;
        }

        /// <summary>Creates a world and returns its folder's name (its save name).</summary>
        public static string Create(string name, long seed, Difficulty difficulty) {
            string saveName = freeFolderName(name);
            WorldStorage.Create(Path.Combine(Folder, saveName), name, seed, difficulty);
            return saveName;
        }

        /// <summary>Deletes a saved world: its terrain changes and its players (inventories, positions...).</summary>
        public static void Delete(string saveName) {
            string folder = Path.Combine(Folder, saveName);

            // only world folders, whatever name is given
            if (Path.GetDirectoryName(Path.GetFullPath(folder)) != Path.GetFullPath(Folder) || WorldStorage.ReadInfo(folder) == null) {
                throw new ArgumentException("There's no world called " + saveName);
            }
            Directory.Delete(folder, true);
            Debug.Log("Deleted world " + folder);
        }

        /// <summary>
        /// The seed typed when creating a world: a number as it is, any other text turned into one
        /// (the same text, the same world), nothing for a random one.
        /// </summary>
        public static long ParseSeed(string text) {
            text = text == null ? "" : text.Trim();
            if (text.Length == 0) {
                return new System.Random().Next(1, int.MaxValue);
            }
            long number;
            if (long.TryParse(text, out number)) {
                return number;
            }
            // FNV-1a, stable across runs and platforms (string.GetHashCode isn't)
            ulong hash = 14695981039346656037;
            foreach (byte b in Encoding.UTF8.GetBytes(text)) {
                hash = (hash ^ b) * 1099511628211;
            }
            return (long)hash;
        }

        /// <summary>Null if the name is fine, otherwise what's wrong with it.</summary>
        public static string ValidateName(string name) {
            name = name == null ? "" : name.Trim();
            if (name.Length == 0) {
                return "Give the world a name";
            }
            if (name.Length > MaxNameLength) {
                return "World names are up to " + MaxNameLength + " characters";
            }
            return null;
        }

        // a folder named after the world, "my_world", "my_world_2"... if taken
        private static string freeFolderName(string name) {
            StringBuilder slug = new StringBuilder();
            foreach (char c in name.Trim().ToLowerInvariant()) {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) {
                    slug.Append(c);
                } else if (slug.Length > 0 && slug[slug.Length - 1] != '_') {
                    slug.Append('_');
                }
            }
            string baseName = slug.ToString().Trim('_');
            if (baseName.Length == 0) {
                baseName = "world";
            }

            string folderName = baseName;
            for (int i = 2; Directory.Exists(Path.Combine(Folder, folderName)); i++) {
                folderName = baseName + "_" + i;
            }
            return folderName;
        }
    }
}
