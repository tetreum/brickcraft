namespace Brickcraft.World
{
    /// <summary>What a world's terrain is (see WorldStorage.Terrain), made by CustomChunkGenerator.</summary>
    public enum TerrainKind : byte
    {
        /// <summary>Generated from its seed: hills, seas, caves...</summary>
        Generated = 0,
        /// <summary>Grass at the same height everywhere (a tutorial's).</summary>
        Flat = 1,
        /// <summary>Flat grass, and a sea ahead of the spawn (towards +Z) past a sand beach (a boat's tutorial).</summary>
        Coast = 2,
    }
}
