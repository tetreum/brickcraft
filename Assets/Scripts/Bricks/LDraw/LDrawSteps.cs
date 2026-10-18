using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>
    /// The building steps of an LDraw model, for its instructions: its file's own (0 STEP, 0 ROTSTEP), or made when it
    /// has none: layer by layer from the bottom (a part's layer is where its bottom is), each layer in groups of parts
    /// that touch, at most so many a step, and what hangs in a slot (glass, doors, a chest's lid, see BrickSlot) after
    /// the rest of its layer. A submodel (0 FILE of an MPD) has steps of its own, before the model that uses it, where
    /// it goes as one part (named after it). Positions are LDU with LDraw's axes (Y down).
    /// </summary>
    public class LDrawSteps
    {
        /// <summary>The most parts a step it makes has.</summary>
        public const int MaxPartsPerStep = 8;

        public class Model
        {
            /// <summary>Its section's name ("bed.ldr"), "" for a plain LDR.</summary>
            public string name;
            public bool isSubmodel;
            /// <summary>Its parts in its file's order; a submodel it uses is one, its part being the submodel's (see Key).</summary>
            public readonly List<LDrawContraption.Part> parts = new List<LDrawContraption.Part>();
            public readonly List<Step> steps = new List<Step>();
            /// <summary>Its steps were made (its file had none).</summary>
            public bool madeSteps;
        }

        public class Step
        {
            /// <summary>Its parts, their indexes in its model's parts.</summary>
            public readonly List<int> parts = new List<int>();
            /// <summary>The view turned so (0 ROTSTEP, degrees around X, Y and Z), null for the usual one.</summary>
            public Vector3? rotation;
        }

        /// <summary>Its submodels, each once, before what uses them (in the order they're used), and the main model last.</summary>
        public readonly List<Model> models = new List<Model>();

        public Model Main {
            get { return models[models.Count - 1]; }
        }

        // its models by key
        private readonly Dictionary<string, Model> byKey = new Dictionary<string, Model>();

        /// <summary>The key of a section's name: its parts name it so (lowercase, without its extension).</summary>
        public static string Key(string name) {
            return Path.GetFileNameWithoutExtension((name ?? "").Replace('\\', '/')).ToLowerInvariant();
        }

        /// <summary>The submodel a part of one of its models is, null if it's a part.</summary>
        public Model SubmodelOf(LDrawContraption.Part part) {
            return byKey.TryGetValue(part.part, out Model model) && model.isSubmodel ? model : null;
        }

        /// <summary>
        /// A model's steps (an LDR's lines, or an MPD's). What hangs in a slot is told by its part number (by default,
        /// a part whose item is an attachment, see PartsList.ItemOfPart).
        /// </summary>
        public static LDrawSteps Read(string[] lines, Func<string, bool> isAttachment = null, int maxParts = MaxPartsPerStep) {
            LDrawSteps steps = new LDrawSteps();
            Dictionary<string, List<string>> files = LDrawContraption.Files(lines, out string main);
            if (main != null) {
                steps.read(files, main, false, isAttachment ?? IsAttachment, Mathf.Max(1, maxParts), 0);
            }
            if (steps.models.Count == 0) {
                steps.models.Add(new Model() { name = main ?? "" });
            }
            return steps;
        }

        /// <summary>Whether an LDraw part is the game's attachment (it hangs in a slot, see BrickSlot).</summary>
        public static bool IsAttachment(string part) {
            Item item = ItemOf(part);
            return item != null && item.brickModel != null && item.brickModel.IsAttachment;
        }

        /// <summary>The game's brick of an LDraw part: the catalogue's, or one of the game's own made of it (its ldrawId, the door's 7102); null if none.</summary>
        public static Item ItemOf(string part) {
            Item item = Network.PartsList.ItemOfPart(part);
            if (item != null) {
                return item;
            }
            foreach (Item own in Server.items.Values) {
                if (own.type == Item.Type.Brick && own.ldrawId == part) {
                    return own;
                }
            }
            return null;
        }

        /// <summary>
        /// The box around an LDraw part, in LDU with LDraw's axes from its origin: its shape in the library, or for a
        /// part the library hasn't (the game's own door), its game model's shape around where its LDraw origin is
        /// (see LDrawWriter.OriginOf); null if neither.
        /// </summary>
        public static Bounds? PartBounds(string part) {
            Bounds? library = LDrawModelBuilder.BoundsOf(part);
            if (library.HasValue) {
                return library;
            }
            Item item = ItemOf(part);
            Bounds? shape = item != null && item.brickModel != null ? LDrawWriter.ShapeOf(item.brickModel) : null;
            if (!shape.HasValue) {
                return null;
            }
            // game units from its pivot (Y up) to LDU from its LDraw origin (Y down)
            Vector3 origin = LDrawWriter.OriginOf(item.brickModel, item.LDrawPart, item.ldrawShape);
            Vector3 min = shape.Value.min / LDrawModelBuilder.Scale - origin, max = shape.Value.max / LDrawModelBuilder.Scale - origin;
            Bounds bounds = new Bounds(new Vector3(min.x, -min.y, min.z), Vector3.zero);
            bounds.Encapsulate(new Vector3(max.x, -max.y, max.z));
            return bounds;
        }

        private Model read(Dictionary<string, List<string>> files, string name, bool isSubmodel, Func<string, bool> isAttachment, int maxParts, int depth) {
            string key = Key(name);
            if (byKey.TryGetValue(key, out Model known)) {
                return known;
            }
            Model model = new Model() { name = name, isSubmodel = isSubmodel };
            byKey[key] = model; // (a model using itself is a part of it)
            Step step = new Step();
            Vector3? rotation = null;
            bool hasSteps = false;
            foreach (string line in files[name]) {
                if (LDrawContraption.ReadPart(line, out LDrawContraption.Part part, out string file)) {
                    // its submodels first
                    if (files.ContainsKey(file) && depth < 16) {
                        read(files, file, true, isAttachment, maxParts, depth + 1);
                    }
                    step.parts.Add(model.parts.Count);
                    model.parts.Add(part);
                    continue;
                }
                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 2 && f[0] == "0" && (f[1] == "STEP" || f[1] == "ROTSTEP")) {
                    hasSteps = true;
                    if (f[1] == "ROTSTEP") {
                        // the step it ends is seen so, and the next ones too until another (END: the usual view)
                        rotation = f.Length >= 5 ? new Vector3(num(f[2]), num(f[3]), num(f[4])) : (Vector3?)null;
                    }
                    step.rotation = rotation;
                    if (step.parts.Count > 0) {
                        model.steps.Add(step);
                    }
                    step = new Step();
                }
            }
            if (step.parts.Count > 0) {
                step.rotation = rotation;
                model.steps.Add(step);
            }
            if (!hasSteps) {
                model.steps.Clear();
                model.steps.AddRange(Make(model.parts, i => boundsOf(model.parts[i]), i => isAttachment(model.parts[i].part), maxParts));
                model.madeSteps = true;
            }
            models.Add(model);
            return model;
        }

        // where a part of a model is: its box (or its submodel's), placed
        private Bounds? boundsOf(LDrawContraption.Part part) {
            Bounds? own = byKey.TryGetValue(part.part, out Model submodel) && submodel.isSubmodel ? BoundsOf(submodel) : PartBounds(part.part);
            return own.HasValue ? LDrawModelBuilder.Encapsulate(null, own.Value, part.Matrix) : (Bounds?)null;
        }

        /// <summary>The box around a model's parts (its submodels' too), in its space; null if it has none with a shape.</summary>
        public Bounds? BoundsOf(Model model) {
            Bounds? all = null;
            foreach (LDrawContraption.Part part in model.parts) {
                Bounds? placed = boundsOf(part);
                if (placed.HasValue) {
                    all = all.HasValue ? LDrawModelBuilder.Encapsulate(all, placed.Value, Matrix4x4.identity) : placed;
                }
            }
            return all;
        }

        // -------- making steps --------

        // how far apart (LDU) two parts can be and still touch: files round their numbers
        private const float Touching = 1;

        /// <summary>
        /// Steps for parts that have none (their count, where each is: its box placed, null if unknown, and whether it
        /// hangs in a slot): layer by layer from the bottom, each in groups of parts that touch, at most maxParts a step.
        /// </summary>
        public static List<Step> Make(IList<LDrawContraption.Part> parts, Func<int, Bounds?> boundsOf, Func<int, bool> hangs, int maxParts) {
            int count = parts.Count;
            Bounds[] boxes = new Bounds[count];
            for (int i = 0; i < count; i++) {
                boxes[i] = boundsOf(i) ?? new Bounds(parts[i].position, Vector3.zero);
            }
            // layers by their bottom (the biggest Y), to a half plate
            SortedDictionary<int, List<int>> layers = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < count; i++) {
                int layer = -Mathf.RoundToInt(boxes[i].max.y / 4);
                if (!layers.TryGetValue(layer, out List<int> inLayer)) {
                    inLayer = new List<int>();
                    layers[layer] = inLayer;
                }
                inLayer.Add(i);
            }
            List<Step> steps = new List<Step>();
            foreach (List<int> layer in layers.Values) {
                // what hangs in a slot, once the rest is there
                List<int> standing = layer.FindAll(i => !hangs(i));
                List<int> hanging = layer.FindAll(i => hangs(i));
                foreach (List<int> some in new[] { standing, hanging }) {
                    Step step = new Step();
                    foreach (List<int> group in groups(some, boxes, maxParts)) {
                        if (step.parts.Count > 0 && step.parts.Count + group.Count > maxParts) {
                            steps.Add(step);
                            step = new Step();
                        }
                        step.parts.AddRange(group);
                    }
                    if (step.parts.Count > 0) {
                        steps.Add(step);
                    }
                }
            }
            return steps;
        }

        // parts that touch (on the ground's plane), in order across the layer; groups bigger than a step cut along it
        private static List<List<int>> groups(List<int> parts, Bounds[] boxes, int maxParts) {
            int[] root = new int[parts.Count];
            for (int i = 0; i < root.Length; i++) {
                root[i] = i;
            }
            int find(int i) {
                while (root[i] != i) {
                    root[i] = root[root[i]];
                    i = root[i];
                }
                return i;
            }
            for (int a = 0; a < parts.Count; a++) {
                for (int b = a + 1; b < parts.Count; b++) {
                    if (touch(boxes[parts[a]], boxes[parts[b]])) {
                        root[find(a)] = find(b);
                    }
                }
            }
            Dictionary<int, List<int>> byRoot = new Dictionary<int, List<int>>();
            for (int i = 0; i < parts.Count; i++) {
                int r = find(i);
                if (!byRoot.TryGetValue(r, out List<int> group)) {
                    group = new List<int>();
                    byRoot[r] = group;
                }
                group.Add(parts[i]);
            }
            List<List<int>> all = new List<List<int>>(byRoot.Values);
            all.Sort((a, b) => compare(corner(a, boxes), corner(b, boxes)));
            List<List<int>> cut = new List<List<int>>();
            foreach (List<int> group in all) {
                if (group.Count <= maxParts) {
                    cut.Add(group);
                    continue;
                }
                // along its longer side, so each piece is a stretch of it
                Bounds box = boxes[group[0]];
                foreach (int i in group) {
                    box.Encapsulate(boxes[i]);
                }
                bool alongX = box.size.x >= box.size.z;
                group.Sort((a, b) => alongX
                    ? compare(new Vector2(boxes[a].center.x, boxes[a].center.z), new Vector2(boxes[b].center.x, boxes[b].center.z))
                    : compare(new Vector2(boxes[a].center.z, boxes[a].center.x), new Vector2(boxes[b].center.z, boxes[b].center.x)));
                for (int start = 0; start < group.Count; start += maxParts) {
                    cut.Add(group.GetRange(start, Mathf.Min(maxParts, group.Count - start)));
                }
            }
            return cut;
        }

        private static bool touch(Bounds a, Bounds b) {
            return a.min.x <= b.max.x + Touching && b.min.x <= a.max.x + Touching && a.min.z <= b.max.z + Touching && b.min.z <= a.max.z + Touching;
        }

        // a group's corner on the ground's plane (its lowest X and Z)
        private static Vector2 corner(List<int> group, Bounds[] boxes) {
            Vector2 lowest = new Vector2(float.MaxValue, float.MaxValue);
            foreach (int i in group) {
                lowest = Vector2.Min(lowest, new Vector2(boxes[i].min.x, boxes[i].min.z));
            }
            return lowest;
        }

        private static int compare(Vector2 a, Vector2 b) {
            int x = a.x.CompareTo(b.x);
            return x != 0 ? x : a.y.CompareTo(b.y);
        }

        // -------- writing them --------

        /// <summary>
        /// A model's lines with steps: each section that has none gets the ones made for it (its part lines in their
        /// order, a 0 STEP after each step but its last); the rest is as it was.
        /// </summary>
        public static List<string> WithSteps(string[] lines, Func<string, bool> isAttachment = null, int maxParts = MaxPartsPerStep) {
            LDrawSteps steps = Read(lines, isAttachment, maxParts);
            Dictionary<string, Model> byName = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
            foreach (Model model in steps.models) {
                byName[model.name] = model;
            }
            List<string> written = new List<string>();
            // a section: its lines before its parts, its parts, and its other lines after its first part
            Model current = null;
            List<string> header = new List<string>(), partLines = new List<string>(), after = new List<string>();
            void flush() {
                written.AddRange(header);
                if (current == null || !current.madeSteps || current.parts.Count != partLines.Count) {
                    written.AddRange(partLines);
                } else {
                    for (int s = 0; s < current.steps.Count; s++) {
                        if (s > 0) {
                            written.Add("0 STEP");
                        }
                        foreach (int part in current.steps[s].parts) {
                            written.Add(partLines[part]);
                        }
                    }
                }
                written.AddRange(after);
                header.Clear();
                partLines.Clear();
                after.Clear();
            }
            byName.TryGetValue("", out current);
            foreach (string raw in lines) {
                string line = raw.TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed.StartsWith("0 FILE ")) {
                    flush();
                    byName.TryGetValue(trimmed.Substring(7).Trim(), out current);
                    header.Add(line);
                } else if (LDrawContraption.ReadPart(trimmed, out _, out _)) {
                    partLines.Add(line);
                } else if (partLines.Count == 0) {
                    header.Add(line);
                } else {
                    after.Add(line);
                }
            }
            flush();
            return written;
        }

        private static float num(string value) {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0;
        }
    }
}
