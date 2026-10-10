using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Brickcraft.Models
{
    /// <summary>
    /// A glTF 2.0 file (.glb, or .gltf with its buffers and images embedded or in files next to it) read
    /// right away: its JSON, its buffers, and its accessors as plain arrays. What to build from it is up
    /// to who reads it (Bricks.GltfModels makes one mesh of it, the NPC importer a whole model).
    ///
    /// Values are in glTF's (right handed) space: like Unity's importers, readers mirror X (positions and
    /// normals (-x, y, z), rotations (x, -y, -z, w)) and turn triangles the other way. Compressed meshes
    /// (Draco, meshopt), sparse accessors and required extensions aren't supported.
    /// </summary>
    public class GltfFile
    {
        private const uint GlbMagic = 0x46546C67; // "glTF"
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinaryChunk = 0x004E4942;

        public const int FloatType = 5126;

        public JObject Json { get; private set; }
        private byte[][] buffers;
        private string folder;

        public static GltfFile Load(string path) {
            byte[] file = File.ReadAllBytes(path);
            GltfFile gltf = new GltfFile() { folder = Path.GetDirectoryName(path) };
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
                gltf.Json = JObject.Parse(json);
            } else {
                gltf.Json = JObject.Parse(Encoding.UTF8.GetString(file));
            }

            if (gltf.Json["extensionsRequired"] is JArray required && required.Count > 0) {
                throw new InvalidDataException("it needs extensions that aren't supported: " + string.Join(", ", required.ToObject<string[]>()));
            }
            gltf.loadBuffers(glbBuffer);
            return gltf;
        }

        public JArray Nodes {
            get { return Json["nodes"] as JArray; }
        }

        /// <summary>The root nodes of the default scene, null if it has no scene.</summary>
        public int[] SceneRoots {
            get {
                JArray scenes = Json["scenes"] as JArray;
                if (scenes == null || Nodes == null) {
                    return null;
                }
                int scene = (int?)Json["scene"] ?? 0;
                return ((JArray)scenes[scene]["nodes"]).ToObject<int[]>();
            }
        }

        private void loadBuffers(byte[] glbBuffer) {
            JArray list = Json["buffers"] as JArray;
            buffers = new byte[list != null ? list.Count : 0][];
            for (int i = 0; i < buffers.Length; i++) {
                string uri = (string)list[i]["uri"];
                buffers[i] = uri == null ? glbBuffer : readUri(uri); // no uri: a .glb's own buffer
            }
        }

        // embedded (data:) or a file next to it, never outside the model's folder
        private byte[] readUri(string uri) {
            if (uri.StartsWith("data:")) {
                return Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
            }
            string file = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(uri)));
            if (!file.StartsWith(Path.GetFullPath(folder))) {
                throw new InvalidDataException(uri + " is outside the model's folder");
            }
            return File.ReadAllBytes(file);
        }

        /// <summary>The bytes of an image (PNG or JPEG), embedded, in a buffer or in a file next to it.</summary>
        public byte[] ReadImage(int imageIndex) {
            JToken image = Json["images"][imageIndex];
            if (image["uri"] != null) {
                return readUri((string)image["uri"]);
            }
            JToken view = Json["bufferViews"][(int)image["bufferView"]];
            byte[] data = buffers[(int)view["buffer"]];
            byte[] bytes = new byte[(int)view["byteLength"]];
            Buffer.BlockCopy(data, (int?)view["byteOffset"] ?? 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>A node's transform relative to its parent, in glTF's space.</summary>
        public static Matrix4x4 LocalMatrix(JToken node) {
            if (node["matrix"] is JArray m) {
                float[] v = m.ToObject<float[]>(); // column major
                Matrix4x4 matrix = new Matrix4x4();
                for (int i = 0; i < 16; i++) {
                    matrix[i % 4, i / 4] = v[i];
                }
                return matrix;
            }
            ReadTrs(node, out Vector3 t, out Quaternion r, out Vector3 s);
            return Matrix4x4.TRS(t, r, s);
        }

        /// <summary>A node's translation, rotation and scale, in glTF's space (a matrix is split up).</summary>
        public static void ReadTrs(JToken node, out Vector3 translation, out Quaternion rotation, out Vector3 scale) {
            if (node["matrix"] is JArray) {
                Matrix4x4 matrix = LocalMatrix(node);
                translation = matrix.GetColumn(3);
                rotation = matrix.rotation;
                scale = matrix.lossyScale;
                return;
            }
            float[] t = node["translation"] != null ? node["translation"].ToObject<float[]>() : new float[] { 0, 0, 0 };
            float[] r = node["rotation"] != null ? node["rotation"].ToObject<float[]>() : new float[] { 0, 0, 0, 1 };
            float[] s = node["scale"] != null ? node["scale"].ToObject<float[]>() : new float[] { 1, 1, 1 };
            translation = new Vector3(t[0], t[1], t[2]);
            rotation = new Quaternion(r[0], r[1], r[2], r[3]);
            scale = new Vector3(s[0], s[1], s[2]);
        }

        /// <summary>An accessor of floats (SCALAR to VEC4) as vectors, missing components 0.</summary>
        public Vector4[] ReadFloats(int accessorIndex, int components) {
            JToken accessor = Json["accessors"][accessorIndex];
            if ((int)accessor["componentType"] != FloatType) {
                throw new InvalidDataException("only float attributes are supported");
            }
            int count = (int)accessor["count"];
            locate(accessor, components * 4, out byte[] data, out int offset, out int stride);
            Vector4[] result = new Vector4[count];
            for (int i = 0; i < count; i++) {
                int at = offset + i * stride;
                Vector4 value = Vector4.zero;
                for (int c = 0; c < components; c++) {
                    value[c] = BitConverter.ToSingle(data, at + c * 4);
                }
                result[i] = value;
            }
            return result;
        }

        /// <summary>An accessor of floats (VEC2 or VEC3) as vectors.</summary>
        public Vector3[] ReadVectors(int accessorIndex, int components) {
            Vector4[] values = ReadFloats(accessorIndex, components);
            Vector3[] result = new Vector3[values.Length];
            for (int i = 0; i < values.Length; i++) {
                result[i] = values[i];
            }
            return result;
        }

        public int[] ReadIndexes(int accessorIndex) {
            JToken accessor = Json["accessors"][accessorIndex];
            int componentType = (int)accessor["componentType"];
            int size = componentType == 5121 ? 1 : componentType == 5123 ? 2 : componentType == 5125 ? 4 : 0;
            if (size == 0) {
                throw new InvalidDataException("unsupported index type " + componentType);
            }
            int count = (int)accessor["count"];
            locate(accessor, size, out byte[] data, out int offset, out int stride);
            int[] result = new int[count];
            for (int i = 0; i < count; i++) {
                int at = offset + i * stride;
                result[i] = size == 1 ? data[at] : size == 2 ? BitConverter.ToUInt16(data, at) : (int)BitConverter.ToUInt32(data, at);
            }
            return result;
        }

        // where an accessor's elements are: its buffer, the first one's position and the distance between them
        private void locate(JToken accessor, int elementSize, out byte[] data, out int offset, out int stride) {
            if (accessor["bufferView"] == null) {
                throw new InvalidDataException("accessors without a buffer view aren't supported");
            }
            if (accessor["sparse"] != null) {
                throw new InvalidDataException("sparse accessors aren't supported");
            }
            JToken view = Json["bufferViews"][(int)accessor["bufferView"]];
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
