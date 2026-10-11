using System.Collections.Generic;
using UnityEngine;
using Brickcraft.World;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// The single coordinate system shared by the generated world and the bricks placed by players.
    ///
    /// A cell is one stud wide/deep and one plate high. A world block (voxel) is exactly a 2x2 brick,
    /// so it covers 2x3x2 cells. Every brick mesh (the world voxel mesh included) has its pivot at the
    /// center of its footprint, on its bottom face, which is what makes both systems line up.
    ///
    /// It also knows which brick fills each cell, and which bricks are in each chunk (every chunk their
    /// footprint touches), so what looks for bricks somewhere (the NPCs' navigation grid, scripts, clients
    /// unloading chunks) doesn't go through all of them. Bricks on the grid only: attachments are in
    /// their holder's place (see Server.attachmentOf).
    /// </summary>
    public static class BrickGrid
    {
        public const int StudsPerBlock = 2;
        public const int PlatesPerBlock = 3;

        public static readonly Vector3 CellSize = new Vector3(Server.studSize, Server.plateHeight, Server.studSize);

        // Unity position of the corner of the floating origin's cell (FloatingOrigin.Cell), which is also
        // the corner of a world block. Blocks have their pivot at the center of their footprint, so the
        // corner is half a block back.
        public static readonly Vector3 Origin = new Vector3(-Server.brickWidth / 2, 0, -Server.brickWidth / 2);

        /// <summary>Cells a chunk is wide (16 blocks), the same chunks as the world's.</summary>
        public const int ChunkCells = 16 * StudsPerBlock;

        private static readonly Dictionary<Vector3Int, Brick> occupiedCells = new Dictionary<Vector3Int, Brick>();
        private static readonly Dictionary<Vector2Int, HashSet<Brick>> chunkBricks = new Dictionary<Vector2Int, HashSet<Brick>>();
        private static readonly HashSet<Brick> noBricks = new HashSet<Brick>();

        public static void Clear() {
            occupiedCells.Clear();
            chunkBricks.Clear();
        }

        // Cells are integers, exact anywhere in the world. They're converted to Unity positions relative to
        // the floating origin, subtracting it while still integers, so floats stay small (see FloatingOrigin).

        /// <summary>Unity position of a cell's corner, plus an offset in cells.</summary>
        public static Vector3 CellToWorld(Vector3Int cell, Vector3 offset = default) {
            Vector3 relative = (Vector3)(cell - FloatingOrigin.Cell) + offset;
            return Origin + Vector3.Scale(relative, CellSize);
        }

        /// <summary>Position in cells relative to the floating origin's cell (add FloatingOrigin.Cell once rounded).</summary>
        public static Vector3 WorldToLocalGrid(Vector3 position) {
            Vector3 p = position - Origin;
            return new Vector3(p.x / CellSize.x, p.y / CellSize.y, p.z / CellSize.z);
        }

        public static Vector3Int WorldToCell(Vector3 position) {
            return Vector3Int.FloorToInt(WorldToLocalGrid(position)) + FloatingOrigin.Cell;
        }

        public static Vector3Int CellToBlock(Vector3Int cell) {
            return new Vector3Int(
                FloorDiv(cell.x, StudsPerBlock),
                FloorDiv(cell.y, PlatesPerBlock),
                FloorDiv(cell.z, StudsPerBlock)
            );
        }

        public static Vector3Int BlockToCell(Vector3Int block) {
            return new Vector3Int(block.x * StudsPerBlock, block.y * PlatesPerBlock, block.z * StudsPerBlock);
        }

        public static bool IsBlockAligned(Vector3Int cell) {
            return Mod(cell.x, StudsPerBlock) == 0 && Mod(cell.y, PlatesPerBlock) == 0 && Mod(cell.z, StudsPerBlock) == 0;
        }

        public static Brick GetBrickAt(Vector3Int cell) {
            occupiedCells.TryGetValue(cell, out Brick brick);
            return brick;
        }

        public static bool IsCellFree(Vector3Int cell) {
            if (occupiedCells.ContainsKey(cell)) {
                return false;
            }
            if (WorldBehaviour.Instance == null) {
                return true; // scenes without a generated world
            }
            Vector3Int block = CellToBlock(cell);

            if (block.y < 0 || block.y >= Chunk.NumSlices * Chunk.SliceHeight) {
                return false;
            }
            return BlockDatabase.Get(WorldBehaviour.Instance.GetBlockType(block.x, block.y, block.z)).isReplaceable;
        }

        public static bool IsFree(BrickPlacement placement) {
            foreach (Vector3Int cell in placement.Cells) {
                if (!IsCellFree(cell)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>A brick is on the grid there (its placement). Unregister it before its placement changes.</summary>
        public static void Register(Brick brick) {
            foreach (Vector3Int cell in brick.placement.Cells) {
                occupiedCells[cell] = brick;
            }
            foreach (Vector2Int chunk in chunksOf(brick.placement)) {
                if (!chunkBricks.TryGetValue(chunk, out HashSet<Brick> set)) {
                    set = new HashSet<Brick>();
                    chunkBricks[chunk] = set;
                }
                set.Add(brick);
            }
        }

        public static void Unregister(Brick brick) {
            foreach (Vector3Int cell in brick.placement.Cells) {
                if (occupiedCells.TryGetValue(cell, out Brick owner) && owner == brick) {
                    occupiedCells.Remove(cell);
                }
            }
            foreach (Vector2Int chunk in chunksOf(brick.placement)) {
                if (chunkBricks.TryGetValue(chunk, out HashSet<Brick> set) && set.Remove(brick) && set.Count == 0) {
                    chunkBricks.Remove(chunk);
                }
            }
        }

        /// <summary>The chunk a cell is in (WorldChanges.ChunkOfBlock of its block).</summary>
        public static Vector2Int ChunkOfCell(Vector3Int cell) {
            return new Vector2Int(FloorDiv(cell.x, ChunkCells), FloorDiv(cell.z, ChunkCells));
        }

        /// <summary>The bricks on the grid whose footprint is (at least partly) in the chunk. Don't change it.</summary>
        public static IReadOnlyCollection<Brick> BricksInChunk(Vector2Int chunk) {
            return chunkBricks.TryGetValue(chunk, out HashSet<Brick> set) ? set : noBricks;
        }

        /// <summary>The bricks on the grid whose footprint is in the chunks around those cells (xz, inclusive), each once: a superset of those touching them.</summary>
        public static List<Brick> BricksNear(Vector3Int min, Vector3Int max) {
            Vector2Int from = ChunkOfCell(min), to = ChunkOfCell(max);
            HashSet<Brick> found = new HashSet<Brick>();
            long chunks = (long)(to.x - from.x + 1) * (to.y - from.y + 1);
            if (chunks > chunkBricks.Count) {
                // a big area: the chunks that have bricks are fewer
                foreach (KeyValuePair<Vector2Int, HashSet<Brick>> chunk in chunkBricks) {
                    if (chunk.Key.x >= from.x && chunk.Key.x <= to.x && chunk.Key.y >= from.y && chunk.Key.y <= to.y) {
                        found.UnionWith(chunk.Value);
                    }
                }
            } else {
                for (int x = from.x; x <= to.x; x++) {
                    for (int z = from.y; z <= to.y; z++) {
                        if (chunkBricks.TryGetValue(new Vector2Int(x, z), out HashSet<Brick> set)) {
                            found.UnionWith(set);
                        }
                    }
                }
            }
            return new List<Brick>(found);
        }

        private static IEnumerable<Vector2Int> chunksOf(BrickPlacement placement) {
            Vector3Int last = placement.origin + placement.Size - Vector3Int.one;
            Vector2Int from = ChunkOfCell(placement.origin), to = ChunkOfCell(last);
            for (int x = from.x; x <= to.x; x++) {
                for (int z = from.y; z <= to.y; z++) {
                    yield return new Vector2Int(x, z);
                }
            }
        }

        public static int FloorDiv(int a, int b) {
            return Mathf.FloorToInt((float)a / b);
        }

        public static int Mod(int a, int b) {
            int m = a % b;
            return m < 0 ? m + b : m;
        }
    }
}
