using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// What a FluidSimulation sees of the world and does to it: the game's (GameFluidWorld), or a test's.
    /// </summary>
    public interface IFluidWorld
    {
        /// <summary>The block there, BlockType.NULL where the world isn't loaded.</summary>
        BlockType GetBlockType(Vector3Int block);

        /// <summary>Changes a block (the fluid flowing, drying up, hardening).</summary>
        void SetBlock(Vector3Int block, BlockType type);

        /// <summary>Bricks fluids can't wash away are in that block.</summary>
        bool HasBricksThatStay(Vector3Int block);

        /// <summary>A fluid comes into that block: the bricks it washes away there go (dropped, or burnt).</summary>
        void WashAway(Vector3Int block, bool burns);
    }
}
