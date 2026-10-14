using System;
using System.Collections.Generic;
using Brickcraft.Combat;
using Brickcraft.Network;
using Brickcraft.World;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Brickcraft.Npcs
{
    /// <summary>
    /// The server's NPCs: spawning them, and where they're alive. Only chunks within ActiveRadius of a player
    /// are active: NPCs elsewhere stand still. In scenes without a streamed world (the test scene) every
    /// place is active. Players get the NPCs of the chunks they have (see Network.ChunkInterestManagement).
    ///
    /// Saving: the NPCs of a chunk are saved in its record (World.SavedNpc) when it's unloaded, when the
    /// world autosaves and when the server stops, and come back when it's loaded again, once it can be
    /// walked on (until then NPCs neither go there nor move there). Pets following their owner are saved
    /// with them instead (players.db) when they leave, and come back when they join; pets told to stay
    /// stay in the world. NPCs also appear by themselves, see NpcSpawner.
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

        // chunks generated but not walkable yet, and those whose saved NPCs are in the world
        private static readonly HashSet<Vector2Int> generated = new HashSet<Vector2Int>();
        private static readonly HashSet<Vector2Int> populated = new HashSet<Vector2Int>();
        // owners whose pets came back (theirs are saved with them from then on)
        private static readonly HashSet<int> petsBack = new HashSet<int>();
        // saved for the last time: what's destroyed from now on isn't saved again
        private static bool isStopped;

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
            generated.Clear();
            populated.Clear();
            petsBack.Clear();
            isStopped = false;
            NpcBeds.Clear();
            DroppedItems.Clear();
            Health.ServerAnyDamaged += onAnyDamaged;
            WorldTime.ServerHourStarted += onHourStarted;
            subscribedWorld = World.WorldBehaviour.Instance;
            if (subscribedWorld != null) {
                subscribedWorld.ChunkGenerated += onChunkGenerated;
                subscribedWorld.ChunkUnloaded += onChunkUnloaded;
            }
        }

        public static void StopServer() {
            ServerSave(true);
            Navigation.NavGrid.Stop();
            NpcPerception.Clear();
            NpcSteering.Clear();
            NpcGroups.Clear();
            Health.ServerAnyDamaged -= onAnyDamaged;
            WorldTime.ServerHourStarted -= onHourStarted;
            NpcBeds.Clear();
            hits.Clear();
            lastPositions.Clear();
            if (subscribedWorld != null) {
                subscribedWorld.ChunkGenerated -= onChunkGenerated;
                subscribedWorld.ChunkUnloaded -= onChunkUnloaded;
                subscribedWorld = null;
            }
            npcs.Clear();
            activeChunks.Clear();
            generated.Clear();
            populated.Clear();
            petsBack.Clear();
        }

        /// <summary>
        /// Puts a new NPC in the world (at a local position, facing yaw degrees, with that health or all of it),
        /// null if there are too many.
        /// </summary>
        public static Npc ServerSpawn(NpcInfo info, Vector3 position, float yaw, int health = 0) {
            if (npcs.Count >= MaxNpcs) {
                return null;
            }
            GameObject go = Object.Instantiate(Resources.Load<GameObject>(PrefabPath), position, Quaternion.Euler(0, yaw, 0));
            go.name = "NPC " + info.id;
            Npc npc = go.GetComponent<Npc>();
            npc.type = info.id;
            Health life = go.GetComponent<Health>();
            life.maxHealth = info.health;
            life.health = health > 0 ? Mathf.Min(health, info.health) : info.health;
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
            populate();
            activeChunks.Clear();
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity == null) {
                    continue;
                }
                footsteps(conn);
                bringPetsBack(conn);
                Vector2Int center = World.WorldBehaviour.ChunkAt(conn.identity.transform.position);
                for (int x = -ActiveRadius; x <= ActiveRadius; x++) {
                    for (int z = -ActiveRadius; z <= ActiveRadius; z++) {
                        activeChunks.Add(center + new Vector2Int(x, z));
                    }
                }
            }
            NpcSpawner.Update();
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

        /// <summary>A player left: the pets following them go with them (saved with them), those staying stay.</summary>
        public static void ServerPlayerLeft(NetworkConnectionToClient conn) {
            lastPositions.Remove(conn);
            if (!(conn.authenticationData is ConnectedPlayer player)) {
                return;
            }
            savePets(player.record.Id);
            petsBack.Remove(player.record.Id);
            foreach (Npc npc in new List<Npc>(npcs)) {
                if (npc.Brain != null && npc.Brain.Owner == player.record.Id && npc.Brain.IsFollowing) {
                    npc.Despawn();
                }
            }
        }

        /// <summary>NPCs there think and move.</summary>
        public static bool IsActive(Vector3 position) {
            if (World.WorldBehaviour.Instance == null) {
                return true;
            }
            Vector2Int chunk = World.WorldBehaviour.ChunkAt(position);
            return activeChunks.Contains(chunk) && populated.Contains(chunk);
        }

        /// <summary>The world is there and can be walked on (NPCs don't go into chunks that aren't, nor appear there).</summary>
        public static bool IsLoaded(Vector3 position) {
            return World.WorldBehaviour.Instance == null || populated.Contains(World.WorldBehaviour.ChunkAt(position));
        }

        /// <summary>
        /// A place to put an NPC that wide and tall next to someone: on the ground behind them (else beside,
        /// else in front), not in a wall nor in them. False if there's none (they're in the air, in a tunnel...).
        /// </summary>
        public static bool SpotNear(Transform someone, float radius, float height, out Vector3 spot) {
            int layers = Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water);
            Vector3 forward = someone.forward;
            forward.y = 0;
            forward = forward.sqrMagnitude > 0 ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3[] directions = { -forward, (-forward + right).normalized, (-forward - right).normalized, right, -right, forward };
            float distance = radius + 1;
            foreach (Vector3 direction in directions) {
                Vector3 candidate = someone.position + direction * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out RaycastHit ground, 6, layers, QueryTriggerInteraction.Ignore)) {
                    continue; // nothing to stand on
                }
                Vector3 bottom = ground.point + Vector3.up * (radius + 0.05f);
                Vector3 top = ground.point + Vector3.up * Mathf.Max(radius + 0.05f, height - radius);
                if (!Physics.CheckCapsule(bottom, top, radius, layers, QueryTriggerInteraction.Ignore)) {
                    spot = ground.point + Vector3.up * 0.05f;
                    return true;
                }
            }
            spot = someone.position;
            return false;
        }

        /// <summary>How wide (its capsule's radius) and tall an NPC of that kind is, before there's one.</summary>
        public static void SizeOf(NpcInfo info, out float radius, out float height) {
            Bounds hitbox = NpcDatabase.ModelOf(info).GetComponent<NpcModel>().hitbox;
            height = hitbox.size.y * info.scale;
            radius = Mathf.Min(Mathf.Max(hitbox.size.x, hitbox.size.z) * info.scale / 2, height / 2);
        }

        // -------- saving --------

        private static WorldChanges savedChanges {
            get { return WorldNetwork.Storage != null ? WorldNetwork.Storage.Changes : null; }
        }

        /// <summary>
        /// Puts the NPCs of every loaded chunk in its record and the pets following their owners in players.db
        /// (written with the next save). last: the server stops, nothing is saved after it.
        /// </summary>
        public static void ServerSave(bool last = false) {
            if (isStopped) {
                return;
            }
            isStopped = last;
            WorldChanges changes = savedChanges;
            if (changes != null) {
                Dictionary<Vector2Int, List<SavedNpc>> byChunk = new Dictionary<Vector2Int, List<SavedNpc>>();
                foreach (Npc npc in npcs) {
                    Vector2Int coords = World.WorldBehaviour.ChunkAt(npc.transform.position);
                    if (!isSavedWithChunk(npc) || !populated.Contains(coords)) {
                        continue;
                    }
                    if (!byChunk.TryGetValue(coords, out List<SavedNpc> list)) {
                        list = new List<SavedNpc>();
                        byChunk[coords] = list;
                    }
                    list.Add(toSaved(npc));
                }
                List<SavedNpc> none = new List<SavedNpc>();
                foreach (Vector2Int coords in populated) {
                    changes.SetNpcs(coords, byChunk.TryGetValue(coords, out List<SavedNpc> list) ? list : none);
                    changes.SetDrops(coords, DroppedItems.SavedIn(coords, false)); // items on the ground go with the chunks too
                    changes.SetVehicles(coords, Vehicles.VehicleSystem.SavedIn(coords, false)); // and vehicles
                }
            }
            foreach (int owner in petsBack) {
                savePets(owner);
            }
        }

        // pets following their owner are saved with them, dead ones not at all
        private static bool isSavedWithChunk(Npc npc) {
            return npc.Info != null && npc.Brain != null && !npc.Health.isDead && !(npc.Brain.Owner != 0 && npc.Brain.IsFollowing);
        }

        private static SavedNpc toSaved(Npc npc) {
            FloatingOrigin.ToAbsolute(npc.transform.position, out double x, out double y, out double z);
            NpcGroups.Pack pack = NpcGroups.PackOf(npc);
            return new SavedNpc() {
                type = npc.type,
                x = x,
                y = y,
                z = z,
                yaw = npc.transform.eulerAngles.y,
                health = npc.Health.health,
                owner = npc.Brain.Owner,
                ownerName = npc.ownerName,
                attacks = (byte)npc.Brain.Attacks,
                pack = pack != null ? pack.id : Guid.Empty, // (its packmates may be gone already, saved with another chunk)
                natural = npc.IsNatural,
                hasHome = true,
                home = npc.Home,
                bed = npc.BedId != null && Guid.TryParse(npc.BedId, out Guid bed) ? bed : Guid.Empty,
            };
        }

        private static void restore(SavedNpc saved) {
            NpcInfo info = NpcDatabase.Get(saved.type);
            if (info == null) {
                Debug.LogWarning("The save had an NPC of an unknown kind (" + saved.type + ", a removed mod?), it's gone");
                return;
            }
            Npc npc = ServerSpawn(info, FloatingOrigin.ToLocal(saved.x, saved.y, saved.z) + Vector3.up * 0.05f, saved.yaw, Mathf.Max(1, saved.health));
            if (npc == null) {
                return;
            }
            if (saved.owner != 0) {
                npc.Brain.Tame(saved.owner, saved.ownerName ?? "");
                npc.Brain.SetOrders(false, (NpcBrain.PetAttack)saved.attacks);
            }
            if (saved.pack != Guid.Empty) {
                NpcGroups.Join(npc, saved.pack);
            }
            npc.IsNatural = saved.natural;
            if (saved.hasHome) {
                npc.Home = saved.home;
            }
            if (saved.bed != Guid.Empty) {
                NpcBeds.Restore(npc, saved.bed.ToString());
            }
        }

        private static void onChunkGenerated(Chunk chunk) {
            generated.Add(new Vector2Int(chunk.X, chunk.Z));
        }

        // chunks that can be walked on now get their saved NPCs back
        private static void populate() {
            World.WorldBehaviour world = World.WorldBehaviour.Instance;
            if (world == null || generated.Count == 0) {
                return;
            }
            List<Vector2Int> ready = new List<Vector2Int>();
            foreach (Vector2Int coords in generated) {
                if (world.IsWalkable(coords)) {
                    ready.Add(coords);
                }
            }
            WorldChanges changes = savedChanges;
            foreach (Vector2Int coords in ready) {
                generated.Remove(coords);
                populated.Add(coords);
                if (changes != null) {
                    foreach (SavedNpc saved in changes.GetNpcs(coords)) {
                        restore(saved);
                    }
                    DroppedItems.Restore(changes.GetDrops(coords));
                    Vehicles.VehicleSystem.Restore(changes.GetVehicles(coords));
                }
            }
        }

        // its NPCs are saved with it (before it's written to disk, right after this) and go
        private static void onChunkUnloaded(Vector2Int coords) {
            generated.Remove(coords);
            bool wasPopulated = populated.Remove(coords);
            List<SavedNpc> left = new List<SavedNpc>();
            foreach (Npc npc in new List<Npc>(npcs)) {
                if (World.WorldBehaviour.ChunkAt(npc.transform.position) != coords) {
                    continue;
                }
                if (npc.Brain != null && npc.Brain.Owner != 0 && npc.Brain.IsFollowing && !npc.Health.isDead) {
                    // a pet left behind catches up with its owner instead
                    Health owner = PlayerHealth(npc.Brain.Owner);
                    if (owner != null && IsLoaded(owner.transform.position) && SpotNear(owner.transform, npc.Radius, npc.Height, out Vector3 spot)) {
                        npc.ServerTeleport(spot);
                        continue;
                    }
                }
                if (wasPopulated && isSavedWithChunk(npc)) {
                    left.Add(toSaved(npc));
                }
                NetworkServer.Destroy(npc.gameObject);
            }
            WorldChanges changes = savedChanges;
            List<SavedDrop> drops = DroppedItems.SavedIn(coords, true);
            List<SavedVehicle> vehicles = Vehicles.VehicleSystem.SavedIn(coords, true);
            if (wasPopulated && !isStopped && changes != null) {
                changes.SetNpcs(coords, left);
                changes.SetDrops(coords, drops);
                changes.SetVehicles(coords, vehicles);
            }
        }

        // a new hour: mods' scripts and NPCs' scripts hear it
        private static void onHourStarted(int hour) {
            Scripting.ModScripts.ModEvent("onHour", hour, WorldTime.Day + 1); // days count from 1 for scripts, like world.day()
            foreach (Npc npc in new List<Npc>(npcs)) {
                if (npc != null && !npc.Health.isDead && Scripting.ModScripts.HasNpcEvent(npc.type, "onHour")) {
                    Scripting.ModScripts.NpcEvent(npc.type, "onHour", script => new object[] { Scripting.NpcHandle.For(npc), hour });
                }
            }
        }

        // -------- pets --------

        private static GameDatabase database {
            get { return BrickcraftNetworkManager.Instance != null ? BrickcraftNetworkManager.Instance.Database : null; }
        }

        // the pets following the player, kept for when they're back
        private static void savePets(int owner) {
            if (database == null || !petsBack.Contains(owner)) {
                return; // theirs didn't come back yet: what's kept is still theirs
            }
            List<PetRecord> pets = new List<PetRecord>();
            foreach (Npc npc in npcs) {
                if (npc.Brain != null && npc.Brain.Owner == owner && npc.Brain.IsFollowing && !npc.Health.isDead && npc.Info != null) {
                    pets.Add(new PetRecord() { Type = npc.type, Health = npc.Health.health, Attacks = (int)npc.Brain.Attacks });
                }
            }
            foreach (PetRecord kept in database.LoadPets(owner)) {
                if (NpcDatabase.Get(kept.Type) == null) {
                    pets.Add(kept); // of a mod that isn't there now
                }
            }
            database.SavePets(owner, pets);
        }

        // a player who joined gets their pets back next to them, once the ground there is ready
        private static void bringPetsBack(NetworkConnectionToClient conn) {
            if (!(conn.authenticationData is ConnectedPlayer player) || !player.hasJoined || player.hasLeft
                    || petsBack.Contains(player.record.Id) || !IsLoaded(conn.identity.transform.position)) {
                return;
            }
            if (database == null) {
                petsBack.Add(player.record.Id);
                return;
            }
            List<PetRecord> pets = database.LoadPets(player.record.Id);
            Transform owner = conn.identity.transform;
            // each next to them (never in them: it'd push them through the ground), once there's room
            List<Vector3> spots = new List<Vector3>();
            foreach (PetRecord pet in pets) {
                NpcInfo info = NpcDatabase.Get(pet.Type);
                if (info == null) {
                    continue;
                }
                SizeOf(info, out float radius, out float height);
                if (!SpotNear(owner, radius, height, out Vector3 spot)) {
                    return; // they're falling, or somewhere tight: next time
                }
                spots.Add(spot);
            }
            petsBack.Add(player.record.Id);
            List<PetRecord> unknown = new List<PetRecord>();
            int next = 0;
            foreach (PetRecord pet in pets) {
                NpcInfo info = NpcDatabase.Get(pet.Type);
                if (info == null) {
                    unknown.Add(pet); // a mod that isn't there now: kept for when it is
                    continue;
                }
                Npc npc = ServerSpawn(info, spots[next++], owner.eulerAngles.y, Mathf.Max(1, pet.Health));
                if (npc != null) {
                    npc.Brain.Tame(player.record.Id, player.record.Name);
                    npc.Brain.SetOrders(true, (NpcBrain.PetAttack)pet.Attacks);
                }
            }
            // they're in the world now, saved again when the player leaves (or the world autosaves)
            database.SavePets(player.record.Id, unknown);
        }
    }
}
