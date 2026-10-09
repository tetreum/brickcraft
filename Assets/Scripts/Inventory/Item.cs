using UnityEngine;
using Brickcraft.World;

namespace Brickcraft
{
    public class Item
    {
        /// <summary>What kind of item it is, the "type" of its info.json.</summary>
        public enum Type
        {
            Brick = 1,
            Helmet = 2,
            Weapon = 3,
            Food = 4,
        }
        /// <summary>Its slug, see Slugs.</summary>
        public string id;
        public int brickModelId;
        public string materialName;
        public int layer;
        public Type type;
        public string name;
        /// <summary>Its folder, StreamingAssets/Items/[item] (see ItemDatabase).</summary>
        public string folder;

        public const int DefaultMaxStack = 64;

        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = DefaultMaxStack;

        // World block this item turns into when placed exactly over a world block (2x2 bricks only)
        public BlockType? blockType;

        // its icon, loaded from iconFile (see ItemDatabase)
        public string iconFile;
        public Texture2D iconTexture;

        public Texture2D icon {
            get { return iconTexture; }
        }
        public BrickModel brickModel {
            get {
                return Server.brickModels[brickModelId];
            }
        }
        public Material material {
            get {
                return Game.Instance.getBrickMaterial(materialName);
            }
        }
    }
}