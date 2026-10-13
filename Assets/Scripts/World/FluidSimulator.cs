using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Makes fluids flow (see Fluid), on the server, like Minecraft's water: blocks next to a change are looked at
    /// once their fluid's time passes (FlowInfo.seconds), so still water costs nothing.
    ///
    /// Flowing blocks keep their level: one more than the lowest of the same fluid that spreads into them (a
    /// source, or a flowing block that can't go down), falling if the fluid is above them, a source if two sources
    /// are beside them and below them is solid or a source; with none, they dry up. Then they spread: down first
    /// (falling, as far as it goes), and sideways one level weaker, up to its reach (sources and what can't go
    /// down only), toward the nearest way down within FindDownReach blocks, or every way if there's none.
    /// Where a fluid that hardens touches the one it hardens with (see HardenInfo: lava and water), it becomes a
    /// block, and what of the other it flows down onto too.
    /// Fluids don't go into bricks, but wash away those tagged "washable" (a torch: dropped, the current carries them;
    /// those that burn, lava, destroy them),
    /// nor into chunks that aren't loaded: they go on when those load. Flowing blocks of a
    /// chunk that loads (saved flowing, the server stopped) go on too.
    /// </summary>
    public static class FluidSimulator
    {
        /// <summary>How far sideways a way down is looked for, to flow toward it.</summary>
        public const int FindDownReach = 4;
        private const int NoWayDown = 1000;

        private static readonly Vector3Int[] Sideways = {
            new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
        };
        private static readonly Vector3Int Up = Vector3Int.up;

        // blocks to look at, and when
        private static readonly Dictionary<Vector3Int, float> scheduled = new Dictionary<Vector3Int, float>();
        private static readonly List<Vector3Int> due = new List<Vector3Int>();
        // blocks that wanted to flow into a chunk that isn't loaded, by that chunk
        private static readonly Dictionary<Vector2Int, HashSet<Vector3Int>> waiting = new Dictionary<Vector2Int, HashSet<Vector3Int>>();

        /// <summary>Forgets what was going on (the world was left).</summary>
        public static void Clear() {
            scheduled.Clear();
            waiting.Clear();
        }

        /// <summary>A block changed (see WorldNetwork.ServerSetBlock): the fluid in and around it is looked at.</summary>
        public static void BlockChanged(Vector3Int block) {
            schedule(block);
            schedule(block + Up);
            schedule(block - Up);
            foreach (Vector3Int side in Sideways) {
                schedule(block + side);
            }
        }

        /// <summary>Bricks were placed or removed there (cells, max exclusive): the fluid around their blocks is looked at.</summary>
        public static void CellsChanged(Vector3Int cellsMin, Vector3Int cellsMax) {
            Vector3Int min = BrickGrid.CellToBlock(cellsMin), max = BrickGrid.CellToBlock(cellsMax - Vector3Int.one);
            for (int x = min.x; x <= max.x; x++) {
                for (int y = min.y; y <= max.y; y++) {
                    for (int z = min.z; z <= max.z; z++) {
                        BlockChanged(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        /// <summary>The server's frame: the blocks whose time came are looked at.</summary>
        public static void ServerUpdate() {
            WorldBehaviour world = WorldBehaviour.Instance;
            if (world == null || scheduled.Count == 0) {
                return;
            }
            float now = Time.time;
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
                update(world, block);
            }
        }

        // a block of a fluid that flows is looked at once its fluid's time passes
        private static void schedule(Vector3Int block) {
            BlockDefinition definition = definitionAt(WorldBehaviour.Instance, block);
            if (definition == null || definition.fluid == null || scheduled.ContainsKey(block)) {
                return;
            }
            scheduled[block] = Time.time + Mathf.Max(0.02f, definition.fluid.flow.seconds);
        }

        private static void update(WorldBehaviour world, Vector3Int block) {
            BlockDefinition definition = definitionAt(world, block);
            if (definition == null || definition.fluid == null) {
                return;
            }
            Fluid fluid = definition.fluid;
            int level = definition.fluidLevel;

            // touched by the fluid it hardens with (lava by water): it becomes a block (obsidian, cobblestone)
            if (fluid.hardensWith != null && touches(world, block, fluid.hardensWith)) {
                BlockDefinition hard = level == 0 ? fluid.hardSource : fluid.hardFlowing;
                if (hard != null) {
                    set(block, (BlockType)hard.id);
                    return;
                }
            }

            // flowing: what feeds it decides its level (its change has it looked at again)
            if (level != 0) {
                int wanted = levelFor(world, block, fluid);
                if (wanted != level) {
                    set(block, wanted < 0 ? BlockType.Air : (BlockType)fluid.At(wanted).id);
                    return;
                }
            }
            spread(world, block, fluid, level);
        }

        // the level that block should have: -1 for none (it dries up)
        private static int levelFor(WorldBehaviour world, Vector3Int block, Fluid fluid) {
            BlockDefinition above = definitionAt(world, block + Up);
            if (above != null && above.fluid == fluid) {
                return Fluid.Falling;
            }
            int lowest = int.MaxValue;
            int sources = 0;
            foreach (Vector3Int side in Sideways) {
                Vector3Int next = block + side;
                BlockDefinition neighbour = definitionAt(world, next);
                if (neighbour == null || neighbour.fluid != fluid) {
                    continue;
                }
                if (neighbour.fluidLevel == 0) {
                    sources++;
                }
                if (spreadsSideways(world, next, neighbour)) {
                    lowest = Mathf.Min(lowest, neighbour.fluidLevel == Fluid.Falling ? 0 : neighbour.fluidLevel);
                }
            }
            // two sources beside it, on something solid (or a source): a source of its own
            if (sources >= 2) {
                BlockDefinition below = definitionAt(world, block - Up);
                if (below != null && (below.fluid == fluid ? below.fluidLevel == 0 : !canFlowInto(world, block - Up, fluid))) {
                    return 0;
                }
            }
            if (lowest == int.MaxValue || lowest + 1 > fluid.Reach) {
                return -1;
            }
            return lowest + 1;
        }

        // sources always do; what flows only where it can't go down
        private static bool spreadsSideways(WorldBehaviour world, Vector3Int block, BlockDefinition definition) {
            return definition.fluidLevel == 0 || !canFlowInto(world, block - Up, definition.fluid);
        }

        // the other fluid is beside or above it
        private static bool touches(WorldBehaviour world, Vector3Int block, Fluid other) {
            BlockDefinition above = definitionAt(world, block + Up);
            if (above != null && above.fluid == other) {
                return true;
            }
            foreach (Vector3Int side in Sideways) {
                BlockDefinition neighbour = definitionAt(world, block + side);
                if (neighbour != null && neighbour.fluid == other) {
                    return true;
                }
            }
            return false;
        }

        private static void spread(WorldBehaviour world, Vector3Int block, Fluid fluid, int level) {
            Vector3Int below = block - Up;
            // flowing down onto the fluid it hardens with (lava onto water): that one hardens (stone)
            BlockDefinition under = definitionAt(world, below);
            if (fluid.hardensWith != null && fluid.hardBelow != null && under != null && under.fluid == fluid.hardensWith) {
                set(below, (BlockType)fluid.hardBelow.id);
                return;
            }
            if (canFlowInto(world, below, fluid)) {
                BlockDefinition there = definitionAt(world, below);
                if (there.fluid != fluid || there.fluidLevel != Fluid.Falling) {
                    set(below, (BlockType)fluid.falling.id);
                }
                if (level != 0) {
                    return; // flowing water that can go down doesn't spread sideways
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
                weights[i] = canFlowInto(world, next, fluid) ? distanceDown(world, next, fluid, -Sideways[i]) : int.MaxValue;
                lightest = Mathf.Min(lightest, weights[i]);
            }
            if (lightest == int.MaxValue) {
                return;
            }
            BlockType flowing = (BlockType)fluid.At(spreadLevel).id;
            for (int i = 0; i < Sideways.Length; i++) {
                if (weights[i] == lightest && canSpreadInto(world, block + Sideways[i], fluid, spreadLevel)) {
                    set(block + Sideways[i], flowing);
                }
            }
        }

        // blocks from there to a way down (0: right below it), walking where the fluid could, not back where it came from
        private static int distanceDown(WorldBehaviour world, Vector3Int from, Fluid fluid, Vector3Int back) {
            Queue<(Vector3Int block, int distance)> open = new Queue<(Vector3Int block, int distance)>();
            HashSet<Vector3Int> seen = new HashSet<Vector3Int>() { from, from + back };
            open.Enqueue((from, 0));
            while (open.Count > 0) {
                (Vector3Int block, int distance) = open.Dequeue();
                if (canFlowInto(world, block - Up, fluid)) {
                    return distance;
                }
                if (distance >= FindDownReach) {
                    continue;
                }
                foreach (Vector3Int side in Sideways) {
                    Vector3Int next = block + side;
                    if (seen.Add(next) && canFlowInto(world, next, fluid)) {
                        open.Enqueue((next, distance + 1));
                    }
                }
            }
            return NoWayDown;
        }

        // sideways at that level: where it can go, unless the same fluid is already as strong there
        private static bool canSpreadInto(WorldBehaviour world, Vector3Int block, Fluid fluid, int level) {
            if (!canFlowInto(world, block, fluid)) {
                return false;
            }
            BlockDefinition there = definitionAt(world, block);
            return there.fluid != fluid || (there.fluidLevel != Fluid.Falling && there.fluidLevel > level);
        }

        // air (without bricks in it), something it washes over (replaceable), or the same fluid flowing
        private static bool canFlowInto(WorldBehaviour world, Vector3Int block, Fluid fluid) {
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
            return isFreeOfBricks(block, fluid);
        }

        // no bricks there, or only some it washes away
        private static bool isFreeOfBricks(Vector3Int block, Fluid fluid) {
            foreach (Brick brick in bricksIn(block)) {
                if (!isWashable(brick)) {
                    return false;
                }
            }
            return true;
        }

        private static bool isWashable(Brick brick) {
            return brick.item != null && brick.item.tags.Contains(Item.WashableTag) && brick.attachedTo == null && Server.attachmentOf(brick) == null;
        }

        // the bricks with a cell in that block
        private static List<Brick> bricksIn(Vector3Int block) {
            List<Brick> found = new List<Brick>();
            Vector3Int min = BrickGrid.BlockToCell(block);
            Vector3Int max = BrickGrid.BlockToCell(block + Vector3Int.one);
            for (int x = min.x; x < max.x; x++) {
                for (int y = min.y; y < max.y; y++) {
                    for (int z = min.z; z < max.z; z++) {
                        Brick brick = BrickGrid.GetBrickAt(new Vector3Int(x, y, z));
                        if (brick != null && !found.Contains(brick)) {
                            found.Add(brick);
                        }
                    }
                }
            }
            return found;
        }

        // the fluid comes in: what it washes away there goes, dropped (it floats along the current), or burnt (lava)
        private static void washAway(Vector3Int block, Fluid fluid) {
            foreach (Brick brick in bricksIn(block)) {
                if (!isWashable(brick)) {
                    continue;
                }
                Vector3 at = brick.placement.WorldBounds.center;
                foreach (Brick removed in WorldNetwork.ServerRemoveBrick(brick)) {
                    if (!fluid.flow.burns) {
                        DroppedItems.ServerDrop(removed.itemId, removed.color, 1, 0, at, Vector3.zero);
                    }
                }
            }
        }

        private static BlockDefinition definitionAt(WorldBehaviour world, Vector3Int block) {
            if (world == null) {
                return null;
            }
            BlockType type = world.GetBlockType(block);
            return type == BlockType.NULL ? null : BlockDatabase.Get(type);
        }

        private static void set(Vector3Int block, BlockType type) {
            Fluid fluid = BlockDatabase.Get((byte)type).fluid;
            if (fluid != null) {
                washAway(block, fluid);
            }
            WorldNetwork.ServerSetBlock(block, type, null);
        }

        // flowing toward a chunk that isn't there yet: the block next to it goes on once it's loaded
        private static void waitFor(Vector3Int block) {
            Vector2Int chunk = new Vector2Int(block.x >> 4, block.z >> 4);
            if (!waiting.TryGetValue(chunk, out HashSet<Vector3Int> blocks)) {
                waiting[chunk] = blocks = new HashSet<Vector3Int>();
            }
            blocks.Add(block);
        }

        /// <summary>A chunk was loaded on the server (see WorldNetwork): what waited for it, and what was flowing in it, goes on.</summary>
        public static void ChunkGenerated(Chunk chunk) {
            Vector2Int coords = new Vector2Int(chunk.X, chunk.Z);
            if (waiting.TryGetValue(coords, out HashSet<Vector3Int> blocks)) {
                waiting.Remove(coords);
                foreach (Vector3Int block in blocks) {
                    BlockChanged(block);
                }
            }
            // what was flowing when it was saved (flowing blocks only come from changes)
            foreach (Vector3Int block in WorldNetwork.ServerChangedBlocks(coords, isFlowing)) {
                schedule(block);
            }
        }

        private static bool isFlowing(byte id) {
            BlockDefinition definition = BlockDatabase.Get(id);
            return definition.fluid != null && definition.fluidLevel != 0;
        }
    }
}
