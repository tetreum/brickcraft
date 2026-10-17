using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>
    /// Items made of real LEGO parts without being one (their model isn't a part number, like a creation saved as a
    /// mod): their info.json has "ldr": true and their folder a model.ldr with the parts (an LDraw model, or an MPD
    /// with submodels). Parts lists count those parts, and LDraw models put them where the item is (see
    /// Network.PartsList). The file's origin is the item's brick pivot (the middle of its footprint, on its bottom),
    /// with LDraw's axes (Y down); parts of colour 16 take the brick's colour.
    /// </summary>
    public static class LDrawContraption
    {
        public const string FileName = "model.ldr";

        /// <summary>One of its parts: its LDraw part number, colour (an LDraw code, 16 the brick's) and transform (LDU).</summary>
        public struct Part
        {
            public string part;
            public int color;
            public Vector3 position;
            public Vector3 row0, row1, row2;

            public Matrix4x4 Matrix {
                get {
                    Matrix4x4 m = Matrix4x4.identity;
                    m.SetRow(0, new Vector4(row0.x, row0.y, row0.z, position.x));
                    m.SetRow(1, new Vector4(row1.x, row1.y, row1.z, position.y));
                    m.SetRow(2, new Vector4(row2.x, row2.y, row2.z, position.z));
                    return m;
                }
            }
        }

        // read so far, by item id (null: none or unreadable)
        private static readonly Dictionary<string, List<Part>> parsed = new Dictionary<string, List<Part>>();

        public static void ClearCache() {
            parsed.Clear();
        }

        /// <summary>The model.ldr of an item marked "ldr": true, null for others.</summary>
        public static string FileOf(Item item) {
            return item != null && item.ldr && !string.IsNullOrEmpty(item.folder) ? Path.Combine(item.folder, FileName) : null;
        }

        /// <summary>The parts of an item marked "ldr": true (every one, submodels taken apart), null for others.</summary>
        public static List<Part> PartsOf(Item item) {
            string file = FileOf(item);
            if (file == null) {
                return null;
            }
            if (parsed.TryGetValue(item.id, out List<Part> parts)) {
                return parts;
            }
            parts = null;
            try {
                if (File.Exists(file)) {
                    parts = Parse(File.ReadAllLines(file));
                } else {
                    Debug.LogError("The item " + item.id + " has \"ldr\": true but no " + file);
                }
            } catch (Exception e) {
                Debug.LogError("Can't read " + file + ": " + e.Message);
            }
            parsed[item.id] = parts;
            return parts;
        }

        /// <summary>
        /// An LDR, or an MPD (0 FILE sections, the first the main model): its parts, submodels taken apart, but those
        /// kept whole (by their name without extension), which are a part of that name.
        /// </summary>
        public static List<Part> Parse(string[] lines, Func<string, bool> keepWhole = null) {
            Dictionary<string, List<string>> files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string main = null;
            List<string> current = null;
            foreach (string raw in lines) {
                string line = raw.Trim();
                if (line.StartsWith("0 FILE ")) {
                    string name = line.Substring(7).Trim();
                    current = new List<string>();
                    files[name] = current;
                    if (main == null) {
                        main = name;
                    }
                    continue;
                }
                if (line.StartsWith("0 NOFILE")) {
                    current = null;
                    continue;
                }
                if (current == null) {
                    if (main != null) {
                        continue;
                    }
                    // a plain LDR: everything is the main model
                    main = "";
                    current = new List<string>();
                    files[main] = current;
                }
                current.Add(line);
            }
            List<Part> parts = new List<Part>();
            if (main != null) {
                take(files, main, Matrix4x4.identity, LDrawModelBuilder.MainColor, parts, 0, keepWhole);
            }
            return parts;
        }

        private static void take(Dictionary<string, List<string>> files, string name, Matrix4x4 matrix, int color, List<Part> parts, int depth,
                Func<string, bool> keepWhole) {
            if (depth > 16 || !files.TryGetValue(name, out List<string> lines)) {
                return;
            }
            foreach (string line in lines) {
                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 15 || f[0] != "1") {
                    continue;
                }
                int code = int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : LDrawModelBuilder.MainColor;
                int partColor = code == LDrawModelBuilder.MainColor ? color : code;
                Matrix4x4 m = Matrix4x4.identity;
                m.SetRow(0, new Vector4(num(f[5]), num(f[6]), num(f[7]), num(f[2])));
                m.SetRow(1, new Vector4(num(f[8]), num(f[9]), num(f[10]), num(f[3])));
                m.SetRow(2, new Vector4(num(f[11]), num(f[12]), num(f[13]), num(f[4])));
                Matrix4x4 placed = matrix * m;
                string file = string.Join(" ", f, 14, f.Length - 14);
                string part = Path.GetFileNameWithoutExtension(file.Replace('\\', '/')).ToLowerInvariant();
                if (files.ContainsKey(file) && (keepWhole == null || !keepWhole(part))) {
                    take(files, file, placed, partColor, parts, depth + 1, keepWhole); // a submodel
                    continue;
                }
                parts.Add(new Part() {
                    part = part, color = partColor, position = placed.GetColumn(3),
                    row0 = placed.GetRow(0), row1 = placed.GetRow(1), row2 = placed.GetRow(2),
                });
            }
        }

        private static float num(string value) {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
