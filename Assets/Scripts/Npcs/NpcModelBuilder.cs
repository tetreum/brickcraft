using System.Collections.Generic;
using Brickcraft.Models;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Turns an animated glTF model (like Blockbench's exports: a tree of nodes with meshes, animated by their
    /// translation, rotation and scale) into an NPC model (see NpcModel): its meshes, textures (unfiltered, for
    /// pixel art), materials (copies of Resources/Materials/NpcModel*) and an AnimationClip per animation.
    /// Used while the game runs for models in NPC folders (model.glb or model.gltf, see NpcDatabase), whose
    /// clips play on an Animation component, and by the editor's importer (Brickcraft > Import NPC model),
    /// which saves it all as assets and plays them with an Animator.
    ///
    /// Like Unity's importers X is mirrored (glTF is right handed). A node named "hitbox" isn't drawn: it's
    /// where the NPC can be hit. Skinned meshes aren't supported (Blockbench animates the nodes themselves).
    /// Meshes of nodes no animation moves are merged into their parent's, so a model of many cubes is drawn
    /// with a mesh per moving part. NPCs walk towards their +Z: Blockbench models face its north (-Z), so
    /// theirs are turned around (under a "Facing" node).
    /// </summary>
    public class NpcModelBuilder
    {
        public const string HitboxNode = "hitbox";
        /// <summary>Played over and over; the others once.</summary>
        public static readonly string[] LoopingAnimations = { "idle", "walk", "run", "swim", "fly" };

        public GameObject Root { get; private set; }
        public readonly List<Texture2D> Textures = new List<Texture2D>();
        public readonly List<Material> Materials = new List<Material>();
        /// <summary>Every mesh it made, the merged ones too.</summary>
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<AnimationClip> Clips = new List<AnimationClip>();

        private readonly GltfFile gltf;
        private readonly string name;
        private readonly bool legacy;
        private Mesh[] nodeMeshes;
        private readonly Dictionary<int, string> paths = new Dictionary<int, string>();
        private readonly List<Bounds> hitboxes = new List<Bounds>();

        private NpcModelBuilder(GltfFile gltf, string name, bool legacy) {
            this.gltf = gltf;
            this.name = name;
            this.legacy = legacy;
        }

        /// <summary>
        /// Builds the model, its root named name. legacy: clips for an Animation component on the root (built
        /// while the game runs); otherwise for an Animator the caller adds (the editor's importer).
        /// </summary>
        public static NpcModelBuilder Build(GltfFile gltf, string name, bool legacy) {
            NpcModelBuilder builder = new NpcModelBuilder(gltf, name, legacy);
            builder.build();
            return builder;
        }

        private void build() {
            buildMaterials();
            buildMeshes();

            Root = new GameObject(name);
            Transform parent = Root.transform;
            string parentPath = "";
            if (facesNorth()) {
                parent = new GameObject("Facing").transform;
                parent.SetParent(Root.transform, false);
                parent.localRotation = Quaternion.Euler(0, 180, 0);
                parentPath = parent.name;
            }
            foreach (int node in gltf.SceneRoots ?? new int[0]) {
                buildNode(node, parent, parentPath);
            }

            List<string> names = new List<string>();
            buildAnimations(names);
            mergeStaticParts(Root.transform, "", animatedPaths());

            NpcModel model = Root.AddComponent<NpcModel>();
            model.hitbox = hitboxes.Count > 0 ? encapsulate(hitboxes) : rendererBounds(Root);
            model.animations = names.ToArray();
            model.durations = Clips.ConvertAll(c => c.length).ToArray();
            if (legacy) {
                Animation animation = Root.AddComponent<Animation>();
                animation.playAutomatically = false;
                for (int i = 0; i < Clips.Count; i++) {
                    animation.AddClip(Clips[i], names[i]);
                }
            }
        }

        // Blockbench's front is its north, -Z
        private bool facesNorth() {
            string generator = (string)gltf.Json["asset"]?["generator"] ?? "";
            return generator.Contains("Blockbench");
        }

        // -------- materials --------

        private void buildMaterials() {
            JArray images = gltf.Json["images"] as JArray;
            for (int i = 0; images != null && i < images.Count; i++) {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(gltf.ReadImage(i));
                texture.name = name + "_texture" + i;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                Textures.Add(texture);
            }

            Material single = Resources.Load<Material>("Materials/NpcModel");
            Material doubleSided = Resources.Load<Material>("Materials/NpcModelDoubleSided");
            JArray list = gltf.Json["materials"] as JArray;
            for (int i = 0; list != null && i < list.Count; i++) {
                JToken info = list[i];
                JToken pbr = info["pbrMetallicRoughness"];
                Material material = new Material((bool?)info["doubleSided"] == true ? doubleSided : single);
                material.name = name + "_" + i;

                Color color = Color.white;
                if (pbr != null && pbr["baseColorFactor"] is JArray factor) {
                    float[] c = factor.ToObject<float[]>();
                    color = new Color(c[0], c[1], c[2], c[3]);
                }
                material.SetColor("_BaseColor", color);
                if (pbr != null && pbr["baseColorTexture"] != null) {
                    int texture = (int)pbr["baseColorTexture"]["index"];
                    int image = (int)gltf.Json["textures"][texture]["source"];
                    material.SetTexture("_BaseColorMap", Textures[image]);
                }
                material.SetFloat("_Metallic", pbr != null ? (float?)pbr["metallicFactor"] ?? 1 : 1);
                material.SetFloat("_Smoothness", 1 - (pbr != null ? (float?)pbr["roughnessFactor"] ?? 1 : 1));
                // blending is cut out too: models made of pixels have hard edges
                string alphaMode = (string)info["alphaMode"] ?? "OPAQUE";
                material.SetFloat("_AlphaCutoff", alphaMode != "OPAQUE" ? (float?)info["alphaCutoff"] ?? 0.5f : 0.01f);
                Materials.Add(material);
            }
        }

        // -------- meshes --------

        // a mesh per glTF mesh, a submesh per primitive
        private void buildMeshes() {
            JArray list = gltf.Json["meshes"] as JArray;
            nodeMeshes = new Mesh[list != null ? list.Count : 0];
            for (int m = 0; m < nodeMeshes.Length; m++) {
                List<Vector3> vertices = new List<Vector3>();
                List<Vector3> normals = new List<Vector3>();
                List<Vector2> uvs = new List<Vector2>();
                List<List<int>> submeshes = new List<List<int>>();
                foreach (JToken primitive in (JArray)list[m]["primitives"]) {
                    JToken attributes = primitive["attributes"];
                    if (((int?)primitive["mode"] ?? 4) != 4 || attributes["POSITION"] == null) {
                        continue;
                    }
                    Vector3[] positions = gltf.ReadVectors((int)attributes["POSITION"], 3);
                    Vector3[] primitiveNormals = attributes["NORMAL"] != null ? gltf.ReadVectors((int)attributes["NORMAL"], 3) : null;
                    Vector3[] primitiveUvs = attributes["TEXCOORD_0"] != null ? gltf.ReadVectors((int)attributes["TEXCOORD_0"], 2) : null;
                    int[] indexes = primitive["indices"] != null ? gltf.ReadIndexes((int)primitive["indices"]) : sequence(positions.Length);

                    int first = vertices.Count;
                    for (int i = 0; i < positions.Length; i++) {
                        vertices.Add(mirror(positions[i]));
                        normals.Add(primitiveNormals != null ? mirror(primitiveNormals[i]) : Vector3.up);
                        // glTF's v goes down, Unity's up
                        uvs.Add(primitiveUvs != null ? new Vector2(primitiveUvs[i].x, 1 - primitiveUvs[i].y) : Vector2.zero);
                    }
                    List<int> triangles = new List<int>();
                    for (int i = 0; i + 2 < indexes.Length; i += 3) {
                        // mirrored, so the other way around
                        triangles.Add(first + indexes[i]);
                        triangles.Add(first + indexes[i + 2]);
                        triangles.Add(first + indexes[i + 1]);
                    }
                    submeshes.Add(triangles);
                }

                Mesh mesh = new Mesh();
                mesh.name = name + "_mesh" + m;
                mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = Mathf.Max(1, submeshes.Count);
                for (int s = 0; s < submeshes.Count; s++) {
                    mesh.SetTriangles(submeshes[s], s);
                }
                mesh.RecalculateBounds();
                nodeMeshes[m] = mesh;
                Meshes.Add(mesh);
            }
        }

        private static int[] sequence(int count) {
            int[] values = new int[count];
            for (int i = 0; i < count; i++) {
                values[i] = i;
            }
            return values;
        }

        // -------- nodes --------

        // a GameObject per node, named after it (unique among its siblings, so animations find it)
        private void buildNode(int index, Transform parent, string parentPath) {
            JToken node = gltf.Nodes[index];
            string nodeName = uniqueName(parent, (string)node["name"] ?? "node" + index);
            GameObject go = new GameObject(nodeName);
            go.transform.SetParent(parent, false);
            GltfFile.ReadTrs(node, out Vector3 t, out Quaternion r, out Vector3 s);
            go.transform.localPosition = mirror(t);
            go.transform.localRotation = mirror(r);
            go.transform.localScale = s;

            string path = parentPath.Length > 0 ? parentPath + "/" + nodeName : nodeName;
            paths[index] = path;

            if (node["mesh"] != null) {
                Mesh mesh = nodeMeshes[(int)node["mesh"]];
                if (isHitbox(go.transform)) {
                    hitboxes.Add(transformBounds(mesh.bounds, Root.transform.worldToLocalMatrix * go.transform.localToWorldMatrix));
                } else {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
                    List<Material> used = new List<Material>();
                    foreach (JToken primitive in (JArray)gltf.Json["meshes"][(int)node["mesh"]]["primitives"]) {
                        used.Add(primitive["material"] != null ? Materials[(int)primitive["material"]] : null);
                    }
                    meshRenderer.sharedMaterials = used.ToArray();
                }
            }
            if (node["children"] is JArray children) {
                foreach (int child in children.ToObject<int[]>()) {
                    buildNode(child, go.transform, path);
                }
            }
        }

        // the "hitbox" node, or a mesh in it (Blockbench puts its cube in a child)
        private bool isHitbox(Transform node) {
            for (Transform t = node; t != null && t != Root.transform; t = t.parent) {
                if (t.name.ToLowerInvariant() == HitboxNode) {
                    return true;
                }
            }
            return false;
        }

        private static string uniqueName(Transform parent, string name) {
            string unique = name;
            for (int n = 1; parent.Find(unique) != null; n++) {
                unique = name + "_" + n;
            }
            return unique;
        }

        private static Vector3 mirror(Vector3 v) {
            return new Vector3(-v.x, v.y, v.z);
        }

        private static Quaternion mirror(Quaternion q) {
            return new Quaternion(q.x, -q.y, -q.z, q.w);
        }

        private static Bounds transformBounds(Bounds bounds, Matrix4x4 matrix) {
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++) {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }
            return result;
        }

        private static Bounds encapsulate(List<Bounds> list) {
            Bounds result = list[0];
            foreach (Bounds bounds in list) {
                result.Encapsulate(bounds);
            }
            return result;
        }

        private static Bounds rendererBounds(GameObject root) {
            List<Bounds> list = new List<Bounds>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>()) {
                list.Add(transformBounds(filter.sharedMesh.bounds, root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix));
            }
            return list.Count > 0 ? encapsulate(list) : new Bounds(Vector3.up, Vector3.one * 2);
        }

        // -------- merging --------

        // the nodes some animation moves
        private HashSet<string> animatedPaths() {
            HashSet<string> animated = new HashSet<string>();
            if (gltf.Json["animations"] is JArray animations) {
                foreach (JToken animation in animations) {
                    foreach (JToken channel in (JArray)animation["channels"]) {
                        JToken node = channel["target"]["node"];
                        if (node != null && paths.TryGetValue((int)node, out string path)) {
                            animated.Add(path);
                        }
                    }
                }
            }
            return animated;
        }

        // children with a mesh, no children and no animation of their own become one mesh of their parent
        // (a submesh per material), on a child "Parts"; nodes deeper down first
        private void mergeStaticParts(Transform node, string path, HashSet<string> animated) {
            List<Transform> children = new List<Transform>();
            foreach (Transform child in node) {
                children.Add(child);
            }
            List<MeshFilter> parts = new List<MeshFilter>();
            foreach (Transform child in children) {
                string childPath = path.Length > 0 ? path + "/" + child.name : child.name;
                mergeStaticParts(child, childPath, animated);
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter != null && child.childCount == 0 && !animated.Contains(childPath)) {
                    parts.Add(filter);
                }
            }
            if (parts.Count < 2) {
                return;
            }

            // the triangles of each material, placed in this node's space
            List<Material> materials = new List<Material>();
            List<List<CombineInstance>> byMaterial = new List<List<CombineInstance>>();
            foreach (MeshFilter part in parts) {
                Material[] partMaterials = part.GetComponent<MeshRenderer>().sharedMaterials;
                Matrix4x4 matrix = node.worldToLocalMatrix * part.transform.localToWorldMatrix;
                for (int s = 0; s < part.sharedMesh.subMeshCount; s++) {
                    Material material = s < partMaterials.Length ? partMaterials[s] : null;
                    int index = materials.IndexOf(material);
                    if (index < 0) {
                        index = materials.Count;
                        materials.Add(material);
                        byMaterial.Add(new List<CombineInstance>());
                    }
                    byMaterial[index].Add(new CombineInstance() { mesh = part.sharedMesh, subMeshIndex = s, transform = matrix });
                }
            }
            List<CombineInstance> submeshes = new List<CombineInstance>();
            List<Mesh> temporary = new List<Mesh>();
            foreach (List<CombineInstance> instances in byMaterial) {
                Mesh merged = new Mesh() { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                merged.CombineMeshes(instances.ToArray(), true, true);
                temporary.Add(merged);
                submeshes.Add(new CombineInstance() { mesh = merged, transform = Matrix4x4.identity });
            }
            Mesh mesh = new Mesh() { name = name + "_meshes_" + (path.Length > 0 ? path.Replace('/', '_') : "root") };
            // before the triangles go in: changing it afterwards drops them
            int vertices = 0;
            foreach (Mesh part in temporary) {
                vertices += part.vertexCount;
            }
            mesh.indexFormat = vertices > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.CombineMeshes(submeshes.ToArray(), false, false);
            mesh.RecalculateBounds();
            Meshes.Add(mesh);
            foreach (Mesh part in temporary) {
                Object.DestroyImmediate(part);
            }

            GameObject partsObject = new GameObject("Parts");
            partsObject.transform.SetParent(node, false);
            partsObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            partsObject.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            foreach (MeshFilter part in parts) {
                Object.DestroyImmediate(part.gameObject);
            }
        }

        // -------- animations --------

        // names gets each clip's name in the glTF
        private void buildAnimations(List<string> names) {
            JArray animations = gltf.Json["animations"] as JArray;
            if (animations == null) {
                return;
            }
            for (int a = 0; a < animations.Count; a++) {
                JToken animation = animations[a];
                AnimationClip clip = new AnimationClip() { legacy = legacy };
                clip.name = (string)animation["name"] ?? "animation" + a;
                JArray samplers = (JArray)animation["samplers"];

                foreach (JToken channel in (JArray)animation["channels"]) {
                    JToken target = channel["target"];
                    if (target["node"] == null || !paths.TryGetValue((int)target["node"], out string path)) {
                        continue;
                    }
                    JToken sampler = samplers[(int)channel["sampler"]];
                    string interpolation = (string)sampler["interpolation"] ?? "LINEAR";
                    float[] times = System.Array.ConvertAll(gltf.ReadFloats((int)sampler["input"], 1), v => v.x);
                    string property = (string)target["path"];
                    int components = property == "rotation" ? 4 : 3;
                    if (property != "translation" && property != "rotation" && property != "scale") {
                        continue; // morph weights
                    }
                    Vector4[] values = gltf.ReadFloats((int)sampler["output"], components);
                    // cubic splines store in-tangent, value, out-tangent: only the value is used
                    if (interpolation == "CUBICSPLINE") {
                        Vector4[] middle = new Vector4[times.Length];
                        for (int i = 0; i < middle.Length; i++) {
                            middle[i] = values[i * 3 + 1];
                        }
                        values = middle;
                    }
                    addCurves(clip, path, property, times, values, interpolation == "STEP");
                }
                clip.EnsureQuaternionContinuity();
                bool loops = System.Array.IndexOf(LoopingAnimations, clip.name.ToLowerInvariant()) >= 0;
                if (legacy) {
                    // the others stay on their last frame (death)
                    clip.wrapMode = loops ? WrapMode.Loop : WrapMode.ClampForever;
                }
                names.Add(clip.name);
                Clips.Add(clip);
            }
        }

        private static void addCurves(AnimationClip clip, string path, string property, float[] times, Vector4[] values, bool step) {
            string unityProperty = property == "translation" ? "localPosition" : property == "rotation" ? "localRotation" : "localScale";
            string[] components = property == "rotation" ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" };
            int count = Mathf.Min(times.Length, values.Length);
            Quaternion previous = Quaternion.identity;
            Vector4[] keys = new Vector4[count];
            for (int i = 0; i < count; i++) {
                Vector4 value = values[i];
                if (property == "translation") {
                    value = mirror((Vector3)value);
                } else if (property == "rotation") {
                    Quaternion q = mirror(new Quaternion(value.x, value.y, value.z, value.w));
                    // the shortest way from the previous key
                    if (i > 0 && Quaternion.Dot(previous, q) < 0) {
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    }
                    previous = q;
                    value = new Vector4(q.x, q.y, q.z, q.w);
                }
                keys[i] = value;
            }
            for (int c = 0; c < components.Length; c++) {
                // straight lines between keys (or steps)
                Keyframe[] frames = new Keyframe[count];
                for (int i = 0; i < count; i++) {
                    float inTangent = step ? float.PositiveInfinity : i > 0 ? slope(times, keys, c, i - 1) : 0;
                    float outTangent = step ? float.PositiveInfinity : i + 1 < count ? slope(times, keys, c, i) : 0;
                    frames[i] = new Keyframe(times[i], keys[i][c], inTangent, outTangent);
                }
                clip.SetCurve(path, typeof(Transform), unityProperty + "." + components[c], new AnimationCurve(frames));
            }
        }

        private static float slope(float[] times, Vector4[] keys, int component, int from) {
            float duration = times[from + 1] - times[from];
            return duration > 0 ? (keys[from + 1][component] - keys[from][component]) / duration : 0;
        }
    }
}
