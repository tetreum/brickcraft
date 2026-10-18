using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>A part of the LDraw catalogue (StreamingAssets/LDraw/catalog.json): every one is a brick item of the game.</summary>
    public class LDrawPart
    {
        /// <summary>Its part number, its file's name (lowercase, without ".dat"), like "3001".</summary>
        public string id;
        /// <summary>LDraw's description, like "Brick  2 x  4".</summary>
        public string name;
        /// <summary>Its LDraw category, like "Brick" or "Slope".</summary>
        public string category;
    }

    /// <summary>
    /// The LDraw parts library shipped with the game (StreamingAssets/LDraw: library.zip, a pruned copy of
    /// library.ldraw.org's with the catalogue's parts, their subparts and primitives, files in lowercase; see the
    /// editor's LDraw menu).
    /// Its parts are brick models (see LDrawModelBuilder, BrickModels) and its catalogue's are items (ItemDatabase).
    /// Primitives are read low resolution (p/8) where there's one: LDraw's studs have no logo already.
    /// Part numbers that are another part now (LDraw's aliases, "=", and moved parts, "~Moved to") aren't in it but in
    /// aliases.txt, with the part they are (see MainPart): a model using one gets that part.
    ///
    /// LDraw is licensed CC BY 4.0 (see StreamingAssets/LDraw/CAreadme.txt).
    /// </summary>
    public static class LDrawLibrary
    {
        public const string Folder = "LDraw";
        public const string CatalogFile = "catalog.json";
        public const string ZipFile = "library.zip";
        /// <summary>The catalogue's icons ([part].png, see the editor's LDraw menu).</summary>
        public const string IconsFile = "icons.zip";
        /// <summary>Part numbers that are another part now: "alias part" lines (see MainPart).</summary>
        public const string AliasesFile = "aliases.txt";

        // the aliases.txt read (null: not yet)
        private static Dictionary<string, string> aliases;

        /// <summary>
        /// The part a part number is (lowercase, without ".dat"): for an alias or a moved part, the one it is now
        /// (followed until one that isn't), else the number itself.
        /// </summary>
        public static string MainPart(string id) {
            if (string.IsNullOrEmpty(id)) {
                return id;
            }
            loadAliases();
            string part = id.ToLowerInvariant();
            for (int i = 0; i < 8 && aliases.TryGetValue(part, out string next); i++) {
                part = next;
            }
            return part;
        }

        private static void loadAliases() {
            if (aliases != null) {
                return;
            }
            aliases = new Dictionary<string, string>();
            string path = Path.Combine(Root, AliasesFile);
            if (!File.Exists(path)) {
                return;
            }
            foreach (string raw in File.ReadAllLines(path)) {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) {
                    continue;
                }
                string[] f = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 2) {
                    aliases[f[0].ToLowerInvariant()] = f[1].ToLowerInvariant();
                }
            }
        }

        private static List<LDrawPart> catalog;
        private static Dictionary<string, LDrawPart> byId;

        public static string Root {
            get { return Path.Combine(Application.streamingAssetsPath, Folder); }
        }

        /// <summary>The catalogue's parts (empty without the library).</summary>
        public static IReadOnlyList<LDrawPart> Catalog {
            get {
                load();
                return catalog;
            }
        }

        /// <summary>Whether the library has that part (a part number, lowercase), without reading it.</summary>
        public static bool HasPart(string id) {
            return !string.IsNullOrEmpty(id) && FindFile(id + ".dat") != null;
        }

        public static LDrawPart CatalogPart(string id) {
            load();
            return id != null && byId.TryGetValue(id, out LDrawPart part) ? part : null;
        }

        /// <summary>Forgets the catalogue (it's read again when needed).</summary>
        public static void Reload() {
            catalog = null;
            byId = null;
            aliases = null;
            LDrawGlass.ClearCache();
            files.Clear();
            if (zip != null) {
                zip.Dispose();
                zip = null;
            }
            if (icons != null) {
                icons.Dispose();
                icons = null;
            }
            entries = null;
        }

        private static ZipArchive zip;
        // its files, by name (lowercase, with / folders)
        private static Dictionary<string, ZipArchiveEntry> entries;

        private static Dictionary<string, ZipArchiveEntry> Entries {
            get {
                if (entries != null) {
                    return entries;
                }
                entries = new Dictionary<string, ZipArchiveEntry>();
                string path = Path.Combine(Root, ZipFile);
                if (!File.Exists(path)) {
                    return entries;
                }
                try {
                    zip = System.IO.Compression.ZipFile.OpenRead(path);
                    foreach (ZipArchiveEntry entry in zip.Entries) {
                        entries[entry.FullName.Replace('\\', '/').ToLowerInvariant()] = entry;
                    }
                } catch (System.Exception e) {
                    Debug.LogError("Can't open the LDraw library " + path + ": " + e.Message);
                }
                return entries;
            }
        }

        /// <summary>The icon of a part of the catalogue (from icons.zip), null if there's none.</summary>
        public static Texture2D LoadIcon(string partId) {
            string path = Path.Combine(Root, IconsFile);
            if (!File.Exists(path)) {
                return null;
            }
            try {
                if (icons == null) {
                    icons = System.IO.Compression.ZipFile.OpenRead(path);
                }
                ZipArchiveEntry entry = icons.GetEntry(partId + ".png");
                if (entry == null) {
                    return null;
                }
                byte[] bytes;
                using (Stream stream = entry.Open())
                using (MemoryStream memory = new MemoryStream()) {
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }
                Texture2D icon = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                icon.name = partId;
                return icon.LoadImage(bytes) ? icon : null;
            } catch (System.Exception e) {
                Debug.LogError("Can't read the icon of the LDraw part " + partId + ": " + e.Message);
                return null;
            }
        }

        private static ZipArchive icons;

        /// <summary>The lines of a library file (a name FindFile gave).</summary>
        public static IEnumerable<string> ReadLines(string file) {
            if (!Entries.TryGetValue(file, out ZipArchiveEntry entry)) {
                yield break;
            }
            // read whole: entries of one archive can't be read at the same time, and files use other files
            string text;
            using (StreamReader reader = new StreamReader(entry.Open())) {
                text = reader.ReadToEnd();
            }
            foreach (string line in text.Split('\n')) {
                yield return line;
            }
        }

        private static void load() {
            if (catalog != null) {
                return;
            }
            catalog = new List<LDrawPart>();
            byId = new Dictionary<string, LDrawPart>();
            string file = Path.Combine(Root, CatalogFile);
            if (!File.Exists(file)) {
                return;
            }
            try {
                catalog = JsonConvert.DeserializeObject<List<LDrawPart>>(File.ReadAllText(file)) ?? catalog;
            } catch (System.Exception e) {
                Debug.LogError("Invalid LDraw catalogue " + file + ": " + e.Message);
            }
            foreach (LDrawPart part in catalog) {
                byId[part.id] = part;
            }
        }

        // files looked for so far (null: not there)
        private static readonly Dictionary<string, string> files = new Dictionary<string, string>();

        /// <summary>
        /// The file an LDraw line refers to ("3001.dat", "s\3001s01.dat", "4-4cyli.dat", "48\4-4edge.dat"), null if
        /// the library hasn't it: a part (parts/), a subpart (parts/s/) or a primitive (p/8/, low resolution, before p/).
        /// </summary>
        public static string FindFile(string reference) {
            string name = reference.Trim().Replace('\\', '/').ToLowerInvariant();
            if (files.TryGetValue(name, out string found)) {
                return found;
            }
            // high resolution primitives are read low resolution too
            string plain = name.StartsWith("48/") ? name.Substring(3) : name;
            string[] candidates = {
                "parts/" + name,
                "p/8/" + plain,
                "p/" + name,
                "p/" + plain,
            };
            found = null;
            foreach (string candidate in candidates) {
                if (Entries.ContainsKey(candidate)) {
                    found = candidate;
                    break;
                }
            }
            // a part number that's another part now: that part's file
            if (found == null && name.EndsWith(".dat") && name.IndexOf('/') < 0) {
                string part = name.Substring(0, name.Length - 4);
                string main = MainPart(part);
                if (main != part) {
                    found = FindFile(main + ".dat");
                }
            }
            files[name] = found;
            return found;
        }
    }
}
