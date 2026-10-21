using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World.CustomGenerator;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Finds the closest of what the world generated, for the admins' /closest command: a town, a river or
    /// lake, a cave, or a biome (desert, snow, mountains). A town is where its bricks stand, so it can't point
    /// at one the terrain didn't let stand (see Towns.TownPlacer); the rest are quiet lookups of the seed's
    /// terrain (see CustomChunkGenerator), answered with the height the world has there when it's loaded.
    /// </summary>
    public static class Closest
    {
        /// <summary>How far out to look, in blocks.</summary>
        public const int Range = 2048;
        /// <summary>Blocks a chunk is wide, see WorldBehaviour.ChunkAt.</summary>
        private const int BlocksPerChunk = 16;
        /// <summary>The first block above the sea (see GenerateTerrain): water whose surface is higher is a river or lake.</summary>
        private const int SeaLevel = 64;

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

        // a town is where its bricks are: the world's own (see Network.WorldNetwork), so it's one that really stands
        private static bool findTown(Vector3Int from, out Vector3Int found) {
            found = default(Vector3Int);
            long best = long.MaxValue;
            bool any = false;
            foreach (Brick brick in Server.bricks.Values) {
                if (brick == null || brick.vehicle != null || WorldNetwork.ServerPlacerOf(brick).playerId != 0) {
                    continue; // a player's, or a vehicle's
                }
                Vector3Int block = BrickGrid.CellToBlock(brick.placement.origin);
                long dx = block.x - from.x, dz = block.z - from.z;
                long distance = dx * dx + dz * dz;
                if (distance < best) {
                    best = distance;
                    found = block;
                    any = true;
                }
            }
            return any;
        }

        // rivers, lakes and biomes, from the seed, without loading any chunk; the height is read when it's loaded
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
                            found = new Vector3Int(x, heightAt(x, z, from.y), z);
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

        // the world's ground height at that column, where it's loaded; the fallback otherwise
        private static int heightAt(int x, int z, int fallback) {
            int surface = WorldBehaviour.Instance != null ? WorldBehaviour.Instance.SurfaceBlockY(x, z) : 0;
            return surface > 0 ? surface : fallback;
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
                    int surface = heightAt(bx, bz, 0);
                    if (surface <= SeaLevel) {
                        continue; // under the sea: no cave carves there
                    }
                    // a cave carves the stone above the sea (see GenerateTerrain): an air pocket under the surface
                    for (int y = SeaLevel; y <= surface - 2; y++) {
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