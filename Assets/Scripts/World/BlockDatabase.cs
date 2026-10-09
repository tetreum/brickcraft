using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// The world blocks: the "block" of brick items, added by <see cref="ItemDatabase"/> from their folder
    /// (StreamingAssets/Items/[item]/), where these files define how the block looks:
    ///   texture.png                 texture of every side
    ///   top.png, side.png, bottom.png  optional per side textures, they override texture.png
    ///   model.obj                   optional terrain model, the default 2x2 brick otherwise
    ///   collider.obj                optional collider model, model.obj (or the default collider) otherwise
    ///
    /// Lookups are a plain array access by block id, so they are cheap enough for meshing
    /// and safe from the world generation threads.
    /// </summary>
    public static class BlockDatabase
    {
        public const string TextureFile = "texture.png";
        public const string ModelFile = "model.obj";
        public const string ColliderFile = "collider.obj";

        public const string DefaultModelFile = "Models/brick_2x2.obj";
        public const string DefaultColliderFile = "Models/brick_2x2_collider.obj";

        private static readonly BlockDefinition[] definitions = new BlockDefinition[256];
        private static readonly Dictionary<string, BlockDefinition> definitionsByName = new Dictionary<string, BlockDefinition>();
        private static TerrainTextures textures;

        private static BlockShape defaultShape;
        private static BlockShape defaultColliderShape;

        /// <summary>Texture array for the terrain material, see <see cref="TerrainTextures"/>.</summary>
        public static Texture2DArray TextureArray { get; private set; }

        public static BlockDefinition Get(byte id) {
            return definitions[id];
        }

        public static BlockDefinition Get(BlockType type) {
            return definitions[(byte)type];
        }

        public static bool TryGet(string name, out BlockDefinition definition) {
            return definitionsByName.TryGetValue(name, out definition);
        }

        /// <summary>
        /// The number of a block in this game, by its slug (what saves and the network use). Unknown
        /// blocks (a mod that isn't installed anymore) are air.
        /// </summary>
        public static byte IdOf(string name) {
            BlockDefinition definition;
            if (name != null && definitionsByName.TryGetValue(name, out definition)) {
                return definition.id;
            }
            warnUnknown(name);
            return (byte)BlockType.Air;
        }

        private static readonly HashSet<string> warnedUnknown = new HashSet<string>();

        // saves are read on other threads
        private static void warnUnknown(string name) {
            bool isNew;
            lock (warnedUnknown) {
                isNew = warnedUnknown.Add(name ?? "");
            }
            if (isNew) {
                Debug.LogWarning("Unknown block \"" + name + "\" (removed mod?), it's air from now on");
            }
        }

        // Blocks are numbered when the game starts, numbers aren't saved: built-in blocks keep the number
        // of their BlockType (the world generator uses them), the others get free ones.
        private static byte numberFor(string name) {
            BlockDefinition existing;
            if (definitionsByName.TryGetValue(name, out existing)) {
                return existing.id; // overriding a block, it keeps its number
            }
            byte builtIn;
            if (BuiltInNumbers.TryGetValue(name.Replace("_", ""), out builtIn)) {
                return builtIn;
            }
            // free numbers not meant for built-in blocks first
            for (int pass = 0; pass < 2; pass++) {
                for (int id = 1; id < (int)BlockType.NULL; id++) {
                    if (definitions[id].isUnknown && (pass == 1 || !builtInValues.Contains((byte)id))) {
                        return (byte)id;
                    }
                }
            }
            return 0;
        }

        // BlockType names (without "_", lowercase) and their numbers
        private static readonly Dictionary<string, byte> BuiltInNumbers = createBuiltInNumbers();
        private static readonly HashSet<byte> builtInValues = new HashSet<byte>(BuiltInNumbers.Values);

        private static Dictionary<string, byte> createBuiltInNumbers() {
            Dictionary<string, byte> numbers = new Dictionary<string, byte>();
            foreach (string name in System.Enum.GetNames(typeof(BlockType))) {
                BlockType type = (BlockType)System.Enum.Parse(typeof(BlockType), name);
                string key = name.Replace("_", "").ToLowerInvariant();
                if (type != BlockType.Air && type != BlockType.NULL && !numbers.ContainsKey(key)) {
                    numbers[key] = (byte)type;
                }
            }
            return numbers;
        }

        /// <summary>Forgets the blocks, before ItemDatabase adds them again.</summary>
        public static void BeginLoading() {
            definitionsByName.Clear();
            warnedUnknown.Clear();

            defaultShape = loadDefaultShape(DefaultModelFile);
            defaultColliderShape = loadDefaultShape(DefaultColliderFile);

            for (int id = 0; id < definitions.Length; id++) {
                definitions[id] = createUnknown((byte)id);
            }

            // engine blocks, they can't be overridden
            definitions[(byte)BlockType.Air] = createBuiltIn(BlockType.Air, "air");
            definitions[(byte)BlockType.NULL] = createBuiltIn(BlockType.NULL, "void"); // outside the generated world

            textures = new TerrainTextures();

            // a plain layer per brick colour, for blocks placed with a colour
            Dictionary<int, int> layers = new Dictionary<int, int>();
            HashSet<int> transparent = new HashSet<int>();
            foreach (BrickColor color in BrickColorPalette.All) {
                layers[color.id] = textures.AddSolid(color.Color32);
                if (color.isTransparent) {
                    transparent.Add(color.id);
                }
            }
            colorLayers = layers;
            transparentColors = transparent;
        }

        private static Dictionary<int, int> colorLayers = new Dictionary<int, int>();
        private static HashSet<int> transparentColors = new HashSet<int>();

        /// <summary>The texture layer of a brick colour, -1 if it isn't one (safe from any thread).</summary>
        public static int ColorLayer(int colorId) {
            int layer;
            return colorLayers.TryGetValue(colorId, out layer) ? layer : -1;
        }

        /// <summary>See-through colours, whose blocks are drawn like water (safe from any thread).</summary>
        public static bool IsTransparentColor(int colorId) {
            return transparentColors.Contains(colorId);
        }

        /// <summary>
        /// The block of a brick item, named after it. Null (and logged) if it can't be added. Blocks
        /// without textures are drawn with their item's colour.
        /// </summary>
        public static BlockDefinition AddBlock(string folder, string name, BlockInfo info, int color = BrickColor.None) {
            if (info.hardness < 0) {
                Debug.LogError("The block of " + name + " has a negative hardness, use \"breakable\": false instead");
                return null;
            }
            byte id = numberFor(name);
            if (id == 0) {
                Debug.LogError("The block of " + name + " can't be added, there are already 254 blocks");
                return null;
            }

            BlockDefinition definition = new BlockDefinition() {
                id = id,
                name = name,
                folder = folder,
                hardness = info.hardness,
                isBreakable = info.breakable,
                isReplaceable = info.replaceable,
                isTransparent = info.transparent,
                isTranslucent = info.translucent,
                itemId = name,
                dropItemId = string.IsNullOrEmpty(info.drop) ? name : info.drop,
            };
            loadTextures(definition, textures, ColorLayer(color));
            loadShapes(definition);

            definitions[id] = definition;
            definitionsByName[name] = definition;
            return definition;
        }

        /// <summary>Takes a block out again (its item was overridden by one without a block).</summary>
        public static void RemoveBlock(string name) {
            BlockDefinition definition;
            if (definitionsByName.TryGetValue(name, out definition)) {
                definitionsByName.Remove(name);
                definitions[definition.id] = createUnknown(definition.id);
            }
        }

        /// <summary>Builds the terrain texture array once every block is added.</summary>
        public static void FinishLoading() {
            if (TextureArray != null) {
                TerrainTextures.Destroy(TextureArray);
            }
            TextureArray = textures.Build();
            textures = null;
        }

        private static void loadTextures(BlockDefinition definition, TerrainTextures textures, int colorLayer) {
            string all = Path.Combine(definition.folder, TextureFile);
            int allLayer = File.Exists(all) ? textures.Add(all) : (colorLayer >= 0 ? colorLayer : TerrainTextures.MissingLayer);

            definition.topTextureLayer = loadSideTexture(definition.folder, "top.png", allLayer, textures);
            definition.sideTextureLayer = loadSideTexture(definition.folder, "side.png", allLayer, textures);
            definition.bottomTextureLayer = loadSideTexture(definition.folder, "bottom.png", allLayer, textures);

            if (allLayer == TerrainTextures.MissingLayer && definition.sideTextureLayer == TerrainTextures.MissingLayer) {
                Debug.LogWarning("Block " + definition.name + " has no " + TextureFile);
            }
        }

        private static int loadSideTexture(string folder, string file, int fallback, TerrainTextures textures) {
            string path = Path.Combine(folder, file);

            return File.Exists(path) ? textures.Add(path) : fallback;
        }

        private static void loadShapes(BlockDefinition definition) {
            string model = Path.Combine(definition.folder, ModelFile);
            string collider = Path.Combine(definition.folder, ColliderFile);

            definition.shape = File.Exists(model) ? loadShape(model, defaultShape) : defaultShape;

            if (File.Exists(collider)) {
                definition.colliderShape = loadShape(collider, defaultColliderShape);
            } else {
                // a custom model is its own collider, unless one is given
                definition.colliderShape = definition.shape == defaultShape ? defaultColliderShape : definition.shape;
            }
        }

        private static BlockShape loadShape(string path, BlockShape fallback) {
            try {
                return BlockShape.FromObj(path);
            } catch (System.Exception e) {
                Debug.LogError("Can't load model " + path + ": " + e.Message);
                return fallback;
            }
        }

        private static BlockShape loadDefaultShape(string file) {
            string path = Path.Combine(Application.streamingAssetsPath, file);

            try {
                return BlockShape.FromObj(path);
            } catch (System.Exception e) {
                Debug.LogError("Can't load the default block model " + path + ": " + e.Message);
                return new BlockShape(new Vector3[0], new int[0]);
            }
        }

        private static BlockDefinition createBuiltIn(BlockType type, string name) {
            BlockDefinition definition = new BlockDefinition() {
                id = (byte)type,
                name = name,
                isBreakable = false,
                isReplaceable = true,
                isTransparent = true,
                shape = defaultShape,
                colliderShape = defaultColliderShape,
            };
            definitionsByName[name] = definition;

            return definition;
        }

        // used by ids without a folder, so the world still renders (with the missing texture) if one is missing
        private static BlockDefinition createUnknown(byte id) {
            return new BlockDefinition() {
                id = id,
                name = "unknown_" + id,
                isUnknown = true,
                hardness = 1,
                isBreakable = true,
                shape = defaultShape,
                colliderShape = defaultColliderShape,
            };
        }
    }
}
