using System.Collections.Generic;
using Brickcraft.Mods;
using Brickcraft.World;
using UnityEngine;

namespace Brickcraft.Tests
{
    /// <summary>The game's items and blocks (water, lava...), loaded once for every test that needs them.</summary>
    public static class GameData
    {
        private static bool loaded;

        public static void Load() {
            if (!loaded) {
                ItemDatabase.Load(new ModInfo[0]);
                loaded = true;
            }
        }

        public static BlockType Block(string name) {
            Load();
            return (BlockType)BlockDatabase.IdOf(name);
        }

        public static BlockDefinition Definition(BlockType type) {
            return BlockDatabase.Get(type);
        }
    }

    /// <summary>
    /// A world of blocks in memory for a FluidSimulation: air wherever nothing was set, not loaded (NULL) in the
    /// chunks marked so, bricks that stay or wash away where told, and a clock moved by hand.
    /// </summary>
    public class TestWorld : IFluidWorld
    {
        public readonly Dictionary<Vector3Int, BlockType> blocks = new Dictionary<Vector3Int, BlockType>();
        public readonly HashSet<Vector2Int> unloadedChunks = new HashSet<Vector2Int>();
        public readonly HashSet<Vector3Int> bricksThatStay = new HashSet<Vector3Int>();
        public readonly HashSet<Vector3Int> washableBricks = new HashSet<Vector3Int>();
        /// <summary>What was washed away, and whether burnt.</summary>
        public readonly Dictionary<Vector3Int, bool> washed = new Dictionary<Vector3Int, bool>();

        public float now;
        public readonly FluidSimulation simulation;

        public TestWorld() {
            simulation = new FluidSimulation(this, () => now);
        }

        public BlockType GetBlockType(Vector3Int block) {
            if (unloadedChunks.Contains(new Vector2Int(block.x >> 4, block.z >> 4))) {
                return BlockType.NULL;
            }
            return blocks.TryGetValue(block, out BlockType type) ? type : BlockType.Air;
        }

        public void SetBlock(Vector3Int block, BlockType type) {
            blocks[block] = type;
        }

        public bool HasBricksThatStay(Vector3Int block) {
            return bricksThatStay.Contains(block);
        }

        public void WashAway(Vector3Int block, bool burns) {
            if (washableBricks.Remove(block)) {
                washed[block] = burns;
            }
        }

        /// <summary>A block changed by someone (a player, a test): the simulation hears about it, like the game's.</summary>
        public void Change(Vector3Int block, BlockType type) {
            blocks[block] = type;
            simulation.BlockChanged(block);
        }

        /// <summary>A floor of that block from min to max (x and z) at that height.</summary>
        public void Floor(int y, int min, int max, BlockType type) {
            for (int x = min; x <= max; x++) {
                for (int z = min; z <= max; z++) {
                    blocks[new Vector3Int(x, y, z)] = type;
                }
            }
        }

        /// <summary>Runs the simulation for that many seconds, in small steps.</summary>
        public void Run(float seconds) {
            float end = now + seconds;
            while (now < end) {
                now += 0.05f;
                simulation.Update();
            }
        }

        public BlockDefinition At(int x, int y, int z) {
            return BlockDatabase.Get(GetBlockType(new Vector3Int(x, y, z)));
        }
    }
}
