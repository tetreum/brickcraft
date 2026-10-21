using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks.LDraw;
using UnityEngine;

namespace Brickcraft.World.Towns
{
    /// <summary>
    /// The buildings towns are made of: the LDraw models (.mpd, .ldr) of StreamingAssets/Buildings, read
    /// once and kept. They need the game's items (see LDrawImport), so they're read only while a game runs.
    /// </summary>
    public static class TownBuildings
    {
        /// <summary>Where they are, in StreamingAssets.</summary>
        public const string Folder = "Buildings";

        private static List<LDrawImport> loaded;

        /// <summary>Its folder on disk.</summary>
        public static string Path {
            get { return System.IO.Path.Combine(Application.streamingAssetsPath, Folder); }
        }

        /// <summary>The models, read on first use, sorted by file name so every game picks them the same way.</summary>
        public static List<LDrawImport> All {
            get {
                if (loaded == null) {
                    loaded = read();
                }
                return loaded;
            }
        }

        /// <summary>Forgets them, so they're read again (a test, or a game with other items).</summary>
        public static void Clear() {
            loaded = null;
        }

        private static List<LDrawImport> read() {
            List<LDrawImport> models = new List<LDrawImport>();
            if (!Directory.Exists(Path)) {
                Debug.LogWarning("The buildings folder, " + Path + ", isn't there");
                return models;
            }
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(Path)) {
                string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".mpd" || extension == ".ldr") {
                    files.Add(file);
                }
            }
            files.Sort(System.StringComparer.OrdinalIgnoreCase);
            foreach (string file in files) {
                try {
                    LDrawImport model = LDrawImport.Load(file);
                    if (model.bricks.Count > 0) {
                        models.Add(model);
                    } else {
                        Debug.LogWarning("The building " + file + " has no parts the game knows");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning("Couldn't read the building " + file + ": " + e.Message);
                }
            }
            if (models.Count == 0) {
                Debug.LogWarning("No buildings loaded from " + Path);
            }
            return models;
        }
    }
}