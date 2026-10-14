using Unity.Mathematics;

namespace Brickcraft.Npcs.Navigation
{
    /// <summary>
    /// Where something can stand in a column of the brick grid (see NavGrid): the first free cell above solid
    /// ground (floor), and the first solid cell above it (ceiling). Cells are studs sideways and plates up.
    /// </summary>
    public struct NavSpan
    {
        /// <summary>Its floor is under a fluid (water, lava): those that walk keep out.</summary>
        public const byte Fluid = 1;

        public int floor;
        public int ceiling;
        public byte flags;

        public bool IsFluid {
            get { return (flags & Fluid) != 0; }
        }
    }

    /// <summary>What an NPC needs to stand and move somewhere, in cells (see NavGrid.AgentFor).</summary>
    public struct NavAgent
    {
        /// <summary>Columns on each side of its center its body takes (0: one column, 1: 3 x 3...).</summary>
        public int radius;
        /// <summary>Cells of headroom it needs.</summary>
        public int height;
        /// <summary>How many cells it steps up, and how many it drops down at most.</summary>
        public int step;
        public int drop;
        public bool avoidFluids;
    }

    /// <summary>A path's outcome.</summary>
    public enum NavResult : byte
    {
        /// <summary>It can't even stand where it is (or nothing there is known yet).</summary>
        Failed = 0,
        /// <summary>It gets to the goal (within the range asked).</summary>
        Found = 1,
        /// <summary>It can't get there: the path goes as close as it can.</summary>
        Partial = 2,
    }

    internal struct NavSpanEntry
    {
        public int2 column;
        public NavSpan span;
    }
}
