using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Mods;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// The shapes of bricks, by id (what items' "brickModel" says). An id is looked for, in this order:
    ///   Mods/[mod]/models/[name]/            a mod's file model, "[mod]:[name]"
    ///   Mods/[mod]/models/*.bundle           a mod's prefabs (AssetBundles built with Unity), "[mod]:[prefab name]"
    ///   Assets/Resources/BrickModels/[id]    a custom prefab of the game (complex models)
    ///   StreamingAssets/BrickModels/[id]/    a file model of the game (most bricks, by part number)
    /// A file model's folder has:
    ///   model.obj, model.glb or model.gltf   its shape (the first found)
    ///   normal.png                           optional normal map (bevels...) for its first uv set
    ///   model.json                           optional, see ModelInfo
    ///
    /// Only the ids are listed when the game starts (and when a world's mods load); a model is read the
    /// first time it's needed and kept, so the library can hold thousands while a world only loads what it
    /// uses. File models are built into objects kept aside (inactive) that bricks are copies of. A model's
    /// size is its footprint in studs and its height in plates (under 3, it's a plate). Main thread only.
    /// </summary>
    public static class BrickModels
    {
        public const string Folder = "BrickModels";
        public const string ModFolder = "models";
        public const string ResourcesFolder = "BrickModels";
        public const string NormalMapFile = "normal.png";
        public const string InfoFile = "model.json";
        public const string BundleExtension = ".bundle";
        public static readonly string[] ShapeFiles = { "model.obj", "model.glb", "model.gltf" };
        /// <summary>The colour models are made in, bricks get theirs when placed.</summary>
        public const int TemplateColor = 71;

        /// <summary>The optional model.json of a file model.</summary>
        public class ModelInfo
        {
            /// <summary>What the model's units are worth in the game's: 1 for game units (a stud is 0.398), 0.04975 for millimeters.</summary>
            public float scale = 1;
            /// <summary>Its size, when the one worked out from its shape isn't right: studs wide and deep, plates high.</summary>
            public int width;
            public int depth;
            public int plates;
            /// <summary>"box" (a box of its size, the default) or "mesh" (its shape, for slopes, arches...).</summary>
            public string collider = "box";
            /// <summary>
            /// Parts (materials of the model: "usemtl" in OBJ, materials in glTF) that keep a colour of their
            /// own, by name: a palette colour id, like 47 for a Trans-Clear window. The others are the brick's.
            /// </summary>
            public Dictionary<string, int> parts;
        }

        // ids of the file models, and their folders
        private static readonly Dictionary<string, string> gameFiles = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> modFiles = new Dictionary<string, string>();
        // mods' prefabs, in their AssetBundles (open while the mods are)
        private static readonly Dictionary<string, KeyValuePair<AssetBundle, string>> modPrefabs = new Dictionary<string, KeyValuePair<AssetBundle, string>>();
        private static readonly List<AssetBundle> bundles = new List<AssetBundle>();
        // models read so far (null: looked for, not found)
        private static readonly Dictionary<string, BrickModel> cache = new Dictionary<string, BrickModel>();
        private static readonly List<UnityEngine.Object> loaded = new List<UnityEngine.Object>();
        private static GameObject templates;
        // the mods' folders when they were listed, null before
        private static string indexed;

        /// <summary>
        /// Lists the game's models and the mods' (see ModDatabase.Activate). With the same mods as before it does
        /// nothing: bricks already in the scene keep their models (a host's scene loads before its mods are set).
        /// </summary>
        public static void Index(IList<ModInfo> mods) {
            if (keyOf(mods) != indexed) {
                Reload(mods);
            }
        }

        /// <summary>Lists the models again, forgetting the ones read before (their files may have changed).</summary>
        public static void Reload(IList<ModInfo> mods) {
            unload();
            indexed = keyOf(mods);
            gameFiles.Clear();
            modFiles.Clear();
            indexFolder(Path.Combine(Application.streamingAssetsPath, Folder), null, gameFiles);
            foreach (ModInfo mod in mods) {
                indexFolder(Path.Combine(mod.folder, ModFolder), mod, modFiles);
                indexBundles(Path.Combine(mod.folder, ModFolder), mod);
            }
        }

        private static string keyOf(IList<ModInfo> mods) {
            List<string> folders = new List<string>();
            foreach (ModInfo mod in mods) {
                folders.Add(mod.folder);
            }
            return string.Join("|", folders);
        }

        // a mod's AssetBundles: their prefabs are models named after them (sized like the game's, see fromPrefab)
        private static void indexBundles(string folder, ModInfo mod) {
            if (!Directory.Exists(folder)) {
                return;
            }
            foreach (string file in Directory.GetFiles(folder, "*" + BundleExtension)) {
                AssetBundle bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null && unloadLeftover(Path.GetFileName(file))) {
                    bundle = AssetBundle.LoadFromFile(file);
                }
                if (bundle == null) {
                    Debug.LogError("Can't open " + file + " (an AssetBundle built for another platform or Unity version?)");
                    continue;
                }
                bundles.Add(bundle);
                foreach (string asset in bundle.GetAllAssetNames()) {
                    if (!asset.EndsWith(".prefab")) {
                        continue;
                    }
                    string id = mod.id + ":" + Path.GetFileNameWithoutExtension(asset).ToLowerInvariant();
                    if (Slugs.IsValid(id)) {
                        modPrefabs[id] = new KeyValuePair<AssetBundle, string>(bundle, asset);
                    } else {
                        Debug.LogError("The prefab " + asset + " of " + file + " isn't a valid model id, prefabs are named with " + Slugs.Rules);
                    }
                }
            }
        }

        private static void indexFolder(string folder, ModInfo mod, Dictionary<string, string> into) {
            if (!Directory.Exists(folder)) {
                return;
            }
            foreach (string modelFolder in Directory.GetDirectories(folder)) {
                string id = (mod != null ? mod.id + ":" : "") + Path.GetFileName(modelFolder);
                if (Slugs.IsValid(id)) {
                    into[id] = modelFolder;
                } else {
                    Debug.LogError("The model folder " + modelFolder + " isn't a valid id, model folders are named with " + Slugs.Rules);
                }
            }
        }

        /// <summary>The model with that id (read now if it wasn't yet), null if there's none.</summary>
        public static BrickModel Get(string id) {
            if (string.IsNullOrEmpty(id)) {
                return null;
            }
            BrickModel model;
            if (cache.TryGetValue(id, out model)) {
                return model;
            }
            string folder;
            KeyValuePair<AssetBundle, string> bundled;
            if (modFiles.TryGetValue(id, out folder)) {
                model = loadFile(id, folder);
            } else if (modPrefabs.TryGetValue(id, out bundled)) {
                model = fromPrefab(id, bundled.Key.LoadAsset<GameObject>(bundled.Value));
            } else if ((model = loadPrefab(id)) == null && gameFiles.TryGetValue(id, out folder)) {
                model = loadFile(id, folder);
            }
            cache[id] = model;
            return model;
        }

        /// <summary>Whether there's a model with that id, without reading file models.</summary>
        public static bool Exists(string id) {
            if (string.IsNullOrEmpty(id)) {
                return false;
            }
            BrickModel model;
            if (cache.TryGetValue(id, out model)) {
                return model != null;
            }
            // custom prefabs are only known by loading them (Resources has no listing)
            return modFiles.ContainsKey(id) || modPrefabs.ContainsKey(id) || gameFiles.ContainsKey(id) || Get(id) != null;
        }

        /// <summary>
        /// A model id written in a mod's info.json: the mod's own model if it has one with that name ("wall"
        /// is "[mod]:wall"), otherwise the game's.
        /// </summary>
        public static string Resolve(string id, ModInfo mod) {
            if (string.IsNullOrEmpty(id) || mod == null || id.Contains(":")) {
                return id;
            }
            string own = mod.id + ":" + id;
            return modFiles.ContainsKey(own) || modPrefabs.ContainsKey(own) ? own : id;
        }

        /// <summary>
        /// Gives a brick object (a copy of a model's prefab) its colour: every part takes the brick's material,
        /// except the ones with a colour of their own (see BrickModelParts).
        /// </summary>
        public static void ApplyColor(GameObject brick, Material brickMaterial) {
            if (brickMaterial == null) {
                return;
            }
            BrickModelParts parts = brick.GetComponent<BrickModelParts>();
            foreach (MeshRenderer meshRenderer in brick.GetComponentsInChildren<MeshRenderer>()) {
                Material[] materials = meshRenderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) {
                    BrickColor own = parts != null && meshRenderer.gameObject == brick && i < parts.colors.Length && parts.colors[i] != BrickColor.None
                        ? BrickColorPalette.Get(parts.colors[i])
                        : null;
                    materials[i] = own != null ? own.material : brickMaterial;
                }
                meshRenderer.sharedMaterials = materials;
            }
        }

        // -------- custom prefabs --------

        // a prefab of Resources/BrickModels, see fromPrefab
        private static BrickModel loadPrefab(string id) {
            if (id.Contains(":")) {
                return null; // mods' models are files
            }
            GameObject prefab = Resources.Load<GameObject>(ResourcesFolder + "/" + id);
            return prefab != null ? fromPrefab(id, prefab) : null;
        }

        // its size: its BoxCollider's, or else its mesh's (like file models: studs on top don't count); attachments have none
        private static BrickModel fromPrefab(string id, GameObject prefab) {
            if (prefab == null) {
                return null;
            }
            BrickAttachment attachment = prefab.GetComponent<BrickAttachment>();
            if (attachment != null) {
                BrickModel attached = create(id, prefab, 1, 1, 1);
                attached.attachesTo = attachment.fits ?? "";
                return attached;
            }
            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            if (collider != null) {
                Vector3 size = collider.size;
                return create(id, prefab, Mathf.RoundToInt(size.x / Server.studSize), Mathf.RoundToInt(size.z / Server.studSize), Mathf.RoundToInt(size.y / Server.plateHeight));
            }
            MeshFilter meshFilter = prefab.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) {
                Debug.LogError("The brick model prefab " + id + " needs a BoxCollider of its size (studs left out), or a mesh on its root");
                return null;
            }
            Bounds bounds = meshFilter.sharedMesh.bounds;
            return create(id, prefab, Mathf.RoundToInt(bounds.size.x / Server.studSize), Mathf.RoundToInt(bounds.size.z / Server.studSize),
                Mathf.FloorToInt(bounds.size.y / Server.plateHeight + 0.15f));
        }

        // -------- file models --------

        private static BrickModel loadFile(string id, string folder) {
            string shapeFile = null;
            foreach (string file in ShapeFiles) {
                if (File.Exists(Path.Combine(folder, file))) {
                    shapeFile = Path.Combine(folder, file);
                    break;
                }
            }
            if (shapeFile == null) {
                Debug.LogError("The model folder " + folder + " has no " + string.Join(", ", ShapeFiles));
                return null;
            }
            try {
                ModelInfo info = new ModelInfo();
                string infoFile = Path.Combine(folder, InfoFile);
                if (File.Exists(infoFile)) {
                    info = JsonConvert.DeserializeObject<ModelInfo>(File.ReadAllText(infoFile)) ?? info;
                }
                string[] materialNames;
                Mesh mesh = shapeFile.EndsWith(".obj") ? World.ObjLoader.LoadMesh(shapeFile, id, out materialNames) : GltfModels.LoadMesh(shapeFile, id, out materialNames);
                if (mesh.vertexCount == 0) {
                    throw new InvalidDataException("it has no shape");
                }
                place(mesh, info.scale > 0 ? info.scale : 1);
                loaded.Add(mesh);

                // its size, from its shape: studs on top are less than a plate high, so plates are rounded down
                Bounds bounds = mesh.bounds;
                int width = Mathf.Max(1, info.width > 0 ? info.width : Mathf.RoundToInt(bounds.size.x / Server.studSize));
                int depth = Mathf.Max(1, info.depth > 0 ? info.depth : Mathf.RoundToInt(bounds.size.z / Server.studSize));
                int plates = Mathf.Max(1, info.plates > 0 ? info.plates : Mathf.FloorToInt(bounds.size.y / Server.plateHeight + 0.15f));

                GameObject template = makeTemplate(id, mesh, info, materialNames, width, depth, plates);
                Texture2D normalMap = loadNormalMap(Path.Combine(folder, NormalMapFile));
                if (normalMap != null) {
                    template.AddComponent<BrickNormalMap>().normalMap = normalMap;
                }
                return create(id, template, width, depth, plates);
            } catch (Exception e) {
                Debug.LogError("Can't read the model " + folder + ": " + e.Message);
                return null;
            }
        }

        // scaled to the game's units and moved so its pivot is at the center of its footprint, on its bottom
        private static void place(Mesh mesh, float scale) {
            Vector3[] vertices = mesh.vertices;
            Bounds bounds = mesh.bounds;
            Vector3 offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            for (int i = 0; i < vertices.Length; i++) {
                vertices[i] = (vertices[i] + offset) * scale;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        // linear (it holds directions, not colours): HDRP reads x from red and y from green
        private static Texture2D loadNormalMap(string path) {
            if (!File.Exists(path)) {
                return null;
            }
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
            if (!texture.LoadImage(File.ReadAllBytes(path))) {
                World.TerrainTextures.Destroy(texture);
                Debug.LogError("Unsupported normal map " + path);
                return null;
            }
            texture.name = path;
            loaded.Add(texture);
            return texture;
        }

        // like the game's prefabs: the brick's mesh, a collider (a box of its size, or its shape) and its parts' colours
        private static GameObject makeTemplate(string id, Mesh mesh, ModelInfo info, string[] materialNames, int width, int depth, int plates) {
            if (templates == null) {
                templates = new GameObject("BrickModels");
                templates.SetActive(false); // what's in it stays out of the world
                if (Application.isPlaying) {
                    UnityEngine.Object.DontDestroyOnLoad(templates);
                } else {
                    templates.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            GameObject brick = new GameObject(id);
            brick.transform.SetParent(templates.transform, false);
            brick.tag = "Block";
            brick.AddComponent<MeshFilter>().sharedMesh = mesh;

            BrickColor templateColor = BrickColorPalette.Get(TemplateColor);
            Material[] materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) {
                materials[i] = templateColor != null ? templateColor.material : null;
            }
            brick.AddComponent<MeshRenderer>().sharedMaterials = materials;

            if (info.parts != null && info.parts.Count > 0) {
                int[] colors = new int[mesh.subMeshCount];
                for (int i = 0; i < colors.Length; i++) {
                    int color;
                    colors[i] = i < materialNames.Length && info.parts.TryGetValue(materialNames[i], out color) && BrickColorPalette.Get(color) != null
                        ? color
                        : BrickColor.None;
                }
                brick.AddComponent<BrickModelParts>().colors = colors;
                ApplyColor(brick, templateColor != null ? templateColor.material : null);
            }

            if (info.collider == "mesh") {
                brick.AddComponent<MeshCollider>().sharedMesh = mesh;
            } else {
                float height = plates * Server.plateHeight;
                BoxCollider collider = brick.AddComponent<BoxCollider>();
                collider.center = new Vector3(0, height / 2, 0);
                collider.size = new Vector3(width * Server.studSize, height, depth * Server.studSize);
            }
            return brick;
        }

        private static BrickModel create(string id, GameObject prefab, int width, int depth, int plates) {
            plates = Mathf.Max(1, plates); // baseplates are thinner than a plate
            return new BrickModel() {
                id = id,
                prefab = prefab,
                category = plates < 3 ? BrickModel.Category.Plate : BrickModel.Category.Brick,
                width = Mathf.Max(1, width),
                depth = Mathf.Max(1, depth),
                heightInPlates = plates,
            };
        }

        // bundles stay open through a domain reload (entering play mode, recompiling) while the list of them
        // doesn't: one left open with that name is closed so it can be opened again
        private static bool unloadLeftover(string name) {
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles()) {
                if (!bundles.Contains(bundle) && string.Equals(bundle.name, name, StringComparison.OrdinalIgnoreCase)) {
                    bundle.Unload(true);
                    return true;
                }
            }
            return false;
        }

        private static void unload() {
            cache.Clear();
            modPrefabs.Clear();
            foreach (AssetBundle bundle in bundles) {
                if (bundle != null) {
                    bundle.Unload(true);
                }
            }
            bundles.Clear();
            foreach (UnityEngine.Object asset in loaded) {
                if (asset != null) {
                    World.TerrainTextures.Destroy(asset);
                }
            }
            loaded.Clear();
            if (templates != null) {
                World.TerrainTextures.Destroy(templates);
                templates = null;
            }
        }
    }
}
