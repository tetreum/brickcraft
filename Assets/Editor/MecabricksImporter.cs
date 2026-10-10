using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft;
using Brickcraft.Bricks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a brick model exported from Mecabricks (a folder with config.json, geometry.json and its
/// normals image) into a mesh and a prefab like the other bricks' (Assets/Models/Bricks/[id]/[id].prefab):
/// pivot at the center of its bottom, a box collider of its size, studs where config.json puts them,
/// and its normal map (see BrickNormalMap), and the icons of the items made of it (see BrickIconRenderer).
/// It's added to the Server prefab's list; the brick's size
/// still has to be added to Server.setupBrickModels (the log says what to add).
///
/// geometry.json is three.js's JSON model format (version 3): vertices, normals and uvs in flat lists,
/// faces as a type with bit flags followed by its indices. Mecabricks works in millimeters: a stud is
/// 8 wide and a brick 9.6 high, the game's 0.398 and 0.478.
/// </summary>
public static class MecabricksImporter
{
    private const float Scale = Server.studSize / 8f;
    private const float StudRadius = 2.4f;
    private const float StudHeight = 1.8f;
    private const int StudSegments = 24;
    private const string ServerPrefab = "Assets/Prefabs/Server.prefab";
    private const string DefaultMaterial = "Assets/Materials/BrickColors/Palette/71_LightBluishGray.mat";

    [MenuItem("Brickcraft/Import Mecabricks model...")]
    public static void ImportFromMenu() {
        string folder = EditorUtility.OpenFolderPanel("Mecabricks model (config.json and geometry.json)", "Assets/Models/Bricks", "");
        if (string.IsNullOrEmpty(folder)) {
            return;
        }
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
        folder = folder.Replace('\\', '/');
        if (!folder.StartsWith(project)) {
            EditorUtility.DisplayDialog("Mecabricks model", "Pick a folder inside the project's Assets.", "OK");
            return;
        }
        Import(folder.Substring(project.Length));
    }

    /// <summary>Imports the model in the folder (a path like Assets/Models/Bricks/3009), returns its prefab.</summary>
    public static GameObject Import(string folder) {
        JObject config = JObject.Parse(File.ReadAllText(Path.Combine(folder, "config.json")));
        JObject geometry = JObject.Parse(File.ReadAllText(Path.Combine(folder, "geometry.json")));
        string name = (string)config["name"];

        // the normal map, applied with the first uv set
        Texture2D normalMap = null;
        Vector2 flatUv = new Vector2(0.5f, 0.5f);
        JArray normals = config["normals"] as JArray;
        if (normals != null && normals.Count > 0) {
            string normalPath = folder + "/" + (string)normals[0]["file"];
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            if (importer != null) {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = (bool?)normals[0]["repeat"] == true ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                flatUv = findFlatUv(normalPath);
            }
        }

        MeshData data = new MeshData();
        readGeometry(geometry, data);
        float bodyHeight = data.maxY;
        JArray knobs = config["geometry"]["extras"]["knobs"] as JArray;
        if (knobs != null) {
            foreach (JToken knob in knobs) {
                addStud(data, knob["transform"], flatUv);
            }
        }

        Mesh mesh = saveMesh(folder + "/" + name + ".asset", name, data);

        // its size in the grid
        Bounds bounds = mesh.bounds;
        int width = Mathf.RoundToInt(bounds.size.x / Server.studSize);
        int depth = Mathf.RoundToInt(bounds.size.z / Server.studSize);
        int plates = Mathf.Max(1, Mathf.RoundToInt(bodyHeight / Server.plateHeight));

        GameObject prefab = savePrefab(folder + "/" + name + ".prefab", name, mesh, normalMap, width, depth, plates);
        register(prefab);

        // the icons of the items made of it, in their colours
        int model;
        if (int.TryParse(name, out model)) {
            int icons = BrickIconRenderer.GenerateForModel(model);
            if (icons > 0) {
                Debug.Log("Rendered the icons of " + icons + " item(s) made of " + name);
            }
        }

        Debug.Log("Imported the Mecabricks model " + name + ": " + width + "x" + depth + ", " + plates + " plates high, "
            + mesh.vertexCount + " vertices. Add its size to Server.setupBrickModels if it isn't there: addBrickModel("
            + name + ", BrickModel.Category." + (plates >= 3 ? "Brick" : "Plate") + ", " + width + ", " + depth + ", " + plates + ");");
        return prefab;
    }

    private class MeshData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uv0 = new List<Vector2>();
        public readonly List<Vector2> uv1 = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();
        public float maxY = float.MinValue;
    }

    // three.js is right handed and Unity left handed: z is mirrored, so triangles turn the other way
    private static Vector3 toUnity(float x, float y, float z) {
        return new Vector3(x, y, -z);
    }

    private static void readGeometry(JObject geometry, MeshData data) {
        float[] positions = geometry["vertices"].ToObject<float[]>();
        float[] normals = geometry["normals"].ToObject<float[]>();
        int[] faces = geometry["faces"].ToObject<int[]>();
        JArray uvLayers = (JArray)geometry["uvs"];
        float[][] uvs = new float[uvLayers.Count][];
        for (int i = 0; i < uvs.Length; i++) {
            uvs[i] = uvLayers[i].ToObject<float[]>();
        }

        // corners that share position, normal and uvs are one vertex
        Dictionary<string, int> corners = new Dictionary<string, int>();
        int[] vertex = new int[4], normal = new int[4];
        int[][] uv = { new int[4], new int[4] };

        for (int i = 0; i < faces.Length;) {
            int type = faces[i++];
            int count = (type & 1) != 0 ? 4 : 3;
            for (int c = 0; c < count; c++) {
                vertex[c] = faces[i++];
            }
            if ((type & 2) != 0) {
                i++; // material
            }
            if ((type & 4) != 0) {
                i += uvs.Length; // face uv
            }
            for (int c = 0; c < count; c++) {
                normal[c] = -1;
                uv[0][c] = uv[1][c] = -1;
            }
            if ((type & 8) != 0) {
                for (int layer = 0; layer < uvs.Length; layer++) {
                    for (int c = 0; c < count; c++) {
                        int index = faces[i++];
                        if (layer < 2) {
                            uv[layer][c] = index;
                        }
                    }
                }
            }
            int faceNormal = -1;
            if ((type & 16) != 0) {
                faceNormal = faces[i++];
            }
            if ((type & 32) != 0) {
                for (int c = 0; c < count; c++) {
                    normal[c] = faces[i++];
                }
            }
            if ((type & 64) != 0) {
                i++; // face color
            }
            if ((type & 128) != 0) {
                i += count; // vertex colors
            }

            int[] indexes = new int[count];
            for (int c = 0; c < count; c++) {
                int n = normal[c] >= 0 ? normal[c] : faceNormal;
                string key = vertex[c] + "/" + n + "/" + uv[0][c] + "/" + uv[1][c];
                int index;
                if (!corners.TryGetValue(key, out index)) {
                    index = data.vertices.Count;
                    corners[key] = index;
                    int p = vertex[c] * 3;
                    data.vertices.Add(toUnity(positions[p], positions[p + 1], positions[p + 2]) * Scale);
                    data.maxY = Mathf.Max(data.maxY, positions[p + 1] * Scale);
                    data.normals.Add(n >= 0 ? toUnity(normals[n * 3], normals[n * 3 + 1], normals[n * 3 + 2]) : Vector3.up);
                    data.uv0.Add(uv[0][c] >= 0 ? new Vector2(uvs[0][uv[0][c] * 2], uvs[0][uv[0][c] * 2 + 1]) : Vector2.zero);
                    data.uv1.Add(uvs.Length > 1 && uv[1][c] >= 0 ? new Vector2(uvs[1][uv[1][c] * 2], uvs[1][uv[1][c] * 2 + 1]) : Vector2.zero);
                }
                indexes[c] = index;
            }
            // mirrored, so the other way around
            data.triangles.Add(indexes[0]); data.triangles.Add(indexes[2]); data.triangles.Add(indexes[1]);
            if (count == 4) {
                data.triangles.Add(indexes[0]); data.triangles.Add(indexes[3]); data.triangles.Add(indexes[2]);
            }
        }
    }

    // a stud: a cylinder with its top, standing at the knob's position (millimeters, three.js axes)
    private static void addStud(MeshData data, JToken transform, Vector2 flatUv) {
        float[] p = transform["position"].ToObject<float[]>();
        float[] q = transform["quaternion"].ToObject<float[]>();
        Vector3 origin = toUnity(p[0], p[1], p[2]) * Scale;
        Quaternion rotation = new Quaternion(-q[0], -q[1], q[2], q[3]); // mirrored like positions
        float radius = StudRadius * Scale, height = StudHeight * Scale;

        int first = data.vertices.Count;
        for (int s = 0; s <= StudSegments; s++) {
            float angle = s * Mathf.PI * 2 / StudSegments;
            Vector3 side = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            addVertex(data, origin + rotation * (side * radius), rotation * side, flatUv);
            addVertex(data, origin + rotation * (side * radius + Vector3.up * height), rotation * side, flatUv);
        }
        for (int s = 0; s < StudSegments; s++) {
            int a = first + s * 2;
            data.triangles.Add(a); data.triangles.Add(a + 1); data.triangles.Add(a + 3);
            data.triangles.Add(a); data.triangles.Add(a + 3); data.triangles.Add(a + 2);
        }
        int center = data.vertices.Count;
        addVertex(data, origin + rotation * (Vector3.up * height), rotation * Vector3.up, flatUv);
        for (int s = 0; s <= StudSegments; s++) {
            float angle = s * Mathf.PI * 2 / StudSegments;
            addVertex(data, origin + rotation * (new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius + Vector3.up * height), rotation * Vector3.up, flatUv);
        }
        for (int s = 0; s < StudSegments; s++) {
            data.triangles.Add(center); data.triangles.Add(center + 2 + s); data.triangles.Add(center + 1 + s);
        }
    }

    private static void addVertex(MeshData data, Vector3 position, Vector3 normal, Vector2 uv) {
        data.vertices.Add(position);
        data.normals.Add(normal);
        data.uv0.Add(uv);
        data.uv1.Add(Vector2.zero);
    }

    // a point of the normal map with no bevel around it, for the studs (they have no uvs of their own)
    private static Vector2 findFlatUv(string path) {
        Texture2D texture = new Texture2D(2, 2);
        try {
            texture.LoadImage(File.ReadAllBytes(path));
            Color32[] pixels = texture.GetPixels32();
            int w = texture.width, h = texture.height;
            Func<int, int, bool> isFlat = (x, y) => {
                Color32 c = pixels[y * w + x];
                return Mathf.Abs(c.r - 128) < 6 && Mathf.Abs(c.g - 128) < 6 && c.b > 245;
            };
            for (int y = 4; y < h - 4; y++) {
                for (int x = 4; x < w - 4; x++) {
                    bool flat = true;
                    for (int dy = -4; dy <= 4 && flat; dy++) {
                        for (int dx = -4; dx <= 4 && flat; dx++) {
                            flat = isFlat(x + dx, y + dy);
                        }
                    }
                    if (flat) {
                        return new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
                    }
                }
            }
        } finally {
            UnityEngine.Object.DestroyImmediate(texture);
        }
        return new Vector2(0.5f, 0.5f);
    }

    // updated in place if it exists, so what uses it keeps it
    private static Mesh saveMesh(string path, string name, MeshData data) {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;
        if (isNew) {
            mesh = new Mesh();
        }
        mesh.Clear();
        mesh.name = name;
        mesh.indexFormat = data.vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(data.vertices);
        mesh.SetNormals(data.normals);
        mesh.SetUVs(0, data.uv0);
        mesh.SetUVs(1, data.uv1);
        mesh.SetTriangles(data.triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents(); // normal mapping needs them
        if (isNew) {
            AssetDatabase.CreateAsset(mesh, path);
        } else {
            EditorUtility.SetDirty(mesh);
        }
        AssetDatabase.SaveAssets();
        return mesh;
    }

    private static GameObject savePrefab(string path, string name, Mesh mesh, Texture2D normalMap, int width, int depth, int plates) {
        GameObject brick = new GameObject(name);
        try {
            brick.tag = "Block";
            brick.AddComponent<MeshFilter>().sharedMesh = mesh;
            brick.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterial);
            float height = plates * Server.plateHeight;
            BoxCollider collider = brick.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, height / 2, 0);
            collider.size = new Vector3(width * Server.studSize, height, depth * Server.studSize);
            if (normalMap != null) {
                brick.AddComponent<BrickNormalMap>().normalMap = normalMap;
            }
            return PrefabUtility.SaveAsPrefabAsset(brick, path);
        } finally {
            UnityEngine.Object.DestroyImmediate(brick);
        }
    }

    // the Server spawns bricks from its prefabs list, by name
    private static void register(GameObject prefab) {
        GameObject root = PrefabUtility.LoadPrefabContents(ServerPrefab);
        try {
            Server server = root.GetComponent<Server>();
            List<GameObject> prefabs = new List<GameObject>(server.prefabs ?? new GameObject[0]);
            prefabs.RemoveAll(p => p == null || p.name == prefab.name);
            prefabs.Add(prefab);
            server.prefabs = prefabs.ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, ServerPrefab);
        } finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
