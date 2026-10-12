using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Brickcraft;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the game's LDraw library (StreamingAssets/LDraw) from an unpacked library.ldraw.org complete.zip: the
/// catalogue (catalog.json: every part but subparts, primitives, aliases, moved and obsolete ones, printed parts,
/// stickers, minifigs and other figures, sheets of fabric, cardboard or plastic, and other systems: Duplo, Quatro, Primo, Modulex, Znap, Scala, Homemaker, Belville, Fabuland, Clikits, Constraction), the files they use (subparts
/// and primitives, low resolution where LDraw has them) in library.zip, in lowercase, and LDraw's license files.
/// Run again with a newer library to update it.
/// </summary>
public static class LDrawLibraryBuilder
{
    // categories that aren't plastic bricks, or are minifigs'
    private static readonly HashSet<string> SkippedCategories = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) {
        "Minifig", "Minifig Accessory", "Minifig Footwear", "Minifig Headwear", "Minifig Hipwear", "Minifig Neckwear",
        "Figure", "Figure Accessory", "Sticker", "Sheet Fabric", "Sheet Cardboard", "Sheet Plastic",
    };
    private static readonly Regex Printed = new Regex(@"\d[p]\w*$");
    private static readonly Regex PrintedName = new Regex(@"(?i)pattern|print|sticker");
    private static readonly Regex Composite = new Regex(@"c\d\d$");
    private static readonly Regex Slug = new Regex("^[a-z0-9_]+$");
    // other systems than the bricks (Duplo and its toddler lines, Modulex, Znap, Scala...) and their parts ("Wheel ... Znap"): whatever their category
    private static readonly Regex OtherSystems = new Regex(@"(?i)\b(duplo|quatro|primo|modulex|znap|scala|homemaker|belville|fabuland|clikits|constraction)\b");

    [MenuItem("Brickcraft/LDraw/Build library from complete.zip folder...")]
    public static void BuildFromMenu() {
        string folder = EditorUtility.OpenFolderPanel("The unpacked LDraw library (the folder with parts and p)", "", "");
        if (!string.IsNullOrEmpty(folder)) {
            Debug.Log(Build(folder));
        }
    }

    /// <summary>Builds it from that library folder (the one with parts/ and p/); returns what it did.</summary>
    public static string Build(string source) {
        string target = LDrawLibrary.Root;
        Directory.CreateDirectory(target);
        LDrawLibrary.Reload(); // closes the archives
        // what an older library had; icons stay (RenderMissingIcons drops those of parts no longer in it)
        File.Delete(Path.Combine(target, LDrawLibrary.ZipFile));

        List<LDrawPart> catalog = new List<LDrawPart>();
        Dictionary<string, string> copied = new Dictionary<string, string>(); // target relative path -> source path
        string parts = Path.Combine(source, "parts");
        foreach (string file in Directory.GetFiles(parts, "*.dat")) {
            string id = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            LDrawPart part = catalogPart(file, id);
            if (part == null) {
                continue;
            }
            catalog.Add(part);
            include(source, id + ".dat", copied);
        }
        catalog.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

        using (ZipArchive zip = ZipFile.Open(Path.Combine(target, LDrawLibrary.ZipFile), ZipArchiveMode.Create)) {
            foreach (KeyValuePair<string, string> file in copied) {
                zip.CreateEntryFromFile(file.Value, file.Key, System.IO.Compression.CompressionLevel.Optimal);
            }
        }
        foreach (string license in new[] { "CAreadme.txt", "CAlicense.txt", "CAlicense4.txt", "LDConfig.ldr" }) {
            string from = Path.Combine(source, license);
            if (File.Exists(from)) {
                File.Copy(from, Path.Combine(target, license), true);
            }
        }
        File.WriteAllText(Path.Combine(target, LDrawLibrary.CatalogFile), JsonConvert.SerializeObject(catalog, Formatting.Indented));
        LDrawLibrary.Reload();
        AssetDatabase.Refresh();
        return "LDraw library: " + catalog.Count + " parts in the catalogue, " + copied.Count + " files in " + LDrawLibrary.ZipFile;
    }

    [MenuItem("Brickcraft/LDraw/Render missing catalogue icons")]
    public static void RenderIconsFromMenu() {
        Debug.Log(RenderMissingIcons(int.MaxValue));
    }

    /// <summary>
    /// Renders the icons the catalogue's parts don't have yet into icons.zip (in their item's colour, like
    /// BrickIconRenderer's), at most that many (it takes a while: run it again to go on); returns what it did.
    /// </summary>
    public static string RenderMissingIcons(int max) {
        string path = Path.Combine(LDrawLibrary.Root, LDrawLibrary.IconsFile);
        LDrawLibrary.Reload(); // its icons archive is opened here
        BrickColorPalette.Load();
        BrickModels.Reload(Brickcraft.Mods.ModDatabase.Installed());
        Material material = BrickColorPalette.Get(Brickcraft.ItemDatabase.LDrawItemColor).material;
        int made = 0, failed = 0, left = 0;
        using (ZipArchive zip = ZipFile.Open(path, File.Exists(path) ? ZipArchiveMode.Update : ZipArchiveMode.Create)) {
            HashSet<string> done = new HashSet<string>();
            HashSet<string> wanted = new HashSet<string>();
            foreach (LDrawPart part in LDrawLibrary.Catalog) {
                wanted.Add(part.id + ".png");
            }
            // icons of parts no longer in the catalogue go
            foreach (ZipArchiveEntry entry in new List<ZipArchiveEntry>(zip.Entries)) {
                if (wanted.Contains(entry.FullName)) {
                    done.Add(entry.FullName);
                } else if (zip.Mode == ZipArchiveMode.Update) {
                    entry.Delete();
                }
            }
            foreach (LDrawPart part in LDrawLibrary.Catalog) {
                if (done.Contains(part.id + ".png")) {
                    continue;
                }
                if (made + failed >= max) {
                    left++;
                    continue;
                }
                BrickModel model = BrickModels.Get(part.id);
                if (model == null || model.prefab == null) {
                    failed++;
                    continue;
                }
                Texture2D icon = BrickIconRenderer.Render(model.prefab, material);
                ZipArchiveEntry entry = zip.CreateEntry(part.id + ".png", System.IO.Compression.CompressionLevel.NoCompression);
                using (Stream stream = entry.Open()) {
                    byte[] png = icon.EncodeToPNG();
                    stream.Write(png, 0, png.Length);
                }
                Object.DestroyImmediate(icon);
                made++;
                // models pile up in memory: let them go now and then
                if (made % 200 == 0) {
                    LDrawModelBuilder.ClearCache();
                    BrickModels.Reload(Brickcraft.Mods.ModDatabase.Installed());
                }
            }
        }
        LDrawModelBuilder.ClearCache();
        return "LDraw icons: " + made + " rendered, " + failed + " parts without a model, " + left + " left";
    }

    /// <summary>
    /// A part's name as the game shows it: single spaces, and sizes without them ("Brick  2 x  4" is "Brick 2x4",
    /// "Brick 1 x 1 x 2/3" "Brick 1x1x2/3"), so they're found by their studs.
    /// </summary>
    public static string CompactName(string description) {
        string name = Regex.Replace(description.Trim(), @"\s+", " ");
        return Regex.Replace(name, @"(?<=[0-9/.])\s*x\s*(?=[0-9.])", "x");
    }

    // the catalogue's entry of a part file, null if it isn't one (see the class)
    private static LDrawPart catalogPart(string file, string id) {
        if (!Slug.IsMatch(id) || Printed.IsMatch(id) || Composite.IsMatch(id)) {
            return null;
        }
        string description = null, kind = null, category = null;
        int read = 0;
        foreach (string raw in File.ReadLines(file)) {
            if (++read > 40) {
                break;
            }
            string line = raw.Trim();
            if (!line.StartsWith("0")) {
                continue;
            }
            if (description == null) {
                description = line.Length > 1 ? line.Substring(1).Trim() : "";
            } else if (line.StartsWith("0 !LDRAW_ORG")) {
                string[] words = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                kind = words.Length > 2 ? words[2] : "";
            } else if (line.StartsWith("0 !CATEGORY")) {
                category = line.Substring("0 !CATEGORY".Length).Trim();
            }
        }
        if (string.IsNullOrEmpty(description) || (kind != "Part" && kind != "Unofficial_Part")) {
            return null; // subparts, primitives, shortcuts, aliases...
        }
        if ("~=_|".IndexOf(description[0]) >= 0 || description.Contains("Moved to") || PrintedName.IsMatch(description)) {
            return null;
        }
        if (category == null) {
            string[] words = description.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            category = words.Length > 0 ? words[0] : "";
        }
        if (SkippedCategories.Contains(category) || category.StartsWith("Minifig", System.StringComparison.OrdinalIgnoreCase)
                || OtherSystems.IsMatch(description) || OtherSystems.IsMatch(category)) {
            return null;
        }
        return new LDrawPart() { id = id, name = CompactName(description), category = category };
    }

    // a file and those it uses, found like the game finds them (see LDrawLibrary.FindFile), to copy
    private static void include(string source, string reference, Dictionary<string, string> copied) {
        string name = reference.Trim().Replace('\\', '/').ToLowerInvariant();
        string plain = name.StartsWith("48/") ? name.Substring(3) : name;
        foreach (string candidate in new[] { "parts/" + name, "p/8/" + plain, "p/" + name, "p/" + plain }) {
            if (copied.ContainsKey(candidate)) {
                return;
            }
            string from = findCaseless(source, candidate);
            if (from == null) {
                continue;
            }
            copied[candidate] = from;
            foreach (string line in File.ReadLines(from)) {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("1 ") && !trimmed.StartsWith("1\t")) {
                    continue;
                }
                string[] f = trimmed.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 15) {
                    include(source, string.Join(" ", f, 14, f.Length - 14), copied);
                }
            }
            return;
        }
    }

    // the library's file names have mixed case (on Windows any case finds it)
    private static string findCaseless(string root, string relative) {
        string path = Path.Combine(root, relative);
        return File.Exists(path) ? path : null;
    }
}
