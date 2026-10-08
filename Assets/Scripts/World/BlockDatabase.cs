using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Block definitions, loaded before the first scene loads. Each block has its own folder,
    /// StreamingAssets/Blocks/[block]/, which holds:
    ///   block.json                  its properties, see <see cref="BlockJson"/>
    ///   texture.png                 texture of every side
    ///   top.png, side.png, bottom.png  optional per side textures, they override texture.png
    ///   model.obj                   optional terrain model, the default 2x2 brick otherwise
    ///   collider.obj                optional collider model, model.obj (or the default collider) otherwise
    ///   icon.png                    inventory icon of the block's item
    ///
    /// To add a block, add a new folder (it also works in builds, where the folder is at
    /// [Game]_Data/StreamingAssets/Blocks).
    ///
    /// Lookups are a plain array access by block id, so they are cheap enough for meshing
    /// and safe from the world generation threads.
    /// </summary>
    public static class BlockDatabase
    {
        public const string Folder = "Blocks";
        public const string DefinitionFile = "block.json";
        public const string TextureFile = "texture.png";
        public const string ModelFile = "model.obj";
        public const string ColliderFile = "collider.obj";
        public const string IconFile = "icon.png";

        public const string DefaultModelFile = "Models/brick_2x2.obj";
        public const string DefaultColliderFile = "Models/brick_2x2_collider.obj";

        private static readonly BlockDefinition[] definitions = new BlockDefinition[256];
        private static readonly Dictionary<string, BlockDefinition> definitionsByName = new Dictionary<string, BlockDefinition>();
        private static readonly List<int> registeredItemIds = new List<int>();

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load() {
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            definitionsByName.Clear();
            unregisterItems();

            defaultShape = loadDefaultShape(DefaultModelFile);
            defaultColliderShape = loadDefaultShape(DefaultColliderFile);

            for (int id = 0; id < definitions.Length; id++) {
                definitions[id] = createUnknown((byte)id);
            }

            // engine blocks, they can't be overridden
            definitions[(byte)BlockType.Air] = createBuiltIn(BlockType.Air, "air");
            definitions[(byte)BlockType.NULL] = createBuiltIn(BlockType.NULL, "void"); // outside the generated world

            TerrainTextures textures = new TerrainTextures();
            string folder = Path.Combine(Application.streamingAssetsPath, Folder);

            if (Directory.Exists(folder)) {
                string[] blockFolders = Directory.GetDirectories(folder);
                System.Array.Sort(blockFolders, System.StringComparer.Ordinal); // deterministic overrides

                foreach (string blockFolder in blockFolders) {
                    string file = Path.Combine(blockFolder, DefinitionFile);

                    if (!File.Exists(file)) {
                        Debug.LogWarning("Block folder " + blockFolder + " has no " + DefinitionFile + ", skipping it");
                        continue;
                    }
                    loadBlock(blockFolder, file, textures);
                }
            } else {
                Debug.LogError("Block definitions folder not found: " + folder);
            }

            if (TextureArray != null) {
                TerrainTextures.Destroy(TextureArray);
            }
            TextureArray = textures.Build();

            Debug.Log("Loaded " + definitionsByName.Count + " blocks in " + clock.ElapsedMilliseconds + " ms");
        }

        private static void loadBlock(string blockFolder, string file, TerrainTextures textures) {
            // overwrite a new instance so fields missing from the file keep their defaults
            BlockJson json = new BlockJson();

            try {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(file), json);
            } catch (System.Exception e) {
                Debug.LogError("Invalid block file " + file + ": " + e.Message);
                return;
            }

            if (json.id <= (int)BlockType.Air || json.id >= (int)BlockType.NULL || string.IsNullOrEmpty(json.name)) {
                Debug.LogError("Block file " + file + " needs a name and an id between 1 and 254");
                return;
            }
            if (json.hardness < 0) {
                Debug.LogError("Block file " + file + " has a negative hardness, use \"breakable\": false instead");
                return;
            }

            byte id = (byte)json.id;
            BlockDefinition previous = definitions[id];

            if (definitionsByName.ContainsKey(json.name) && definitionsByName[json.name] != previous) {
                Debug.LogError("Block file " + file + " uses the name \"" + json.name + "\" which is already taken by block " + definitionsByName[json.name].id);
                return;
            }
            if (definitionsByName.ContainsKey(previous.name)) {
                Debug.LogWarning("Block file " + file + " overrides block " + id + " (" + previous.name + ")");
                definitionsByName.Remove(previous.name);
            }

            BlockDefinition definition = new BlockDefinition() {
                id = id,
                name = json.name,
                folder = blockFolder,
                hardness = json.hardness,
                isBreakable = json.breakable,
                isReplaceable = json.replaceable,
                isTransparent = json.transparent,
                dropItemId = json.dropItemId,
            };

            loadTextures(definition, textures);
            loadShapes(definition);
            loadItem(definition, json.item);

            definitions[id] = definition;
            definitionsByName[definition.name] = definition;
        }

        private static void loadTextures(BlockDefinition definition, TerrainTextures textures) {
            string all = Path.Combine(definition.folder, TextureFile);
            int allLayer = File.Exists(all) ? textures.Add(all) : TerrainTextures.MissingLayer;

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

        private static void loadItem(BlockDefinition definition, BlockItemJson json) {
            if (json == null || json.id == 0) {
                return;
            }
            if (string.IsNullOrEmpty(json.name)) {
                Debug.LogError("The item of block " + definition.name + " needs a name");
                return;
            }
            if (Server.items.ContainsKey(json.id)) {
                Debug.LogError("The item of block " + definition.name + " uses id " + json.id + " which is already taken by " + Server.items[json.id].name);
                return;
            }

            string iconPath = Path.Combine(definition.folder, IconFile);
            Item item = new Item() {
                id = json.id,
                type = Item.Type.Brick,
                name = json.name,
                brickModelId = json.brickModel,
                materialName = json.material,
                blockType = (BlockType)definition.id,
                iconFile = iconPath,
                iconTexture = loadIcon(iconPath),
            };

            Server.items.Add(item.id, item);
            registeredItemIds.Add(item.id);

            definition.itemId = item.id;
            if (definition.dropItemId == 0) {
                definition.dropItemId = item.id;
            }
        }

        private static Texture2D loadIcon(string path) {
            if (!File.Exists(path)) {
                return null;
            }
            Texture2D icon = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            icon.name = path;

            if (!icon.LoadImage(File.ReadAllBytes(path))) {
                Debug.LogError("Unsupported icon file " + path);
                TerrainTextures.Destroy(icon);
                return null;
            }
            return icon;
        }

        // items survive between play sessions when domain reload is disabled
        private static void unregisterItems() {
            foreach (int id in registeredItemIds) {
                Server.items.Remove(id);
            }
            registeredItemIds.Clear();
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
                hardness = 1,
                isBreakable = true,
                shape = defaultShape,
                colliderShape = defaultColliderShape,
            };
        }
    }
}
