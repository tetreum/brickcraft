using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks.LDraw;
using UnityEngine;

namespace Brickcraft.World.Towns
{
    /// <summary>
    /// The buildings towns are made of: the LDraw models (.mpd, .ldr) of StreamingAssets/Buildings, read on a
    /// background thread (see Preload) and kept. They're parsed off the main thread (LDrawImport.ReadParts) and
    /// only their attachments touch Unity (LDrawImport.Attach), on the main thread (see All). Until they're ready
    /// <see cref="All"/> is null: a caller that needs them waits or skips.
    /// </summary>
    public static class TownBuildings
    {
        /// <summary>Where they are, in StreamingAssets.</summary>
        public const string Folder = "Buildings";

        private static List<LDrawImport> loaded;
        private static System.Threading.Tasks.Task<List<LDrawImport>> load;

        /// <summary>Its folder on disk.</summary>
        public static string Path {
            get { return System.IO.Path.Combine(Application.streamingAssetsPath, Folder); }
        }

        /// <summary>
        /// The models, read on first use, sorted by file name so every game picks them the same way; null while
        /// they're still being read (see Preload). The main thread: it makes the parsed models final.
        /// </summary>
        public static List<LDrawImport> All {
            get {
                pump();
                return loaded;
            }
        }

        /// <summary>The models, reading them and waiting if needed: for tests and tools, not the game (see Preload).</summary>
        public static List<LDrawImport> LoadAll() {
            Preload();
            while (loaded == null) {
                pump();
                System.Threading.Thread.Sleep(1);
            }
            return loaded;
        }

        /// <summary>Starts reading them on a background thread; safe to call again (only the first reads).</summary>
        public static void Preload() {
            if (loaded == null && load == null) {
                MainThread.Capture();
                load = System.Threading.Tasks.Task.Run(() => read());
            }
        }

        /// <summary>Forgets them, so they're read again (a test, or a game with other items).</summary>
        public static void Clear() {
            loaded = null;
            load = null;
        }

        // the main thread: runs the background read's Unity work and, once it's done, attaches the models
        private static void pump() {
            MainThread.Pump();
            if (load == null || !load.IsCompleted) {
                return;
            }
            List<LDrawImport> parsed = null;
            try {
                parsed = load.Result;
            } catch (System.Exception e) {
                Debug.LogError("Couldn't read the buildings: " + e);
            }
            load = null;
            if (parsed == null) {
                parsed = new List<LDrawImport>();
            }
            foreach (LDrawImport model in parsed) {
                model.Attach();
            }
            loaded = parsed;
        }

        // the background thread: reads and parses the models. The brick models their parts need are read on the main
        // thread first (they load prefabs and meshes, Unity's), then the parse and layout (the CPU-heavy part) run here.
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

            // their parts, read here (pure), so their models can be read on the main thread in one go
            List<string> parts = new List<string>();
            List<string[]> contents = new List<string[]>();
            foreach (string file in files) {
                try {
                    string[] lines = File.ReadAllLines(file);
                    contents.Add(lines);
                    foreach (LDrawContraption.Part part in LDrawContraption.Parse(lines)) {
                        parts.Add(part.part);
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning("Couldn't read the building " + file + ": " + e.Message);
                    contents.Add(null);
                }
            }
            MainThread.Run(() => warm(parts));

            for (int i = 0; i < contents.Count; i++) {
                if (contents[i] == null) {
                    continue;
                }
                try {
                    LDrawImport model = LDrawImport.ReadParts(contents[i]);
                    if (model.bricks.Count > 0) {
                        models.Add(model);
                    } else {
                        Debug.LogWarning("The building " + files[i] + " has no parts the game knows");
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning("Couldn't read the building " + files[i] + ": " + e.Message);
                }
            }
            if (models.Count == 0) {
                Debug.LogWarning("No buildings loaded from " + Path);
            }
            return models;
        }

        // the main thread: reads the brick models of those parts, so parsing them later doesn't touch Unity. One that
        // can't be read is skipped (the building that uses it then fails alone, in ReadParts)
        private static void warm(List<string> parts) {
            foreach (string part in parts) {
                try {
                    Item item = Network.PartsList.ItemOfPart(part);
                    if (item != null) {
                        _ = item.brickModel;
                    }
                } catch (System.Exception e) {
                    Debug.LogWarning("Couldn't read the brick model of the part " + part + ": " + e.Message);
                }
            }
        }
    }
}