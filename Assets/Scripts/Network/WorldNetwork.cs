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

        /// <summary>The saved world, null in scenes that aren't saved (the test scene).</summary>
        public static WorldStorage Storage { get; private set; }

        private class ClientInterest
        {
            public readonly HashSet<Vector2Int> sentChunks = new HashSet<Vector2Int>();
        }

        private static WorldChanges serverChanges;
        private static Vector3 spawnPosition;
        private static readonly Dictionary<NetworkConnectionToClient, ClientInterest> interests = new Dictionary<NetworkConnectionToClient, ClientInterest>();
        private static readonly HashSet<Vector2Int> serverChunks = new HashSet<Vector2Int>();
        private static float nextServerUpdate;

        private static int loadRadius {
            get { return WorldBehaviour.Instance.ViewDistance + 1; }
        }

        public static void StartServer(WorldStorage storage, long unsavedSeed, Vector3 spawn) {
            Storage = storage;
            Seed = storage != null ? storage.Seed : unsavedSeed;
            serverChanges = storage != null ? storage.Changes : new WorldChanges();
            spawnPosition = spawn;
            interests.Clear();
            serverChunks.Clear();

            NetworkServer.RegisterHandler<JoinWorldMessage>(onJoinWorld);

            WorldBehaviour world = WorldBehaviour.Instance;
            if (world != null) {
                world.Initialize(Seed, serverChanges, storage != null ? storage.EnsureLoaded : (Action<Vector2Int>)null);
                world.SpawnPosition = spawn;
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

        public static bool ServerSetBlock(Vector3Int block, BlockType type) {
            if (WorldBehaviour.Instance == null || !WorldBehaviour.Instance.SetBlockType(block, type)) {
                return false;
            }
            serverChanges.SetBlock(block, type);
            sendToChunk(WorldChanges.ChunkOfBlock(block), new BlockChangedMessage() { block = block, blockType = (byte)type });

            return true;
        }

        public static Brick ServerPlaceBrick(Item item, BrickPlacement placement) {
            Brick brick = Server.Instance.spawnBrick(item, placement);
            serverChanges.AddBrick(toSaved(brick));
            sendToChunk(WorldChanges.ChunkOfBrick(placement.origin), new BrickPlacedMessage() {
                id = brick.id,
                itemId = brick.itemId,
                origin = placement.origin,
                rotation = (byte)placement.rotation,
            });

            return brick;
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
                Vector2Int center = WorldBehaviour.ChunkAt(conn.identity != null ? conn.identity.transform.position : spawnPosition);
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
            interests[conn] = new ClientInterest();

            conn.Send(new WorldInfoMessage() {
                seed = Seed,
                spawn = spawnPosition,
                viewDistance = WorldBehaviour.Instance != null ? WorldBehaviour.Instance.ViewDistance : 0,
            });

            // scenes without a streamed world (the test scene) just send their bricks
            if (WorldBehaviour.Instance == null && !(conn is LocalConnectionToClient)) {
                foreach (Brick brick in Server.bricks.Values) {
                    conn.Send(new BrickPlacedMessage() {
                        id = brick.id,
                        itemId = brick.itemId,
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

        private static SavedBrick toSaved(Brick brick) {
            return new SavedBrick() {
                id = new Guid(brick.id),
                itemId = brick.itemId,
                origin = brick.placement.origin,
                rotation = (byte)brick.placement.rotation,
            };
        }

        // -------- client --------

        private static bool hasRequestedWorld;
        private static bool hasWorldInfo;
        private static bool isSpawnAreaReady;
        private static Vector3 spawn;

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

        /// <summary>Spawns the player once the area around the spawn can be walked on.</summary>
        public static void ClientUpdate() {
            if (!hasWorldInfo || isSpawnAreaReady) {
                return;
            }
            WorldBehaviour world = WorldBehaviour.Instance;

            if (world != null) {
                Vector2Int center = WorldBehaviour.ChunkAt(spawn);

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
            spawn = message.spawn;
            hasWorldInfo = true;

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
            world.SpawnPosition = message.spawn;
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
            clientChanges.SetBlock(message.block, (BlockType)message.blockType);

            if (WorldBehaviour.Instance.IsGenerated(WorldChanges.ChunkOfBlock(message.block))) {
                WorldBehaviour.Instance.SetBlockType(message.block, (BlockType)message.blockType);
            }
            logChange("block " + message.block + " is now " + (BlockType)message.blockType);
        }

        private static void onBrickPlaced(BrickPlacedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            SavedBrick brick = new SavedBrick() { id = new Guid(message.id), itemId = message.itemId, origin = message.origin, rotation = message.rotation };
            clientChanges.AddBrick(brick);

            if (WorldBehaviour.Instance == null || WorldBehaviour.Instance.IsGenerated(WorldChanges.ChunkOfBrick(message.origin))) {
                spawnBricks(new List<SavedBrick>() { brick });
            }
            logChange("brick placed at " + message.origin);
        }

        private static void onBrickRemoved(BrickRemovedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            clientChanges.RemoveBrick(new Guid(message.id), message.origin);

            if (Server.bricks.TryGetValue(message.id, out Brick brick)) {
                Server.Instance.removeBrick(brick);
            }
            logChange("brick " + message.id + " removed");
        }

        private static void onClientChunkGenerated(Chunk chunk) {
            spawnBricks(clientChanges.GetBricks(new Vector2Int(chunk.X, chunk.Z)));
        }

        private static void onClientChunkUnloaded(Vector2Int coords) {
            removeBricks(coords);
        }

        private static void logChange(string description) {
            if (Debug.isDebugBuild) {
                Debug.Log("World change from the server: " + description);
            }
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
                Server.Instance.spawnBrick(item, new BrickPlacement(item.brickModel, saved.origin, saved.rotation), id);
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
