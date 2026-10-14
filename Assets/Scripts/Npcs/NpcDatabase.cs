using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Mods;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Every kind of NPC: a folder each in StreamingAssets/NPCs (and Mods/[mod]/npcs, ids "[mod]:[npc]") with
    /// an info.json (see NpcInfo). Loaded with the items (see ItemDatabase.Load), since drops are items.
    ///
    /// Its model (see NpcModel) is, the first found: a glTF model in its folder (model.glb or model.gltf,
    /// built when the NPCs load, see NpcModelBuilder); for a mod's NPC, the prefab named its "model" in one of
    /// the mod's AssetBundles (npcs/*.bundle); a prefab of the game, Resources/NpcModels/[model].prefab.
    /// </summary>
    public static class NpcDatabase
    {
        public const string Folder = "NPCs";
        public const string ModFolder = "npcs";
        public const string InfoFile = "info.json";
        public const string BundleExtension = ".bundle";
        private static readonly string[] ModelFiles = { "model.glb", "model.gltf" };

        private static readonly Dictionary<string, NpcInfo> npcs = new Dictionary<string, NpcInfo>();
        // each NPC's model, by its id
        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();
        // models built from glTF files and what they're made of, and mods' bundles: gone when the NPCs load again
        private static readonly List<Object> built = new List<Object>();
        private static readonly List<AssetBundle> bundles = new List<AssetBundle>();
        private static GameObject modelsRoot;

        public static IEnumerable<NpcInfo> All {
            get { return npcs.Values; }
        }

        /// <summary>The NPC with that id, null if there's none.</summary>
        public static NpcInfo Get(string id) {
            return id != null && npcs.TryGetValue(id, out NpcInfo info) ? info : null;
        }

        /// <summary>Its model, to instantiate (a prefab, or a model built from a glTF file), null if it's missing.</summary>
        public static GameObject ModelOf(NpcInfo info) {
            return info != null && models.TryGetValue(info.id, out GameObject model) ? model : null;
        }

        public static void Load(IList<ModInfo> mods) {
            unload();
            npcs.Clear();
            loadFolder(Path.Combine(Application.streamingAssetsPath, Folder), null, null);
            foreach (ModInfo mod in mods) {
                string folder = Path.Combine(mod.folder, ModFolder);
                loadFolder(folder, mod, openBundles(folder));
            }
        }

        private static void unload() {
            models.Clear();
            foreach (Object asset in built) {
                if (asset != null) {
                    Object.Destroy(asset);
                }
            }
            built.Clear();
            foreach (AssetBundle bundle in bundles) {
                if (bundle != null) {
                    bundle.Unload(true);
                }
            }
            bundles.Clear();
        }

        // a mod's AssetBundles: their prefabs by name (lowercase)
        private static Dictionary<string, GameObject> openBundles(string folder) {
            Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
            if (!Directory.Exists(folder)) {
                return prefabs;
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
                    if (asset.EndsWith(".prefab")) {
                        prefabs[Path.GetFileNameWithoutExtension(asset).ToLowerInvariant()] = bundle.LoadAsset<GameObject>(asset);
                    }
                }
            }
            return prefabs;
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

        // its folder's glTF model, a prefab of its mod's bundles, or one of the game's
        private static GameObject findModel(NpcInfo info, string folder, Dictionary<string, GameObject> bundled) {
            foreach (string name in ModelFiles) {
                string file = Path.Combine(folder, name);
                if (File.Exists(file)) {
                    return build(info, file);
                }
            }
            if (string.IsNullOrEmpty(info.model)) {
                return null;
            }
            if (bundled != null && bundled.TryGetValue(info.model.ToLowerInvariant(), out GameObject prefab)) {
                return prefab;
            }
            return Resources.Load<GameObject>(NpcInfo.ModelsFolder + "/" + info.model);
        }

        // built once, kept under an inactive object: instances of it are active
        private static GameObject build(NpcInfo info, string file) {
            NpcModelBuilder builder;
            try {
                builder = NpcModelBuilder.Build(Models.GltfFile.Load(file), info.id.Replace(':', '_'), true);
            } catch (Exception e) {
                Debug.LogError("Can't read the model " + file + ": " + e.Message);
                return null;
            }
            if (modelsRoot == null) {
                modelsRoot = new GameObject("NPC models");
                modelsRoot.SetActive(false);
                Object.DontDestroyOnLoad(modelsRoot);
            }
            builder.Root.transform.SetParent(modelsRoot.transform, false);
            built.Add(builder.Root);
            built.AddRange(builder.Textures);
            built.AddRange(builder.Materials);
            built.AddRange(builder.Meshes);
            built.AddRange(builder.Clips);
            return builder.Root;
        }

        private static void loadFolder(string folder, ModInfo mod, Dictionary<string, GameObject> bundled) {
            if (!Directory.Exists(folder)) {
                return;
            }
            string[] folders = Directory.GetDirectories(folder);
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (string npcFolder in folders) {
                string file = Path.Combine(npcFolder, InfoFile);
                if (File.Exists(file)) {
                    load(npcFolder, file, mod, bundled);
                }
            }
        }

        private static void load(string folder, string file, ModInfo mod, Dictionary<string, GameObject> bundled) {
            NpcInfo info;
            try {
                info = JsonConvert.DeserializeObject<NpcInfo>(File.ReadAllText(file));
            } catch (Exception e) {
                Debug.LogError("Invalid NPC file " + file + ": " + e.Message);
                return;
            }
            string id = string.IsNullOrEmpty(info?.id) ? Path.GetFileName(folder) : info.id;
            if (info == null || string.IsNullOrEmpty(info.name)) {
                Debug.LogError("NPC file " + file + " needs a name");
                return;
            }
            if (!Slugs.IsValid(id) || (mod != null && id.Contains(":"))) {
                Debug.LogError("NPC file " + file + " has the id \"" + id + "\", ids are " + Slugs.Rules + (mod != null ? " without a prefix" : ""));
                return;
            }
            info.id = mod != null ? mod.id + ":" + id : id;
            GameObject model = findModel(info, folder, bundled);
            if (model == null || model.GetComponent<NpcModel>() == null) {
                Debug.LogError("The NPC " + info.id + " has no model: a model.glb or model.gltf in its folder, or a \"model\" naming a prefab with an NpcModel"
                    + (mod != null ? " (in the mod's npcs/*.bundle, or the game's)" : " (in Resources/NpcModels)"));
                return;
            }
            models[info.id] = model;
            info.attack = info.attack ?? new AttackInfo();
            if (info.spawn != null) {
                if (!World.TimeWindow.TryParse(info.spawn.time, out info.spawn.window)) {
                    Debug.LogError("The NPC " + info.id + " has the spawn time \"" + info.spawn.time + "\", times are day, night, any or HH:MM-HH:MM");
                }
                if (info.spawn.atDay != SpawnInfo.Stay && info.spawn.atDay != SpawnInfo.Despawn) {
                    Debug.LogError("The NPC " + info.id + " has the spawn atDay \"" + info.spawn.atDay + "\", it's stay or despawn");
                    info.spawn.atDay = SpawnInfo.Stay;
                }
            }
            List<ScheduleEntry> schedule = new List<ScheduleEntry>();
            foreach (ScheduleEntry entry in info.schedule ?? new ScheduleEntry[0]) {
                if (entry == null || !World.TimeWindow.TryParse(entry.time, out entry.window) || string.IsNullOrEmpty(entry.activity)) {
                    Debug.LogError("The NPC " + info.id + " has a schedule entry it can't read (time " + entry?.time + ", activity " + entry?.activity + ")");
                    continue;
                }
                schedule.Add(entry);
            }
            info.schedule = schedule.ToArray();
            info.drops = info.drops ?? new DropInfo[0];
            foreach (DropInfo drop in info.drops) {
                // the mod's item if it has one with that name
                string own = mod != null && drop.item != null && !drop.item.Contains(":") ? mod.id + ":" + drop.item : null;
                drop.item = own != null && Server.items.ContainsKey(own) ? own : drop.item;
                if (!Server.items.ContainsKey(drop.item ?? "")) {
                    Debug.LogError("The NPC " + info.id + " drops the item " + drop.item + ", which doesn't exist");
                }
            }
            npcs[info.id] = info;
        }

        /// <summary>The name of its animation for idle, walk, attack or death.</summary>
        public static string AnimationOf(NpcInfo info, string animation) {
            return info.animations != null && info.animations.TryGetValue(animation, out string name) ? name : animation;
        }
    }
}
