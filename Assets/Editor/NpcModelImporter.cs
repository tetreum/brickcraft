using System.Collections.Generic;
using System.IO;
using Brickcraft.Models;
using Brickcraft.Npcs;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Turns an animated glTF model (like Blockbench's exports: a tree of nodes with meshes, animated by their
/// translation, rotation and scale) into an NPC model: a prefab in Assets/Resources/NpcModels/[name].prefab
/// (see Npcs.NpcModel), and next to the glTF its meshes, textures, materials, an AnimationClip per animation
/// and an Animator controller with a state for each.
///
/// Like Unity's importers X is mirrored (glTF is right handed). A node named "hitbox" isn't drawn: it's
/// where the NPC can be hit. Skinned meshes aren't supported (Blockbench animates the nodes themselves).
/// Meshes of nodes no animation moves are merged into their parent's, so a model of many cubes is drawn
/// with a mesh per moving part. NPCs walk towards their +Z: Blockbench models face its north (-Z), so
/// theirs are turned around (under a "Facing" node).
/// </summary>
public static class NpcModelImporter
{
    public const string PrefabsFolder = "Assets/Resources/" + NpcInfo.ModelsFolder;
    private const string HitboxNode = "hitbox";
    // played over and over; the others once
    private static readonly string[] LoopingAnimations = { "idle", "walk", "run", "swim", "fly" };

    [MenuItem("Brickcraft/Import NPC model (glTF)...")]
    public static void ImportFromMenu() {
        string file = EditorUtility.OpenFilePanelWithFilters("NPC model", "Assets/Models/NPCs", new[] { "glTF", "gltf,glb" });
        if (string.IsNullOrEmpty(file)) {
            return;
        }
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
        file = file.Replace('\\', '/');
        if (!file.StartsWith(project)) {
            EditorUtility.DisplayDialog("NPC model", "Pick a file inside the project's Assets.", "OK");
            return;
        }
        Import(file.Substring(project.Length));
    }

    /// <summary>Imports the model (a path like Assets/Models/NPCs/Golem/Golem.gltf), returns its prefab.</summary>
    public static GameObject Import(string path) {
        GltfFile gltf = GltfFile.Load(path);
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');

        Material[] materials = importMaterials(gltf, folder, name);
        Mesh[] meshes = importMeshes(gltf, folder, name);

        GameObject root = new GameObject(name);
        try {
            Dictionary<int, string> paths = new Dictionary<int, string>();
            List<Bounds> hitboxes = new List<Bounds>();
            Transform parent = root.transform;
            string parentPath = "";
            if (facesNorth(gltf)) {
                parent = new GameObject("Facing").transform;
                parent.SetParent(root.transform, false);
                parent.localRotation = Quaternion.Euler(0, 180, 0);
                parentPath = parent.name;
            }
            foreach (int node in gltf.SceneRoots ?? new int[0]) {
                buildNode(gltf, node, parent, parentPath, root.transform, meshes, materials, paths, hitboxes);
            }

            NpcModel model = root.AddComponent<NpcModel>();
            model.hitbox = hitboxes.Count > 0 ? encapsulate(hitboxes) : rendererBounds(root);

            Animator animator = root.GetComponent<Animator>();
            animator.applyRootMotion = false;
            List<string> names = new List<string>();
            List<AnimationClip> clips = importAnimations(gltf, folder, name, paths, names);
            animator.runtimeAnimatorController = makeController(folder + "/" + name + ".controller", clips, names);
            model.animations = names.ToArray();
            model.durations = clips.ConvertAll(c => c.length).ToArray();

            HashSet<string> animated = animatedPaths(gltf, paths);
            int renderers = root.GetComponentsInChildren<MeshRenderer>().Length;
            mergeStaticParts(root.transform, root.transform, "", animated, folder + "/" + name + "_meshes.asset");

            Directory.CreateDirectory(PrefabsFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/" + name + ".prefab");
            Debug.Log("Imported the NPC model " + name + ": " + renderers + " parts drawn as " + root.GetComponentsInChildren<MeshRenderer>().Length
                + ", " + clips.Count + " animations (" + string.Join(", ", model.animations) + "), hitbox " + model.hitbox.size);
            return prefab;
        } finally {
            Object.DestroyImmediate(root);
        }
    }

    // Blockbench's front is its north, -Z
    private static bool facesNorth(GltfFile gltf) {
        string generator = (string)gltf.Json["asset"]?["generator"] ?? "";
        return generator.Contains("Blockbench");
    }

    // -------- materials --------

    private static Material[] importMaterials(GltfFile gltf, string folder, string name) {
        Texture2D[] textures = importTextures(gltf, folder, name);
        JArray list = gltf.Json["materials"] as JArray;
        Material[] materials = new Material[list != null ? list.Count : 0];
        for (int i = 0; i < materials.Length; i++) {
            JToken info = list[i];
            JToken pbr = info["pbrMetallicRoughness"];
            Material material = new Material(Shader.Find("HDRP/Lit"));
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
                material.SetTexture("_BaseColorMap", textures[image]);
            }
            material.SetFloat("_Metallic", pbr != null ? (float?)pbr["metallicFactor"] ?? 1 : 1);
            material.SetFloat("_Smoothness", 1 - (pbr != null ? (float?)pbr["roughnessFactor"] ?? 1 : 1));

            // blending is cut out too: models made of pixels have hard edges
            string alphaMode = (string)info["alphaMode"] ?? "OPAQUE";
            if (alphaMode != "OPAQUE") {
                material.SetFloat("_AlphaCutoffEnable", 1);
                material.SetFloat("_AlphaCutoff", (float?)info["alphaCutoff"] ?? 0.5f);
            }
            if ((bool?)info["doubleSided"] == true) {
                material.SetFloat("_DoubleSidedEnable", 1);
            }
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(material);

            string materialPath = folder + "/" + material.name + ".mat";
            AssetDatabase.DeleteAsset(materialPath);
            AssetDatabase.CreateAsset(material, materialPath);
            materials[i] = material;
        }
        return materials;
    }

    // saved as PNG files next to the model, unfiltered (pixel art)
    private static Texture2D[] importTextures(GltfFile gltf, string folder, string name) {
        JArray images = gltf.Json["images"] as JArray;
        Texture2D[] textures = new Texture2D[images != null ? images.Count : 0];
        for (int i = 0; i < textures.Length; i++) {
            string texturePath = folder + "/" + name + "_texture" + i + ".png";
            byte[] bytes = gltf.ReadImage(i);
            Texture2D decoded = new Texture2D(2, 2);
            decoded.LoadImage(bytes);
            File.WriteAllBytes(texturePath, decoded.EncodeToPNG());
            Object.DestroyImmediate(decoded);
            AssetDatabase.ImportAsset(texturePath);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        }
        return textures;
    }

    // -------- meshes --------

    // one asset with every mesh: a submesh per primitive
    private static Mesh[] importMeshes(GltfFile gltf, string folder, string name) {
        JArray list = gltf.Json["meshes"] as JArray;
        Mesh[] meshes = new Mesh[list != null ? list.Count : 0];
        string meshesPath = folder + "/" + name + "_meshes.asset";
        AssetDatabase.DeleteAsset(meshesPath);

        for (int m = 0; m < meshes.Length; m++) {
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
            if (m == 0) {
                AssetDatabase.CreateAsset(mesh, meshesPath);
            } else {
                AssetDatabase.AddObjectToAsset(mesh, meshesPath);
            }
            meshes[m] = mesh;
        }
        AssetDatabase.SaveAssets();
        return meshes;
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
    private static void buildNode(GltfFile gltf, int index, Transform parent, string parentPath, Transform root, Mesh[] meshes,
            Material[] materials, Dictionary<int, string> paths, List<Bounds> hitboxes) {
        JToken node = gltf.Nodes[index];
        string name = uniqueName(parent, (string)node["name"] ?? "node" + index);
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        GltfFile.ReadTrs(node, out Vector3 t, out Quaternion r, out Vector3 s);
        go.transform.localPosition = mirror(t);
        go.transform.localRotation = mirror(r);
        go.transform.localScale = s;

        string path = parentPath.Length > 0 ? parentPath + "/" + name : name;
        paths[index] = path;

        if (node["mesh"] != null) {
            Mesh mesh = meshes[(int)node["mesh"]];
            if (isHitbox(go.transform, root)) {
                hitboxes.Add(transformBounds(mesh.bounds, root.worldToLocalMatrix * go.transform.localToWorldMatrix));
            } else {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
                List<Material> used = new List<Material>();
                foreach (JToken primitive in (JArray)gltf.Json["meshes"][(int)node["mesh"]]["primitives"]) {
                    used.Add(primitive["material"] != null ? materials[(int)primitive["material"]] : null);
                }
                meshRenderer.sharedMaterials = used.ToArray();
            }
        }
        if (node["children"] is JArray children) {
            foreach (int child in children.ToObject<int[]>()) {
                buildNode(gltf, child, go.transform, path, root, meshes, materials, paths, hitboxes);
            }
        }
    }

    // the "hitbox" node, or a mesh in it (Blockbench puts its cube in a child)
    private static bool isHitbox(Transform node, Transform root) {
        for (Transform t = node; t != null && t != root; t = t.parent) {
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
    private static HashSet<string> animatedPaths(GltfFile gltf, Dictionary<int, string> paths) {
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
    private static void mergeStaticParts(Transform node, Transform root, string path, HashSet<string> animated, string meshesPath) {
        List<Transform> children = new List<Transform>();
        foreach (Transform child in node) {
            children.Add(child);
        }
        List<MeshFilter> parts = new List<MeshFilter>();
        foreach (Transform child in children) {
            string childPath = path.Length > 0 ? path + "/" + child.name : child.name;
            mergeStaticParts(child, root, childPath, animated, meshesPath);
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
        Mesh mesh = new Mesh() { name = Path.GetFileNameWithoutExtension(meshesPath) + "_" + (path.Length > 0 ? path.Replace('/', '_') : "root") };
        // before the triangles go in: changing it afterwards drops them
        int vertices = 0;
        foreach (Mesh part in temporary) {
            vertices += part.vertexCount;
        }
        mesh.indexFormat = vertices > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.CombineMeshes(submeshes.ToArray(), false, false);
        mesh.RecalculateBounds();
        AssetDatabase.AddObjectToAsset(mesh, meshesPath);
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

    // names gets each clip's name in the glTF (saving a clip renames it after its file)
    private static List<AnimationClip> importAnimations(GltfFile gltf, string folder, string name, Dictionary<int, string> paths, List<string> names) {
        List<AnimationClip> clips = new List<AnimationClip>();
        JArray animations = gltf.Json["animations"] as JArray;
        if (animations == null) {
            return clips;
        }
        for (int a = 0; a < animations.Count; a++) {
            JToken animation = animations[a];
            AnimationClip clip = new AnimationClip();
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

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = System.Array.IndexOf(LoopingAnimations, clip.name.ToLowerInvariant()) >= 0;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string clipPath = folder + "/" + name + "_" + clip.name + ".anim";
            AssetDatabase.DeleteAsset(clipPath);
            string animationName = clip.name;
            AssetDatabase.CreateAsset(clip, clipPath);
            clips.Add(clip);
            names.Add(animationName);
        }
        return clips;
    }

    private static void addCurves(AnimationClip clip, string path, string property, float[] times, Vector4[] values, bool step) {
        string unityProperty = property == "translation" ? "m_LocalPosition" : property == "rotation" ? "m_LocalRotation" : "m_LocalScale";
        string[] components = property == "rotation" ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" };
        AnimationCurve[] curves = new AnimationCurve[components.Length];
        for (int c = 0; c < curves.Length; c++) {
            curves[c] = new AnimationCurve();
        }
        Quaternion previous = Quaternion.identity;
        for (int i = 0; i < times.Length && i < values.Length; i++) {
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
            for (int c = 0; c < curves.Length; c++) {
                curves[c].AddKey(new Keyframe(times[i], value[c]));
            }
        }
        for (int c = 0; c < curves.Length; c++) {
            AnimationUtility.TangentMode mode = step ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear;
            for (int k = 0; k < curves[c].length; k++) {
                AnimationUtility.SetKeyLeftTangentMode(curves[c], k, mode);
                AnimationUtility.SetKeyRightTangentMode(curves[c], k, mode);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), unityProperty + "." + components[c]), curves[c]);
        }
    }

    // a state per clip, played by name (see Npcs.Npc); idle first if there's one
    private static AnimatorController makeController(string path, List<AnimationClip> clips, List<string> names) {
        AssetDatabase.DeleteAsset(path);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        for (int i = 0; i < clips.Count; i++) {
            AnimatorState state = machine.AddState(names[i]);
            state.motion = clips[i];
            if (names[i].ToLowerInvariant() == "idle") {
                machine.defaultState = state;
            }
        }
        AssetDatabase.SaveAssets();
        return controller;
    }
}
