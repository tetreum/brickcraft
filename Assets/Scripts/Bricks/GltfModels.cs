using System;
using System.Collections.Generic;
using Brickcraft.Models;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Reads the shape of a glTF model (.glb, or .gltf with its buffers embedded or in files next to it)
    /// into one mesh, right away: mods' models are needed before the world loads (see BrickModels). Only
    /// the geometry is read (positions, normals, the first uv set and triangles of every mesh of the
    /// default scene, placed by their nodes); textures are left out and bricks get their colour, but each
    /// material is a submesh, named after it (for fixed colour parts, see BrickModels.ModelInfo).
    /// Read with GltfFile (no compressed meshes); like Unity's importers, X is mirrored and triangles
    /// turned the other way.
    /// </summary>
    public static class GltfModels
    {
        public static Mesh LoadMesh(string path, string name, out string[] materialNames) {
            GltfFile gltf = GltfFile.Load(path);
            MeshBuilder builder = new MeshBuilder();

            int[] roots = gltf.SceneRoots;
            if (roots != null) {
                foreach (int node in roots) {
                    addNode(gltf, node, Matrix4x4.identity, builder);
                }
            } else if (gltf.Json["meshes"] is JArray meshes) {
                // no scene: every mesh as it is
                for (int i = 0; i < meshes.Count; i++) {
                    addMesh(gltf, i, Matrix4x4.identity, builder);
                }
            }
            return builder.ToMesh(name, out materialNames);
        }

        private class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            // the triangles of each material, by its name
            public readonly List<string> materials = new List<string>();
            public readonly List<List<int>> submeshes = new List<List<int>>();
            public bool hasUvs;

            public List<int> Triangles(string material) {
                int index = materials.IndexOf(material);
                if (index == -1) {
                    index = materials.Count;
                    materials.Add(material);
                    submeshes.Add(new List<int>());
                }
                return submeshes[index];
            }

            public Mesh ToMesh(string name, out string[] materialNames) {
                Mesh mesh = new Mesh();
                mesh.name = name;
                mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = Mathf.Max(1, submeshes.Count);
                for (int i = 0; i < submeshes.Count; i++) {
                    mesh.SetTriangles(submeshes[i], i);
                }
                materialNames = materials.Count > 0 ? materials.ToArray() : new[] { "" };
                if (hasUvs) {
                    mesh.RecalculateTangents(); // for normal maps
                }
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static void addNode(GltfFile gltf, int index, Matrix4x4 parent, MeshBuilder builder) {
            JToken node = gltf.Nodes[index];
            Matrix4x4 matrix = parent * GltfFile.LocalMatrix(node);
            if (node["mesh"] != null) {
                addMesh(gltf, (int)node["mesh"], matrix, builder);
            }
            if (node["children"] is JArray children) {
                foreach (int child in children.ToObject<int[]>()) {
                    addNode(gltf, child, matrix, builder);
                }
            }
        }

        private static void addMesh(GltfFile gltf, int meshIndex, Matrix4x4 matrix, MeshBuilder builder) {
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            foreach (JToken primitive in (JArray)gltf.Json["meshes"][meshIndex]["primitives"]) {
                int mode = (int?)primitive["mode"] ?? 4;
                if (mode != 4) {
                    continue; // only triangle lists
                }
                JToken attributes = primitive["attributes"];
                if (attributes["POSITION"] == null) {
                    continue;
                }
                Vector3[] positions = gltf.ReadVectors((int)attributes["POSITION"], 3);
                Vector3[] normals = attributes["NORMAL"] != null ? gltf.ReadVectors((int)attributes["NORMAL"], 3) : null;
                Vector3[] uvs = attributes["TEXCOORD_0"] != null ? gltf.ReadVectors((int)attributes["TEXCOORD_0"], 2) : null;
                int[] indexes = primitive["indices"] != null ? gltf.ReadIndexes((int)primitive["indices"]) : null;
                if (indexes == null) {
                    indexes = new int[positions.Length];
                    for (int i = 0; i < indexes.Length; i++) {
                        indexes[i] = i;
                    }
                }

                List<int> triangles = builder.Triangles(materialName(gltf, primitive));
                if (normals == null) {
                    addFlat(builder, triangles, positions, uvs, indexes, matrix);
                    continue;
                }
                int first = builder.vertices.Count;
                for (int i = 0; i < positions.Length; i++) {
                    Vector3 p = matrix.MultiplyPoint3x4(positions[i]);
                    builder.vertices.Add(new Vector3(-p.x, p.y, p.z));
                    Vector3 n = normals != null ? normalMatrix.MultiplyVector(normals[i]).normalized : Vector3.zero;
                    builder.normals.Add(new Vector3(-n.x, n.y, n.z));
                    // glTF's v goes down, Unity's up
                    builder.uvs.Add(uvs != null ? new Vector2(uvs[i].x, 1 - uvs[i].y) : Vector2.zero);
                }
                builder.hasUvs |= uvs != null;
                // mirroring X flips the winding, so reverse it back
                for (int i = 0; i + 2 < indexes.Length; i += 3) {
                    triangles.Add(first + indexes[i]);
                    triangles.Add(first + indexes[i + 2]);
                    triangles.Add(first + indexes[i + 1]);
                }
            }
        }

        // a material's name, "material[n]" if it has none, "" for primitives without one
        private static string materialName(GltfFile gltf, JToken primitive) {
            if (primitive["material"] == null) {
                return "";
            }
            int index = (int)primitive["material"];
            string name = gltf.Json["materials"] != null ? (string)gltf.Json["materials"][index]["name"] : null;
            return string.IsNullOrEmpty(name) ? "material" + index : name;
        }

        // a primitive without normals is flat shaded (as glTF says): each triangle gets its own corners
        private static void addFlat(MeshBuilder builder, List<int> triangles, Vector3[] positions, Vector3[] uvs, int[] indexes, Matrix4x4 matrix) {
            for (int i = 0; i + 2 < indexes.Length; i += 3) {
                int[] corner = { indexes[i], indexes[i + 2], indexes[i + 1] }; // mirrored, so the other way around
                Vector3[] p = new Vector3[3];
                for (int k = 0; k < 3; k++) {
                    Vector3 world = matrix.MultiplyPoint3x4(positions[corner[k]]);
                    p[k] = new Vector3(-world.x, world.y, world.z);
                }
                Vector3 normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
                for (int k = 0; k < 3; k++) {
                    triangles.Add(builder.vertices.Count);
                    builder.vertices.Add(p[k]);
                    builder.normals.Add(normal);
                    builder.uvs.Add(uvs != null ? new Vector2(uvs[corner[k]].x, 1 - uvs[corner[k]].y) : Vector2.zero);
                }
            }
            builder.hasUvs |= uvs != null;
        }
    }
}
