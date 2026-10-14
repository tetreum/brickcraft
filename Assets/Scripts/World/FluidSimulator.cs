using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// The game's fluids flowing, on the server: a FluidSimulation of its world (GameFluidWorld), told about every
    /// block that changes (see WorldNetwork.ServerSetBlock) and the chunks that load, looking at blocks each frame.
    /// </summary>
    public static class FluidSimulator
    {
        /// <summary>The game's simulation.</summary>
        public static readonly FluidSimulation Game = new FluidSimulation(new GameFluidWorld(), () => Time.time);

        /// <summary>Forgets what was going on (the world was left).</summary>
        public static void Clear() {
            Game.Clear();
        }

        /// <summary>A block changed (see WorldNetwork.ServerSetBlock): the fluid in and around it is looked at.</summary>
        public static void BlockChanged(Vector3Int block) {
            Game.BlockChanged(block);
        }

        /// <summary>Bricks were placed or removed there (cells, max exclusive): the fluid around their blocks is looked at.</summary>
        public static void CellsChanged(Vector3Int cellsMin, Vector3Int cellsMax) {
            Game.CellsChanged(cellsMin, cellsMax);
        }

        /// <summary>The server's frame: the blocks whose time came are looked at.</summary>
        public static void ServerUpdate() {
            if (WorldBehaviour.Instance != null) {
                Game.Update();
            }
        }

        /// <summary>A chunk was loaded on the server (see WorldNetwork): what waited for it, and what was flowing in it, goes on.</summary>
        public static void ChunkGenerated(Chunk chunk) {
            Vector2Int coords = new Vector2Int(chunk.X, chunk.Z);
            Game.ChunkLoaded(coords, WorldNetwork.ServerChangedBlocks(coords, FluidSimulation.IsFlowing));
        }
    }
}
