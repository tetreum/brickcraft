using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Brickcraft.Bricks.LDraw
{
    /// <summary>
    /// Builds a brick model's mesh from an LDraw part (see LDrawLibrary): its triangles and quads, those of the files
    /// it uses (subparts, primitives) moved where they go, facing out (BFC: winding, INVERTNEXT, mirroring matrices;
    /// files that aren't certified are drawn from both sides), in the game's units (a stud is 20 LDU) with up being
    /// up (LDraw's Y goes down). Its pivot is the middle of its footprint, on its bottom, like every brick model.
    ///
    /// Faces of the brick's colour (16) are the first submesh; faces of colours of their own (a window's glass) are
    /// a submesh each, named "c[LDraw colour]". Edge lines (types 2 and 5) aren't drawn. Normals are smoothed across
    /// edges under CreaseAngle, so rounded parts look round while edges stay sharp.
    /// </summary>
    public static class LDrawModelBuilder
    {
        /// <summary>LDraw's units in the game's: a stud is 20 LDU.</summary>
        public static readonly float Scale = Server.studSize / 20f;
        public const float CreaseAngle = 35;
        public const int MainColor = 16;
        public const int EdgeColor = 24;
        public const string MainPart = "main";

        private class LDrawFile
        {
            public bool certified;
            public readonly List<Line> lines = new List<Line>();
        }

        private struct Line
        {
            // 1: another file; 2: an edge (a and b); 3 or 4: a face
            public int type;
            public int color;
            public Matrix4x4 matrix;
            public string file;
            public Vector3 a, b, c, d;
            // faces: wound clockwise (BFC CW); files: the next one inverted (BFC INVERTNEXT)
            public bool clockwise;
            public bool invert;
        }

        // files read while building (parts reuse their primitives)
        private static readonly Dictionary<string, LDrawFile> parsed = new Dictionary<string, LDrawFile>();

        /// <summary>Forgets the files read (their memory), the next build reads them again.</summary>
        public static void ClearCache() {
            parsed.Clear();
            fileBounds.Clear();
        }

        // the box around each file's faces, in its own space (null: it has none), worked out so far
        private static readonly Dictionary<string, Bounds?> fileBounds = new Dictionary<string, Bounds?>();

        /// <summary>
        /// A part's edges (LDraw's lines, type 2) as a mesh of lines, in the game's units and axes around where its LDraw
        /// origin is, like Build's faces; null if the library hasn't it or it has none. Building instructions draw
        /// them (see Instructions.StepRenderer).
        /// </summary>
        public static Mesh BuildEdges(string partId) {
            string path = LDrawLibrary.FindFile(partId + ".dat");
            if (path == null) {
                return null;
            }
            List<Vector3> points = new List<Vector3>();
            collectEdges(path, Matrix4x4.Scale(new Vector3(Scale, -Scale, Scale)), points, 0);
            if (points.Count == 0) {
                return null;
            }
            Mesh mesh = new Mesh();
            mesh.name = partId + " edges";
            if (points.Count > 65000) {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(points);
            int[] indexes = new int[points.Count];
            for (int i = 0; i < indexes.Length; i++) {
                indexes[i] = i;
            }
            mesh.SetIndices(indexes, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void collectEdges(string path, Matrix4x4 matrix, List<Vector3> points, int depth) {
            LDrawFile file = depth <= 32 ? parse(path) : null;
            if (file == null) {
                return;
            }
            foreach (Line line in file.lines) {
                if (line.type == 1) {
                    string child = LDrawLibrary.FindFile(line.file);
                    if (child != null) {
                        collectEdges(child, matrix * line.matrix, points, depth + 1);
                    }
                } else if (line.type == 2) {
                    points.Add(matrix.MultiplyPoint3x4(line.a));
                    points.Add(matrix.MultiplyPoint3x4(line.b));
                }
            }
        }

        /// <summary>
        /// The box around a part's faces (its part number), in LDU with LDraw's axes (Y down) from its origin; null if
        /// the library hasn't it or it has no faces. Cheaper than Build: no mesh.
        /// </summary>
        public static Bounds? BoundsOf(string partId) {
            string path = LDrawLibrary.FindFile(partId + ".dat");
            return path != null ? boundsOfFile(path, 0) : null;
        }

        private static Bounds? boundsOfFile(string path, int depth) {
            if (fileBounds.TryGetValue(path, out Bounds? known)) {
                return known;
            }
            fileBounds[path] = null; // a file using itself adds nothing
            LDrawFile file = depth <= 32 ? parse(path) : null;
            Bounds? bounds = null;
            if (file != null) {
                foreach (Line line in file.lines) {
                    if (line.type == 1) {
                        string child = LDrawLibrary.FindFile(line.file);
                        Bounds? inner = child != null ? boundsOfFile(child, depth + 1) : null;
                        if (inner.HasValue) {
                            bounds = Encapsulate(bounds, inner.Value, line.matrix);
                        }
                    } else if (line.type == 3 || line.type == 4) {
                        bounds = Encapsulate(bounds, line.a);
                        bounds = Encapsulate(bounds, line.b);
                        bounds = Encapsulate(bounds, line.c);
                        if (line.type == 4) {
                            bounds = Encapsulate(bounds, line.d);
                        }
                    }
                }
            }
            fileBounds[path] = bounds;
            return bounds;
        }

        /// <summary>A box (or none yet) grown to hold that point.</summary>
        public static Bounds? Encapsulate(Bounds? bounds, Vector3 point) {
            if (!bounds.HasValue) {
                return new Bounds(point, Vector3.zero);
            }
            Bounds grown = bounds.Value;
            grown.Encapsulate(point);
            return grown;
        }

        /// <summary>A box (or none yet) grown to hold another, moved and turned by that matrix (its eight corners).</summary>
        public static Bounds? Encapsulate(Bounds? bounds, Bounds other, Matrix4x4 matrix) {
            for (int corner = 0; corner < 8; corner++) {
                Vector3 point = other.center + Vector3.Scale(other.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                bounds = Encapsulate(bounds, matrix.MultiplyPoint3x4(point));
            }
            return bounds;
        }

        // the faces gathered, by part ("main" or "c[colour]"), in LDraw's space
        private class Faces
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
        }

        /// <summary>
        /// The mesh of that part (its part number), null if the library hasn't it. materialNames: its submeshes' names
        /// ("main", then "c47"...).
        /// </summary>
        public static Mesh Build(string partId, out string[] materialNames) {
            materialNames = new string[0];
            string path = LDrawLibrary.FindFile(partId + ".dat");
            if (path == null) {
                return null;
            }
            Dictionary<string, Faces> parts = new Dictionary<string, Faces>();
            parts[MainPart] = new Faces();
            collect(path, Matrix4x4.identity, MainColor, false, true, parts, 0);

            // LDraw's space to the game's: Y up (faces were wound for it, see collect)
            Matrix4x4 toGame = Matrix4x4.Scale(new Vector3(Scale, -Scale, Scale));
            List<string> names = new List<string>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<List<int>> submeshes = new List<List<int>>();
            foreach (KeyValuePair<string, Faces> part in parts) {
                if (part.Value.vertices.Count == 0 && part.Key != MainPart) {
                    continue;
                }
                List<Vector3> triangles = new List<Vector3>(part.Value.vertices.Count);
                foreach (Vector3 vertex in part.Value.vertices) {
                    triangles.Add(toGame.MultiplyPoint3x4(vertex));
                }
                names.Add(part.Key);
                submeshes.Add(smooth(triangles, vertices, normals));
            }

            Mesh mesh = new Mesh();
            mesh.name = partId;
            if (vertices.Count > 65000) {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) {
                mesh.SetTriangles(submeshes[i], i);
            }
            mesh.RecalculateBounds();
            materialNames = names.ToArray();
            return mesh;
        }

        // the faces of a file and of those it uses, where they go (matrix), in their colour, facing out
        private static void collect(string path, Matrix4x4 matrix, int color, bool inverted, bool trusted, Dictionary<string, Faces> parts, int depth) {
            if (depth > 32) {
                return; // a file using itself
            }
            LDrawFile file = parse(path);
            if (file == null) {
                return;
            }
            bool certified = trusted && file.certified;
            foreach (Line line in file.lines) {
                int lineColor = line.color == MainColor ? color : line.color;
                if (line.type == 1) {
                    string child = LDrawLibrary.FindFile(line.file);
                    if (child == null) {
                        continue;
                    }
                    Matrix4x4 childMatrix = matrix * line.matrix;
                    bool mirrored = line.matrix.determinant < 0;
                    collect(child, childMatrix, lineColor, inverted ^ mirrored ^ line.invert, certified, parts, depth + 1);
                    continue;
                }
                if (line.type == 2 || lineColor == EdgeColor) {
                    continue; // edges aren't faces (see BuildEdges)
                }
                string key = lineColor == MainColor ? MainPart : "c" + lineColor;
                if (!parts.TryGetValue(key, out Faces faces)) {
                    faces = new Faces();
                    parts[key] = faces;
                }
                Vector3 a = matrix.MultiplyPoint3x4(line.a), b = matrix.MultiplyPoint3x4(line.b), c = matrix.MultiplyPoint3x4(line.c);
                Vector3 d = line.type == 4 ? matrix.MultiplyPoint3x4(line.d) : Vector3.zero;
                // Unity's front faces are clockwise once Y flips (see Build): LDraw's counter-clockwise ones are reversed
                bool reverse = !(line.clockwise ^ inverted);
                addFace(faces, a, b, c, d, line.type == 4, reverse);
                if (!certified) {
                    addFace(faces, a, b, c, d, line.type == 4, !reverse); // its front is unknown: both
                }
            }
        }

        private static void addFace(Faces faces, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool quad, bool reverse) {
            if (reverse) {
                if (quad) {
                    add(faces, a, d, c);
                    add(faces, a, c, b);
                } else {
                    add(faces, a, c, b);
                }
                return;
            }
            add(faces, a, b, c);
            if (quad) {
                add(faces, a, c, d);
            }
        }

        private static void add(Faces faces, Vector3 a, Vector3 b, Vector3 c) {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-10f) {
                return; // no area
            }
            faces.vertices.Add(a);
            faces.vertices.Add(b);
            faces.vertices.Add(c);
        }

        // its triangles (3 points each) as indices into vertices and normals: corners on the same spot share a
        // normal if their faces meet under CreaseAngle (rounded surfaces), each face keeps its own otherwise (edges)
        private static List<int> smooth(List<Vector3> triangles, List<Vector3> vertices, List<Vector3> normals) {
            int count = triangles.Count / 3;
            Vector3[] faceNormals = new Vector3[count];
            Dictionary<Vector3Int, List<int>> bySpot = new Dictionary<Vector3Int, List<int>>();
            for (int t = 0; t < count; t++) {
                Vector3 a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                faceNormals[t] = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++) {
                    Vector3Int spot = spotOf(triangles[t * 3 + k]);
                    if (!bySpot.TryGetValue(spot, out List<int> around)) {
                        around = new List<int>();
                        bySpot[spot] = around;
                    }
                    around.Add(t);
                }
            }
            float crease = Mathf.Cos(CreaseAngle * Mathf.Deg2Rad);
            Dictionary<Corner, int> made = new Dictionary<Corner, int>();
            List<int> indices = new List<int>(triangles.Count);
            for (int t = 0; t < count; t++) {
                for (int k = 0; k < 3; k++) {
                    Vector3 position = triangles[t * 3 + k];
                    Vector3Int spot = spotOf(position);
                    Vector3 normal = Vector3.zero;
                    foreach (int other in bySpot[spot]) {
                        if (Vector3.Dot(faceNormals[other], faceNormals[t]) >= crease) {
                            normal += faceNormals[other];
                        }
                    }
                    normal = normal.sqrMagnitude > 0 ? normal.normalized : faceNormals[t];
                    // a corner already made with (about) that normal is reused
                    Corner key = new Corner() { spot = spot, direction = Vector3Int.RoundToInt(normal * 64) };
                    if (!made.TryGetValue(key, out int index)) {
                        index = vertices.Count;
                        vertices.Add(position);
                        normals.Add(normal);
                        made[key] = index;
                    }
                    indices.Add(index);
                }
            }
            return indices;
        }

        private static Vector3Int spotOf(Vector3 position) {
            return Vector3Int.RoundToInt(position * 2000); // about a hundredth of an LDU
        }

        // a vertex made: where, and which way it faces (rounded)
        private struct Corner : IEquatable<Corner>
        {
            public Vector3Int spot;
            public Vector3Int direction;

            public bool Equals(Corner other) {
                return spot == other.spot && direction == other.direction;
            }

            public override bool Equals(object other) {
                return other is Corner corner && Equals(corner);
            }

            public override int GetHashCode() {
                return spot.GetHashCode() * 397 ^ direction.GetHashCode();
            }
        }

        // -------- reading --------

        private static LDrawFile parse(string path) {
            if (parsed.TryGetValue(path, out LDrawFile cached)) {
                return cached;
            }
            LDrawFile file = new LDrawFile();
            bool clockwise = false;
            bool invertNext = false;
            try {
                foreach (string raw in LDrawLibrary.ReadLines(path)) {
                    string[] f = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length == 0) {
                        continue;
                    }
                    switch (f[0]) {
                        case "0":
                            if (f.Length >= 2 && f[1] == "BFC") {
                                for (int i = 2; i < f.Length; i++) {
                                    switch (f[i]) {
                                        case "CERTIFY": file.certified = true; break;
                                        case "NOCERTIFY": file.certified = false; break;
                                        case "CW": clockwise = true; break;
                                        case "CCW": clockwise = false; break;
                                        case "INVERTNEXT": invertNext = true; break;
                                    }
                                }
                            }
                            break;
                        case "1":
                            if (f.Length < 15) {
                                break;
                            }
                            Matrix4x4 m = Matrix4x4.identity;
                            m.SetRow(0, new Vector4(num(f[5]), num(f[6]), num(f[7]), num(f[2])));
                            m.SetRow(1, new Vector4(num(f[8]), num(f[9]), num(f[10]), num(f[3])));
                            m.SetRow(2, new Vector4(num(f[11]), num(f[12]), num(f[13]), num(f[4])));
                            m.SetRow(3, new Vector4(0, 0, 0, 1));
                            file.lines.Add(new Line() {
                                type = 1, color = colorOf(f[1]), matrix = m,
                                file = string.Join(" ", f, 14, f.Length - 14), invert = invertNext,
                            });
                            invertNext = false;
                            break;
                        case "2":
                            if (f.Length >= 8) {
                                file.lines.Add(new Line() { type = 2, color = colorOf(f[1]), a = point(f, 2), b = point(f, 5) });
                            }
                            break;
                        case "3":
                            if (f.Length >= 11) {
                                file.lines.Add(new Line() {
                                    type = 3, color = colorOf(f[1]), clockwise = clockwise,
                                    a = point(f, 2), b = point(f, 5), c = point(f, 8),
                                });
                            }
                            invertNext = false;
                            break;
                        case "4":
                            if (f.Length >= 14) {
                                file.lines.Add(new Line() {
                                    type = 4, color = colorOf(f[1]), clockwise = clockwise,
                                    a = point(f, 2), b = point(f, 5), c = point(f, 8), d = point(f, 11),
                                });
                            }
                            invertNext = false;
                            break;
                    }
                }
            } catch (Exception e) {
                Debug.LogError("Can't read the LDraw file " + path + ": " + e.Message);
                file = null;
            }
            parsed[path] = file;
            return file;
        }

        private static float num(string value) {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static Vector3 point(string[] f, int at) {
            return new Vector3(num(f[at]), num(f[at + 1]), num(f[at + 2]));
        }

        // colours are numbers, or direct colours (0x2RRGGBB) taken as the brick's
        private static int colorOf(string value) {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int color) ? color : MainColor;
        }
    }
}
