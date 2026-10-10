using System.Collections.Generic;
using Brickcraft.Combat;
using Mirror;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// The server's NPCs: spawning them, and where they're alive. Only chunks within ActiveRadius of a player
    /// are active: NPCs elsewhere stand still, and those in a chunk the server unloads go with it (keeping
    /// them for later comes with the saves). In scenes without a streamed world (the test scene) every
    /// place is active. Players get the NPCs of the chunks they have (see Network.ChunkInterestManagement).
    /// </summary>
    public static class NpcSystem
    {
        public const string PrefabPath = "Npc";
        /// <summary>Chunks around each player where NPCs move and think.</summary>
        public const int ActiveRadius = 4;
        /// <summary>No more than this many at once.</summary>
        public const int MaxNpcs = 300;

        private const float RefreshInterval = 0.5f;

        private static readonly List<Npc> npcs = new List<Npc>();
        private static readonly HashSet<Vector2Int> activeChunks = new HashSet<Vector2Int>();
        private static float nextRefresh;
        private static World.WorldBehaviour subscribedWorld;

        public static IReadOnlyList<Npc> All {
            get { return npcs; }
        }

        internal static void Register(Npc npc) {
            npcs.Add(npc);
        }

        internal static void Unregister(Npc npc) {
            npcs.Remove(npc);
        }

        public static void StartServer() {
            npcs.Clear();
            activeChunks.Clear();
            subscribedWorld = World.WorldBehaviour.Instance;
            if (subscribedWorld != null) {
                subscribedWorld.ChunkUnloaded += onChunkUnloaded;
            }
        }

        public static void StopServer() {
            if (subscribedWorld != null) {
                subscribedWorld.ChunkUnloaded -= onChunkUnloaded;
                subscribedWorld = null;
            }
            npcs.Clear();
            activeChunks.Clear();
        }

        /// <summary>Puts a new NPC in the world (at a local position, facing yaw degrees), null if there are too many.</summary>
        public static Npc ServerSpawn(NpcInfo info, Vector3 position, float yaw) {
            if (npcs.Count >= MaxNpcs) {
                return null;
            }
            GameObject go = Object.Instantiate(Resources.Load<GameObject>(PrefabPath), position, Quaternion.Euler(0, yaw, 0));
            go.name = "NPC " + info.id;
            Npc npc = go.GetComponent<Npc>();
            npc.type = info.id;
            Health health = go.GetComponent<Health>();
            health.maxHealth = info.health;
            health.health = info.health;
            NetworkServer.Spawn(go);
            return npc;
        }

        /// <summary>Takes every NPC out of the world (nobody gets their drops). Returns how many.</summary>
        public static int ServerRemoveAll() {
            List<Npc> all = new List<Npc>(npcs);
            foreach (Npc npc in all) {
                NetworkServer.Destroy(npc.gameObject);
            }
            return all.Count;
        }

        public static void ServerUpdate() {
            if (Time.unscaledTime < nextRefresh) {
                return;
            }
            nextRefresh = Time.unscaledTime + RefreshInterval;
            activeChunks.Clear();
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity == null) {
                    continue;
                }
                Vector2Int center = World.WorldBehaviour.ChunkAt(conn.identity.transform.position);
                for (int x = -ActiveRadius; x <= ActiveRadius; x++) {
                    for (int z = -ActiveRadius; z <= ActiveRadius; z++) {
                        activeChunks.Add(center + new Vector2Int(x, z));
                    }
                }
            }
        }

        /// <summary>NPCs there think and move.</summary>
        public static bool IsActive(Vector3 position) {
            return World.WorldBehaviour.Instance == null || activeChunks.Contains(World.WorldBehaviour.ChunkAt(position));
        }

        /// <summary>The world is there (NPCs don't walk off into chunks that aren't).</summary>
        public static bool IsLoaded(Vector3 position) {
            return World.WorldBehaviour.Instance == null || World.WorldBehaviour.Instance.IsGenerated(World.WorldBehaviour.ChunkAt(position));
        }

        private static void onChunkUnloaded(Vector2Int coords) {
            foreach (Npc npc in new List<Npc>(npcs)) {
                if (World.WorldBehaviour.ChunkAt(npc.transform.position) == coords) {
                    NetworkServer.Destroy(npc.gameObject);
                }
            }
        }
    }
}
