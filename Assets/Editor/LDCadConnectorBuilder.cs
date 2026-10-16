using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Brickcraft.Bricks.LDraw;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the game's connectors file (StreamingAssets/LDraw/connectors.txt, see Bricks.LDraw.LDrawConnectors) from an
/// unpacked copy of the LDCad Shadow Library (github.com/RolandMelkert/LDCadShadowLibrary, CC BY-SA 4.0): where the
/// catalogue's parts take axles, pins and the like. Its SNAP_CYL metas are read as LDCad reads them: a part has the
/// ones of the files it uses (moved, turned and scaled as they're used), then its own shadow file's (SNAP_CLEAR drops
/// what it had, SNAP_INCL adds another shadow file's, grids repeat them). Studs and anti-studs are left out (the grid
/// places those). The library's licence goes next to the file, which is under it too.
///
/// Where it has nothing, the game's own rules fill in: technic primitives it doesn't describe (whole axle holes, peg
/// hole ends, paired into holes) and, for wheels with nothing at all, a hub through their middle along their thin side
/// (an axle hole or a pin hole by their name).
///
/// It also writes where window frames take their glass (StreamingAssets/LDraw/glass.txt, see Bricks.LDraw.LDrawGlass):
/// the depths of their female grooves and glass snaps (SNAP_FGR, SNAP_GEN), a frame often having one either side.
/// </summary>
public static class LDCadConnectorBuilder
{
    public const string OutputFile = "connectors.txt";
    public const string LicenseFile = "connectors-LICENSE.md";
    public const string GlassFile = "glass.txt";

    [MenuItem("Brickcraft/LDraw/Build connectors from LDCad shadow library folder...")]
    public static void BuildFromMenu() {
        string folder = EditorUtility.OpenFolderPanel("The LDCad Shadow Library (its folder with parts/ and p/)", "", "");
        if (string.IsNullOrEmpty(folder)) {
            return;
        }
        string result = Build(folder);
        EditorUtility.DisplayDialog("Connectors", result, "OK");
    }

    // a cylinder connection, in the space of the file it's in (LDU, LDraw's axes); or a groove or another kind of snap
    // (no sections), only where it is counting
    private class Snap
    {
        public string id;
        public bool cylinder = true;
        public bool male;
        public string caps = "one";
        public bool slide;
        public bool center;
        public string scale = "none";
        public Vector3 pos;
        // its axes: the shape runs along -Y
        public Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward;
        public List<Section> secs = new List<Section>();

        public Snap Copy() {
            Snap copy = (Snap)MemberwiseClone();
            copy.secs = new List<Section>(secs);
            return copy;
        }

        public float Length {
            get {
                float length = 0;
                foreach (Section s in secs) {
                    length += s.length;
                }
                return length;
            }
        }

        // moved by a file reference (or an include), scaled as its scale says
        public Snap Moved(Matrix4x4 m) {
            Snap moved = Copy();
            moved.pos = m.MultiplyPoint3x4(pos);
            Vector3 mx = m.MultiplyVector(x), my = m.MultiplyVector(y), mz = m.MultiplyVector(z);
            float sx = mx.magnitude, sy = my.magnitude, sz = mz.magnitude;
            bool lengths = scale == "yonly" || scale == "yandr";
            bool radii = scale == "ronly" || scale == "yandr";
            for (int i = 0; i < moved.secs.Count; i++) {
                Section s = moved.secs[i];
                if (lengths) {
                    s.length *= sy;
                }
                if (radii) {
                    s.radius *= (sx + sz) / 2;
                }
                moved.secs[i] = s;
            }
            moved.x = sx > 0 ? mx / sx : Vector3.right;
            moved.y = sy > 0 ? my / sy : Vector3.up;
            moved.z = sz > 0 ? mz / sz : Vector3.forward;
            // mirrored: still a right-handed set (LDCad's "cor")
            if (Vector3.Dot(Vector3.Cross(moved.x, moved.y), moved.z) < 0) {
                moved.x = -moved.x;
            }
            return moved;
        }
    }

    private struct Section
    {
        public char shape; // R round, A axle, S square
        public float radius;
        public float length;
    }

    private static string shadowRoot;
    private static Dictionary<string, string> shadowFiles;
    private static readonly Dictionary<string, List<Snap>> resolved = new Dictionary<string, List<Snap>>();
    private static readonly Dictionary<string, List<string>> shadowLines = new Dictionary<string, List<string>>();

    /// <summary>Builds the file from the library in that folder; what it did, to show.</summary>
    public static string Build(string folder) {
        if (!Directory.Exists(Path.Combine(folder, "parts")) || !Directory.Exists(Path.Combine(folder, "p"))) {
            return "That isn't the LDCad Shadow Library: it has no parts/ and p/ folders.";
        }
        shadowRoot = folder;
        shadowFiles = new Dictionary<string, string>();
        foreach (string file in Directory.GetFiles(folder, "*.dat", SearchOption.AllDirectories)) {
            string relative = file.Substring(folder.Length).TrimStart('/', '\\').Replace('\\', '/').ToLowerInvariant();
            shadowFiles[relative] = file;
        }
        resolved.Clear();
        shadowLines.Clear();
        boundsCache.Clear();

        StringBuilder text = new StringBuilder();
        text.Append("# Where LDraw parts connect (axles, pins, holes): built from the LDCad Shadow Library\n");
        text.Append("# (github.com/RolandMelkert/LDCadShadowLibrary, by Roland Melkert and contributors) by LDCadConnectorBuilder.\n");
        text.Append("# Licensed under CC BY-SA 4.0 like it, see " + LicenseFile + ". Changed from it: inherited, moved and\n");
        text.Append("# scaled into each part's space, studs and anti-studs left out; the game's own rules where it has nothing.\n");
        text.Append("# part  M|F  slide  caps  start x y z  axis x y z  side x y z  sections (shape radius length, ...)  (LDU, LDraw's axes)\n");
        StringBuilder glass = new StringBuilder();
        glass.Append("# Where window frames take their glass: built from the LDCad Shadow Library\n");
        glass.Append("# (github.com/RolandMelkert/LDCadShadowLibrary, by Roland Melkert and contributors) by LDCadConnectorBuilder.\n");
        glass.Append("# Licensed under CC BY-SA 4.0 like it, see " + LicenseFile + ". Changed from it: only the depths of the frames'\n");
        glass.Append("# female grooves and glass snaps, inherited and moved into each part's space.\n");
        glass.Append("# part  z...  (LDU, LDraw's axes: the glass's middle goes at that depth)\n");
        int parts = 0, connectors = 0, frames = 0;
        foreach (LDrawPart part in LDrawLibrary.Catalog) {
            string path = LDrawLibrary.FindFile(part.id + ".dat");
            if (path == null) {
                continue;
            }
            if (LDrawGlass.FrameFits(part) != null && writeSeats(glass, part.id, resolve(path, 0))) {
                frames++;
            }
            List<Snap> snaps = connections(part, path);
            int before = connectors;
            // the same one inherited and included again is one
            HashSet<string> written = new HashSet<string>();
            foreach (Snap snap in snaps) {
                StringBuilder line = new StringBuilder();
                write(line, part.id, snap);
                if (written.Add(line.ToString())) {
                    text.Append(line);
                    connectors++;
                }
            }
            if (connectors > before) {
                parts++;
            }
        }
        string output = Path.Combine(LDrawLibrary.Root, OutputFile);
        File.WriteAllText(output, text.ToString());
        File.WriteAllText(Path.Combine(LDrawLibrary.Root, GlassFile), glass.ToString());
        string license = Path.Combine(folder, "LICENSE.md");
        if (File.Exists(license)) {
            File.Copy(license, Path.Combine(LDrawLibrary.Root, LicenseFile), true);
        }
        LDrawConnectors.Reload();
        LDrawGlass.ClearCache();
        AssetDatabase.Refresh();
        return parts + " parts of " + LDrawLibrary.Catalog.Count + " have connectors (" + connectors + " in all), written to " + output
            + "; " + frames + " window frames say where their glass goes";
    }

    // a frame's glass depths, its female snaps that aren't cylinders (each once); false if it has none
    private static bool writeSeats(StringBuilder text, string part, List<Snap> snaps) {
        List<float> depths = new List<float>();
        foreach (Snap snap in snaps) {
            if (snap.cylinder || snap.male) {
                continue;
            }
            float z = Mathf.Round(snap.pos.z * 2) / 2;
            if (!depths.Contains(z)) {
                depths.Add(z);
            }
        }
        if (depths.Count == 0) {
            return false;
        }
        depths.Sort();
        text.Append(part);
        foreach (float z in depths) {
            text.Append(' ').Append(num(z));
        }
        text.Append('\n');
        return true;
    }

    // ids of the game's own snaps (see BuiltIn), used only where the library has nothing for a part
    private const string PegEnd = "brickcraft-pegEnd";

    /// <summary>
    /// Shadow lines for primitives the library doesn't describe: whole axle holes (like its axlehole.dat, a unit tall,
    /// scaled), and peg hole ends (a hole's end, 2 deep: paired into holes, see connections).
    /// </summary>
    private static readonly Dictionary<string, string> BuiltIn = new Dictionary<string, string>() {
        { "p/axl2hole.dat", "0 !LDCAD SNAP_CYL [gender=F] [caps=none] [secs=A 6 1] [slide=true] [pos=0 1 0] [scale=YOnly]" },
        { "p/axl4hole.dat", "0 !LDCAD SNAP_CYL [gender=F] [caps=none] [secs=A 6 1] [slide=true] [pos=0 1 0] [scale=YOnly]" },
        { "p/peghole.dat", "0 !LDCAD SNAP_CYL [id=" + PegEnd + "] [gender=F] [caps=none] [secs=R 6 2] [slide=true] [pos=0 2 0]" },
    };

    // a part's connections: the library's; else the game's peg holes, paired; else a wheel's hub
    private static List<Snap> connections(LDrawPart part, string path) {
        List<Snap> all = resolve(path, 0);
        List<Snap> found = all.FindAll(s => s.id != PegEnd && isConnection(s));
        if (found.Count > 0) {
            return found;
        }
        found = pairPegEnds(all.FindAll(s => s.id == PegEnd)).FindAll(isConnection);
        if (found.Count > 0 || part.category != "Wheel") {
            return found;
        }
        Snap hub = wheelHub(part, path);
        if (hub != null) {
            found.Add(hub);
        }
        return found;
    }

    // peg hole ends on one line facing each other: a hole from one to the other
    private static List<Snap> pairPegEnds(List<Snap> ends) {
        List<Snap> holes = new List<Snap>();
        bool[] used = new bool[ends.Count];
        for (int i = 0; i < ends.Count; i++) {
            if (used[i]) {
                continue;
            }
            Vector3 axis = -ends[i].y;
            for (int j = i + 1; j < ends.Count; j++) {
                Vector3 between = ends[j].pos - ends[i].pos;
                float along = Vector3.Dot(between, axis);
                if (used[j] || Vector3.Dot(-ends[j].y, axis) > -0.99f || along <= 0 || along > 40
                        || (between - axis * along).magnitude > 0.1f) {
                    continue;
                }
                Snap hole = ends[i].Copy();
                hole.id = null;
                hole.secs = new List<Section>() { new Section() { shape = 'R', radius = 6, length = along } };
                holes.Add(hole);
                used[i] = used[j] = true;
                break;
            }
        }
        return holes;
    }

    // its middle, along its thinnest side: an axle hole ("Axlehole"), a pin hole (anything else); none for wheels that
    // go on something else (stub axles, covers)
    private static Snap wheelHub(LDrawPart part, string path) {
        string name = part.name.ToLowerInvariant();
        if (name.Contains("stub axle") || name.Contains("cover") || name.Contains("integral axle")) {
            return null;
        }
        Bounds? measured = boundsOf(path, 0);
        if (!measured.HasValue) {
            return null;
        }
        Bounds b = measured.Value;
        Vector3 size = b.size;
        Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.z <= size.y ? Vector3.forward : Vector3.up;
        float length = Vector3.Dot(size, axis);
        bool axle = name.Contains("axlehole") || name.Contains("axle hole");
        Snap hub = new Snap() { male = false, caps = "none", slide = true, center = true, pos = b.center };
        // the shape runs along -Y: Y is the axis turned the other way
        hub.y = -axis;
        hub.x = axis == Vector3.right ? Vector3.up : Vector3.right;
        hub.z = Vector3.Cross(hub.x, hub.y);
        hub.secs.Add(new Section() { shape = axle ? 'A' : 'R', radius = 6, length = length });
        return hub;
    }

    // the box around a file's faces, in its space (null if it has none)
    private static readonly Dictionary<string, Bounds?> boundsCache = new Dictionary<string, Bounds?>();

    private static Bounds? boundsOf(string path, int depth) {
        if (boundsCache.TryGetValue(path, out Bounds? cached)) {
            return cached;
        }
        boundsCache[path] = null;
        Bounds? bounds = null;
        void add(Vector3 p) {
            if (bounds.HasValue) {
                Bounds b = bounds.Value;
                b.Encapsulate(p);
                bounds = b;
            } else {
                bounds = new Bounds(p, Vector3.zero);
            }
        }
        foreach (string raw in LDrawLibrary.ReadLines(path)) {
            string[] f = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0) {
                continue;
            }
            if ((f[0] == "3" && f.Length >= 11) || (f[0] == "4" && f.Length >= 14)) {
                int corners = f[0] == "3" ? 3 : 4;
                for (int c = 0; c < corners; c++) {
                    add(new Vector3(n(f[2 + c * 3]), n(f[3 + c * 3]), n(f[4 + c * 3])));
                }
            } else if (f[0] == "1" && f.Length >= 15 && depth < 32) {
                string child = LDrawLibrary.FindFile(string.Join(" ", f, 14, f.Length - 14));
                Bounds? inner = child != null ? boundsOf(child, depth + 1) : null;
                if (!inner.HasValue) {
                    continue;
                }
                Matrix4x4 m = Matrix4x4.identity;
                m.SetColumn(0, new Vector4(n(f[5]), n(f[8]), n(f[11]), 0));
                m.SetColumn(1, new Vector4(n(f[6]), n(f[9]), n(f[12]), 0));
                m.SetColumn(2, new Vector4(n(f[7]), n(f[10]), n(f[13]), 0));
                m.SetColumn(3, new Vector4(n(f[2]), n(f[3]), n(f[4]), 1));
                Bounds ib = inner.Value;
                for (int c = 0; c < 8; c++) {
                    add(m.MultiplyPoint3x4(ib.center + Vector3.Scale(ib.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                }
            }
        }
        boundsCache[path] = bounds;
        return bounds;
    }

    // studs and anti-studs are the grid's: short, or only stud-wide round sections that don't slide (a brick's
    // underside); the rest connect (anything with an axle section, pins and holes, bars...)
    private static bool isConnection(Snap snap) {
        if (snap.secs.Count == 0) {
            return false;
        }
        bool studWide = true;
        foreach (Section s in snap.secs) {
            if (s.shape == 'A') {
                return true;
            }
            studWide &= Mathf.Abs(s.radius - 6) < 0.3f;
        }
        return snap.Length > 4.01f && !(studWide && !snap.slide);
    }

    private static void write(StringBuilder text, string part, Snap snap) {
        // the shape runs from its start along -Y
        Vector3 axis = -snap.y;
        Vector3 start = snap.center ? snap.pos - axis * snap.Length / 2 : snap.pos;
        text.Append(part).Append(' ').Append(snap.male ? 'M' : 'F').Append(' ').Append(snap.slide ? '1' : '0').Append(' ').Append(snap.caps);
        vector(text, start);
        vector(text, axis);
        vector(text, snap.x);
        foreach (Section s in snap.secs) {
            text.Append(' ').Append(s.shape).Append(' ').Append(num(s.radius)).Append(' ').Append(num(s.length));
        }
        text.Append('\n');
    }

    private static void vector(StringBuilder text, Vector3 v) {
        text.Append(' ').Append(num(v.x)).Append(' ').Append(num(v.y)).Append(' ').Append(num(v.z));
    }

    private static string num(float value) {
        float rounded = Mathf.Round(value * 1000) / 1000;
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }

    // -------- resolving a file's snaps --------

    // the snaps of a library file (a name LDrawLibrary.FindFile gave), in its space
    private static List<Snap> resolve(string path, int depth) {
        if (resolved.TryGetValue(path, out List<Snap> done)) {
            return done;
        }
        List<Snap> snaps = new List<Snap>();
        resolved[path] = snaps; // (a file using itself gets nothing more)
        if (depth > 32) {
            return snaps;
        }
        foreach (string raw in LDrawLibrary.ReadLines(path)) {
            string line = raw.Trim();
            if (line.StartsWith("1 ")) {
                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 15) {
                    continue;
                }
                string child = LDrawLibrary.FindFile(string.Join(" ", f, 14, f.Length - 14));
                if (child == null) {
                    continue;
                }
                Matrix4x4 m = Matrix4x4.identity;
                m.SetColumn(0, new Vector4(n(f[5]), n(f[8]), n(f[11]), 0));
                m.SetColumn(1, new Vector4(n(f[6]), n(f[9]), n(f[12]), 0));
                m.SetColumn(2, new Vector4(n(f[7]), n(f[10]), n(f[13]), 0));
                m.SetColumn(3, new Vector4(n(f[2]), n(f[3]), n(f[4]), 1));
                foreach (Snap snap in resolve(child, depth + 1)) {
                    snaps.Add(snap.Moved(m));
                }
            } else if (line.StartsWith("0 !LDCAD")) {
                apply(snaps, line, depth);
            }
        }
        // its shadow file's, after what it has
        foreach (string line in shadowOf(path)) {
            apply(snaps, line, depth);
        }
        return snaps;
    }

    // the shadow file of a library file: the same name in the shadow library (low resolution primitives as theirs)
    private static List<string> shadowOf(string path) {
        string key = path.Replace("p/8/", "p/").Replace("p/48/", "p/");
        return shadowFile(key);
    }

    private static List<string> shadowFile(string key) {
        if (shadowLines.TryGetValue(key, out List<string> lines)) {
            return lines;
        }
        lines = new List<string>();
        if (shadowFiles.TryGetValue(key, out string file)) {
            foreach (string raw in File.ReadAllLines(file)) {
                string line = raw.Trim();
                if (line.StartsWith("0 !LDCAD")) {
                    lines.Add(line);
                }
            }
        } else if (BuiltIn.TryGetValue(key, out string builtIn)) {
            lines.Add(builtIn);
        }
        shadowLines[key] = lines;
        return lines;
    }

    // a SNAP_ meta on the snaps gathered so far
    private static void apply(List<Snap> snaps, string line, int depth) {
        string body = line.Substring("0 !LDCAD".Length).Trim();
        int space = body.IndexOf(' ');
        string command = (space < 0 ? body : body.Substring(0, space)).ToUpperInvariant();
        Dictionary<string, string> args = parameters(space < 0 ? "" : body.Substring(space));
        switch (command) {
            case "SNAP_CLEAR":
                if (args.TryGetValue("id", out string id)) {
                    snaps.RemoveAll(s => string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase));
                } else {
                    snaps.Clear();
                }
                break;
            case "SNAP_CYL":
                Snap snap = parseCyl(args);
                if (snap != null) {
                    foreach (Matrix4x4 at in placements(args)) {
                        snaps.Add(snap.Moved(at));
                    }
                }
                break;
            case "SNAP_FGR":
            case "SNAP_GEN":
                // grooves (fingers) and the rest: where they are, and their gender
                string gender = args.TryGetValue("genderOfs", out string ofs) ? ofs : args.TryGetValue("gender", out string g) ? g : "M";
                Snap other = new Snap() { cylinder = false, male = gender.Trim().ToUpperInvariant() != "F" };
                foreach (Matrix4x4 at in placements(args)) {
                    snaps.Add(other.Moved(at));
                }
                break;
            case "SNAP_INCL":
                if (depth > 16 || !args.TryGetValue("ref", out string reference)) {
                    break;
                }
                List<Snap> included = includedSnaps(reference.Trim().ToLowerInvariant().Replace('\\', '/'), depth);
                foreach (Matrix4x4 at in placements(args)) {
                    foreach (Snap s in included) {
                        snaps.Add(s.Moved(at));
                    }
                }
                break;
        }
    }

    // another shadow file's own snaps (not those of the files it uses)
    private static List<Snap> includedSnaps(string reference, int depth) {
        List<Snap> snaps = new List<Snap>();
        foreach (string prefix in new[] { "p/", "parts/", "parts/s/", "" }) {
            if (shadowFiles.ContainsKey(prefix + reference)) {
                foreach (string line in shadowFile(prefix + reference)) {
                    apply(snaps, line, depth + 1);
                }
                break;
            }
        }
        return snaps;
    }

    // where a meta puts its snap: its pos and ori, repeated over its grid
    private static List<Matrix4x4> placements(Dictionary<string, string> args) {
        Matrix4x4 at = Matrix4x4.identity;
        if (args.TryGetValue("ori", out string ori)) {
            float[] o = numbers(ori);
            if (o.Length == 9) {
                at.SetColumn(0, new Vector4(o[0], o[3], o[6], 0));
                at.SetColumn(1, new Vector4(o[1], o[4], o[7], 0));
                at.SetColumn(2, new Vector4(o[2], o[5], o[8], 0));
            }
        }
        if (args.TryGetValue("pos", out string pos)) {
            float[] p = numbers(pos);
            if (p.Length == 3) {
                at.SetColumn(3, new Vector4(p[0], p[1], p[2], 1));
            }
        }
        List<Matrix4x4> all = new List<Matrix4x4>();
        if (!args.TryGetValue("grid", out string grid)) {
            all.Add(at);
            return all;
        }
        // Xcnt Zcnt Xstep Zstep, a C before a count centring that axis
        string[] g = grid.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        bool centerX = i < g.Length && g[i].ToUpperInvariant() == "C";
        if (centerX) {
            i++;
        }
        int countX = i < g.Length ? (int)n(g[i++]) : 1;
        bool centerZ = i < g.Length && g[i].ToUpperInvariant() == "C";
        if (centerZ) {
            i++;
        }
        int countZ = i < g.Length ? (int)n(g[i++]) : 1;
        float stepX = i < g.Length ? n(g[i++]) : 0;
        float stepZ = i < g.Length ? n(g[i++]) : 0;
        for (int xi = 0; xi < Mathf.Max(1, countX); xi++) {
            for (int zi = 0; zi < Mathf.Max(1, countZ); zi++) {
                float ox = (centerX ? xi - (countX - 1) / 2f : xi) * stepX;
                float oz = (centerZ ? zi - (countZ - 1) / 2f : zi) * stepZ;
                all.Add(at * Matrix4x4.Translate(new Vector3(ox, 0, oz)));
            }
        }
        return all;
    }

    private static Snap parseCyl(Dictionary<string, string> args) {
        Snap snap = new Snap();
        args.TryGetValue("id", out snap.id);
        snap.male = !args.TryGetValue("gender", out string gender) || gender.Trim().ToUpperInvariant() != "F";
        if (args.TryGetValue("caps", out string caps)) {
            snap.caps = caps.Trim().ToLowerInvariant();
        }
        snap.slide = args.TryGetValue("slide", out string slide) && slide.Trim().ToLowerInvariant() == "true";
        snap.center = args.TryGetValue("center", out string center) && center.Trim().ToLowerInvariant() == "true";
        if (args.TryGetValue("scale", out string scale)) {
            snap.scale = scale.Trim().ToLowerInvariant();
        }
        if (!args.TryGetValue("secs", out string secs)) {
            return null;
        }
        string[] f = secs.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 2 < f.Length; i += 3) {
            string shape = f[i].ToUpperInvariant();
            // the flexible extensions (_L, L_) are a little wider or narrower than their neighbour: round
            char kind = shape == "A" ? 'A' : shape == "S" ? 'S' : 'R';
            snap.secs.Add(new Section() { shape = kind, radius = n(f[i + 1]), length = n(f[i + 2]) });
        }
        return snap.secs.Count > 0 ? snap : null;
    }

    // [key=value] [key=value]...
    private static Dictionary<string, string> parameters(string text) {
        Dictionary<string, string> args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int at = 0;
        while ((at = text.IndexOf('[', at)) >= 0) {
            int end = text.IndexOf(']', at);
            if (end < 0) {
                break;
            }
            string pair = text.Substring(at + 1, end - at - 1);
            int equals = pair.IndexOf('=');
            if (equals > 0) {
                args[pair.Substring(0, equals).Trim()] = pair.Substring(equals + 1).Trim();
            }
            at = end + 1;
        }
        return args;
    }

    private static float[] numbers(string text) {
        string[] f = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        float[] values = new float[f.Length];
        for (int i = 0; i < f.Length; i++) {
            values[i] = n(f[i]);
        }
        return values;
    }

    private static float n(string text) {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
    }
}
