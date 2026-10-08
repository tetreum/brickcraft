namespace Brickcraft.World
{
    /// <summary>Gameplay properties of world block types.</summary>
    public static class Blocks
    {
        public const float Unbreakable = -1;

        /// <summary>Can a brick be placed where this block is?</summary>
        public static bool IsReplaceable(BlockType type) {
            switch (type) {
                case BlockType.Air:
                case BlockType.Water:
                case BlockType.Still_Water:
                case BlockType.NULL: // outside the generated world
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Seconds needed to dig the block with bare hands.</summary>
        public static float GetHardness(BlockType type) {
            switch (type) {
                case BlockType.Air:
                case BlockType.NULL:
                case BlockType.Bedrock:
                case BlockType.Water:
                case BlockType.Still_Water:
                case BlockType.Lava:
                case BlockType.Still_Lava:
                    return Unbreakable;
                case BlockType.Leaves:
                case BlockType.TallGrass:
                case BlockType.Flower:
                case BlockType.Rose:
                    return 0.5f;
                case BlockType.Dirt:
                case BlockType.Grass:
                case BlockType.Sand:
                case BlockType.Gravel:
                case BlockType.Snow:
                case BlockType.Snow_Block:
                    return 1f;
                case BlockType.Wood:
                case BlockType.Wood_Planks:
                case BlockType.Cactus:
                    return 2f;
                default:
                    return 3f; // stone, ores...
            }
        }

        public static bool IsBreakable(BlockType type) {
            return GetHardness(type) != Unbreakable;
        }
    }
}
