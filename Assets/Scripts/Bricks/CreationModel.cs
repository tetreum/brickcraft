using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Brickcraft.Network;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// A creation as a brick model of its own (see PartsLister): the parts a player built, merged into one mesh
    /// without what can't be seen, written as a mod ready to copy into Mods/ (or its item and model folders into
    /// StreamingAssets/Items and StreamingAssets/BrickModels):
    ///
    ///   [id]/index.json                    the mod
    ///   [id]/models/[id]/model.obj         the shape, a material ("usemtl c[colour]") for each colour
    ///   [id]/models/[id]/model.json        its size, colours, and an exact collider (it can be walked into)
    ///   [id]/items/[id]/info.json, icon.png, model.ldr: its real LEGO parts ("ldr": true, see LDraw.LDrawContraption),
    ///                                      so parts lists count them when it's placed
    ///
    /// What can't be seen goes in two passes. By the grid, for bricks: studs under another part, and faces against
    /// another part that fills that side. Then by rays: from points of each face that's left, rays go out every
    /// way it faces; a face none of them gets out of the creation from is hidden (a brick's inside, closed by the
    /// bricks around). Transparent parts hide nothing. Doors and lids stay as they were. Both passes run on other
    /// threads, the game goes on meanwhile.
    /// </summary>
    public static class CreationModel
    {
        // how far rays start from the face they look from, in the game's units
        private const float RayStart = 0.002f;
        // the ways rays go (those facing out of each face)
        private const int Directions = 48;
        // how far apart rays start on faces none got out from at first, in the game's units (an LDraw unit)
        private const float SampleSpacing = 0.02f;
        private const int MaxSampleSteps = 80;
        private const float OnPlane = 0.02f; // in cells

        private class Triangle
        {
            public Vector3 a, b, c;
            public Vector3 na, nb, nc;
            public int color;
            public bool transparent;
            public int part;
            public bool hidden;
            public bool seen;

            public Vector3 Normal {
                get { return Vector3.Cross(b - a, c - a).normalized; }
            }

            public Vector3 Middle {
                get { return (a + b + c) / 3; }
            }
        }

        /// <summary>A mod's (and item's, model's) id for a creation's name: lowercase letters, digits and _.</summary>
        public static string IdFor(string name) {
            string id = Regex.Replace((name ?? "").ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_');
            id = Regex.Replace(id, "_+", "_");
            return id.Length > 0 ? id : "creation";
        }

        /// <summary>Makes the creation's mod in that folder (it takes a few frames), then tells what it did.</summary>
        public static IEnumerator Make(List<CreationPart> parts, List<LDraw.LDrawPlacement> ldraw, string name, string author, string folder, Action<string> done) {
            List<Triangle> triangles = new List<Triangle>();
            Vector3Int cellsMin = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue), cellsMax = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            Dictionary<Vector3Int, int> filled = new Dictionary<Vector3Int, int>();
            try {
                gather(parts, triangles, filled, ref cellsMin, ref cellsMax);
            } catch (Exception e) {
                done("The creation's model couldn't be made: " + e.Message);
                yield break;
            }
            if (triangles.Count == 0) {
                done("The creation's model couldn't be made: its parts have no shape");
                yield break;
            }
            int before = triangles.Count;

            // what can't be seen, on other threads: it can take a while for big creations
            Vector3[] ways = Ways;
            Task hiding = Task.Run(() => {
                byGrid(parts, triangles, filled);
                byRays(triangles, ways);
            });
            while (!hiding.IsCompleted) {
                yield return null;
            }
            if (hiding.IsFaulted) {
                done("The creation's model couldn't be made: " + hiding.Exception.GetBaseException().Message);
                yield break;
            }

            List<Triangle> kept = triangles.FindAll(t => !t.hidden);
            string id = IdFor(name);
            string message;
            try {
                message = write(kept, parts, ldraw, cellsMin, cellsMax, id, name, author, folder);
            } catch (Exception e) {
                done("The creation's model couldn't be saved: " + e.Message);
                yield break;
            }
            done(message + " (" + kept.Count + " of its " + before + " triangles are seen)");
        }

        // -------- the parts' shapes --------

        // a shape's data, copied out of Unity once
        private class MeshData
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public int[][] triangles;
        }

        private static void gather(List<CreationPart> parts, List<Triangle> triangles, Dictionary<Vector3Int, int> filled,
                ref Vector3Int cellsMin, ref Vector3Int cellsMax) {
            Dictionary<Mesh, MeshData> meshes = new Dictionary<Mesh, MeshData>();
            for (int p = 0; p < parts.Count; p++) {
                CreationPart part = parts[p];
                if (!Server.items.TryGetValue(part.itemId ?? "", out Item item) || item.brickModel == null || item.brickModel.prefab == null) {
                    continue;
                }
                GameObject prefab = item.brickModel.prefab;
                int color = item.ValidColor(part.color);
                if (color == BrickColor.None) {
                    color = BrickModels.TemplateColor;
                }
                bool special = !string.IsNullOrEmpty(item.materialName); // water...
                BrickModelParts own = prefab.GetComponent<BrickModelParts>();
                Matrix4x4 place = Matrix4x4.TRS(part.position, part.rotation, Vector3.one);
                Transform root = prefab.transform;
                bool opaque = !special;

                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !mesh.isReadable || filter.name.StartsWith("GridStud") || filter.GetComponent<MeshRenderer>() == null) {
                        continue;
                    }
                    Matrix4x4 m = place * root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    // read once for every part of that shape: a creation has many of the same
                    if (!meshes.TryGetValue(mesh, out MeshData data)) {
                        data = new MeshData() { vertices = mesh.vertices, normals = mesh.normals, triangles = new int[mesh.subMeshCount][] };
                        for (int sub = 0; sub < mesh.subMeshCount; sub++) {
                            data.triangles[sub] = mesh.GetTriangles(sub);
                        }
                        meshes[mesh] = data;
                    }
                    Vector3[] vertices = data.vertices;
                    Vector3[] normals = data.normals;
                    for (int sub = 0; sub < data.triangles.Length; sub++) {
                        int subColor = filter.transform == root && own != null && sub < own.colors.Length && own.colors[sub] != BrickColor.None
                            ? own.colors[sub] : color;
                        BrickColor brickColor = BrickColorPalette.Get(subColor);
                        bool transparent = special || (brickColor != null && brickColor.isTransparent);
                        opaque &= !transparent;
                        int[] indices = data.triangles[sub];
                        for (int i = 0; i + 2 < indices.Length; i += 3) {
                            Triangle t = new Triangle() {
                                a = m.MultiplyPoint3x4(vertices[indices[i]]),
                                b = m.MultiplyPoint3x4(vertices[indices[i + 1]]),
                                c = m.MultiplyPoint3x4(vertices[indices[i + 2]]),
                                color = subColor, transparent = transparent, part = p,
                            };
                            Vector3 face = t.Normal;
                            if (face == Vector3.zero) {
                                continue; // no area
                            }
                            t.na = normals.Length > 0 ? m.MultiplyVector(normals[indices[i]]).normalized : face;
                            t.nb = normals.Length > 0 ? m.MultiplyVector(normals[indices[i + 1]]).normalized : face;
                            t.nc = normals.Length > 0 ? m.MultiplyVector(normals[indices[i + 2]]).normalized : face;
                            triangles.Add(t);
                        }
                    }
                }

                // the grid's parts: where they are, and which hide what's against them
                if (part.cellsMax.x > part.cellsMin.x) {
                    cellsMin = Vector3Int.Min(cellsMin, part.cellsMin);
                    cellsMax = Vector3Int.Max(cellsMax, part.cellsMax);
                    if (opaque) {
                        for (int x = part.cellsMin.x; x < part.cellsMax.x; x++) {
                            for (int y = part.cellsMin.y; y < part.cellsMax.y; y++) {
                                for (int z = part.cellsMin.z; z < part.cellsMax.z; z++) {
                                    filled[new Vector3Int(x, y, z)] = p;
                                }
                            }
                        }
                    }
                }
            }
        }

        // -------- hidden by the grid --------

        private static Vector3 toCells(Vector3 position) {
            return new Vector3(position.x / BrickGrid.CellSize.x, position.y / BrickGrid.CellSize.y, position.z / BrickGrid.CellSize.z);
        }

        private static bool filledByOther(Dictionary<Vector3Int, int> filled, Vector3 cells, int part) {
            return filled.TryGetValue(Vector3Int.FloorToInt(cells), out int other) && other != part;
        }

        // a face on a side of its part's cells, facing out of them: what can cover a neighbour's face there
        private struct SideFace
        {
            public int part;
            public Vector2 a, b, c; // on the side's plane, in cells
        }

        // the two axes along a side across that axis
        private static Vector2 onPlane(Vector3 cells, int axis) {
            return axis == 0 ? new Vector2(cells.y, cells.z) : axis == 1 ? new Vector2(cells.x, cells.z) : new Vector2(cells.x, cells.y);
        }

        private static string sideKey(int axis, int side, int plane) {
            return axis + "/" + side + "/" + plane;
        }

        // the faces on the sides of their parts' cells, by side (axis, way it faces, plane)
        private static Dictionary<string, List<SideFace>> sideFaces(List<CreationPart> parts, List<Triangle> triangles) {
            Dictionary<string, List<SideFace>> sides = new Dictionary<string, List<SideFace>>();
            foreach (Triangle t in triangles) {
                CreationPart part = parts[t.part];
                if (t.transparent || part.cellsMax.x <= part.cellsMin.x) {
                    continue;
                }
                Vector3 a = toCells(t.a), b = toCells(t.b), c = toCells(t.c);
                Vector3 normal = t.Normal;
                for (int axis = 0; axis < 3; axis++) {
                    for (int side = -1; side <= 1; side += 2) {
                        int plane = side > 0 ? part.cellsMax[axis] : part.cellsMin[axis];
                        if (Mathf.Abs(a[axis] - plane) > OnPlane || Mathf.Abs(b[axis] - plane) > OnPlane || Mathf.Abs(c[axis] - plane) > OnPlane
                                || normal[axis] * side < 0.7f) {
                            continue;
                        }
                        string key = sideKey(axis, side, plane);
                        if (!sides.TryGetValue(key, out List<SideFace> list)) {
                            sides[key] = list = new List<SideFace>();
                        }
                        list.Add(new SideFace() { part = t.part, a = onPlane(a, axis), b = onPlane(b, axis), c = onPlane(c, axis) });
                    }
                }
            }
            return sides;
        }

        // a point of a side is against a face of another part, on that side facing the other way
        private static bool againstOther(List<SideFace> faces, Vector2 point, int part) {
            if (faces == null) {
                return false;
            }
            foreach (SideFace face in faces) {
                if (face.part != part && contains(face, point)) {
                    return true;
                }
            }
            return false;
        }

        private static bool contains(SideFace face, Vector2 p) {
            const float Margin = 0.001f;
            float d1 = cross(face.a, face.b, p), d2 = cross(face.b, face.c, p), d3 = cross(face.c, face.a, p);
            bool negative = d1 < -Margin || d2 < -Margin || d3 < -Margin;
            bool positive = d1 > Margin || d2 > Margin || d3 > Margin;
            return !(negative && positive);
        }

        private static float cross(Vector2 a, Vector2 b, Vector2 p) {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }

        // studs under another part, and faces against faces of another part (not just its cells: a curved slope
        // doesn't fill them)
        private static void byGrid(List<CreationPart> parts, List<Triangle> triangles, Dictionary<Vector3Int, int> filled) {
            Dictionary<string, List<SideFace>> sides = sideFaces(parts, triangles);
            foreach (Triangle t in triangles) {
                CreationPart part = parts[t.part];
                if (part.cellsMax.x <= part.cellsMin.x) {
                    continue; // attachments: the rays see to them
                }
                Vector3 a = toCells(t.a), b = toCells(t.b), c = toCells(t.c);
                Vector3 middle = (a + b + c) / 3;
                float top = part.cellsMax.y;

                // over its top: a stud, in what's on it
                if (Mathf.Min(a.y, b.y, c.y) >= top - OnPlane && middle.y > top + OnPlane) {
                    if (filledByOther(filled, new Vector3(middle.x, top + 0.5f, middle.z), t.part)) {
                        t.hidden = true;
                    }
                    continue;
                }

                // on a side of its cells, facing out of it, against another part all along
                Vector3 normal = t.Normal;
                for (int axis = 0; axis < 3 && !t.hidden; axis++) {
                    for (int side = -1; side <= 1; side += 2) {
                        int plane = side > 0 ? part.cellsMax[axis] : part.cellsMin[axis];
                        if (Mathf.Abs(a[axis] - plane) > OnPlane || Mathf.Abs(b[axis] - plane) > OnPlane || Mathf.Abs(c[axis] - plane) > OnPlane
                                || normal[axis] * side < 0.7f) {
                            continue;
                        }
                        sides.TryGetValue(sideKey(axis, -side, plane), out List<SideFace> facing);
                        bool covered = true;
                        foreach (Vector3 corner in new[] { middle, Vector3.Lerp(a, middle, 0.2f), Vector3.Lerp(b, middle, 0.2f), Vector3.Lerp(c, middle, 0.2f) }) {
                            if (!againstOther(facing, onPlane(corner, axis), t.part)) {
                                covered = false;
                                break;
                            }
                        }
                        if (covered) {
                            t.hidden = true;
                            break;
                        }
                    }
                }
            }
        }

        // -------- hidden from every way --------

        private static Vector3[] directions;

        // ways spread evenly over a sphere
        private static Vector3[] Ways {
            get {
                if (directions == null) {
                    directions = new Vector3[Directions];
                    float golden = Mathf.PI * (3 - Mathf.Sqrt(5));
                    for (int i = 0; i < Directions; i++) {
                        float y = 1 - (i + 0.5f) * 2f / Directions;
                        float radius = Mathf.Sqrt(1 - y * y);
                        directions[i] = new Vector3(Mathf.Cos(golden * i) * radius, y, Mathf.Sin(golden * i) * radius);
                    }
                }
                return directions;
            }
        }

        // the faces still standing that no ray gets out from are hidden: rays from their middle first, then from
        // nearer each corner, then from all over them, a face's stopping at the first that gets out. On threads of
        // their own (see TriangleRays), all but one of the processor's and its main one: the game goes on meanwhile
        private static void byRays(List<Triangle> triangles, Vector3[] ways) {
            List<Triangle> looked = triangles.FindAll(t => !t.hidden && !t.transparent);
            if (looked.Count == 0) {
                return;
            }
            Vector3[] a = new Vector3[looked.Count], b = new Vector3[looked.Count], c = new Vector3[looked.Count];
            Bounds bounds = new Bounds(looked[0].a, Vector3.zero);
            for (int i = 0; i < looked.Count; i++) {
                Triangle t = looked[i];
                a[i] = t.a;
                b[i] = t.b;
                c[i] = t.c;
                bounds.Encapsulate(t.a);
                bounds.Encapsulate(t.b);
                bounds.Encapsulate(t.c);
            }
            TriangleRays rays = new TriangleRays(a, b, c);
            float reach = bounds.size.magnitude * 2 + 1;
            ParallelOptions options = new ParallelOptions() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
            Parallel.For(0, looked.Count, options, i => {
                if (!getsOut(looked[i], rays, ways, reach)) {
                    looked[i].hidden = true;
                }
            });
        }

        // a ray from the face gets out of the creation
        private static bool getsOut(Triangle t, TriangleRays rays, Vector3[] ways, float reach) {
            Vector3 normal = t.Normal;
            for (int pass = 0; pass < 5; pass++) {
                foreach (Vector3 point in samples(t, pass)) {
                    Vector3 from = point + normal * RayStart;
                    foreach (Vector3 way in ways) {
                        if (Vector3.Dot(way, normal) > 0.1f && !rays.Hits(from, way, reach)) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // where rays go from on a face: its middle, then nearer each corner, then a grid all over it
        private static IEnumerable<Vector3> samples(Triangle t, int pass) {
            if (pass == 0) {
                yield return t.Middle;
            } else if (pass < 4) {
                yield return Vector3.Lerp(pass == 1 ? t.a : pass == 2 ? t.b : t.c, t.Middle, 0.25f);
            } else {
                float longest = Mathf.Max((t.b - t.a).magnitude, (t.c - t.b).magnitude, (t.a - t.c).magnitude);
                int steps = Mathf.Clamp(Mathf.CeilToInt(longest / SampleSpacing), 1, MaxSampleSteps);
                for (int i = 0; i <= steps; i++) {
                    for (int j = 0; i + j <= steps; j++) {
                        // a bit inside the face: its edges are against its neighbours
                        float u = (i + 0.1f) / (steps + 0.3f), v = (j + 0.1f) / (steps + 0.3f);
                        yield return t.a + (t.b - t.a) * u + (t.c - t.a) * v;
                    }
                }
            }
        }

        // -------- the files --------

        private static string write(List<Triangle> kept, List<CreationPart> parts, List<LDraw.LDrawPlacement> ldraw, Vector3Int cellsMin, Vector3Int cellsMax,
                string id, string name, string author, string folder) {
            // its pivot: the middle of its footprint, at its bottom (what it fills of the grid)
            Vector3 pivot;
            Vector3Int size;
            if (cellsMax.x > cellsMin.x) {
                size = cellsMax - cellsMin;
                pivot = Vector3.Scale(new Vector3((cellsMin.x + cellsMax.x) / 2f, cellsMin.y, (cellsMin.z + cellsMax.z) / 2f), BrickGrid.CellSize);
            } else {
                Bounds bounds = new Bounds(kept[0].a, Vector3.zero);
                foreach (Triangle t in kept) {
                    bounds.Encapsulate(t.a); bounds.Encapsulate(t.b); bounds.Encapsulate(t.c);
                }
                pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                size = Vector3Int.Max(Vector3Int.one, Vector3Int.RoundToInt(toCells(bounds.size)));
            }

            string mod = Path.Combine(folder, id);
            string modelFolder = Path.Combine(mod, "models", id);
            string itemFolder = Path.Combine(mod, "items", id);
            Directory.CreateDirectory(modelFolder);
            Directory.CreateDirectory(itemFolder);

            // by colour, corners shared where they're the same (place and normal)
            SortedDictionary<int, List<Triangle>> byColor = new SortedDictionary<int, List<Triangle>>();
            foreach (Triangle t in kept) {
                if (!byColor.TryGetValue(t.color, out List<Triangle> list)) {
                    list = new List<Triangle>();
                    byColor[t.color] = list;
                }
                list.Add(t);
            }
            StringBuilder obj = new StringBuilder();
            obj.Append("# ").Append(name).Append(", made in Brickcraft\n");
            List<Vector3> positions = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            Dictionary<string, int> corners = new Dictionary<string, int>();
            StringBuilder faces = new StringBuilder();
            Dictionary<string, int> partColors = new Dictionary<string, int>();
            List<Vector3> meshVertices = new List<Vector3>();
            List<Vector3> meshNormals = new List<Vector3>();
            List<List<int>> meshTriangles = new List<List<int>>();
            List<Material> materials = new List<Material>();
            foreach (KeyValuePair<int, List<Triangle>> group in byColor) {
                string material = "c" + group.Key;
                partColors[material] = group.Key;
                faces.Append("usemtl ").Append(material).Append('\n');
                List<int> meshGroup = new List<int>();
                Dictionary<string, int> meshCorners = new Dictionary<string, int>();
                foreach (Triangle t in group.Value) {
                    int a = corner(t.a - pivot, t.na, positions, normals, corners);
                    int b = corner(t.b - pivot, t.nb, positions, normals, corners);
                    int c = corner(t.c - pivot, t.nc, positions, normals, corners);
                    // OBJ's X goes the other way (see ObjLoader): its winding too
                    faces.Append("f ").Append(a).Append("//").Append(a).Append(' ').Append(c).Append("//").Append(c).Append(' ').Append(b).Append("//").Append(b).Append('\n');
                    meshGroup.Add(corner(t.a - pivot, t.na, meshVertices, meshNormals, meshCorners) - 1);
                    meshGroup.Add(corner(t.b - pivot, t.nb, meshVertices, meshNormals, meshCorners) - 1);
                    meshGroup.Add(corner(t.c - pivot, t.nc, meshVertices, meshNormals, meshCorners) - 1);
                }
                meshTriangles.Add(meshGroup);
                BrickColor brickColor = BrickColorPalette.Get(group.Key);
                materials.Add(brickColor != null ? brickColor.material : null);
            }
            foreach (Vector3 v in positions) {
                obj.Append("v ").Append(number(-v.x)).Append(' ').Append(number(v.y)).Append(' ').Append(number(v.z)).Append('\n');
            }
            foreach (Vector3 n in normals) {
                obj.Append("vn ").Append(number(-n.x)).Append(' ').Append(number(n.y)).Append(' ').Append(number(n.z)).Append('\n');
            }
            obj.Append(faces);
            File.WriteAllText(Path.Combine(modelFolder, "model.obj"), obj.ToString(), new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(modelFolder, BrickModels.InfoFile), JsonConvert.SerializeObject(new {
                width = size.x, depth = size.z, plates = size.y,
                collider = "mesh", keepPivot = true, parts = partColors,
            }, Formatting.Indented));
            // its real parts, from its pivot (the parts list's LDraw model is from the box's corner, in LDU, Y down)
            Vector3 pivotLDU = new Vector3(Mathf.Round(pivot.x / BrickGrid.CellSize.x * 20 * 2) / 2, -Mathf.Round(pivot.y / BrickGrid.CellSize.y) * 8,
                Mathf.Round(pivot.z / BrickGrid.CellSize.z * 20 * 2) / 2);
            List<LDraw.LDrawPlacement> fromPivot = new List<LDraw.LDrawPlacement>();
            foreach (LDraw.LDrawPlacement placed in ldraw) {
                LDraw.LDrawPlacement moved = placed;
                moved.position -= pivotLDU;
                fromPivot.Add(moved);
            }
            bool hasParts = fromPivot.Count > 0;
            if (hasParts) {
                LDraw.LDrawWriter.Write(Path.Combine(itemFolder, LDraw.LDrawContraption.FileName), name, fromPivot);
            }
            File.WriteAllText(Path.Combine(itemFolder, "info.json"), hasParts
                ? JsonConvert.SerializeObject(new { name = name, type = "brick", brickModel = id, maxStack = 1, ldr = true }, Formatting.Indented)
                : JsonConvert.SerializeObject(new { name = name, type = "brick", brickModel = id, maxStack = 1 }, Formatting.Indented));
            File.WriteAllText(Path.Combine(mod, "index.json"), JsonConvert.SerializeObject(new {
                name = name, author = author, version = "1.0", description = "A creation saved with the Parts Lister",
            }, Formatting.Indented));

            // its icon, from the same mesh
            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(meshVertices);
            mesh.SetNormals(meshNormals);
            mesh.subMeshCount = meshTriangles.Count;
            for (int i = 0; i < meshTriangles.Count; i++) {
                mesh.SetTriangles(meshTriangles[i], i);
            }
            mesh.RecalculateBounds();
            GameObject model = new GameObject("Creation icon");
            model.SetActive(false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            try {
                Texture2D icon = ModelIconRenderer.Render(model);
                File.WriteAllBytes(Path.Combine(itemFolder, "icon.png"), icon.EncodeToPNG());
                UnityEngine.Object.Destroy(icon);
            } finally {
                UnityEngine.Object.Destroy(model);
                UnityEngine.Object.Destroy(mesh);
            }
            return "Model saved as the mod \"" + id + "\" (copy " + mod + " into Mods/, then add it to a world)";
        }

        // a corner's 1-based index, made the first time that place and normal come
        private static int corner(Vector3 position, Vector3 normal, List<Vector3> positions, List<Vector3> normals, Dictionary<string, int> corners) {
            string key = Mathf.RoundToInt(position.x * 10000) + "," + Mathf.RoundToInt(position.y * 10000) + "," + Mathf.RoundToInt(position.z * 10000)
                + "/" + Mathf.RoundToInt(normal.x * 100) + "," + Mathf.RoundToInt(normal.y * 100) + "," + Mathf.RoundToInt(normal.z * 100);
            if (!corners.TryGetValue(key, out int index)) {
                positions.Add(position);
                normals.Add(normal);
                index = positions.Count;
                corners[key] = index;
            }
            return index;
        }

        private static string number(float value) {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }
}
