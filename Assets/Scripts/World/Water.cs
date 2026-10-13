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

        /// <summary>How fast currents push what's in them (players, NPCs, items on the ground), meters a second (Minecraft's).</summary>
        public const float CurrentSpeed = 1.39f;

        /// <summary>
        /// Where flowing fluid at that point pushes what's in it (meters a second; none in sources and still
        /// water, nor outside fluids): from where it's higher to where it's lower or flows out, down where it falls.
        /// </summary>
        public static Vector3 CurrentAt(Vector3 position) {
            WorldBehaviour world = WorldBehaviour.Instance;
            if (world == null) {
                return Vector3.zero;
            }
            Vector3Int block = BrickGrid.CellToBlock(BrickGrid.WorldToCell(position));
            BlockType type = world.GetBlockType(block);
            if (type == BlockType.NULL) {
                return Vector3.zero;
            }
            BlockDefinition definition = BlockDatabase.Get(type);
            if (definition.fluid == null || definition.fluidLevel == 0 || !IsFluidBlockAt(position)) {
                return Vector3.zero;
            }
            float height = Fluid.HeightOf(definition.fluidLevel);
            Vector3 flow = Vector3.zero;
            foreach (BlockSide side in new[] { BlockSide.Front, BlockSide.Back, BlockSide.Left, BlockSide.Right }) {
                Vector3Int direction = BlockShape.SideDirections[(int)side];
                BlockType neighbourType = world.GetBlockType(block + direction);
                if (neighbourType == BlockType.NULL) {
                    continue;
                }
                BlockDefinition neighbour = BlockDatabase.Get(neighbourType);
                if (neighbour.fluid == definition.fluid) {
                    flow += (Vector3)direction * (height - Fluid.HeightOf(neighbour.fluidLevel));
                } else if (neighbourType == BlockType.Air || (neighbour.isReplaceable && !neighbour.isFluid)) {
                    flow += (Vector3)direction * height; // it flows out there
                }
            }
            flow.y = 0;
            Vector3 current = flow.sqrMagnitude > 0.0001f ? flow.normalized * CurrentSpeed : Vector3.zero;
            if (definition.fluidLevel == Fluid.Falling) {
                current.y = -CurrentSpeed;
            }
            return current;
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
