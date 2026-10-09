using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.World;
using Newtonsoft.Json;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// Every item of the game, loaded before the first scene loads. Each item has its own folder,
    /// StreamingAssets/Items/[item]/, which holds:
    ///   info.json       what the item is, see <see cref="ItemInfo"/>: its kind, its world block if it
    ///                   has one (brick items), and the recipes that craft it
    ///   icon.png        inventory icon, its block's top (or every side) texture otherwise
    ///   and the block's textures and models, see <see cref="BlockDatabase"/>
    ///
    /// To add an item, add a new folder (it also works in builds, where the folder is at
    /// [Game]_Data/StreamingAssets/Items). A folder with the id of another item replaces it.
    /// </summary>
    public static class ItemDatabase
    {
        public const string Folder = "Items";
        public const string InfoFile = "info.json";
        public const string IconFile = "icon.png";

        /// <summary>Crafting grid slots, 1 to 4.</summary>
        public const int CraftingSlots = 4;

        // their info, to build the recipes once every item is known
        private static readonly Dictionary<string, ItemInfo> infos = new Dictionary<string, ItemInfo>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load() {
            Server.items.Clear();
            Recipes.All.Clear();
            infos.Clear();
            BlockDatabase.BeginLoading();

            string folder = Path.Combine(Application.streamingAssetsPath, Folder);
            if (Directory.Exists(folder)) {
                string[] itemFolders = Directory.GetDirectories(folder);
                Array.Sort(itemFolders, StringComparer.Ordinal); // deterministic overrides

                foreach (string itemFolder in itemFolders) {
                    string file = Path.Combine(itemFolder, InfoFile);

                    if (!File.Exists(file)) {
                        Debug.LogWarning("Item folder " + itemFolder + " has no " + InfoFile + ", skipping it");
                        continue;
                    }
                    loadItem(itemFolder, file);
                }
            } else {
                Debug.LogError("Items folder not found: " + folder);
            }

            BlockDatabase.FinishLoading();
            loadRecipes();
        }

        private static void loadItem(string folder, string file) {
            ItemInfo info;
            try {
                info = JsonConvert.DeserializeObject<ItemInfo>(File.ReadAllText(file));
            } catch (Exception e) {
                Debug.LogError("Invalid item file " + file + ": " + e.Message);
                return;
            }
            if (info == null) {
                Debug.LogError("Empty item file " + file);
                return;
            }

            string id = string.IsNullOrEmpty(info.id) ? Path.GetFileName(folder) : info.id;
            if (!Slugs.IsValid(id)) {
                Debug.LogError("Item file " + file + " has the id \"" + id + "\", ids are " + Slugs.Rules);
                return;
            }
            if (string.IsNullOrEmpty(info.name)) {
                Debug.LogError("Item file " + file + " needs a name");
                return;
            }
            Item.Type type;
            if (!Enum.TryParse(info.type, true, out type) || !Enum.IsDefined(typeof(Item.Type), type)) {
                Debug.LogError("Item file " + file + " has the type \"" + info.type + "\", types are brick, helmet, weapon and food");
                return;
            }
            int layer = 0;
            if (!string.IsNullOrEmpty(info.layer)) {
                Game.Layers named;
                if (!Enum.TryParse(info.layer, true, out named)) {
                    Debug.LogError("Item file " + file + " has the layer \"" + info.layer + "\", layers are " + string.Join(", ", Enum.GetNames(typeof(Game.Layers))));
                    return;
                }
                layer = (int)named;
            }

            if (Server.items.ContainsKey(id)) {
                Debug.LogWarning("Item folder " + folder + " replaces the item " + id + " of " + Server.items[id].folder);
                BlockDatabase.RemoveBlock(id);
            }

            string iconPath = iconFor(folder);
            Item item = new Item() {
                id = id,
                name = info.name,
                type = type,
                folder = folder,
                maxStack = Math.Max(1, info.maxStack),
                brickModelId = info.brickModel,
                materialName = info.material,
                layer = layer,
                iconFile = iconPath,
                iconTexture = loadIcon(iconPath),
            };

            // brick items can be a world block too
            if (info.block != null) {
                if (type != Item.Type.Brick) {
                    Debug.LogError("Item file " + file + " has a block, only bricks can");
                } else {
                    BlockDefinition block = BlockDatabase.AddBlock(folder, id, info.block);
                    if (block != null) {
                        item.blockType = (BlockType)block.id;
                    }
                }
            }

            Server.items[id] = item;
            infos[id] = info;
        }

        // once every item is known, so recipes can use any of them
        private static void loadRecipes() {
            foreach (KeyValuePair<string, ItemInfo> entry in infos) {
                if (entry.Value.recipes == null) {
                    continue;
                }
                for (int i = 0; i < entry.Value.recipes.Length; i++) {
                    Recipe recipe = toRecipe(entry.Key, i, entry.Value.recipes[i]);
                    if (recipe != null) {
                        Recipes.All.Add(recipe);
                    }
                }
            }
            infos.Clear();
        }

        private static Recipe toRecipe(string itemId, int index, RecipeInfo info) {
            string where = "Recipe " + (index + 1) + " of " + itemId;

            if (info == null || info.ingredients == null || info.ingredients.Length == 0) {
                Debug.LogError(where + " has no ingredients");
                return null;
            }
            if (info.quantity < 1) {
                Debug.LogError(where + " makes " + info.quantity + " items, it should make at least 1");
                return null;
            }

            List<Ingredient> ingredients = new List<Ingredient>();
            HashSet<int> slots = new HashSet<int>();
            foreach (IngredientInfo ingredient in info.ingredients) {
                if (ingredient == null || string.IsNullOrEmpty(ingredient.id) || !Server.items.ContainsKey(ingredient.id)) {
                    Debug.LogError(where + " uses the item \"" + (ingredient != null ? ingredient.id : null) + "\", which doesn't exist");
                    return null;
                }
                if (ingredient.slot < 1 || ingredient.slot > CraftingSlots || !slots.Add(ingredient.slot)) {
                    Debug.LogError(where + " puts " + ingredient.id + " in slot " + ingredient.slot + ", slots are 1 to " + CraftingSlots + ", one ingredient each");
                    return null;
                }
                if (ingredient.quantity < 1) {
                    Debug.LogError(where + " needs " + ingredient.quantity + " " + ingredient.id + ", it should need at least 1");
                    return null;
                }
                ingredients.Add(new Ingredient() { itemId = ingredient.id, quantity = ingredient.quantity, slot = ingredient.slot });
            }

            return new Recipe() {
                id = itemId + "#" + (index + 1),
                itemId = itemId,
                quantity = info.quantity,
                ingredients = ingredients.ToArray(),
            };
        }

        // icon.png, or the texture of the block's top, so items without an icon don't show blank
        private static string iconFor(string folder) {
            foreach (string file in new[] { IconFile, "top.png", BlockDatabase.TextureFile }) {
                string path = Path.Combine(folder, file);
                if (File.Exists(path)) {
                    return path;
                }
            }
            return Path.Combine(folder, IconFile);
        }

        private static Texture2D loadIcon(string path) {
            if (!File.Exists(path)) {
                return null;
            }
            Texture2D icon = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            icon.name = path;
            if (Path.GetFileName(path) != IconFile) {
                icon.filterMode = FilterMode.Point; // block textures are pixel art
            }

            if (!icon.LoadImage(File.ReadAllBytes(path))) {
                Debug.LogError("Unsupported icon file " + path);
                TerrainTextures.Destroy(icon);
                return null;
            }
            return icon;
        }
    }
}
