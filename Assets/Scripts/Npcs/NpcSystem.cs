using System.Collections.Generic;
using Brickcraft.Combat;
using Brickcraft.Network;
using Mirror;
using UnityEngine;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// The server's NPCs: spawning them, and where they're alive. Only chunks within ActiveRadius of a player
    /// are active: NPCs elsewhere stand still, and those in a chunk the server unloads go with it (keeping
    /// them for later comes with the saves). In scenes without a streamed world (the test scene) every
    /// place is active. Players get the NPCs of the chunks they have (see Network.ChunkInterestManagement).
    ///
    /// It also keeps what NPCs' brains look up: who hit whom lately (pets defend their owners), players
    /// by their id, and players' footsteps as noises (see NpcPerception).
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

        // who hit whom, the last seconds
        private const float HitMemory = 10;
        private struct Hit
        {
            public Health attacker;
            public Health victim;
            public double time;
        }
        private static readonly List<Hit> hits = new List<Hit>();

        // players' footsteps: how fast they moved since the last look
        private const float WalkSpeed = 1;
        private const float SprintSpeed = 4.5f;
        private const float StepsRadius = 4;
        private const float SprintRadius = 10;
        private static readonly Dictionary<NetworkConnectionToClient, Vector3> lastPositions = new Dictionary<NetworkConnectionToClient, Vector3>();
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
            Navigation.NavGrid.Start();
            NpcPerception.Clear();
            NpcSteering.Clear();
            NpcGroups.Clear();
            hits.Clear();
            lastPositions.Clear();
            Health.ServerAnyDamaged += onAnyDamaged;
            subscribedWorld = World.WorldBehaviour.Instance;
            if (subscribedWorld != null) {
                subscribedWorld.ChunkUnloaded += onChunkUnloaded;
            }
        }

        public static void StopServer() {
            Navigation.NavGrid.Stop();
            NpcPerception.Clear();
            NpcSteering.Clear();
            NpcGroups.Clear();
            Health.ServerAnyDamaged -= onAnyDamaged;
            hits.Clear();
            lastPositions.Clear();
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
            Navigation.NavGrid.Update();
            NpcPerception.Update();
            NpcSteering.Update();
            if (Time.unscaledTime < nextRefresh) {
                return;
            }
            nextRefresh = Time.unscaledTime + RefreshInterval;
            hits.RemoveAll(h => Time.timeAsDouble - h.time > HitMemory || h.attacker == null || h.victim == null);
            activeChunks.Clear();
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity == null) {
                    continue;
                }
                footsteps(conn);
                Vector2Int center = World.WorldBehaviour.ChunkAt(conn.identity.transform.position);
                for (int x = -ActiveRadius; x <= ActiveRadius; x++) {
                    for (int z = -ActiveRadius; z <= ActiveRadius; z++) {
                        activeChunks.Add(center + new Vector2Int(x, z));
                    }
                }
            }
        }

        // walking players are heard a little, running ones further
        private static void footsteps(NetworkConnectionToClient conn) {
            Vector3 position = conn.identity.transform.position;
            if (lastPositions.TryGetValue(conn, out Vector3 last)) {
                float speed = Npc.horizontalDistance(position, last) / RefreshInterval;
                Health player = conn.identity.GetComponent<Health>();
                if (speed >= SprintSpeed) {
                    NpcPerception.MakeNoise(position, SprintRadius, player, Noise.Sprint);
                } else if (speed >= WalkSpeed) {
                    NpcPerception.MakeNoise(position, StepsRadius, player, Noise.Steps);
                }
            }
            lastPositions[conn] = position;
        }

        private static void onAnyDamaged(Health victim, DamageInfo damage) {
            Health attacker = damage.attacker != null ? damage.attacker.GetComponent<Health>() : null;
            if (attacker != null) {
                hits.Add(new Hit() { attacker = attacker, victim = victim, time = Time.timeAsDouble });
            }
        }

        /// <summary>Who hurt it the last seconds.</summary>
        public static IEnumerable<Health> RecentAttackersOf(Health victim) {
            foreach (Hit hit in hits) {
                if (hit.victim == victim && hit.attacker != null) {
                    yield return hit.attacker;
                }
            }
        }

        /// <summary>Whom it hurt the last seconds.</summary>
        public static IEnumerable<Health> RecentTargetsOf(Health attacker) {
            foreach (Hit hit in hits) {
                if (hit.attacker == attacker && hit.victim != null) {
                    yield return hit.victim;
                }
            }
        }

        /// <summary>The player with that players.db id, null if they're not in the game.</summary>
        public static Health PlayerHealth(int playerId) {
            if (playerId == 0) {
                return null;
            }
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity != null && conn.authenticationData is ConnectedPlayer player && player.record.Id == playerId && !player.hasLeft) {
                    return conn.identity.GetComponent<Health>();
                }
            }
            return null;
        }

        /// <summary>A player left: their pets go with them.</summary>
        public static void ServerPlayerLeft(NetworkConnectionToClient conn) {
            lastPositions.Remove(conn);
            if (!(conn.authenticationData is ConnectedPlayer player)) {
                return;
            }
            foreach (Npc npc in new List<Npc>(npcs)) {
                if (npc.Brain != null && npc.Brain.Owner == player.record.Id) {
                    npc.Despawn();
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
