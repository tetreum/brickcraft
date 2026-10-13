using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Makes fluids flow (see Fluid) in a world (see IFluidWorld: the game's is FluidSimulator's), like Minecraft's
    /// water: blocks next to a change are looked at once their fluid's time passes (FlowInfo.seconds), so still
    /// water costs nothing.
    ///
    /// Flowing blocks keep their level: one more than the lowest of the same fluid that spreads into them (a
    /// source, or a flowing block that can't go down), falling if the fluid is above them, a source if two sources
    /// are beside them and below them is solid or a source; with none, they dry up. Then they spread: down first
    /// (falling, as far as it goes), and sideways one level weaker, up to its reach (what can't go down only, and
    /// sources with three sources beside them), toward the nearest way down within FindDownReach blocks, or every way if there's none.
    /// Where a fluid that hardens touches the one it hardens with (see HardenInfo: lava and water), it becomes a
    /// block, and what of the other it flows down onto too.
    /// Fluids don't go into bricks, but wash away those the world says they do (see IFluidWorld.WashAway), nor
    /// into the world where it isn't loaded: they go on once it is (see ChunkLoaded).
    /// </summary>
    public class FluidSimulation
    {
        /// <summary>How far sideways a way down is looked for, to flow toward it.</summary>
        public const int FindDownReach = 4;
        private const int NoWayDown = 1000;

        private static readonly Vector3Int[] Sideways = {
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
        };
        private static readonly Vector3Int Up = Vector3Int.up;

        private readonly IFluidWorld world;
        // seconds, what fluids' times are counted in
        private readonly Func<float> clock;

        // blocks to look at, and when
        private readonly Dictionary<Vector3Int, float> scheduled = new Dictionary<Vector3Int, float>();
        private readonly List<Vector3Int> due = new List<Vector3Int>();
        // blocks that wanted to flow into a chunk that isn't loaded, by that chunk
        private readonly Dictionary<Vector2Int, HashSet<Vector3Int>> waiting = new Dictionary<Vector2Int, HashSet<Vector3Int>>();

        public FluidSimulation(IFluidWorld world, Func<float> clock) {
            this.world = world;
            this.clock = clock;
        }

        /// <summary>Blocks waiting to be looked at.</summary>
        public int ScheduledCount {
            get { return scheduled.Count; }
        }

        /// <summary>Forgets what was going on (the world was left).</summary>
        public void Clear() {
            scheduled.Clear();
            waiting.Clear();
        }

        /// <summary>A block changed: the fluid in and around it is looked at.</summary>
        public void BlockChanged(Vector3Int block) {
            schedule(block);
            schedule(block + Up);
            schedule(block - Up);
            foreach (Vector3Int side in Sideways) {
                schedule(block + side);
            }
        }

        /// <summary>Bricks were placed or removed there (cells, max exclusive): the fluid around their blocks is looked at.</summary>
        public void CellsChanged(Vector3Int cellsMin, Vector3Int cellsMax) {
            Vector3Int min = BrickGrid.CellToBlock(cellsMin), max = BrickGrid.CellToBlock(cellsMax - Vector3Int.one);
            for (int x = min.x; x <= max.x; x++) {
                for (int y = min.y; y <= max.y; y++) {
                    for (int z = min.z; z <= max.z; z++) {
                        BlockChanged(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        /// <summary>A chunk was loaded: what waited for it goes on, and the flowing blocks it was saved with (a server stopped mid-flow).</summary>
        public void ChunkLoaded(Vector2Int chunk, IEnumerable<Vector3Int> savedFlowing) {
            if (waiting.TryGetValue(chunk, out HashSet<Vector3Int> blocks)) {
                waiting.Remove(chunk);
                foreach (Vector3Int block in blocks) {
                    BlockChanged(block);
                }
            }
            // the sources beside them too, which may not have spread everywhere yet
            foreach (Vector3Int block in savedFlowing) {
                BlockChanged(block);
            }
        }

        /// <summary>The blocks whose time came are looked at.</summary>
        public void Update() {
            if (scheduled.Count == 0) {
                return;
            }
            float now = clock();
            due.Clear();
            foreach (KeyValuePair<Vector3Int, float> entry in scheduled) {
                if (entry.Value <= now) {
                    due.Add(entry.Key);
                }
            }
            foreach (Vector3Int block in due) {
                scheduled.Remove(block);
            }
            foreach (Vector3Int block in due) {
                update(block);
            }
        }

        /// <summary>Whether that block is of a fluid, not a source (what a loaded chunk's saved changes are looked through for).</summary>
        public static bool IsFlowing(byte id) {
            BlockDefinition definition = BlockDatabase.Get(id);
            return definition.fluid != null && definition.fluidLevel != 0;
        }

        // a block of a fluid that flows is looked at once its fluid's time passes
        private void schedule(Vector3Int block) {
            BlockDefinition definition = definitionAt(block);
            if (definition == null || definition.fluid == null || scheduled.ContainsKey(block)) {
                return;
            }
            scheduled[block] = clock() + Mathf.Max(0.02f, definition.fluid.flow.seconds);
        }

        private void update(Vector3Int block) {
            BlockDefinition definition = definitionAt(block);
            if (definition == null || definition.fluid == null) {
                return;
            }
            Fluid fluid = definition.fluid;
            int level = definition.fluidLevel;

            // touched by the fluid it hardens with (lava by water): it becomes a block (obsidian, cobblestone)
            if (fluid.hardensWith != null && touches(block, fluid.hardensWith)) {
                BlockDefinition hard = level == 0 ? fluid.hardSource : fluid.hardFlowing;
                if (hard != null) {
                    set(block, (BlockType)hard.id);
                    return;
                }
            }

            // flowing: what feeds it decides its level (its change has it looked at again)
            if (level != 0) {
                int wanted = levelFor(block, fluid);
                if (wanted != level) {
                    set(block, wanted < 0 ? BlockType.Air : (BlockType)fluid.At(wanted).id);
                    return;
                }
            }
            spread(block, fluid, level);
        }

        // the level that block should have: -1 for none (it dries up)
        private int levelFor(Vector3Int block, Fluid fluid) {
            BlockDefinition above = definitionAt(block + Up);
            if (above != null && above.fluid == fluid) {
                return Fluid.Falling;
            }
            int lowest = int.MaxValue;
            int sources = 0;
            foreach (Vector3Int side in Sideways) {
                Vector3Int next = block + side;
                BlockDefinition neighbour = definitionAt(next);
                if (neighbour == null || neighbour.fluid != fluid) {
                    continue;
                }
                if (neighbour.fluidLevel == 0) {
                    sources++;
                }
                if (spreadsSideways(next, neighbour)) {
                    lowest = Mathf.Min(lowest, neighbour.fluidLevel == Fluid.Falling ? 0 : neighbour.fluidLevel);
                }
            }
            // two sources beside it, on something solid (or a source): a source of its own
            if (sources >= 2) {
                BlockDefinition below = definitionAt(block - Up);
                if (below != null && (below.fluid == fluid ? below.fluidLevel == 0 : !canFlowInto(block - Up, fluid))) {
                    return 0;
                }
            }
            if (lowest == int.MaxValue || lowest + 1 > fluid.Reach) {
                return -1;
            }
            return lowest + 1;
        }

        // where it can't go down; where it can, only sources in a body of them (three beside it), like Minecraft's
        private bool spreadsSideways(Vector3Int block, BlockDefinition definition) {
            if (!canFlowInto(block - Up, definition.fluid)) {
                return true;
            }
            return definition.fluidLevel == 0 && sourcesBeside(block, definition.fluid) >= 3;
        }

        private int sourcesBeside(Vector3Int block, Fluid fluid) {
            int sources = 0;
            foreach (Vector3Int side in Sideways) {
                BlockDefinition neighbour = definitionAt(block + side);
                if (neighbour != null && neighbour.fluid == fluid && neighbour.fluidLevel == 0) {
                    sources++;
                }
            }
            return sources;
        }

        // the other fluid is beside or above it
        private bool touches(Vector3Int block, Fluid other) {
            BlockDefinition above = definitionAt(block + Up);
            if (above != null && above.fluid == other) {
                return true;
            }
            foreach (Vector3Int side in Sideways) {
                BlockDefinition neighbour = definitionAt(block + side);
                if (neighbour != null && neighbour.fluid == other) {
                    return true;
                }
            }
            return false;
        }

        private void spread(Vector3Int block, Fluid fluid, int level) {
            Vector3Int below = block - Up;
            // flowing down onto the fluid it hardens with (lava onto water): that one hardens (stone)
            BlockDefinition under = definitionAt(below);
            if (fluid.hardensWith != null && fluid.hardBelow != null && under != null && under.fluid == fluid.hardensWith) {
                set(below, (BlockType)fluid.hardBelow.id);
                return;
            }
            if (canFlowInto(below, fluid)) {
                BlockDefinition there = definitionAt(below);
                if (there.fluid != fluid || there.fluidLevel != Fluid.Falling) {
                    set(below, (BlockType)fluid.falling.id);
                }
                // what can go down doesn't spread sideways, but sources in a body of them (a lake at a drop)
                if (level != 0 || sourcesBeside(block, fluid) < 3) {
                    return;
                }
            }
            int spreadLevel = (level == Fluid.Falling ? 0 : level) + 1;
            if (spreadLevel > fluid.Reach) {
                return;
            }
            // toward the nearest way down, or every way: weighed among every way it can go (filled ones too, so it
            // doesn't turn elsewhere once those are), it fills the lightest ones that aren't yet
            int[] weights = new int[Sideways.Length];
            int lightest = int.MaxValue;
            for (int i = 0; i < Sideways.Length; i++) {
                Vector3Int next = block + Sideways[i];
                weights[i] = canFlowInto(next, fluid) ? distanceDown(next, fluid, -Sideways[i]) : int.MaxValue;
                lightest = Mathf.Min(lightest, weights[i]);
            }
            if (lightest == int.MaxValue) {
                return;
            }
            BlockType flowing = (BlockType)fluid.At(spreadLevel).id;
            for (int i = 0; i < Sideways.Length; i++) {
                if (weights[i] == lightest && canSpreadInto(block + Sideways[i], fluid, spreadLevel)) {
                    set(block + Sideways[i], flowing);
                }
            }
        }

        // blocks from there to a way down (0: right below it), walking where the fluid could, not back where it came from
        private int distanceDown(Vector3Int from, Fluid fluid, Vector3Int back) {
            Queue<(Vector3Int block, int distance)> open = new Queue<(Vector3Int block, int distance)>();
            HashSet<Vector3Int> seen = new HashSet<Vector3Int>() { from, from + back };
            open.Enqueue((from, 0));
            while (open.Count > 0) {
                (Vector3Int block, int distance) = open.Dequeue();
                if (canFlowInto(block - Up, fluid)) {
                    return distance;
                }
                if (distance >= FindDownReach) {
                    continue;
                }
                foreach (Vector3Int side in Sideways) {
                    Vector3Int next = block + side;
                    if (seen.Add(next) && canFlowInto(next, fluid)) {
                        open.Enqueue((next, distance + 1));
                    }
                }
            }
            return NoWayDown;
        }

        // sideways at that level: where it can go, unless the same fluid is already as strong there
        private bool canSpreadInto(Vector3Int block, Fluid fluid, int level) {
            if (!canFlowInto(block, fluid)) {
                return false;
            }
            BlockDefinition there = definitionAt(block);
            return there.fluid != fluid || (there.fluidLevel != Fluid.Falling && there.fluidLevel > level);
        }

        // air (without bricks that stay in it), something it washes over (replaceable), or the same fluid flowing
        private bool canFlowInto(Vector3Int block, Fluid fluid) {
            BlockType type = world.GetBlockType(block);
            if (type == BlockType.NULL) {
                waitFor(block);
                return false;
            }
            BlockDefinition definition = BlockDatabase.Get(type);
            if (definition.fluid != null) {
                return definition.fluid == fluid && definition.fluidLevel != 0;
            }
            if (type != BlockType.Air && (!definition.isReplaceable || definition.isFluid)) {
                return false;
            }
            return !world.HasBricksThatStay(block);
        }

        private BlockDefinition definitionAt(Vector3Int block) {
            BlockType type = world.GetBlockType(block);
            return type == BlockType.NULL ? null : BlockDatabase.Get(type);
        }

        // the change has the blocks around looked at (the game's world tells it too, see FluidSimulator: once is kept)
        private void set(Vector3Int block, BlockType type) {
            Fluid fluid = BlockDatabase.Get((byte)type).fluid;
            if (fluid != null) {
                world.WashAway(block, fluid.flow.burns);
            }
            world.SetBlock(block, type);
            BlockChanged(block);
        }

        // flowing toward a chunk that isn't there yet: the block next to it goes on once it's loaded
        private void waitFor(Vector3Int block) {
            Vector2Int chunk = new Vector2Int(block.x >> 4, block.z >> 4);
            if (!waiting.TryGetValue(chunk, out HashSet<Vector3Int> blocks)) {
                waiting[chunk] = blocks = new HashSet<Vector3Int>();
            }
            blocks.Add(block);
        }
    }
}
