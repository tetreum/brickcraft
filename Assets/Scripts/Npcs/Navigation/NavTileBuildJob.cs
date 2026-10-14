using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Brickcraft.Npcs.Navigation
{
    /// <summary>
    /// Makes the spans of a tile (a chunk: 32 x 32 columns of cells) from what fills its cells: its terrain
    /// blocks and the cells bricks take. Runs on a worker thread, see NavGrid.
    /// </summary>
    [BurstCompile]
    internal struct NavTileBuildJob : IJob
    {
        public const byte Empty = 0;
        public const byte Solid = 1;
        public const byte FluidCell = 2;

        /// <summary>What each block of the chunk is (Empty, Solid, FluidCell), by (y * 16 + z) * 16 + x; empty without terrain.</summary>
        [ReadOnly] public NativeArray<byte> blocks;
        /// <summary>Cells bricks fill: x and z in the tile (0 to 31), y absolute, w what it is.</summary>
        [ReadOnly] public NativeArray<int4> brickCells;
        /// <summary>Its corner column, in cells.</summary>
        public int2 origin;
        /// <summary>Scratch: what fills every cell of the tile, NavGrid.Height per column.</summary>
        public NativeArray<byte> cells;
        public NativeList<NavSpanEntry> spans;

        public void Execute() {
            int height = NavGrid.Height;
            for (int i = 0; i < cells.Length; i++) {
                cells[i] = Empty;
            }
            if (blocks.Length > 0) {
                for (int x = 0; x < NavGrid.TileCells; x++) {
                    for (int z = 0; z < NavGrid.TileCells; z++) {
                        int column = (z * NavGrid.TileCells + x) * height;
                        for (int by = 0; by < 256; by++) {
                            byte kind = blocks[(by * 16 + (z >> 1)) * 16 + (x >> 1)];
                            if (kind == Empty) {
                                continue;
                            }
                            int y = by * 3 - NavGrid.MinY;
                            cells[column + y] = kind;
                            cells[column + y + 1] = kind;
                            cells[column + y + 2] = kind;
                        }
                    }
                }
            }
            for (int i = 0; i < brickCells.Length; i++) {
                int4 cell = brickCells[i];
                int y = cell.y - NavGrid.MinY;
                if (y < 0 || y >= height) {
                    continue;
                }
                int index = (cell.z * NavGrid.TileCells + cell.x) * height + y;
                // solid wins over fluid
                if (cells[index] != Solid) {
                    cells[index] = (byte)cell.w;
                }
            }

            // a floor wherever a free cell sits on a solid one
            for (int x = 0; x < NavGrid.TileCells; x++) {
                for (int z = 0; z < NavGrid.TileCells; z++) {
                    int column = (z * NavGrid.TileCells + x) * height;
                    for (int y = 1; y < height; y++) {
                        if (cells[column + y - 1] != Solid || cells[column + y] == Solid) {
                            continue;
                        }
                        int ceiling = y + 1;
                        while (ceiling < height && cells[column + ceiling] != Solid) {
                            ceiling++;
                        }
                        spans.Add(new NavSpanEntry() {
                            column = origin + new int2(x, z),
                            span = new NavSpan() {
                                floor = y + NavGrid.MinY,
                                ceiling = ceiling + NavGrid.MinY,
                                flags = cells[column + y] == FluidCell ? NavSpan.Fluid : (byte)0,
                            },
                        });
                        y = ceiling;
                    }
                }
            }
        }
    }
}
