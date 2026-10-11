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

        /// <summary>Whether that point is in a fluid world block (not a water brick).</summary>
        public static bool IsFluidBlockAt(Vector3 position) {
            if (WorldBehaviour.Instance == null) {
                return false;
            }
            BlockType type = WorldBehaviour.Instance.GetBlockType(BrickGrid.CellToBlock(BrickGrid.WorldToCell(position)));
            return type != BlockType.NULL && BlockDatabase.Get(type).isFluid;
        }
    }
}
