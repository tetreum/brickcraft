using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Streams the world. The server owns it: every change goes through the Server* methods, which
    /// apply it, record it in the <see cref="WorldChanges"/> (saved by <see cref="WorldStorage"/>)
    /// and send it to the clients that have that chunk.
    ///
    /// The server keeps the chunks around every player loaded and unloads the rest (their regions
    /// get saved and dropped from memory). Each client gets the chunks around its player, nearest
    /// first, as the changes players made to them; it generates the terrain itself from the seed
    /// and applies them. Chunks that get too far are unloaded on both sides.
    ///
    /// In host mode the host's client shares the server's world, so it ignores chunk messages.
    /// </summary>
    public static class WorldNetwork
    {
        // chunks are unloaded this many chunks past the area they're loaded in, so walking along a border doesn't thrash
        private const int UnloadMargin = 2;
        private const int ChunksSentPerUpdate = 16;
        private const float ServerUpdateInterval = 0.1f;

        // chunks around the spawn a joining player waits for
        private const int SpawnAreaRadius = 1;

        // -------- server --------

        public static long Seed { get; private set; }

        /// <summary>How hard the world is (saved with it, see WorldStorage), on the server and its clients.</summary>
        public static Difficulty Difficulty { get; private set; }

        /// <summary>Server: players can hurt each other (see WorldStorage.Pvp; the test scene allows it).</summary>
        public static bool Pvp {
            get { return Storage != null ? Storage.Pvp : testScenePvp; }
            set {
                if (Storage != null) {
                    Storage.SetPvp(value);
                } else {
                    testScenePvp = value;
                }
            }
        }
        private static bool testScenePvp = true;

        /// <summary>The saved world, null in scenes that aren't saved (the test scene).</summary>
        public static WorldStorage Storage { get; private set; }

        private class ClientInterest
        {
            public readonly HashSet<Vector2Int> sentChunks = new HashSet<Vector2Int>();
            // how far its player draws the world, in chunks
            public int viewDistance = GameSettings.DefaultRenderDistance;

            // the chunks it gets: those it draws, and a ring more for their borders
            public int LoadRadius {
                get { return viewDistance + 1; }
            }

            // where the player will appear: the world spawn, or where it left last time
            public Vector2Int spawnChunk;
        }

        private static WorldChanges serverChanges;
        private static Vector2Int spawnChunk;
        private static readonly Dictionary<NetworkConnectionToClient, ClientInterest> interests = new Dictionary<NetworkConnectionToClient, ClientInterest>();
        private static readonly HashSet<Vector2Int> serverChunks = new HashSet<Vector2Int>();
        private static float nextServerUpdate;


        /// <param name="spawn">absolute position players spawn at</param>
        public static void StartServer(WorldStorage storage, long unsavedSeed, Vector3 spawn) {
            Storage = storage;
            Seed = storage != null ? storage.Seed : unsavedSeed;
            Difficulty = storage != null ? storage.Difficulty : Difficulty.Normal;
            serverChanges = storage != null ? storage.Changes : new WorldChanges();
            interests.Clear();
            serverChunks.Clear();

            NetworkServer.RegisterHandler<JoinWorldMessage>(onJoinWorld);
            NetworkServer.RegisterHandler<ViewDistanceMessage>(onViewDistance);

            spawnChunk = WorldBehaviour.ChunkAt(FloatingOrigin.ToLocal(spawn));

            WorldBehaviour world = WorldBehaviour.Instance;
            if (world != null) {
                world.Initialize(Seed, serverChanges, storage != null ? storage.EnsureLoaded : (Action<Vector2Int>)null);
                world.SpawnChunk = spawnChunk;
                world.ChunkGenerated += onServerChunkGenerated;
                world.ChunkUnloaded += onServerChunkUnloaded;
            }
            // the game's time goes on from where it was saved (the test scene starts at noon)
            WorldTime.Time = storage != null ? storage.GameTime : 0.5;
            WorldTime.DayLength = storage != null ? storage.DayLength : WorldTime.DefaultDayLength;
            WorldTime.IsPaused = false;
            nextTimeBroadcast = 0;
            PlayerSleep.Start();
            BrickStorage.Clear();
            Npcs.NpcSystem.StartServer();
        }

        /// <summary>Whether the server sent that chunk to the client (and it still has it).</summary>
        public static bool HasSentChunk(NetworkConnectionToClient conn, Vector2Int chunk) {
            return interests.TryGetValue(conn, out ClientInterest interest) && interest.sentChunks.Contains(chunk);
        }

        public static void StopServer() {
            PlayerSleep.Stop();
            Npcs.NpcSystem.StopServer();
            SaveTime();
            if (WorldBehaviour.Instance != null) {
                WorldBehaviour.Instance.ChunkGenerated -= onServerChunkGenerated;
                WorldBehaviour.Instance.ChunkUnloaded -= onServerChunkUnloaded;
            }
            if (Storage != null) {
                Storage.Dispose();
                Storage = null;
            }
            serverChanges = null;
            interests.Clear();
            serverChunks.Clear();
        }

        public static void ServerDisconnect(NetworkConnectionToClient conn) {
            interests.Remove(conn);
            Npcs.NpcSystem.ServerPlayerLeft(conn);
        }

        /// <summary>
        /// Changes a block, placer is who placed it (null when it was dug), color the colour it's drawn
        /// with (BrickColor.None for its own textures).
        /// </summary>
        public static bool ServerSetBlock(Vector3Int block, BlockType type, Placer? placer, int color = BrickColor.None) {
            if (WorldBehaviour.Instance == null || !WorldBehaviour.Instance.SetBlockType(block, type, color)) {
                return false;
            }
            serverChanges.SetBlock(block, type, placer, color);
            Npcs.Navigation.NavGrid.MarkBlockDirty(block);
            Placer sent = placer ?? default(Placer);
            sendToChunk(WorldChanges.ChunkOfBlock(block), new BlockChangedMessage() {
                block = block,
                blockName = BlockDatabase.Get(type).name,
                color = color,
                placedBy = sent.playerId,
                placedAt = sent.placedAt,
            });

            return true;
        }

        public static Brick ServerPlaceBrick(Item item, int color, BrickPlacement placement, Placer placer) {
            Brick brick = Server.Instance.spawnBrick(item, color, placement);
            serverChanges.AddBrick(toSaved(brick, placer));
            sendPlaced(brick, placer);
            Npcs.Navigation.NavGrid.MarkDirty(brick);
            return brick;
        }

        /// <summary>
        /// Puts an attachment (a door...) in a brick's slot, see Bricks.BrickSlot. The caller checks it fits
        /// and the slot is free.
        /// </summary>
        public static Brick ServerAttachBrick(Item item, int color, Brick holder, Placer placer) {
            Brick brick = Server.Instance.spawnBrick(item, color, holder.placement, null, 0, holder.id);
            serverChanges.AddBrick(toSaved(brick, placer));
            sendPlaced(brick, placer);
            Npcs.Navigation.NavGrid.MarkDirty(holder);
            return brick;
        }

        private static void sendPlaced(Brick brick, Placer placer) {
            sendToChunk(WorldChanges.ChunkOfBrick(brick.placement.origin), new BrickPlacedMessage() {
                id = brick.id,
                itemId = brick.itemId,
                color = brick.color,
                origin = brick.placement.origin,
                rotation = (byte)brick.placement.rotation,
                placedBy = placer.playerId,
                placedAt = placer.placedAt,
                state = brick.state,
                attachedTo = brick.attachedTo,
            });
        }

        /// <summary>Who placed a world block, nobody (0) if it was generated.</summary>
        public static Placer ServerPlacerOf(Vector3Int block) {
            return serverChanges != null && serverChanges.TryGetPlacer(block, out Placer placer) ? placer : default(Placer);
        }

        /// <summary>What a storage brick keeps (a copy), see BrickStorage.</summary>
        public static List<InventoryItem> ServerGetStorage(Brick brick) {
            return serverChanges.GetStorage(new Guid(brick.id), brick.placement.origin);
        }

        public static void ServerSetStorage(Brick brick, List<InventoryItem> items) {
            serverChanges.SetStorage(new Guid(brick.id), brick.placement.origin, items);
        }

        /// <summary>Who placed a brick, nobody (0) if no player did.</summary>
        public static Placer ServerPlacerOf(Brick brick) {
            return serverChanges != null && serverChanges.TryGetBrick(new Guid(brick.id), brick.placement.origin, out SavedBrick saved) ? saved.placer : default(Placer);
        }

        /// <summary>
        /// Moves a brick (it keeps its id, colour and placer) and what's attached to it. Players see them
        /// removed and placed again, so those that only have one of the two chunks get the right half.
        /// False if it doesn't fit there, or it's an attachment (they move with their brick).
        /// </summary>
        public static bool ServerMoveBrick(Brick brick, BrickPlacement to) {
            if (brick.attachedTo != null) {
                return false;
            }
            Placer placer = ServerPlacerOf(brick);
            Vector3Int from = brick.placement.origin;
            Brick attached = Server.attachmentOf(brick);
            List<InventoryItem> kept = serverChanges.GetStorage(new Guid(brick.id), from);
            Placer attachedPlacer = attached != null ? ServerPlacerOf(attached) : default(Placer);
            Npcs.Navigation.NavGrid.MarkDirty(brick); // where it was

            if (!Server.Instance.moveBrick(brick, to)) {
                return false;
            }
            Npcs.Navigation.NavGrid.MarkDirty(brick); // and where it is
            serverChanges.RemoveBrick(new Guid(brick.id), from);
            sendToChunk(WorldChanges.ChunkOfBrick(from), new BrickRemovedMessage() { id = brick.id, origin = from });
            serverChanges.AddBrick(toSaved(brick, placer));
            if (kept.Count > 0) {
                serverChanges.SetStorage(new Guid(brick.id), brick.placement.origin, kept); // what it keeps moves with it
            }
            sendPlaced(brick, placer);

            // its object moved along, its placement is its brick's
            if (attached != null) {
                serverChanges.RemoveBrick(new Guid(attached.id), from);
                attached.placement = brick.placement;
                serverChanges.AddBrick(toSaved(attached, attachedPlacer));
                sendPlaced(attached, attachedPlacer);
            }
            return true;
        }

        /// <summary>Changes a brick's state (opens a door...), see Brick.state.</summary>
        public static void ServerSetBrickState(Brick brick, int state) {
            if (brick.state == state) {
                return;
            }
            Placer placer = ServerPlacerOf(brick);
            Server.Instance.setBrickState(brick, state);
            Npcs.Navigation.NavGrid.MarkDirty(brick, true); // a door swinging
            serverChanges.AddBrick(toSaved(brick, placer));
            sendToChunk(WorldChanges.ChunkOfBrick(brick.placement.origin), new BrickStateMessage() {
                id = brick.id,
                origin = brick.placement.origin,
                state = state,
            });
        }

        /// <summary>Removes a brick and what's attached to it (which players remove with it). Returns them all.</summary>
        public static List<Brick> ServerRemoveBrick(Brick brick) {
            List<Brick> removed = new List<Brick>();
            // what a chest kept falls around it
            if (BrickStorage.IsStorage(brick)) {
                BrickStorage.ServerBroken(brick);
            }
            Brick attached = Server.attachmentOf(brick);
            if (attached != null) {
                removed.AddRange(ServerRemoveBrick(attached));
            }
            // whoever slept in it loses their spawn point
            if (Npcs.NpcBeds.IsBed(brick)) {
                PlayerSleep.BedRemoved(brick);
            }
            Npcs.Navigation.NavGrid.MarkDirty(brick);
            Server.Instance.removeBrick(brick);
            serverChanges.RemoveBrick(new Guid(brick.id), brick.placement.origin);
            sendToChunk(WorldChanges.ChunkOfBrick(brick.placement.origin), new BrickRemovedMessage() { id = brick.id, origin = brick.placement.origin });
            removed.Add(brick);
            return removed;
        }

        // -------- time --------

        private const float TimeBroadcastInterval = 5;
        private static float nextTimeBroadcast;

        /// <summary>Puts the game's time in the save (written with the next save).</summary>
        public static void SaveTime() {
            if (Storage != null) {
                Storage.GameTime = WorldTime.Time;
            }
        }

        /// <summary>Changes the game's time (days since the world began) and tells the clients.</summary>
        public static void ServerSetTime(double time) {
            double before = WorldTime.Time;
            WorldTime.Time = Math.Max(0, time);
            WorldTime.RaiseHoursBetween(before, WorldTime.Time);
            SaveTime();
            broadcastTime();
        }

        /// <summary>Changes how long a day lasts (real seconds), saves it and tells the clients.</summary>
        public static void ServerSetDayLength(float seconds) {
            WorldTime.DayLength = seconds;
            if (Storage != null) {
                Storage.SetDayLength(seconds);
            }
            broadcastTime();
        }

        // it goes on while the server runs, but in singleplayer it waits while the game menu is open
        private static void updateTime() {
            bool paused = !BrickcraftNetworkManager.Instance.AllowsRemotePlayers && WorldTime.IsGameMenuOpen;
            if (paused != WorldTime.IsPaused) {
                WorldTime.IsPaused = paused;
                nextTimeBroadcast = 0;
            }
            double before = WorldTime.Time;
            WorldTime.Advance(Time.deltaTime);
            WorldTime.RaiseHoursBetween(before, WorldTime.Time);
            if (Time.unscaledTime >= nextTimeBroadcast) {
                broadcastTime();
            }
        }

        private static TimeMessage timeMessage() {
            return new TimeMessage() { time = WorldTime.Time, dayLength = WorldTime.DayLength, isPaused = WorldTime.IsPaused };
        }

        // the host's own client shares the server's time
        private static void broadcastTime() {
            nextTimeBroadcast = Time.unscaledTime + TimeBroadcastInterval;
            TimeMessage message = timeMessage();
            foreach (NetworkConnectionToClient conn in interests.Keys) {
                if (!(conn is LocalConnectionToClient)) {
                    conn.Send(message);
                }
            }
        }

        private static void onTime(TimeMessage message) {
            if (NetworkServer.active) {
                return;
            }
            WorldTime.Time = message.time;
            WorldTime.DayLength = message.dayLength;
            WorldTime.IsPaused = message.isPaused;
        }

        /// <summary>Loads and unloads chunks around the players and streams them to the clients.</summary>
        public static void ServerUpdate() {
            WorldBehaviour world = WorldBehaviour.Instance;
            updateTime();
            PlayerSleep.Update();
            DroppedItems.Update();
            BrickStorage.Update();

            if (world == null || !world.Seed.HasValue || Time.unscaledTime < nextServerUpdate) {
                return;
            }
            nextServerUpdate = Time.unscaledTime + ServerUpdateInterval;

            HashSet<Vector2Int> wanted = new HashSet<Vector2Int>();
            HashSet<Vector2Int> kept = new HashSet<Vector2Int>();
            Dictionary<NetworkConnectionToClient, Vector2Int> centers = new Dictionary<NetworkConnectionToClient, Vector2Int>();

            foreach (KeyValuePair<NetworkConnectionToClient, ClientInterest> interest in interests) {
                NetworkConnectionToClient conn = interest.Key;
                Vector2Int center = conn.identity != null ? WorldBehaviour.ChunkAt(conn.identity.transform.position) : interest.Value.spawnChunk;
                centers[conn] = center;
                addArea(wanted, center, interest.Value.LoadRadius);
                addArea(kept, center, interest.Value.LoadRadius + UnloadMargin);
            }

            foreach (Vector2Int coords in wanted) {
                if (serverChunks.Add(coords)) {
                    if (Storage != null) {
                        Storage.AcquireChunk(coords);
                    }
                    world.RequestChunk(coords);
                }
            }

            List<Vector2Int> unloaded = new List<Vector2Int>();
            foreach (Vector2Int coords in serverChunks) {
                if (!kept.Contains(coords)) {
                    unloaded.Add(coords);
                }
            }
            foreach (Vector2Int coords in unloaded) {
                serverChunks.Remove(coords);
                world.UnloadChunk(coords);
                if (Storage != null) {
                    Storage.ReleaseChunk(coords);
                }
            }

            foreach (KeyValuePair<NetworkConnectionToClient, ClientInterest> interest in interests) {
                // the host's client shares the server's world
                if (!(interest.Key is LocalConnectionToClient)) {
                    streamTo(interest.Key, interest.Value, centers[interest.Key]);
                }
            }
        }

        private static void streamTo(NetworkConnectionToClient conn, ClientInterest interest, Vector2Int center) {
            WorldBehaviour world = WorldBehaviour.Instance;

            // forget what's too far
            List<Vector2Int> forgotten = new List<Vector2Int>();
            foreach (Vector2Int coords in interest.sentChunks) {
                if (distance(coords, center) > interest.LoadRadius + UnloadMargin) {
                    forgotten.Add(coords);
                }
            }
            foreach (Vector2Int coords in forgotten) {
                interest.sentChunks.Remove(coords);
                conn.Send(new ChunkUnloadMessage() { chunkX = coords.x, chunkZ = coords.y });
            }

            // send what's missing, nearest first
            List<Vector2Int> missing = new List<Vector2Int>();
            int loadRadius = interest.LoadRadius;
            for (int x = -loadRadius; x <= loadRadius; x++) {
                for (int z = -loadRadius; z <= loadRadius; z++) {
                    Vector2Int coords = new Vector2Int(center.x + x, center.y + z);

                    if (!interest.sentChunks.Contains(coords) && world.IsGenerated(coords)) {
                        missing.Add(coords);
                    }
                }
            }
            missing.Sort((a, b) => distance(a, center).CompareTo(distance(b, center)));

            for (int i = 0; i < missing.Count && i < ChunksSentPerUpdate; i++) {
                Vector2Int coords = missing[i];
                List<byte[]> pages = serverChanges.GetPages(coords);

                for (int page = 0; page < pages.Count; page++) {
                    conn.Send(new ChunkChangesMessage() { chunkX = coords.x, chunkZ = coords.y, data = pages[page], isLast = page == pages.Count - 1 });
                }
                interest.sentChunks.Add(coords);
            }
        }

        // live changes only go to the clients that have the chunk, the others get it when it's sent to them
        // (scenes without a streamed world send them to everyone)
        private static void sendToChunk<T>(Vector2Int chunk, T message) where T : struct, NetworkMessage {
            bool hasWorld = WorldBehaviour.Instance != null;

            foreach (KeyValuePair<NetworkConnectionToClient, ClientInterest> interest in interests) {
                if (!(interest.Key is LocalConnectionToClient) && (!hasWorld || interest.Value.sentChunks.Contains(chunk))) {
                    interest.Key.Send(message);
                }
            }
        }

        // the furthest a client gets the world: the server's maximum, the game's for the host's own player
        private static int maxViewDistanceOf(NetworkConnectionToClient conn) {
            return conn is LocalConnectionToClient ? GameSettings.MaxRenderDistance : ServerConfig.Current.maxRenderDistance;
        }

        // what clients ask for, within what's allowed (an old client asking for nothing gets the default)
        private static int allowedViewDistance(NetworkConnectionToClient conn, int asked) {
            int wanted = asked <= 0 ? GameSettings.DefaultRenderDistance : asked;
            return Mathf.Clamp(wanted, GameSettings.MinRenderDistance, maxViewDistanceOf(conn));
        }

        private static void onViewDistance(NetworkConnectionToClient conn, ViewDistanceMessage message) {
            if (interests.TryGetValue(conn, out ClientInterest interest)) {
                interest.viewDistance = allowedViewDistance(conn, message.viewDistance);
            }
        }

        private static void onJoinWorld(NetworkConnectionToClient conn, JoinWorldMessage message) {
            if (!conn.isReady) {
                NetworkServer.SetClientReady(conn);
            }
            // returning players stream in around where they left
            ClientInterest interest = new ClientInterest() { spawnChunk = spawnChunk, viewDistance = allowedViewDistance(conn, message.viewDistance) };
            if (conn.authenticationData is ConnectedPlayer player && BrickcraftNetworkManager.HasSavedPosition(player.record)) {
                PlayerRecord record = player.record;
                interest.spawnChunk = WorldBehaviour.ChunkAt(FloatingOrigin.ToLocal(record.LastX.Value, record.LastY.Value, record.LastZ.Value));
            }
            interests[conn] = interest;

            if (!(conn is LocalConnectionToClient)) {
                conn.Send(timeMessage());
            }
            PlayerSleep.SendStatus(conn);
            conn.Send(new WorldInfoMessage() {
                seed = Seed,
                difficulty = (byte)Difficulty,
                spawnChunk = interest.spawnChunk,
                viewDistance = interest.viewDistance,
                maxViewDistance = maxViewDistanceOf(conn),
            });

            // scenes without a streamed world (the test scene) just send their bricks
            if (WorldBehaviour.Instance == null && !(conn is LocalConnectionToClient)) {
                foreach (Brick brick in Server.bricks.Values) {
                    conn.Send(new BrickPlacedMessage() {
                        id = brick.id,
                        itemId = brick.itemId,
                        color = brick.color,
                        origin = brick.placement.origin,
                        rotation = (byte)brick.placement.rotation,
                    });
                }
            }
        }

        // the bricks of a chunk are game objects, they live while the chunk is loaded
        private static void onServerChunkGenerated(Chunk chunk) {
            spawnBricks(serverChanges.GetBricks(new Vector2Int(chunk.X, chunk.Z)));
        }

        private static void onServerChunkUnloaded(Vector2Int coords) {
            removeBricks(coords);
        }

        private static SavedBrick toSaved(Brick brick, Placer placer) {
            return new SavedBrick() {
                id = new Guid(brick.id),
                itemId = brick.itemId,
                color = brick.color,
                origin = brick.placement.origin,
                rotation = (byte)brick.placement.rotation,
                placer = placer,
                state = brick.state,
                attachedTo = brick.attachedTo != null ? new Guid(brick.attachedTo) : Guid.Empty,
            };
        }

        // -------- client --------

        private static bool hasRequestedWorld;
        private static bool hasWorldInfo;
        private static bool isSpawnAreaReady;
        private static Vector2Int spawn;

        // changes of the chunks the server sent us
        private static WorldChanges clientChanges;

        public static void StartClient() {
            hasRequestedWorld = false;
            ServerMaxViewDistance = 0;
            hasWorldInfo = false;
            isSpawnAreaReady = false;
            clientChanges = new WorldChanges();

            NetworkClient.RegisterHandler<WorldInfoMessage>(onWorldInfo);
            NetworkClient.RegisterHandler<ChunkChangesMessage>(onChunkChanges);
            NetworkClient.RegisterHandler<ChunkUnloadMessage>(onChunkUnload);
            NetworkClient.RegisterHandler<BlockChangedMessage>(onBlockChanged);
            NetworkClient.RegisterHandler<BrickPlacedMessage>(onBrickPlaced);
            NetworkClient.RegisterHandler<BrickRemovedMessage>(onBrickRemoved);
            NetworkClient.RegisterHandler<BrickStateMessage>(onBrickState);
            NetworkClient.RegisterHandler<TimeMessage>(onTime);
            NetworkClient.RegisterHandler<SleepStatusMessage>(message => SleepStatus = message);
            ClientStorage.Register();
            SleepStatus = default(SleepStatusMessage);
            Events.EventManager.SettingChanged.Subscribe(onSettingChanged);
        }

        public static void StopClient() {
            hasWorldInfo = false;
            isSpawnAreaReady = false;
            Events.EventManager.SettingChanged.Unsubscribe(onSettingChanged);

            if (WorldBehaviour.Instance != null && !NetworkServer.active) {
                WorldBehaviour.Instance.ChunkGenerated -= onClientChunkGenerated;
                WorldBehaviour.Instance.ChunkUnloaded -= onClientChunkUnloaded;
            }
        }

        /// <summary>How many players sleep on the server we're on (see PlayerSleep).</summary>
        public static SleepStatusMessage SleepStatus { get; private set; }

        /// <summary>The furthest the server sends us the world, in chunks (0 until we're in one).</summary>
        public static int ServerMaxViewDistance { get; private set; }

        // the player draws the world further or nearer: right away (within what the server allows), and the
        // server sends that far
        private static void onSettingChanged(Events.SettingChangedEvent e) {
            if (e.setting != GameSettings.RenderDistanceSetting || !hasWorldInfo) {
                return;
            }
            if (WorldBehaviour.Instance != null) {
                WorldBehaviour.Instance.ViewDistance = Mathf.Min(GameSettings.RenderDistance, ServerMaxViewDistance);
            }
            NetworkClient.Send(new ViewDistanceMessage() { viewDistance = GameSettings.RenderDistance });
        }

        /// <summary>Asks the server for the world, once the game scene is loaded.</summary>
        public static void RequestWorld() {
            if (hasRequestedWorld) {
                return;
            }
            hasRequestedWorld = true;

            if (!NetworkClient.ready) {
                NetworkClient.Ready();
            }
            NetworkClient.Send(new JoinWorldMessage() { viewDistance = GameSettings.RenderDistance });
        }

        /// <summary>How far joining the game is, from 0 to 1, and what it's doing, for the loading screen.</summary>
        public static float GetLoadingProgress(out string status) {
            string server = BrickcraftAuthenticator.ServerName;
            if (isSpawnAreaReady) {
                status = server.Length > 0 ? "Joining " + server + "..." : "Joining...";
                return 1f;
            }
            if (!NetworkClient.isConnected) {
                status = "Connecting...";
                return 0f;
            }
            if (NetworkManager.loadingSceneAsync != null) {
                status = server.Length > 0 ? "Loading " + server + "..." : "Loading world...";
                return 0.05f + 0.2f * NetworkManager.loadingSceneAsync.progress;
            }
            WorldBehaviour world = WorldBehaviour.Instance;
            if (!hasWorldInfo || world == null) {
                status = "Waiting for the server...";
                return 0.25f;
            }

            float done = 0f;
            int ready = 0;
            int total = 0;
            for (int x = -SpawnAreaRadius; x <= SpawnAreaRadius; x++) {
                for (int z = -SpawnAreaRadius; z <= SpawnAreaRadius; z++) {
                    float chunk = world.GetLoadProgress(new Vector2Int(spawn.x + x, spawn.y + z));
                    done += chunk;
                    ready += chunk >= 1f ? 1 : 0;
                    total++;
                }
            }
            status = "Building terrain (" + ready + "/" + total + " chunks)";
            return 0.3f + 0.7f * done / total;
        }

        /// <summary>Spawns the player once the area around the spawn can be walked on.</summary>
        public static void ClientUpdate() {
            // between the server's times, a client runs it on its side
            if (!NetworkServer.active) {
                WorldTime.Advance(Time.deltaTime);
            }
            if (!hasWorldInfo || isSpawnAreaReady) {
                return;
            }
            WorldBehaviour world = WorldBehaviour.Instance;

            if (world != null) {
                Vector2Int center = spawn;

                for (int x = -SpawnAreaRadius; x <= SpawnAreaRadius; x++) {
                    for (int z = -SpawnAreaRadius; z <= SpawnAreaRadius; z++) {
                        if (!world.IsWalkable(new Vector2Int(center.x + x, center.y + z))) {
                            return;
                        }
                    }
                }
                world.IsLoadingSpawn = false;
                WorldLoadProfiler.Finish();
            }

            isSpawnAreaReady = true;
            if (NetworkClient.localPlayer == null) {
                NetworkClient.AddPlayer();
            }
        }

        private static void onWorldInfo(WorldInfoMessage message) {
            spawn = message.spawnChunk;
            hasWorldInfo = true;
            ServerMaxViewDistance = message.maxViewDistance > 0 ? message.maxViewDistance : message.viewDistance;
            Difficulty = (Difficulty)message.difficulty;

            WorldBehaviour world = WorldBehaviour.Instance;
            if (world == null) {
                return; // scenes without a generated world
            }
            world.ViewDistance = message.viewDistance;
            // the host's world was set up by the server
            if (!NetworkServer.active) {
                world.Initialize(message.seed, clientChanges);
                world.ChunkGenerated += onClientChunkGenerated;
                world.ChunkUnloaded += onClientChunkUnloaded;
            }
            world.SpawnChunk = message.spawnChunk;
            world.IsLoadingSpawn = true;
        }

        private static void onChunkChanges(ChunkChangesMessage message) {
            if (NetworkServer.active) {
                return;
            }
            Vector2Int coords = new Vector2Int(message.chunkX, message.chunkZ);
            clientChanges.Merge(coords, ChunkChangesSerializer.Deserialize(message.data));

            if (message.isLast) {
                WorldBehaviour.Instance.RequestChunk(coords);
            }
        }

        private static void onChunkUnload(ChunkUnloadMessage message) {
            if (NetworkServer.active) {
                return;
            }
            Vector2Int coords = new Vector2Int(message.chunkX, message.chunkZ);
            WorldBehaviour.Instance.UnloadChunk(coords);
            clientChanges.Remove(c => c == coords);
        }

        // changes of chunks still generating are applied when they're done, see onClientChunkGenerated
        private static void onBlockChanged(BlockChangedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            BlockType type = (BlockType)BlockDatabase.IdOf(message.blockName);
            int color = BrickColorPalette.Get(message.color) != null ? message.color : BrickColor.None;
            clientChanges.SetBlock(message.block, type, message.placedBy != 0
                ? new Placer() { playerId = message.placedBy, placedAt = message.placedAt }
                : (Placer?)null, color);

            if (WorldBehaviour.Instance.IsGenerated(WorldChanges.ChunkOfBlock(message.block))) {
                WorldBehaviour.Instance.SetBlockType(message.block, type, color);
            }
        }

        private static void onBrickPlaced(BrickPlacedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            SavedBrick brick = new SavedBrick() {
                id = new Guid(message.id),
                itemId = message.itemId,
                color = message.color,
                origin = message.origin,
                rotation = message.rotation,
                placer = new Placer() { playerId = message.placedBy, placedAt = message.placedAt },
                state = message.state,
                attachedTo = string.IsNullOrEmpty(message.attachedTo) ? Guid.Empty : new Guid(message.attachedTo),
            };
            clientChanges.AddBrick(brick);

            if (WorldBehaviour.Instance == null || WorldBehaviour.Instance.IsGenerated(WorldChanges.ChunkOfBrick(message.origin))) {
                spawnBricks(new List<SavedBrick>() { brick });
            }
        }

        private static void onBrickRemoved(BrickRemovedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            clientChanges.RemoveBrick(new Guid(message.id), message.origin);

            if (Server.bricks.TryGetValue(message.id, out Brick brick)) {
                Server.Instance.removeBrick(brick);
            }
        }

        private static void onBrickState(BrickStateMessage message) {
            if (NetworkServer.active) {
                return;
            }
            if (clientChanges.TryGetBrick(new Guid(message.id), message.origin, out SavedBrick saved)) {
                saved.state = message.state;
                clientChanges.AddBrick(saved);
            }
            if (Server.bricks.TryGetValue(message.id, out Brick brick)) {
                Server.Instance.setBrickState(brick, message.state);
            }
        }

        private static void onClientChunkGenerated(Chunk chunk) {
            spawnBricks(clientChanges.GetBricks(new Vector2Int(chunk.X, chunk.Z)));
        }

        private static void onClientChunkUnloaded(Vector2Int coords) {
            removeBricks(coords);
        }

        // -------- both --------

        private static void spawnBricks(List<SavedBrick> bricks) {
            // attachments after the bricks they go in
            bricks.Sort((a, b) => (a.attachedTo != Guid.Empty).CompareTo(b.attachedTo != Guid.Empty));
            foreach (SavedBrick saved in bricks) {
                string id = saved.id.ToString();

                if (Server.bricks.ContainsKey(id)) {
                    continue;
                }
                if (!Server.items.TryGetValue(saved.itemId, out Item item)) {
                    Debug.LogError("Unknown item " + saved.itemId + " for brick " + id);
                    continue;
                }
                string attachedTo = saved.attachedTo != Guid.Empty ? saved.attachedTo.ToString() : null;
                if (attachedTo != null && !Server.bricks.ContainsKey(attachedTo)) {
                    continue; // its brick isn't there (yet)
                }
                Server.Instance.spawnBrick(item, saved.color, new BrickPlacement(item.brickModel, saved.origin, saved.rotation), id, saved.state, attachedTo);
            }
        }

        // removes the bricks whose origin is in the chunk, their changes stay
        private static void removeBricks(Vector2Int chunk) {
            List<Brick> removed = new List<Brick>();

            foreach (Brick brick in BrickGrid.BricksInChunk(chunk)) {
                if (WorldChanges.ChunkOfBrick(brick.placement.origin) == chunk) {
                    removed.Add(brick); // and what's attached to it, with it
                }
            }
            foreach (Brick brick in removed) {
                Server.Instance.removeBrick(brick);
            }
        }

        private static void addArea(HashSet<Vector2Int> area, Vector2Int center, int radius) {
            for (int x = -radius; x <= radius; x++) {
                for (int z = -radius; z <= radius; z++) {
                    area.Add(new Vector2Int(center.x + x, center.y + z));
                }
            }
        }

        private static int distance(Vector2Int a, Vector2Int b) {
            return Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));
        }
    }
}
