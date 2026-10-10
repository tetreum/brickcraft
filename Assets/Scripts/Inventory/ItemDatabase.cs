using System;
using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks;
using Brickcraft.Mods;
using Brickcraft.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// Every item: the game's own, loaded before the first scene loads, and those of the mods of the
    /// world being played (see ModDatabase), loaded again with them when it starts. Each item has its
    /// own folder, StreamingAssets/Items/[item]/ (or Mods/[mod]/items/[item]/), which holds:
    ///   info.json       what the item is, see <see cref="ItemInfo"/>: its kind, its world block if it
    ///                   has one (brick items), and the recipes that craft it
    ///   icon.png        inventory icon, its block's top (or every side) texture otherwise
    ///   and the block's textures and models, see <see cref="BlockDatabase"/>
    ///
    /// To add an item, add a new folder (it also works in builds, where the folder is at
    /// [Game]_Data/StreamingAssets/Items). A folder with the id of another item replaces it.
    ///
    /// Mod items are "[mod]:[item]". Item ids in a mod's info.json (recipe ingredients, drops) are
    /// first looked for in the mod ("wheel" is "[mod]:wheel" if the mod has it), then in the game.
    /// </summary>
    public static class ItemDatabase
    {
        public const string Folder = "Items";
        public const string InfoFile = "info.json";
        public const string IconFile = "icon.png";

        /// <summary>Crafting grid slots, 1 to 4.</summary>
        public const int CraftingSlots = 4;

        // their info and mod, to build the recipes once every item is known
        private static readonly Dictionary<string, ItemInfo> infos = new Dictionary<string, ItemInfo>();
        private static readonly Dictionary<string, ModInfo> modOf = new Dictionary<string, ModInfo>();

        // the game's own items, until a world is played with its mods
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void loadGameItems() {
            Load(new ModInfo[0]);
        }

        /// <summary>Loads the game's items and those of the mods (see ModDatabase.Activate), replacing the loaded ones.</summary>
        public static void Load(IList<ModInfo> mods) {
            BrickColorPalette.Load(); // blocks and items need the colours
            BrickModels.Index(mods); // mods' items can be made of their models
            foreach (Item old in Server.items.Values) {
                if (old.iconTexture != null) {
                    TerrainTextures.Destroy(old.iconTexture);
                }
            }
            Server.items.Clear();
            Recipes.All.Clear();
            infos.Clear();
            modOf.Clear();
            BlockDatabase.BeginLoading();

            string folder = Path.Combine(Application.streamingAssetsPath, Folder);
            if (Directory.Exists(folder)) {
                loadFolder(folder, null);
            } else {
                Debug.LogError("Items folder not found: " + folder);
            }
            foreach (ModInfo mod in mods) {
                if (Directory.Exists(mod.ItemsFolder)) {
                    loadFolder(mod.ItemsFolder, mod);
                }
            }

            BlockDatabase.FinishLoading();
            resolveDrops();
            loadRecipes();
            Npcs.NpcDatabase.Load(mods); // their drops are items
        }

        private static void loadFolder(string folder, ModInfo mod) {
            string[] itemFolders = Directory.GetDirectories(folder);
            Array.Sort(itemFolders, StringComparer.Ordinal); // deterministic overrides

            foreach (string itemFolder in itemFolders) {
                string file = Path.Combine(itemFolder, InfoFile);

                if (!File.Exists(file)) {
                    Debug.LogWarning("Item folder " + itemFolder + " has no " + InfoFile + ", skipping it");
                    continue;
                }
                loadItem(itemFolder, file, mod);
            }
        }

        // an item id written in an info.json: the mod's item if it has one with that name, otherwise the game's
        private static string resolve(string id, ModInfo mod) {
            if (string.IsNullOrEmpty(id) || mod == null || id.Contains(":")) {
                return id;
            }
            string own = mod.id + ":" + id;
            return Server.items.ContainsKey(own) ? own : id;
        }

        // drops can name items loaded after the block's
        private static void resolveDrops() {
            foreach (KeyValuePair<string, ModInfo> entry in modOf) {
                Item item = Server.items[entry.Key];
                if (entry.Value != null && item.blockType.HasValue) {
                    BlockDefinition block = BlockDatabase.Get(item.blockType.Value);
                    block.dropItemId = resolve(block.dropItemId, entry.Value);
                }
            }
        }

        private static void loadItem(string folder, string file, ModInfo mod) {
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
            if (mod != null) {
                if (id.Contains(":")) {
                    Debug.LogError("Item file " + file + " has the id \"" + id + "\", mod item ids don't have a prefix (it's the mod's: " + mod.id + ":)");
                    return;
                }
                id = mod.id + ":" + id;
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

            if (info.color.HasValue && BrickColorPalette.Get(info.color.Value) == null) {
                Debug.LogError("Item file " + file + " has the color " + info.color.Value + ", which isn't in the palette (Resources/BrickColorPalette)");
                return;
            }
            if ((info.color.HasValue || info.colors != null) && type != Item.Type.Brick) {
                Debug.LogError("Item file " + file + " has a color, only bricks can");
                return;
            }

            string iconPath = iconFor(folder);
            Item item = new Item() {
                id = id,
                name = info.name,
                type = type,
                folder = folder,
                maxStack = Math.Max(1, info.maxStack),
                damage = Math.Max(0, info.damage ?? Item.HandDamage),
                brickModelId = BrickModels.Resolve(info.brickModel, mod),
                materialName = info.material,
                layer = layer,
                iconFile = iconPath,
                iconTexture = loadIcon(iconPath),
                color = info.color ?? BrickColor.None,
            };
            if (!loadColors(item, info.colors, file)) {
                return;
            }
            if (item.iconTexture == null && item.color != BrickColor.None) {
                item.iconTexture = colorIcon(BrickColorPalette.Get(item.color));
            }

            // brick items can be a world block too
            if (info.block != null) {
                if (type != Item.Type.Brick) {
                    Debug.LogError("Item file " + file + " has a block, only bricks can");
                } else {
                    BlockDefinition block = BlockDatabase.AddBlock(folder, id, info.block, item.color);
                    if (block != null) {
                        item.blockType = (BlockType)block.id;
                    }
                }
            }

            Server.items[id] = item;
            infos[id] = info;
            modOf[id] = mod;
        }

        // "colors": "all", or a list of colour ids
        private static bool loadColors(Item item, JToken colors, string file) {
            if (colors == null || colors.Type == JTokenType.Null) {
                return true;
            }
            if (item.color == BrickColor.None) {
                Debug.LogError("Item file " + file + " has colors but no default color");
                return false;
            }
            if (colors.Type == JTokenType.String && (string)colors == "all") {
                item.anyColor = true;
                return true;
            }
            if (colors.Type != JTokenType.Array) {
                Debug.LogError("Item file " + file + " has invalid colors, they're \"all\" or a list of colour ids");
                return false;
            }
            foreach (JToken color in colors) {
                if (color.Type != JTokenType.Integer || BrickColorPalette.Get((int)color) == null) {
                    Debug.LogError("Item file " + file + " has the color " + color + ", which isn't in the palette (Resources/BrickColorPalette)");
                    return false;
                }
                item.colors.Add((int)color);
            }
            return true;
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
                string ingredientId = ingredient != null ? resolve(ingredient.id, modOf[itemId]) : null;
                if (string.IsNullOrEmpty(ingredientId) || !Server.items.ContainsKey(ingredientId)) {
                    Debug.LogError(where + " uses the item \"" + ingredientId + "\", which doesn't exist");
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
                ingredients.Add(new Ingredient() { itemId = ingredientId, quantity = ingredient.quantity, slot = ingredient.slot });
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

        // items without an icon nor textures: a square of their colour
        private static Texture2D colorIcon(BrickColor color) {
            const int size = 16;
            Texture2D icon = new Texture2D(size, size, TextureFormat.RGBA32, false);
            icon.name = color.name;
            icon.filterMode = FilterMode.Point;

            Color32 fill = color.Color32;
            Color32 border = new Color32((byte)(fill.r * 0.7f), (byte)(fill.g * 0.7f), (byte)(fill.b * 0.7f), 255);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    pixels[y * size + x] = edge ? border : fill;
                }
            }
            icon.SetPixels32(pixels);
            icon.Apply();
            return icon;
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
