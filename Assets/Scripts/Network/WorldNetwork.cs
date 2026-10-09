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

        /// <summary>The saved world, null in scenes that aren't saved (the test scene).</summary>
        public static WorldStorage Storage { get; private set; }

        private class ClientInterest
        {
            public readonly HashSet<Vector2Int> sentChunks = new HashSet<Vector2Int>();

            // where the player will appear: the world spawn, or where it left last time
            public Vector2Int spawnChunk;
        }

        private static WorldChanges serverChanges;
        private static Vector2Int spawnChunk;
        private static readonly Dictionary<NetworkConnectionToClient, ClientInterest> interests = new Dictionary<NetworkConnectionToClient, ClientInterest>();
        private static readonly HashSet<Vector2Int> serverChunks = new HashSet<Vector2Int>();
        private static float nextServerUpdate;

        private static int loadRadius {
            get { return WorldBehaviour.Instance.ViewDistance + 1; }
        }

        /// <param name="spawn">absolute position players spawn at</param>
        public static void StartServer(WorldStorage storage, long unsavedSeed, Vector3 spawn) {
            Storage = storage;
            Seed = storage != null ? storage.Seed : unsavedSeed;
            Difficulty = storage != null ? storage.Difficulty : Difficulty.Normal;
            serverChanges = storage != null ? storage.Changes : new WorldChanges();
            interests.Clear();
            serverChunks.Clear();

            NetworkServer.RegisterHandler<JoinWorldMessage>(onJoinWorld);

            spawnChunk = WorldBehaviour.ChunkAt(FloatingOrigin.ToLocal(spawn));

            WorldBehaviour world = WorldBehaviour.Instance;
            if (world != null) {
                world.Initialize(Seed, serverChanges, storage != null ? storage.EnsureLoaded : (Action<Vector2Int>)null);
                world.SpawnChunk = spawnChunk;
                world.ChunkGenerated += onServerChunkGenerated;
                world.ChunkUnloaded += onServerChunkUnloaded;
            }
        }

        public static void StopServer() {
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
            sendToChunk(WorldChanges.ChunkOfBrick(placement.origin), new BrickPlacedMessage() {
                id = brick.id,
                itemId = brick.itemId,
                color = brick.color,
                origin = placement.origin,
                rotation = (byte)placement.rotation,
                placedBy = placer.playerId,
                placedAt = placer.placedAt,
            });

            return brick;
        }

        /// <summary>Who placed a world block, nobody (0) if it was generated.</summary>
        public static Placer ServerPlacerOf(Vector3Int block) {
            return serverChanges != null && serverChanges.TryGetPlacer(block, out Placer placer) ? placer : default(Placer);
        }

        /// <summary>Who placed a brick, nobody (0) if no player did.</summary>
        public static Placer ServerPlacerOf(Brick brick) {
            return serverChanges != null && serverChanges.TryGetBrick(new Guid(brick.id), brick.placement.origin, out SavedBrick saved) ? saved.placer : default(Placer);
        }

        /// <summary>
        /// Moves a brick (it keeps its id, colour and placer). Players see it removed and placed again,
        /// so those that only have one of the two chunks get the right half. False if it doesn't fit there.
        /// </summary>
        public static bool ServerMoveBrick(Brick brick, BrickPlacement to) {
            Placer placer = ServerPlacerOf(brick);
            Vector3Int from = brick.placement.origin;

            if (!Server.Instance.moveBrick(brick, to)) {
                return false;
            }
            serverChanges.RemoveBrick(new Guid(brick.id), from);
            sendToChunk(WorldChanges.ChunkOfBrick(from), new BrickRemovedMessage() { id = brick.id, origin = from });

            serverChanges.AddBrick(toSaved(brick, placer));
            sendToChunk(WorldChanges.ChunkOfBrick(to.origin), new BrickPlacedMessage() {
                id = brick.id,
                itemId = brick.itemId,
                color = brick.color,
                origin = to.origin,
                rotation = (byte)to.rotation,
                placedBy = placer.playerId,
                placedAt = placer.placedAt,
            });
            return true;
        }

        public static void ServerRemoveBrick(Brick brick) {
            Server.Instance.removeBrick(brick);
            serverChanges.RemoveBrick(new Guid(brick.id), brick.placement.origin);
            sendToChunk(WorldChanges.ChunkOfBrick(brick.placement.origin), new BrickRemovedMessage() { id = brick.id, origin = brick.placement.origin });
        }

        /// <summary>Loads and unloads chunks around the players and streams them to the clients.</summary>
        public static void ServerUpdate() {
            WorldBehaviour world = WorldBehaviour.Instance;

            if (world == null || !world.Seed.HasValue || Time.unscaledTime < nextServerUpdate) {
                return;
            }
            nextServerUpdate = Time.unscaledTime + ServerUpdateInterval;

            HashSet<Vector2Int> wanted = new HashSet<Vector2Int>();
            HashSet<Vector2Int> kept = new HashSet<Vector2Int>();
            Dictionary<NetworkConnectionToClient, Vector2Int> centers = new Dictionary<NetworkConnectionToClient, Vector2Int>();

            foreach (NetworkConnectionToClient conn in interests.Keys) {
                Vector2Int center = conn.identity != null ? WorldBehaviour.ChunkAt(conn.identity.transform.position) : interests[conn].spawnChunk;
                centers[conn] = center;
                addArea(wanted, center, loadRadius);
                addArea(kept, center, loadRadius + UnloadMargin);
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
                if (distance(coords, center) > loadRadius + UnloadMargin) {
                    forgotten.Add(coords);
                }
            }
            foreach (Vector2Int coords in forgotten) {
                interest.sentChunks.Remove(coords);
                conn.Send(new ChunkUnloadMessage() { chunkX = coords.x, chunkZ = coords.y });
            }

            // send what's missing, nearest first
            List<Vector2Int> missing = new List<Vector2Int>();
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

        private static void onJoinWorld(NetworkConnectionToClient conn, JoinWorldMessage message) {
            if (!conn.isReady) {
                NetworkServer.SetClientReady(conn);
            }
            // returning players stream in around where they left
            ClientInterest interest = new ClientInterest() { spawnChunk = spawnChunk };
            if (conn.authenticationData is ConnectedPlayer player && BrickcraftNetworkManager.HasSavedPosition(player.record)) {
                PlayerRecord record = player.record;
                interest.spawnChunk = WorldBehaviour.ChunkAt(FloatingOrigin.ToLocal(record.LastX.Value, record.LastY.Value, record.LastZ.Value));
            }
            interests[conn] = interest;

            conn.Send(new WorldInfoMessage() {
                seed = Seed,
                difficulty = (byte)Difficulty,
                spawnChunk = interest.spawnChunk,
                viewDistance = WorldBehaviour.Instance != null ? WorldBehaviour.Instance.ViewDistance : 0,
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
            hasWorldInfo = false;
            isSpawnAreaReady = false;
            clientChanges = new WorldChanges();

            NetworkClient.RegisterHandler<WorldInfoMessage>(onWorldInfo);
            NetworkClient.RegisterHandler<ChunkChangesMessage>(onChunkChanges);
            NetworkClient.RegisterHandler<ChunkUnloadMessage>(onChunkUnload);
            NetworkClient.RegisterHandler<BlockChangedMessage>(onBlockChanged);
            NetworkClient.RegisterHandler<BrickPlacedMessage>(onBrickPlaced);
            NetworkClient.RegisterHandler<BrickRemovedMessage>(onBrickRemoved);
        }

        public static void StopClient() {
            hasWorldInfo = false;
            isSpawnAreaReady = false;

            if (WorldBehaviour.Instance != null && !NetworkServer.active) {
                WorldBehaviour.Instance.ChunkGenerated -= onClientChunkGenerated;
                WorldBehaviour.Instance.ChunkUnloaded -= onClientChunkUnloaded;
            }
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
            NetworkClient.Send(new JoinWorldMessage());
        }

        /// <summary>How far joining the game is, from 0 to 1, and what it's doing, for the loading screen.</summary>
        public static float GetLoadingProgress(out string status) {
            if (isSpawnAreaReady) {
                status = "Joining...";
                return 1f;
            }
            if (!NetworkClient.isConnected) {
                status = "Connecting...";
                return 0f;
            }
            if (NetworkManager.loadingSceneAsync != null) {
                status = "Loading world...";
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
            Difficulty = (Difficulty)message.difficulty;

            WorldBehaviour world = WorldBehaviour.Instance;
            if (world == null) {
                return; // scenes without a generated world
            }
            // the host's world was set up by the server
            if (!NetworkServer.active) {
                world.ViewDistance = message.viewDistance;
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

        private static void onClientChunkGenerated(Chunk chunk) {
            spawnBricks(clientChanges.GetBricks(new Vector2Int(chunk.X, chunk.Z)));
        }

        private static void onClientChunkUnloaded(Vector2Int coords) {
            removeBricks(coords);
        }

        // -------- both --------

        private static void spawnBricks(List<SavedBrick> bricks) {
            foreach (SavedBrick saved in bricks) {
                string id = saved.id.ToString();

                if (Server.bricks.ContainsKey(id)) {
                    continue;
                }
                if (!Server.items.TryGetValue(saved.itemId, out Item item)) {
                    Debug.LogError("Unknown item " + saved.itemId + " for brick " + id);
                    continue;
                }
                Server.Instance.spawnBrick(item, saved.color, new BrickPlacement(item.brickModel, saved.origin, saved.rotation), id);
            }
        }

        // removes the bricks whose origin is in the chunk, their changes stay
        private static void removeBricks(Vector2Int chunk) {
            List<Brick> removed = new List<Brick>();

            foreach (Brick brick in Server.bricks.Values) {
                if (WorldChanges.ChunkOfBrick(brick.placement.origin) == chunk) {
                    removed.Add(brick);
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
