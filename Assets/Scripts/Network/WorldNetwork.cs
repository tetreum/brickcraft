using System;
using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Keeps the world in sync. The server owns the world: every change goes through the
    /// Server* methods, which apply it and send it to the clients.
    ///
    /// Clients generate the terrain themselves from the server's seed, so only what changed
    /// since generation travels over the network. A joining client gets those changes first,
    /// and only then spawns its player.
    ///
    /// In host mode the host's client shares the server's world, so it ignores the changes.
    /// </summary>
    public static class WorldNetwork
    {
        // -------- server --------

        public static long Seed { get; private set; }

        // blocks that differ from the generated world, sent to joining clients
        private static readonly Dictionary<Vector3Int, BlockType> changedBlocks = new Dictionary<Vector3Int, BlockType>();

        public static void StartServer(long seed) {
            Seed = seed;
            changedBlocks.Clear();
            NetworkServer.RegisterHandler<JoinWorldMessage>(onJoinWorld);
        }

        public static bool ServerSetBlock(Vector3Int block, BlockType type) {
            if (WorldBehaviour.Instance == null || !WorldBehaviour.Instance.SetBlockType(block, type)) {
                return false;
            }
            changedBlocks[block] = type;
            NetworkServer.SendToReady(new BlockChangedMessage() { block = block, blockType = (byte)type });

            return true;
        }

        public static Brick ServerPlaceBrick(Item item, BrickPlacement placement) {
            Brick brick = Server.Instance.spawnBrick(item, placement);
            NetworkServer.SendToReady(toMessage(brick));

            return brick;
        }

        public static void ServerRemoveBrick(Brick brick) {
            Server.Instance.removeBrick(brick);
            NetworkServer.SendToReady(new BrickRemovedMessage() { id = brick.id });
        }

        private static void onJoinWorld(NetworkConnectionToClient conn, JoinWorldMessage message) {
            if (!conn.isReady) {
                NetworkServer.SetClientReady(conn);
            }

            conn.Send(new WorldInfoMessage() { seed = Seed });

            // the host's client shares the server's world
            if (!(conn is LocalConnectionToClient)) {
                foreach (KeyValuePair<Vector3Int, BlockType> change in changedBlocks) {
                    conn.Send(new BlockChangedMessage() { block = change.Key, blockType = (byte)change.Value });
                }
                foreach (Brick brick in Server.bricks.Values) {
                    conn.Send(toMessage(brick));
                }
            }

            conn.Send(new WorldSnapshotEndMessage());
        }

        private static BrickPlacedMessage toMessage(Brick brick) {
            return new BrickPlacedMessage() {
                id = brick.id,
                itemId = brick.itemId,
                origin = brick.placement.origin,
                rotation = (byte)brick.placement.rotation,
            };
        }

        // -------- client --------

        private static bool hasRequestedWorld;
        private static bool isWorldReady;
        private static bool hasSnapshot;
        private static int changesReceived;

        // changes received while the world is still being generated
        private static readonly List<Action> pendingChanges = new List<Action>();

        public static void StartClient() {
            hasRequestedWorld = false;
            isWorldReady = false;
            hasSnapshot = false;
            changesReceived = 0;
            pendingChanges.Clear();

            NetworkClient.RegisterHandler<WorldInfoMessage>(onWorldInfo);
            NetworkClient.RegisterHandler<BlockChangedMessage>(onBlockChanged);
            NetworkClient.RegisterHandler<BrickPlacedMessage>(onBrickPlaced);
            NetworkClient.RegisterHandler<BrickRemovedMessage>(onBrickRemoved);
            NetworkClient.RegisterHandler<WorldSnapshotEndMessage>(onSnapshotEnd);
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

        private static void onWorldInfo(WorldInfoMessage message) {
            if (WorldBehaviour.Instance == null) {
                onWorldReady(); // scenes without a generated world
                return;
            }
            WorldBehaviour.Instance.OnReady += onWorldReady;
            WorldBehaviour.Instance.Generate(message.seed);
        }

        private static void onBlockChanged(BlockChangedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            apply(() => WorldBehaviour.Instance.SetBlockType(message.block, (BlockType)message.blockType), "block " + message.block + " is now " + (BlockType)message.blockType);
        }

        private static void onBrickPlaced(BrickPlacedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            apply(() => {
                if (!Server.items.TryGetValue(message.itemId, out Item item)) {
                    Debug.LogError("The server placed an unknown item " + message.itemId);
                    return;
                }
                BrickPlacement placement = new BrickPlacement(item.brickModel, message.origin, message.rotation);
                Server.Instance.spawnBrick(item, placement, message.id);
            }, "brick placed at " + message.origin);
        }

        private static void onBrickRemoved(BrickRemovedMessage message) {
            if (NetworkServer.active) {
                return;
            }
            apply(() => {
                if (Server.bricks.TryGetValue(message.id, out Brick brick)) {
                    Server.Instance.removeBrick(brick);
                }
            }, "brick " + message.id + " removed");
        }

        private static void onSnapshotEnd(WorldSnapshotEndMessage message) {
            if (!NetworkServer.active) {
                Debug.Log("Received the world: " + changesReceived + " changes since it was generated");
            }
            hasSnapshot = true;
            tryAddPlayer();
        }

        private static void apply(Action change, string description) {
            changesReceived++;

            if (hasSnapshot && Debug.isDebugBuild) {
                Debug.Log("World change from the server: " + description);
            }
            if (isWorldReady) {
                change();
            } else {
                pendingChanges.Add(change);
            }
        }

        private static void onWorldReady() {
            if (WorldBehaviour.Instance != null) {
                WorldBehaviour.Instance.OnReady -= onWorldReady;
            }
            isWorldReady = true;

            foreach (Action change in pendingChanges) {
                change();
            }
            pendingChanges.Clear();

            tryAddPlayer();
        }

        private static void tryAddPlayer() {
            if (isWorldReady && hasSnapshot && NetworkClient.localPlayer == null) {
                NetworkClient.AddPlayer();
            }
        }
    }
}
