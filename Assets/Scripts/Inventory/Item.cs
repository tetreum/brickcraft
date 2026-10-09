using System.Collections.Generic;
using UnityEngine;
using Brickcraft.Bricks;
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
        /// <summary>A special material (see Game.brickMaterials) used instead of its colour's, like Water.</summary>
        public string materialName;
        public int layer;
        public Type type;
        public string name;
        /// <summary>Its folder, StreamingAssets/Items/[item] (see ItemDatabase).</summary>
        public string folder;

        public const int DefaultMaxStack = 64;

        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = DefaultMaxStack;

        /// <summary>Its colour when it isn't given one (see BrickColorPalette), BrickColor.None if it has none.</summary>
        public int color = BrickColor.None;
        /// <summary>It can have any colour of the palette.</summary>
        public bool anyColor;
        /// <summary>Other colours it can have, besides its default one.</summary>
        public HashSet<int> colors = new HashSet<int>();

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

        /// <summary>It can have colours other than its default one.</summary>
        public bool IsColorable {
            get { return anyColor || colors.Count > 0; }
        }

        public bool AllowsColor(int colorId) {
            if (colorId == color) {
                return true;
            }
            if (colorId == BrickColor.None || BrickColorPalette.Get(colorId) == null) {
                return false;
            }
            return anyColor || colors.Contains(colorId);
        }

        /// <summary>The colour, or the item's default one if it can't have it (a colour removed from the palette).</summary>
        public int ValidColor(int colorId) {
            return AllowsColor(colorId) ? colorId : color;
        }

        /// <summary>The material of its brick in that colour, null for the prefab's own.</summary>
        public Material MaterialFor(int colorId) {
            if (!string.IsNullOrEmpty(materialName)) {
                return Game.Instance.getBrickMaterial(materialName);
            }
            BrickColor brickColor = BrickColorPalette.Get(ValidColor(colorId));
            return brickColor != null ? brickColor.material : null;
        }
    }
}
