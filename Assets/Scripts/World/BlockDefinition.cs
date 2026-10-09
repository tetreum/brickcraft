using System;

namespace Brickcraft.World
{
    /// <summary>
    /// Everything the game knows about a block type. Built by <see cref="BlockDatabase"/>
    /// from the block's folder and never changed afterwards, so the chunk threads can read it.
    /// </summary>
    public class BlockDefinition
    {
        public byte id;
        public string name;

        /// <summary>
        /// Absolute path of the block's folder, where its block.json and assets (icon, model, texture)
        /// live. Null for engine blocks and ids without a definition.
        /// </summary>
        public string folder;

        /// <summary>Seconds needed to dig it with bare hands.</summary>
        public float hardness;
        public bool isBreakable;

        /// <summary>Bricks can be placed where this block is (air, water...).</summary>
        public bool isReplaceable;

        /// <summary>Sky light goes through it (air, leaves, water...).</summary>
        public bool isTransparent;

        /// <summary>
        /// Drawn see-through (water), with the terrain's translucent material: what's behind it is
        /// drawn too, and its own sides only where they touch air.
        /// </summary>
        public bool isTranslucent;

        /// <summary>Item given when dug, 0 for none.</summary>
        public int dropItemId;

        /// <summary>Item defined by this block's folder, 0 for none.</summary>
        public int itemId;

        /// <summary>Geometry drawn in the terrain mesh.</summary>
        public BlockShape shape;

        /// <summary>Geometry used for the terrain collider.</summary>
        public BlockShape colliderShape;

        public int topTextureLayer;
        public int sideTextureLayer;
        public int bottomTextureLayer;

        /// <summary>Layer of the terrain texture array used by the given side.</summary>
        public int GetTextureLayer(BlockSide side) {
            switch (side) {
                case BlockSide.Top:
                    return topTextureLayer;
                case BlockSide.Bottom:
                    return bottomTextureLayer;
                default:
                    return sideTextureLayer;
            }
        }
    }

    /// <summary>
    /// Shape of a block.json file. Every field but id and name is optional.
    /// </summary>
    [Serializable]
    public class BlockJson
    {
        public int id = -1;
        public string name;
        public float hardness = 1;
        public bool breakable = true;
        public bool replaceable = false;
        public bool transparent = false;
        public bool translucent = false;
        public int dropItemId = 0;
        public BlockItemJson item = new BlockItemJson();
    }

    /// <summary>
    /// Optional item that places this block. Its icon is the block folder's icon.png.
    /// </summary>
    [Serializable]
    public class BlockItemJson
    {
        public int id = 0; // 0 means the block has no item
        public string name;
        public int brickModel = 3003;
        public string material;
        /// <summary>How many fit in one inventory slot.</summary>
        public int maxStack = Item.DefaultMaxStack;
    }
}
