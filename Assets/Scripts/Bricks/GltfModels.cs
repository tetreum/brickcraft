using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// Compressed meshes (Draco, meshopt) aren't supported.
    /// glTF is right handed: like Unity's importers, X is mirrored and triangles turned the other way.
    /// </summary>
    public static class GltfModels
    {
        private const uint GlbMagic = 0x46546C67; // "glTF"
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinaryChunk = 0x004E4942;

        public static Mesh LoadMesh(string path, string name, out string[] materialNames) {
            byte[] file = File.ReadAllBytes(path);
            JObject gltf;
            byte[] glbBuffer = null;

            if (file.Length >= 12 && BitConverter.ToUInt32(file, 0) == GlbMagic) {
                // binary: a header, a JSON chunk and a binary chunk (the first buffer)
                int offset = 12;
                string json = null;
                while (offset + 8 <= file.Length) {
                    int length = BitConverter.ToInt32(file, offset);
                    uint type = BitConverter.ToUInt32(file, offset + 4);
                    if (type == JsonChunk) {
                        json = Encoding.UTF8.GetString(file, offset + 8, length);
                    } else if (type == BinaryChunk) {
                        glbBuffer = new byte[length];
                        Buffer.BlockCopy(file, offset + 8, glbBuffer, 0, length);
                    }
                    offset += 8 + length;
                }
                if (json == null) {
                    throw new InvalidDataException("the .glb has no JSON chunk");
                }
                gltf = JObject.Parse(json);
            } else {
                gltf = JObject.Parse(Encoding.UTF8.GetString(file));
            }

            if (gltf["extensionsRequired"] is JArray required && required.Count > 0) {
                throw new InvalidDataException("it needs extensions that aren't supported: " + string.Join(", ", required.ToObject<string[]>()));
            }

            byte[][] buffers = loadBuffers(gltf, glbBuffer, Path.GetDirectoryName(path));
            MeshBuilder builder = new MeshBuilder();

            JArray scenes = gltf["scenes"] as JArray;
            JArray nodes = gltf["nodes"] as JArray;
            if (scenes != null && nodes != null) {
                int scene = (int?)gltf["scene"] ?? 0;
                foreach (int node in ((JArray)scenes[scene]["nodes"]).ToObject<int[]>()) {
                    addNode(gltf, buffers, nodes, node, Matrix4x4.identity, builder);
                }
            } else if (gltf["meshes"] is JArray meshes) {
                // no scene: every mesh as it is
                for (int i = 0; i < meshes.Count; i++) {
                    addMesh(gltf, buffers, i, Matrix4x4.identity, builder);
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

        private static byte[][] loadBuffers(JObject gltf, byte[] glbBuffer, string folder) {
            JArray buffers = gltf["buffers"] as JArray;
            if (buffers == null) {
                return new byte[0][];
            }
            byte[][] loaded = new byte[buffers.Count][];
            for (int i = 0; i < buffers.Count; i++) {
                string uri = (string)buffers[i]["uri"];
                if (uri == null) {
                    loaded[i] = glbBuffer; // a .glb's own buffer
                } else if (uri.StartsWith("data:")) {
                    loaded[i] = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
                } else {
                    // a file next to it, never outside the model's folder
                    string file = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(uri)));
                    if (!file.StartsWith(Path.GetFullPath(folder))) {
                        throw new InvalidDataException("buffer " + uri + " is outside the model's folder");
                    }
                    loaded[i] = File.ReadAllBytes(file);
                }
            }
            return loaded;
        }

        private static void addNode(JObject gltf, byte[][] buffers, JArray nodes, int index, Matrix4x4 parent, MeshBuilder builder) {
            JToken node = nodes[index];
            Matrix4x4 matrix = parent * localMatrix(node);
            if (node["mesh"] != null) {
                addMesh(gltf, buffers, (int)node["mesh"], matrix, builder);
            }
            if (node["children"] is JArray children) {
                foreach (int child in children.ToObject<int[]>()) {
                    addNode(gltf, buffers, nodes, child, matrix, builder);
                }
            }
        }

        // in glTF's (right handed) space, mirrored when the vertices are added
        private static Matrix4x4 localMatrix(JToken node) {
            if (node["matrix"] is JArray m) {
                float[] v = m.ToObject<float[]>(); // column major
                Matrix4x4 matrix = new Matrix4x4();
                for (int i = 0; i < 16; i++) {
                    matrix[i % 4, i / 4] = v[i];
                }
                return matrix;
            }
            float[] t = node["translation"] != null ? node["translation"].ToObject<float[]>() : new float[] { 0, 0, 0 };
            float[] r = node["rotation"] != null ? node["rotation"].ToObject<float[]>() : new float[] { 0, 0, 0, 1 };
            float[] s = node["scale"] != null ? node["scale"].ToObject<float[]>() : new float[] { 1, 1, 1 };
            return Matrix4x4.TRS(new Vector3(t[0], t[1], t[2]), new Quaternion(r[0], r[1], r[2], r[3]), new Vector3(s[0], s[1], s[2]));
        }

        private static void addMesh(JObject gltf, byte[][] buffers, int meshIndex, Matrix4x4 matrix, MeshBuilder builder) {
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            foreach (JToken primitive in (JArray)gltf["meshes"][meshIndex]["primitives"]) {
                int mode = (int?)primitive["mode"] ?? 4;
                if (mode != 4) {
                    continue; // only triangle lists
                }
                JToken attributes = primitive["attributes"];
                if (attributes["POSITION"] == null) {
                    continue;
                }
                Vector3[] positions = readVectors(gltf, buffers, (int)attributes["POSITION"], 3);
                Vector3[] normals = attributes["NORMAL"] != null ? readVectors(gltf, buffers, (int)attributes["NORMAL"], 3) : null;
                Vector3[] uvs = attributes["TEXCOORD_0"] != null ? readVectors(gltf, buffers, (int)attributes["TEXCOORD_0"], 2) : null;
                int[] indexes = primitive["indices"] != null ? readIndexes(gltf, buffers, (int)primitive["indices"]) : null;
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
        private static string materialName(JObject gltf, JToken primitive) {
            if (primitive["material"] == null) {
                return "";
            }
            int index = (int)primitive["material"];
            string name = gltf["materials"] != null ? (string)gltf["materials"][index]["name"] : null;
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

        // an accessor of floats (VEC2 or VEC3) as vectors
        private static Vector3[] readVectors(JObject gltf, byte[][] buffers, int accessorIndex, int components) {
            JToken accessor = gltf["accessors"][accessorIndex];
            if ((int)accessor["componentType"] != 5126) {
                throw new InvalidDataException("only float vertex attributes are supported");
            }
            int count = (int)accessor["count"];
            byte[] data;
            int offset, stride;
            locate(gltf, buffers, accessor, components * 4, out data, out offset, out stride);
            Vector3[] result = new Vector3[count];
            for (int i = 0; i < count; i++) {
                int at = offset + i * stride;
                result[i] = new Vector3(
                    BitConverter.ToSingle(data, at),
                    BitConverter.ToSingle(data, at + 4),
                    components > 2 ? BitConverter.ToSingle(data, at + 8) : 0);
            }
            return result;
        }

        private static int[] readIndexes(JObject gltf, byte[][] buffers, int accessorIndex) {
            JToken accessor = gltf["accessors"][accessorIndex];
            int componentType = (int)accessor["componentType"];
            int size = componentType == 5121 ? 1 : componentType == 5123 ? 2 : componentType == 5125 ? 4 : 0;
            if (size == 0) {
                throw new InvalidDataException("unsupported index type " + componentType);
            }
            int count = (int)accessor["count"];
            byte[] data;
            int offset, stride;
            locate(gltf, buffers, accessor, size, out data, out offset, out stride);
            int[] result = new int[count];
            for (int i = 0; i < count; i++) {
                int at = offset + i * stride;
                result[i] = size == 1 ? data[at] : size == 2 ? BitConverter.ToUInt16(data, at) : (int)BitConverter.ToUInt32(data, at);
            }
            return result;
        }

        // where an accessor's elements are: its buffer, the first one's position and the distance between them
        private static void locate(JObject gltf, byte[][] buffers, JToken accessor, int elementSize, out byte[] data, out int offset, out int stride) {
            if (accessor["bufferView"] == null) {
                throw new InvalidDataException("accessors without a buffer view aren't supported");
            }
            if (accessor["sparse"] != null) {
                throw new InvalidDataException("sparse accessors aren't supported");
            }
            JToken view = gltf["bufferViews"][(int)accessor["bufferView"]];
            data = buffers[(int)view["buffer"]];
            if (data == null) {
                throw new InvalidDataException("a buffer is missing");
            }
            offset = ((int?)view["byteOffset"] ?? 0) + ((int?)accessor["byteOffset"] ?? 0);
            stride = (int?)view["byteStride"] ?? elementSize;
            int count = (int)accessor["count"];
            if (count > 0 && offset + (count - 1) * stride + elementSize > data.Length) {
                throw new InvalidDataException("an accessor goes past its buffer");
            }
        }
    }
}
