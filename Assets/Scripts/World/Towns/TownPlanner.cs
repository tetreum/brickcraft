using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.World.CustomGenerator;
using UnityEngine;

namespace Brickcraft.World.Towns
{
    /// <summary>One building of a town's plan: which model (see TownBuildings.All), its box's lowest corner (cells) and its quarter turns.</summary>
    public struct TownBuilding
    {
        public int building;
        public Vector3Int corner;
        public int turns;
    }

    /// <summary>Where the buildings of one town go (see TownPlanner).</summary>
    public class TownPlan
    {
        public readonly List<TownBuilding> buildings = new List<TownBuilding>();
    }

    /// <summary>
    /// Where towns are, from the world's seed: one candidate every <see cref="SpanChunks"/> chunks, with a
    /// town in about half of them, a few buildings each drawn at random. It's a pure function of the seed
    /// and the region, so the server decides the same towns however it generates them (see TownPlacer).
    /// </summary>
    public static class TownPlanner
    {
        /// <summary>Chunks a candidate town spans, side by side. A town keeps to the middle of its span.</summary>
        public const int SpanChunks = 16;
        /// <summary>The most buildings a town has.</summary>
        public const int MaxBuildings = 3;
        /// <summary>Cells between two buildings (a small gap, like a path).</summary>
        public const int Gap = 6;
        /// <summary>How wide a row of buildings can be before it wraps (cells).</summary>
        public const int RowWidth = 80;
        /// <summary>Chance in a hundred that a candidate region has a town.</summary>
        public const int TownPercent = 50;

        /// <summary>
        /// The town of that region of the world, null if it has none. <paramref name="buildings"/> is what
        /// it's built from (see TownBuildings.All). The corners are absolute cells.
        /// </summary>
        public static TownPlan Plan(long seed, List<LDrawImport> buildings, int regionX, int regionZ) {
            if (buildings.Count == 0) {
                return null;
            }
            FastRandom rnd = new FastRandom(Hash(seed, regionX, regionZ));
            if (pick(rnd, 100) >= TownPercent) {
                return null; // no town here
            }

            int count = Mathf.Min(1 + pick(rnd, MaxBuildings), MaxBuildings);
            List<TownBuilding> chosen = new List<TownBuilding>(count);
            for (int i = 0; i < count; i++) {
                chosen.Add(new TownBuilding() { building = pick(rnd, buildings.Count), turns = pick(rnd, 4) });
            }

            // shelf them: a row left to right, wrapped when it's wide enough
            int x = 0, z = 0, rowDepth = 0;
            for (int i = 0; i < chosen.Count; i++) {
                TownBuilding tb = chosen[i];
                Vector3Int size = buildings[tb.building].SizeTurned(tb.turns);
                if (x != 0 && x + size.x > RowWidth) {
                    x = 0;
                    z += rowDepth + Gap;
                    rowDepth = 0;
                }
                tb.corner = new Vector3Int(x, 0, z);
                chosen[i] = tb;
                x += size.x + Gap;
                rowDepth = Mathf.Max(rowDepth, size.z);
            }

            // the middle of the whole layout, at the middle of the region's center chunk
            int maxX = 0, maxZ = 0;
            foreach (TownBuilding tb in chosen) {
                Vector3Int size = buildings[tb.building].SizeTurned(tb.turns);
                maxX = Mathf.Max(maxX, tb.corner.x + size.x);
                maxZ = Mathf.Max(maxZ, tb.corner.z + size.z);
            }
            int centerChunkX = regionX * SpanChunks + SpanChunks / 2;
            int centerChunkZ = regionZ * SpanChunks + SpanChunks / 2;
            int centerX = centerChunkX * BrickGrid.ChunkCells + BrickGrid.ChunkCells / 2;
            int centerZ = centerChunkZ * BrickGrid.ChunkCells + BrickGrid.ChunkCells / 2;
            int offsetX = centerX - maxX / 2;
            int offsetZ = centerZ - maxZ / 2;
            // to the block grid, so a building's pad is whole blocks
            offsetX -= BrickGrid.Mod(offsetX, 2);
            offsetZ -= BrickGrid.Mod(offsetZ, 2);

            TownPlan plan = new TownPlan();
            for (int i = 0; i < chosen.Count; i++) {
                TownBuilding tb = chosen[i];
                int cx = tb.corner.x + offsetX;
                int cz = tb.corner.z + offsetZ;
                // to the block grid, so a building's pad is whole blocks
                cx -= BrickGrid.Mod(cx, 2);
                cz -= BrickGrid.Mod(cz, 2);
                tb.corner = new Vector3Int(cx, 0, cz);
                plan.buildings.Add(tb);
            }
            return plan;
        }

        /// <summary>A brick of a town, named from the seed and the region, so it's the same brick every time (a town is placed once).</summary>
        public static Guid BrickId(long seed, int regionX, int regionZ, int brick) {
            string name = "brickcraft.town." + seed + "." + regionX + "." + regionZ + "." + brick;
            using (MD5 md5 = MD5.Create()) {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(name)));
            }
        }

        private static long Hash(long seed, int regionX, int regionZ) {
            return seed ^ (regionX * 341873128712L) ^ (regionZ * 132897987541L);
        }

        // a value in [0, count)
        private static int pick(FastRandom rnd, int count) {
            return (rnd.randomInt() & 0x7fffffff) % count;
        }
    }
}