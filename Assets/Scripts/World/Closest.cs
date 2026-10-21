using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Network;
using Brickcraft.World.CustomGenerator;
using Brickcraft.World.Towns;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Finds the closest of what the world generated, for the admins' /closest command: a town, a river or
    /// lake, a cave, or a biome (desert, snow, mountains). The world's seed decides the towns (see
    /// Towns.TownPlanner) and the terrain's features (see CustomChunkGenerator), so those are found without
    /// loading the world, out to <see cref="Range"/> blocks; a cave is looked for in the chunks that are loaded.
    /// </summary>
    public static class Closest
    {
        /// <summary>How far out to look, in blocks (a town nearer than this is always found).</summary>
        public const int Range = 2048;
        /// <summary>Blocks a chunk is wide, see WorldBehaviour.ChunkAt.</summary>
        private const int BlocksPerChunk = 16;

        /// <summary>What can be looked for, as the command takes it.</summary>
        public static readonly string[] Kinds = { "town", "river", "lake", "cave", "desert", "snow", "mountains" };

        private static CustomChunkGenerator generator;
        private static long generatorSeed;

        /// <summary>What that name means, one of <see cref="Kinds"/>, null if nothing goes by it (or an alias).</summary>
        public static string KindOf(string name) {
            switch ((name ?? "").Trim().ToLowerInvariant()) {
                case "town":
                case "village":
                    return "town";
                case "river":
                    return "river";
                case "lake":
                    return "lake";
                case "cave":
                    return "cave";
                case "desert":
                    return "desert";
                case "snow":
                    return "snow";
                case "mountain":
                case "mountains":
                    return "mountains";
                default:
                    return null;
            }
        }

        /// <summary>The closest one of that thing (see KindOf) to that block, in block coordinates; false if there's none within Range.</summary>
        public static bool Find(string kind, Vector3Int from, out Vector3Int found) {
            found = default(Vector3Int);
            if (kind == "town") {
                return findTown(from, out found);
            }
            if (kind == "cave") {
                return findCave(from, out found);
            }
            return findTerrain(kind, from, out found);
        }

        // rivers, lakes and biomes, from the seed, without loading any chunk
        private static bool findTerrain(string kind, Vector3Int from, out Vector3Int found) {
            found = default(Vector3Int);
            CustomChunkGenerator gen = generatorOf();
            const int step = 8;
            for (int radius = 0; radius <= Range; radius += step) {
                long best = long.MaxValue;
                bool any = false;
                for (int dx = -radius; dx <= radius; dx += step) {
                    for (int dz = -radius; dz <= radius; dz += step) {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) {
                            continue;
                        }
                        int x = from.x + dx, z = from.z + dz;
                        if (!matches(gen, kind, x, z)) {
                            continue;
                        }
                        long distance = (long)dx * dx + (long)dz * dz;
                        if (distance < best) {
                            best = distance;
                            found = new Vector3Int(x, from.y, z);
                            any = true;
                        }
                    }
                }
                if (any) {
                    return true;
                }
            }
            return false;
        }

        private static bool matches(CustomChunkGenerator gen, string kind, int x, int z) {
            switch (kind) {
                case "river":
                case "lake":
                    return gen.IsRiverAt(x, z);
                case "desert":
                    return gen.BiomeAt(x, z) == CustomChunkGenerator.BIOME_TYPE.DESERT;
                case "snow":
                    return gen.BiomeAt(x, z) == CustomChunkGenerator.BIOME_TYPE.SNOW;
                case "mountains":
                    return gen.BiomeAt(x, z) == CustomChunkGenerator.BIOME_TYPE.MOUNTAINS;
                default:
                    return false;
            }
        }

        // the closest town's middle, from the seed (see TownPlanner), without loading any chunk
        private static bool findTown(Vector3Int from, out Vector3Int found) {
            found = default(Vector3Int);
            List<LDrawImport> buildings = TownBuildings.All;
            if (buildings.Count == 0) {
                return false;
            }
            const int region = TownPlanner.SpanChunks * BlocksPerChunk;
            int regionX = BrickGrid.FloorDiv(from.x, region);
            int regionZ = BrickGrid.FloorDiv(from.z, region);
            int most = Range / region + 1;

            for (int radius = 0; radius <= most; radius++) {
                long best = long.MaxValue;
                bool any = false;
                for (int dx = -radius; dx <= radius; dx++) {
                    for (int dz = -radius; dz <= radius; dz++) {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) {
                            continue;
                        }
                        if (TownPlanner.Plan(WorldNetwork.Seed, buildings, regionX + dx, regionZ + dz) == null) {
                            continue;
                        }
                        // its middle chunk's middle, in blocks
                        int chunkX = (regionX + dx) * TownPlanner.SpanChunks + TownPlanner.SpanChunks / 2;
                        int chunkZ = (regionZ + dz) * TownPlanner.SpanChunks + TownPlanner.SpanChunks / 2;
                        int x = chunkX * BlocksPerChunk + BlocksPerChunk / 2;
                        int z = chunkZ * BlocksPerChunk + BlocksPerChunk / 2;
                        long distance = (long)(x - from.x) * (x - from.x) + (long)(z - from.z) * (z - from.z);
                        if (distance < best) {
                            best = distance;
                            found = new Vector3Int(x, from.y, z);
                            any = true;
                        }
                    }
                }
                if (any) {
                    return true;
                }
            }
            return false;
        }

        // caves, in the chunks that are loaded (a cave carves stone above the sea, see GenerateTerrain)
        private static bool findCave(Vector3Int from, out Vector3Int found) {
            found = default(Vector3Int);
            WorldBehaviour world = WorldBehaviour.Instance;
            if (world == null) {
                return false;
            }
            Vector2Int centre = new Vector2Int(BrickGrid.FloorDiv(from.x, BlocksPerChunk), BrickGrid.FloorDiv(from.z, BlocksPerChunk));
            int most = Range / BlocksPerChunk;

            for (int radius = 0; radius <= most; radius++) {
                long best = long.MaxValue;
                bool any = false;
                for (int dx = -radius; dx <= radius; dx++) {
                    for (int dz = -radius; dz <= radius; dz++) {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius) {
                            continue;
                        }
                        Vector2Int chunk = new Vector2Int(centre.x + dx, centre.y + dz);
                        if (!world.IsGenerated(chunk)) {
                            continue;
                        }
                        if (caveInChunk(world, chunk, from, out Vector3Int hit, out long distance) && distance < best) {
                            best = distance;
                            found = hit;
                            any = true;
                        }
                    }
                }
                if (any) {
                    return true;
                }
            }
            return false;
        }

        // the cave block of that chunk closest to the player, if it has one
        private static bool caveInChunk(WorldBehaviour world, Vector2Int chunk, Vector3Int from, out Vector3Int found, out long distance) {
            found = default(Vector3Int);
            distance = long.MaxValue;
            bool any = false;
            for (int x = 0; x < BlocksPerChunk; x++) {
                for (int z = 0; z < BlocksPerChunk; z++) {
                    int bx = chunk.x * BlocksPerChunk + x, bz = chunk.y * BlocksPerChunk + z;
                    int surface = surfaceY(world, bx, bz);
                    if (surface <= 65) {
                        continue; // under the sea: no cave carves there
                    }
                    // a cave carves the stone above the sea (see GenerateTerrain): an air pocket under the surface
                    for (int y = 64; y <= surface - 2; y++) {
                        if (world.GetBlockType(bx, y, bz) != BlockType.Air) {
                            continue;
                        }
                        long dx = bx - from.x, dy = y - from.y, dz = bz - from.z;
                        long candidate = dx * dx + dy * dy + dz * dz;
                        if (candidate < distance) {
                            distance = candidate;
                            found = new Vector3Int(bx, y, bz);
                            any = true;
                        }
                    }
                }
            }
            return any;
        }

        // the first block above the ground at that column, 0 if the world isn't loaded there
        private static int surfaceY(WorldBehaviour world, int x, int z) {
            for (int y = Chunk.NumSlices * Chunk.SliceHeight - 1; y > 0; y--) {
                BlockType type = world.GetBlockType(x, y, z);
                if (type != BlockType.Air && type != BlockType.NULL) {
                    return y + 1;
                }
            }
            return 0;
        }

        // one generator per seed, for the terrain's features
        private static CustomChunkGenerator generatorOf() {
            if (generator == null || generatorSeed != WorldNetwork.Seed) {
                generator = new CustomChunkGenerator();
                generator.Init(WorldNetwork.Seed);
                generatorSeed = WorldNetwork.Seed;
            }
            return generator;
        }
    }
}