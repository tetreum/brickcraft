using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Mods;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// Every kind of NPC: a folder each in StreamingAssets/NPCs (and Mods/[mod]/npcs, ids "[mod]:[npc]") with
    /// an info.json (see NpcInfo). Loaded with the items (see ItemDatabase.Load), since drops are items.
    /// </summary>
    public static class NpcDatabase
    {
        public const string Folder = "NPCs";
        public const string ModFolder = "npcs";
        public const string InfoFile = "info.json";

        private static readonly Dictionary<string, NpcInfo> npcs = new Dictionary<string, NpcInfo>();

        public static IEnumerable<NpcInfo> All {
            get { return npcs.Values; }
        }

        /// <summary>The NPC with that id, null if there's none.</summary>
        public static NpcInfo Get(string id) {
            return id != null && npcs.TryGetValue(id, out NpcInfo info) ? info : null;
        }

        /// <summary>Its model's prefab, null if it's missing.</summary>
        public static GameObject ModelOf(NpcInfo info) {
            return Resources.Load<GameObject>(NpcInfo.ModelsFolder + "/" + info.model);
        }

        public static void Load(IList<ModInfo> mods) {
            npcs.Clear();
            loadFolder(Path.Combine(Application.streamingAssetsPath, Folder), null);
            foreach (ModInfo mod in mods) {
                loadFolder(Path.Combine(mod.folder, ModFolder), mod);
            }
        }

        private static void loadFolder(string folder, ModInfo mod) {
            if (!Directory.Exists(folder)) {
                return;
            }
            string[] folders = Directory.GetDirectories(folder);
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (string npcFolder in folders) {
                string file = Path.Combine(npcFolder, InfoFile);
                if (File.Exists(file)) {
                    load(npcFolder, file, mod);
                }
            }
        }

        private static void load(string folder, string file, ModInfo mod) {
            NpcInfo info;
            try {
                info = JsonConvert.DeserializeObject<NpcInfo>(File.ReadAllText(file));
            } catch (Exception e) {
                Debug.LogError("Invalid NPC file " + file + ": " + e.Message);
                return;
            }
            string id = string.IsNullOrEmpty(info?.id) ? Path.GetFileName(folder) : info.id;
            if (info == null || string.IsNullOrEmpty(info.name) || string.IsNullOrEmpty(info.model)) {
                Debug.LogError("NPC file " + file + " needs a name and a model");
                return;
            }
            if (!Slugs.IsValid(id) || (mod != null && id.Contains(":"))) {
                Debug.LogError("NPC file " + file + " has the id \"" + id + "\", ids are " + Slugs.Rules + (mod != null ? " without a prefix" : ""));
                return;
            }
            info.id = mod != null ? mod.id + ":" + id : id;
            if (ModelOf(info) == null) {
                Debug.LogError("The NPC " + info.id + " has the model " + info.model + ", which doesn't exist (see NpcModel)");
                return;
            }
            info.attack = info.attack ?? new AttackInfo();
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
