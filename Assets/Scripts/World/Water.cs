using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Where there's water (or any liquid): world blocks that are fluid (see BlockInfo.fluid) and water bricks (on
    /// the Water layer). Fluid blocks have no collider (see ChunkRenderer): things go into them, and what's in
    /// them behaves like it (players swim, see FirstPersonController; items on the ground float, see DroppedItem).
    /// </summary>
    public static class Water
    {
        private const float BrickCheckRadius = 0.05f;

        /// <summary>Whether that point (local position) is in a liquid.</summary>
        public static bool IsAt(Vector3 position) {
            return IsFluidBlockAt(position)
                || Physics.CheckSphere(position, BrickCheckRadius, 1 << (int)Game.Layers.Water, QueryTriggerInteraction.Collide);
        }

        /// <summary>Whether that point is in a fluid world block (not a water brick): flowing ones up to their surface (see Fluid).</summary>
        public static bool IsFluidBlockAt(Vector3 position) {
            if (WorldBehaviour.Instance == null) {
                return false;
            }
            Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(position));
            BlockType type = WorldBehaviour.Instance.GetBlockType(block);
            if (type == BlockType.NULL) {
                return false;
            }
            BlockDefinition definition = BlockDatabase.Get(type);
            if (!definition.isFluid) {
                return false;
            }
            if (definition.fluid == null || definition.fluidLevel == 0 || definition.fluidLevel == Fluid.Falling) {
                return true;
            }
            float bottom = BrickGrid.CellToWorld(BrickGrid.BlockToCell(block)).y;
            return position.y - bottom <= Fluid.HeightOf(definition.fluidLevel) * Server.brickHeight;
        }

        /// <summary>Whether that block is a source of that fluid block's fluid (what buckets pick up), or that block itself.</summary>
        public static bool IsSourceOf(BlockType there, BlockType liquid) {
            if (there == liquid) {
                return true;
            }
            if (there == BlockType.NULL || liquid == BlockType.NULL) {
                return false;
            }
            BlockDefinition block = BlockDatabase.Get(there), liquidBlock = BlockDatabase.Get(liquid);
            return liquidBlock.fluid != null && block.fluid == liquidBlock.fluid && block.fluidLevel == 0;
        }
    }
}
