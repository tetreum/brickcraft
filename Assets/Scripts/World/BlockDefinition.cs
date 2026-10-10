using System;

namespace Brickcraft.World
{
    /// <summary>
    /// Everything the game knows about a block type. Built by <see cref="BlockDatabase"/>
    /// from the block's folder and never changed afterwards, so the chunk threads can read it.
    /// </summary>
    public class BlockDefinition
    {
        /// <summary>Its number in this game: what chunks store, given when the game starts and never saved.</summary>
        public byte id;
        /// <summary>Identifies it in saves and the network, see Slugs.</summary>
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

        /// <summary>No block has this number in this game.</summary>
        public bool isUnknown;

        /// <summary>Sky light goes through it (air, leaves, water...).</summary>
        public bool isTransparent;

        /// <summary>
        /// Drawn see-through (water), with the terrain's translucent material: what's behind it is
        /// drawn too, and its own sides only where they touch air.
        /// </summary>
        public bool isTranslucent;

        /// <summary>A liquid (water, lava): NPCs that walk keep out of it.</summary>
        public bool isFluid;

        /// <summary>Item given when dug (its slug, see Slugs), null for none.</summary>
        public string dropItemId;

        /// <summary>Item defined by this block's folder (its slug), null for none.</summary>
        public string itemId;

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
}
