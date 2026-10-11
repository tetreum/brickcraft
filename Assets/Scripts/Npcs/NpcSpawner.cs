using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// NPCs appearing by themselves, on the server: every few seconds, around each player with fewer than
    /// MaxAround wild NPCs near them, a place on the surface out of their way (MinDistance to MaxDistance
    /// from every player) is picked, and one of the NPCs whose spawn rules (NpcInfo.spawn) fit its block and
    /// height appears there, with its group (a pack). Not on fluids, and aggressive ones not in Peaceful
    /// worlds. Only in the generated world (not the test scene). They're saved with their chunks like
    /// any other, so the places players went keep theirs.
    /// </summary>
    public static class NpcSpawner
    {
        /// <summary>No more wild NPCs than this within CountRadius of a player.</summary>
        public const int MaxAround = 8;
        private const float CountRadius = 64;
        private const float MinDistance = 24;
        private const float MaxDistance = 44;
        private const float Interval = 3;
        private const int Tries = 4;
        // how far around the first the rest of its group appears, in blocks
        private const int GroupSpread = 2;

        /// <summary>Whether they appear (scripts or admins may turn it off).</summary>
        public static bool Enabled = true;

        private static float nextTry;

        public static void Update() {
            WorldBehaviour world = WorldBehaviour.Instance;
            if (!Enabled || world == null || Time.unscaledTime < nextTry) {
                return;
            }
            nextTry = Time.unscaledTime + Interval;
            List<NpcInfo> kinds = new List<NpcInfo>();
            foreach (NpcInfo info in NpcDatabase.All) {
                if (info.spawn != null && info.spawn.weight > 0
                        && !(WorldNetwork.Difficulty == Difficulty.Peaceful && info.behaviour == NpcInfo.Aggressive)) {
                    kinds.Add(info);
                }
            }
            if (kinds.Count == 0) {
                return;
            }
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity != null) {
                    spawnAround(world, conn.identity.transform.position, kinds);
                }
            }
        }

        private static void spawnAround(WorldBehaviour world, Vector3 center, List<NpcInfo> kinds) {
            int around = 0;
            foreach (Npc npc in NpcSystem.All) {
                if (npc.Brain != null && npc.Brain.Owner == 0 && Npc.horizontalDistance(npc.transform.position, center) <= CountRadius) {
                    around++;
                }
            }
            if (around >= MaxAround) {
                return;
            }
            for (int i = 0; i < Tries; i++) {
                float angle = Random.Range(0, 2 * Mathf.PI);
                Vector3 place = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Random.Range(MinDistance, MaxDistance);
                if (!NpcSystem.IsLoaded(place) || isNearPlayer(place)) {
                    continue;
                }
                Vector3Int column = BrickGrid.CellToBlock(BrickGrid.WorldToCell(place));
                if (!surface(world, column.x, column.z, out int height, out string block)) {
                    continue;
                }
                NpcInfo info = pick(kinds, block, height);
                if (info == null) {
                    continue;
                }
                spawnGroup(world, info, column.x, column.z, height, MaxAround - around);
                return;
            }
        }

        // as many as its group has, but no more than there's room for
        private static void spawnGroup(WorldBehaviour world, NpcInfo info, int x, int z, int height, int room) {
            int count = Random.Range(Mathf.Max(1, info.spawn.minGroup), Mathf.Max(info.spawn.minGroup, info.spawn.maxGroup) + 1);
            count = Mathf.Min(count, room);
            Npc first = NpcSystem.ServerSpawn(info, onTop(x, height, z), Random.Range(0f, 360f));
            if (first == null) {
                return;
            }
            for (int i = 1; i < count; i++) {
                // the rest around it, where the ground is as high (not in a wall, not down a cliff)
                for (int attempt = 0; attempt < 4; attempt++) {
                    int mx = x + Random.Range(-GroupSpread, GroupSpread + 1);
                    int mz = z + Random.Range(-GroupSpread, GroupSpread + 1);
                    if (surface(world, mx, mz, out int h, out _) && Mathf.Abs(h - height) <= 1) {
                        Npc member = NpcSystem.ServerSpawn(info, onTop(mx, h, mz), Random.Range(0f, 360f));
                        if (member != null) {
                            NpcGroups.Join(member, first);
                        }
                        break;
                    }
                }
            }
        }

        // the top of a column: the first block from the sky that isn't air or replaceable (tall grass);
        // false if it's a fluid
        private static bool surface(WorldBehaviour world, int x, int z, out int height, out string block) {
            for (int y = Chunk.NumSlices * Chunk.SliceHeight - 1; y > 0; y--) {
                BlockDefinition definition = BlockDatabase.Get(world.GetBlockType(new Vector3Int(x, y, z)));
                if (definition.isFluid) {
                    break;
                }
                if (definition.id == (byte)BlockType.Air || definition.isReplaceable) {
                    continue;
                }
                height = y + 1;
                block = definition.name;
                return true;
            }
            height = 0;
            block = null;
            return false;
        }

        // one of the kinds that appear on that block at that height, by their weights
        private static NpcInfo pick(List<NpcInfo> kinds, string block, int height) {
            float total = 0;
            List<NpcInfo> fitting = new List<NpcInfo>();
            foreach (NpcInfo info in kinds) {
                SpawnInfo spawn = info.spawn;
                if (height - 1 < spawn.minHeight || height - 1 > spawn.maxHeight) {
                    continue;
                }
                if (spawn.on != null && spawn.on.Length > 0 && System.Array.IndexOf(spawn.on, block) < 0) {
                    continue;
                }
                fitting.Add(info);
                total += spawn.weight;
            }
            float roll = Random.Range(0, total);
            foreach (NpcInfo info in fitting) {
                roll -= info.spawn.weight;
                if (roll <= 0) {
                    return info;
                }
            }
            return fitting.Count > 0 ? fitting[fitting.Count - 1] : null;
        }

        private static bool isNearPlayer(Vector3 place) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity != null && Npc.horizontalDistance(conn.identity.transform.position, place) < MinDistance) {
                    return true;
                }
            }
            return false;
        }

        // standing in the middle of the block at that height
        private static Vector3 onTop(int x, int height, int z) {
            return BrickGrid.CellToWorld(BrickGrid.BlockToCell(new Vector3Int(x, height, z)), new Vector3(BrickGrid.StudsPerBlock / 2f, 0.3f, BrickGrid.StudsPerBlock / 2f));
        }
    }
}
