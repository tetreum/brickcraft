using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft.World.Towns
{
    /// <summary>
    /// Puts towns in the world as it's generated (see TownPlanner): the server watches chunks being generated,
    /// and when the last one a town needs exists it places its bricks, as the world's (nobody's hand, saved and
    /// sent to the clients like any other change, see Network.WorldNetwork). Buildings sit on a small pad cut
    /// into the ground, so a town on uneven terrain still reads as one. Only the server does this, and only for
    /// worlds generated from a seed (flat tutorial worlds have none).
    /// </summary>
    public static class TownPlacer
    {
        /// <summary>How much the ground under one building can rise, in blocks, before its site is skipped.</summary>
        private const int MaxSlope = 4;

        // regions whose town is placed (or has none) this session, so it isn't looked at for every chunk
        private static readonly HashSet<long> done = new HashSet<long>();

        /// <summary>A game is starting: nothing is placed yet.</summary>
        public static void Reset() {
            done.Clear();
        }

        /// <summary>A chunk was generated on the server (main thread): a town that needed it may go in now.</summary>
        public static void OnServerChunkGenerated(Chunk chunk) {
            if (WorldNetwork.Terrain != TerrainKind.Generated || WorldBehaviour.Instance == null) {
                return;
            }
            int regionX = BrickGrid.FloorDiv(chunk.X, TownPlanner.SpanChunks);
            int regionZ = BrickGrid.FloorDiv(chunk.Z, TownPlanner.SpanChunks);
            long key = Key(regionX, regionZ);
            if (done.Contains(key)) {
                return;
            }

            List<LDrawImport> buildings = TownBuildings.All;
            TownPlan plan = TownPlanner.Plan(WorldNetwork.Seed, buildings, regionX, regionZ);
            if (plan == null) {
                done.Add(key); // this region has none
                return;
            }
            List<Vector2Int> needed = NeededChunks(plan, buildings);
            foreach (Vector2Int coords in needed) {
                if (!WorldBehaviour.Instance.IsGenerated(coords)) {
                    return; // terrain it stands on isn't all there yet
                }
            }
            done.Add(key);
            if (WorldNetwork.ServerHasGeneratedBricks(needed)) {
                return; // it was placed in a past session (its bricks came back from the save)
            }
            place(WorldNetwork.Seed, plan, buildings, regionX, regionZ);
        }

        /// <summary>
        /// Server: the town of that region, placed if the terrain it stands on is all generated and it isn't
        /// already there (see OnServerChunkGenerated); how many of its buildings went. Nothing for a region
        /// without a town, or one whose ground isn't there yet (see NeededChunks). Also the way a test builds
        /// one, without waiting for the chunks to stream in.
        /// </summary>
        public static int PlaceRegion(int regionX, int regionZ) {
            if (WorldBehaviour.Instance == null) {
                return 0;
            }
            List<LDrawImport> buildings = TownBuildings.All;
            TownPlan plan = TownPlanner.Plan(WorldNetwork.Seed, buildings, regionX, regionZ);
            if (plan == null) {
                return 0;
            }
            List<Vector2Int> needed = NeededChunks(plan, buildings);
            foreach (Vector2Int coords in needed) {
                if (!WorldBehaviour.Instance.IsGenerated(coords)) {
                    return 0;
                }
            }
            if (WorldNetwork.ServerHasGeneratedBricks(needed)) {
                return 0; // already there (a past session)
            }
            return place(WorldNetwork.Seed, plan, buildings, regionX, regionZ);
        }

        // every chunk a town's buildings touch
        public static List<Vector2Int> NeededChunks(TownPlan plan, List<LDrawImport> buildings) {
            List<Vector2Int> chunks = new List<Vector2Int>();
            HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
            foreach (TownBuilding tb in plan.buildings) {
                Vector3Int size = buildings[tb.building].SizeTurned(tb.turns);
                Vector2Int from = ChunkOfCell(tb.corner);
                Vector2Int to = ChunkOfCell(tb.corner + size - Vector3Int.one);
                for (int x = from.x; x <= to.x; x++) {
                    for (int z = from.y; z <= to.y; z++) {
                        Vector2Int coords = new Vector2Int(x, z);
                        if (seen.Add(coords)) {
                            chunks.Add(coords);
                        }
                    }
                }
            }
            return chunks;
        }

        private static int place(long seed, TownPlan plan, List<LDrawImport> buildings, int regionX, int regionZ) {
            Placer placer = Placer.Now(0);
            List<SavedBrick> bricks = new List<SavedBrick>();
            int brickIndex = 0;
            int placedBuildings = 0;

            foreach (TownBuilding tb in plan.buildings) {
                LDrawImport model = buildings[tb.building];
                if (!site(model, tb, out int baseY, out BlockType pad)) {
                    continue; // too rough, or something grows there
                }
                level(model, tb, baseY, pad);
                placedBuildings++;

                List<ImportedBrick> placed = model.Placed(
                    tb.corner + new Vector3Int(0, baseY * BrickGrid.PlatesPerBlock, 0), tb.turns);
                Guid[] ids = new Guid[placed.Count];
                for (int i = 0; i < placed.Count; i++) {
                    ids[i] = TownPlanner.BrickId(seed, regionX, regionZ, brickIndex + i);
                }

                // the bricks, then what hangs in their slots
                for (int i = 0; i < placed.Count; i++) {
                    ImportedBrick brick = placed[i];
                    if (brick.IsAttachment || !Server.items.TryGetValue(brick.itemId, out Item item)
                            || item.type != Item.Type.Brick || item.brickModel == null) {
                        continue;
                    }
                    BrickPlacement placement = new BrickPlacement(item.brickModel, brick.origin, brick.rotation);
                    bricks.Add(new SavedBrick() {
                        id = ids[i], itemId = item.id, color = item.ValidColor(brick.color),
                        origin = brick.origin, rotation = (byte)placement.rotation, placer = placer,
                        state = 0, attachedTo = Guid.Empty,
                    });
                }
                for (int i = 0; i < placed.Count; i++) {
                    ImportedBrick brick = placed[i];
                    if (!brick.IsAttachment || !Server.items.TryGetValue(brick.itemId, out Item item) || item.brickModel == null) {
                        continue;
                    }
                    ImportedBrick holder = placed[brick.attachedTo - 1];
                    bricks.Add(new SavedBrick() {
                        id = ids[i], itemId = item.id, color = item.ValidColor(brick.color),
                        origin = holder.origin, rotation = (byte)holder.rotation, placer = placer,
                        state = Mathf.Clamp(brick.seat, 0, 3), attachedTo = ids[brick.attachedTo - 1],
                    });
                }
                brickIndex += placed.Count;
            }

            if (bricks.Count > 0) {
                WorldNetwork.ServerPlaceGeneratedBricks(bricks);
            }
            return placedBuildings;
        }

        /// <summary>Whether a building fits there, and the block height its floor sits at, and the pad's block.</summary>
        private static bool site(LDrawImport model, TownBuilding tb, out int baseY, out BlockType pad) {
            Vector3Int size = model.SizeTurned(tb.turns);
            Vector3Int from = BrickGrid.CellToBlock(tb.corner);
            Vector3Int to = BrickGrid.CellToBlock(tb.corner + size - Vector3Int.one);
            baseY = -1;
            int lowest = int.MaxValue;
            pad = BlockType.Dirt;

            for (int x = from.x; x <= to.x; x++) {
                for (int z = from.z; z <= to.z; z++) {
                    int surface = SurfaceY(x, z);
                    if (surface <= 0) {
                        return false; // nothing to stand on, or the world isn't there
                    }
                    BlockType under = WorldBehaviour.Instance.GetBlockType(x, surface - 1, z);
                    if (under == BlockType.NULL || BlockDatabase.Get(under).isTransparent) {
                        return false; // water, ice, leaves...: not ground
                    }
                    if (surface > baseY) {
                        baseY = surface;
                        pad = under;
                    }
                    if (surface < lowest) {
                        lowest = surface;
                    }
                }
            }
            if (baseY <= 0 || baseY - lowest > MaxSlope) {
                return false;
            }
            // nothing growing where the building's volume goes
            int height = Mathf.CeilToInt(size.y / (float)BrickGrid.PlatesPerBlock) + 1;
            for (int x = from.x; x <= to.x; x++) {
                for (int z = from.z; z <= to.z; z++) {
                    for (int y = baseY; y < baseY + height; y++) {
                        BlockType type = WorldBehaviour.Instance.GetBlockType(x, y, z);
                        if (type != BlockType.Air && type != BlockType.NULL && !BlockDatabase.Get(type).isReplaceable) {
                            return false; // a tree, or terrain the pad wouldn't clear
                        }
                    }
                }
            }
            return true;
        }

        // fills the ground under a building up to its floor, so it doesn't float on a slope
        private static void level(LDrawImport model, TownBuilding tb, int baseY, BlockType pad) {
            Vector3Int size = model.SizeTurned(tb.turns);
            Vector3Int from = BrickGrid.CellToBlock(tb.corner);
            Vector3Int to = BrickGrid.CellToBlock(tb.corner + size - Vector3Int.one);
            for (int x = from.x; x <= to.x; x++) {
                for (int z = from.z; z <= to.z; z++) {
                    int surface = SurfaceY(x, z);
                    for (int y = surface; y < baseY; y++) {
                        WorldNetwork.ServerSetBlock(new Vector3Int(x, y, z), pad, null);
                    }
                }
            }
        }

        // the first block above the ground at that column (a brick's bottom), 0 if there's none
        private static int SurfaceY(int x, int z) {
            for (int y = Chunk.NumSlices * Chunk.SliceHeight - 1; y > 0; y--) {
                BlockType type = WorldBehaviour.Instance.GetBlockType(x, y, z);
                if (type != BlockType.Air && type != BlockType.NULL) {
                    return y + 1;
                }
            }
            return 0;
        }

        private static Vector2Int ChunkOfCell(Vector3Int cell) {
            return new Vector2Int(BrickGrid.FloorDiv(cell.x, BrickGrid.ChunkCells),
                BrickGrid.FloorDiv(cell.z, BrickGrid.ChunkCells));
        }

        private static long Key(int x, int z) {
            return ((long)x << 32) ^ (uint)z;
        }
    }
}