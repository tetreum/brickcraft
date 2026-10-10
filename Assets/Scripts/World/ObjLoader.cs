using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Minimal Wavefront OBJ reader for block models: only vertex positions and faces are used,
    /// polygons are triangulated as fans. Like Unity's importer, X is mirrored to go from OBJ's
    /// right handed coordinates to Unity's left handed ones.
    /// </summary>
    public static class ObjLoader
    {
        public static void Load(string path, out Vector3[] vertices, out int[] triangles) {
            List<Vector3> vertexList = new List<Vector3>();
            List<int> triangleList = new List<int>();
            List<int> polygon = new List<int>();
            int lineNumber = 0;

            foreach (string rawLine in File.ReadLines(path)) {
                lineNumber++;
                string line = rawLine.Trim();

                if (line.StartsWith("v ")) {
                    string[] parts = split(line);
                    vertexList.Add(new Vector3(
                        -parseFloat(parts[1], path, lineNumber),
                        parseFloat(parts[2], path, lineNumber),
                        parseFloat(parts[3], path, lineNumber)
                    ));
                } else if (line.StartsWith("f ")) {
                    string[] parts = split(line);
                    polygon.Clear();

                    for (int i = 1; i < parts.Length; i++) {
                        // "v", "v/vt", "v//vn" or "v/vt/vn", negative indices count from the end
                        int index = int.Parse(parts[i].Split('/')[0], CultureInfo.InvariantCulture);
                        polygon.Add(index > 0 ? index - 1 : vertexList.Count + index);
                    }
                    for (int i = 1; i < polygon.Count - 1; i++) {
                        // mirroring X flips the winding, so reverse it back
                        triangleList.Add(polygon[0]);
                        triangleList.Add(polygon[i + 1]);
                        triangleList.Add(polygon[i]);
                    }
                }
            }

            vertices = vertexList.ToArray();
            triangles = triangleList.ToArray();

            foreach (int index in triangles) {
                if (index < 0 || index >= vertices.Length) {
                    throw new InvalidDataException("Face references a vertex that doesn't exist in " + path);
                }
            }
        }

        /// <summary>
        /// The whole model as a mesh, with its uvs and normals (calculated if it has none): what bricks
        /// of file models are drawn with (see Bricks.BrickModels). Same axes as Load. Each material
        /// ("usemtl") is a submesh, materialNames says which ("" for faces before any).
        /// </summary>
        public static Mesh LoadMesh(string path, string name, out string[] materialNames) {
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> vertexUvs = new List<Vector2>();
            List<Vector3> vertexNormals = new List<Vector3>();
            List<int> triangles = new List<int>();
            // corners with the same position, uv and normal are one vertex
            Dictionary<string, int> corners = new Dictionary<string, int>();
            List<int> polygon = new List<int>();
            bool hasNormals = true, hasUvs = false;
            int lineNumber = 0;
            // the material of each triangle, by its position in materials
            List<string> materials = new List<string>() { "" };
            List<int> triangleMaterials = new List<int>();
            int material = 0;

            foreach (string rawLine in File.ReadLines(path)) {
                lineNumber++;
                string line = rawLine.Trim();

                if (line.StartsWith("v ")) {
                    string[] parts = split(line);
                    positions.Add(new Vector3(-parseFloat(parts[1], path, lineNumber), parseFloat(parts[2], path, lineNumber), parseFloat(parts[3], path, lineNumber)));
                } else if (line.StartsWith("vt ")) {
                    string[] parts = split(line);
                    uvs.Add(new Vector2(parseFloat(parts[1], path, lineNumber), parts.Length > 2 ? parseFloat(parts[2], path, lineNumber) : 0));
                } else if (line.StartsWith("vn ")) {
                    string[] parts = split(line);
                    normals.Add(new Vector3(-parseFloat(parts[1], path, lineNumber), parseFloat(parts[2], path, lineNumber), parseFloat(parts[3], path, lineNumber)));
                } else if (line.StartsWith("usemtl ")) {
                    string materialName = line.Substring(7).Trim();
                    material = materials.IndexOf(materialName);
                    if (material == -1) {
                        material = materials.Count;
                        materials.Add(materialName);
                    }
                } else if (line.StartsWith("f ")) {
                    string[] parts = split(line);
                    polygon.Clear();

                    for (int i = 1; i < parts.Length; i++) {
                        // "v", "v/vt", "v//vn" or "v/vt/vn", negative indices count from the end
                        string[] indexes = parts[i].Split('/');
                        int v = objIndex(indexes[0], positions.Count, path, lineNumber);
                        int vt = indexes.Length > 1 && indexes[1].Length > 0 ? objIndex(indexes[1], uvs.Count, path, lineNumber) : -1;
                        int vn = indexes.Length > 2 && indexes[2].Length > 0 ? objIndex(indexes[2], normals.Count, path, lineNumber) : -1;
                        hasNormals &= vn >= 0;
                        hasUvs |= vt >= 0;

                        string key = v + "/" + vt + "/" + vn;
                        int vertex;
                        if (!corners.TryGetValue(key, out vertex)) {
                            vertex = vertices.Count;
                            corners[key] = vertex;
                            vertices.Add(positions[v]);
                            vertexUvs.Add(vt >= 0 ? uvs[vt] : Vector2.zero);
                            vertexNormals.Add(vn >= 0 ? normals[vn] : Vector3.zero);
                        }
                        polygon.Add(vertex);
                    }
                    for (int i = 1; i < polygon.Count - 1; i++) {
                        // mirroring X flips the winding, so reverse it back
                        triangles.Add(polygon[0]);
                        triangles.Add(polygon[i + 1]);
                        triangles.Add(polygon[i]);
                        triangleMaterials.Add(material);
                    }
                }
            }

            if (!hasNormals) {
                smoothNormals(vertices, vertexUvs, vertexNormals, triangles);
            }
            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, vertexUvs);
            mesh.SetNormals(vertexNormals);

            // a submesh per material that has faces (smoothing keeps the triangles' order)
            List<string> used = new List<string>();
            List<List<int>> submeshes = new List<List<int>>();
            for (int m = 0; m < materials.Count; m++) {
                List<int> submesh = new List<int>();
                for (int t = 0; t < triangleMaterials.Count; t++) {
                    if (triangleMaterials[t] == m) {
                        submesh.Add(triangles[t * 3]);
                        submesh.Add(triangles[t * 3 + 1]);
                        submesh.Add(triangles[t * 3 + 2]);
                    }
                }
                if (submesh.Count > 0) {
                    used.Add(materials[m]);
                    submeshes.Add(submesh);
                }
            }
            mesh.subMeshCount = Mathf.Max(1, submeshes.Count);
            for (int m = 0; m < submeshes.Count; m++) {
                mesh.SetTriangles(submeshes[m], m);
            }
            materialNames = used.Count > 0 ? used.ToArray() : new[] { "" };
            if (hasUvs) {
                mesh.RecalculateTangents(); // for normal maps
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Faces meeting at less than this angle look smooth, sharper edges stay sharp (like Unity's importer).</summary>
        public const float SmoothingAngle = 60;

        // normals for a model that has none: each triangle gets its own corners, and corners at the same
        // place share the normals of the triangles meeting them at less than SmoothingAngle
        private static void smoothNormals(List<Vector3> vertices, List<Vector2> uvs, List<Vector3> normals, List<int> triangles) {
            List<Vector3> splitVertices = new List<Vector3>(triangles.Count);
            List<Vector2> splitUvs = new List<Vector2>(triangles.Count);
            Vector3[] faceNormals = new Vector3[triangles.Count / 3];
            for (int i = 0; i < triangles.Count; i += 3) {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                faceNormals[i / 3] = Vector3.Cross(b - a, c - a); // area weighted
                for (int k = 0; k < 3; k++) {
                    splitVertices.Add(vertices[triangles[i + k]]);
                    splitUvs.Add(uvs[triangles[i + k]]);
                }
            }

            Dictionary<Vector3, List<int>> cornersAt = new Dictionary<Vector3, List<int>>();
            for (int i = 0; i < splitVertices.Count; i++) {
                List<int> corners;
                if (!cornersAt.TryGetValue(splitVertices[i], out corners)) {
                    corners = new List<int>();
                    cornersAt[splitVertices[i]] = corners;
                }
                corners.Add(i);
            }

            float threshold = Mathf.Cos(SmoothingAngle * Mathf.Deg2Rad);
            normals.Clear();
            for (int i = 0; i < splitVertices.Count; i++) {
                Vector3 own = faceNormals[i / 3];
                Vector3 sum = Vector3.zero;
                foreach (int other in cornersAt[splitVertices[i]]) {
                    Vector3 theirs = faceNormals[other / 3];
                    if (Vector3.Dot(own.normalized, theirs.normalized) >= threshold) {
                        sum += theirs;
                    }
                }
                normals.Add(sum.sqrMagnitude > 0 ? sum.normalized : Vector3.up);
            }

            vertices.Clear();
            vertices.AddRange(splitVertices);
            uvs.Clear();
            uvs.AddRange(splitUvs);
            for (int i = 0; i < triangles.Count; i++) {
                triangles[i] = i;
            }
        }

        private static int objIndex(string value, int count, string path, int lineNumber) {
            int index;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) {
                throw new InvalidDataException("Invalid index \"" + value + "\" at " + path + ":" + lineNumber);
            }
            index = index > 0 ? index - 1 : count + index;
            if (index < 0 || index >= count) {
                throw new InvalidDataException("Face references something that doesn't exist at " + path + ":" + lineNumber);
            }
            return index;
        }

        private static string[] split(string line) {
            return line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        }

        private static float parseFloat(string value, string path, int lineNumber) {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)) {
                throw new InvalidDataException("Invalid number \"" + value + "\" at " + path + ":" + lineNumber);
            }
            return result;
        }
    }
}
