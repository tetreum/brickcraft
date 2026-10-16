using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Combat;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>Server to client: a storage brick opened for it, with what it keeps.</summary>
    public struct StorageOpenedMessage : NetworkMessage
    {
        public string brickId;
        /// <summary>What it's called (its item's name).</summary>
        public string title;
        public int slots;
        public InventoryItem[] items;
    }

    /// <summary>Server to the players looking into a storage brick: what it keeps now.</summary>
    public struct StorageContentsMessage : NetworkMessage
    {
        public string brickId;
        public InventoryItem[] items;
    }

    /// <summary>Server to client: it can't look into that storage brick anymore (too far, broken, locked...).</summary>
    public struct StorageClosedMessage : NetworkMessage
    {
        public string brickId;
    }

    /// <summary>
    /// Bricks that keep items (their item has the "storage" tag, like the chest), on the server. What each keeps
    /// is saved with its chunk (WorldChanges' storage, never sent with chunks): a player gets it when opening the
    /// brick (a click on it, PlayerNetwork.CmdOpenStorage) and every change while it looks inside. Only one player
    /// looks into a storage brick at a time: the others are told it's in use. Items move between it and their
    /// inventories with PlayerInventory.CmdStorageMove, under the inventory's rules (see Inventory.MoveBetween).
    ///
    /// Locks: with something attached to it (the chest's lid) only whoever placed it (and admins) can open it;
    /// without, anyone. Anyone can break one: what it kept falls on the ground (see DroppedItems). Its lid is open
    /// while someone looks inside. Looking players stop when they walk away, die, leave, or it breaks or gets locked.
    /// </summary>
    public static class BrickStorage
    {
        /// <summary>How far a player can be from a storage brick it looks into.</summary>
        public const float MaxDistance = 6;

        private class Opened
        {
            public Brick brick;
            public int slots;
            public readonly HashSet<NetworkConnectionToClient> viewers = new HashSet<NetworkConnectionToClient>();
        }

        // the storage bricks someone is looking into, by brick id
        private static readonly Dictionary<string, Opened> opened = new Dictionary<string, Opened>();

        public static void Clear() {
            opened.Clear();
        }

        public static bool IsStorage(Brick brick) {
            return brick != null && Server.items.TryGetValue(brick.itemId, out Item item) && item.IsStorage;
        }

        /// <summary>The storage brick a click on that brick opens: itself, or the one it's attached to (a chest's lid). Null if none.</summary>
        public static Brick StorageOf(Brick brick) {
            if (IsStorage(brick)) {
                return brick;
            }
            if (brick != null && brick.attachedTo != null && Server.bricks.TryGetValue(brick.attachedTo, out Brick holder) && IsStorage(holder)) {
                return holder;
            }
            return null;
        }

        /// <summary>It has something attached (its lid): only its owner opens it.</summary>
        public static bool IsLocked(Brick storage) {
            return Server.attachmentOf(storage) != null;
        }

        /// <summary>Whether that player can look into it: unlocked, or its owner (whoever placed it), or an admin.</summary>
        public static bool CanOpen(NetworkConnectionToClient conn, Brick storage) {
            if (!IsLocked(storage)) {
                return true;
            }
            if (!(conn.authenticationData is ConnectedPlayer player)) {
                return false;
            }
            int owner = WorldNetwork.ServerPlacerOf(storage).playerId;
            return player.record.IsAdmin || owner == 0 || owner == player.record.Id;
        }

        /// <summary>What it keeps (a copy).</summary>
        public static List<InventoryItem> Contents(Brick storage) {
            return WorldNetwork.ServerGetStorage(storage);
        }

        public static int SlotsOf(Brick storage) {
            return Server.items.TryGetValue(storage.itemId, out Item item) ? item.storageSlots : Item.DefaultStorageSlots;
        }

        /// <summary>Someone else is looking into it (only one at a time).</summary>
        public static bool IsInUse(Brick storage, NetworkConnectionToClient conn) {
            if (!opened.TryGetValue(storage.id, out Opened entry)) {
                return false;
            }
            foreach (NetworkConnectionToClient viewer in entry.viewers) {
                if (viewer != conn) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The player looks into it (it was checked it can, and that nobody else is), getting what it keeps.</summary>
        public static void ServerOpen(NetworkConnectionToClient conn, Brick storage) {
            closeAll(conn, storage.id); // one at a time
            if (!opened.TryGetValue(storage.id, out Opened entry)) {
                entry = new Opened() { brick = storage, slots = SlotsOf(storage) };
                opened[storage.id] = entry;
            }
            bool first = entry.viewers.Count == 0;
            entry.viewers.Add(conn);
            // stacks past its slots (its item has fewer now) fall out instead of staying out of reach
            List<InventoryItem> items = Contents(storage);
            List<InventoryItem> outside = items.FindAll(stack => stack.slot > entry.slots);
            if (outside.Count > 0) {
                items.RemoveAll(stack => stack.slot > entry.slots);
                WorldNetwork.ServerSetStorage(storage, items);
                DroppedItems.Spill(outside, storage.WorldBounds.center + Vector3.up * 0.3f);
            }
            conn.Send(new StorageOpenedMessage() {
                brickId = storage.id,
                title = GameText.Reference(Server.items[storage.itemId]), // named by each player
                slots = entry.slots,
                items = items.ToArray(),
            });
            if (first) {
                showLid(storage, true);
            }
        }

        /// <summary>The player stops looking into it.</summary>
        public static void ServerClose(NetworkConnectionToClient conn, string brickId, bool tell) {
            if (!opened.TryGetValue(brickId, out Opened entry) || !entry.viewers.Remove(conn)) {
                return;
            }
            if (tell) {
                conn.Send(new StorageClosedMessage() { brickId = brickId });
            }
            if (entry.viewers.Count == 0) {
                opened.Remove(brickId);
                showLid(entry.brick, false);
            }
        }

        /// <summary>Whether the player is looking into that one now.</summary>
        public static bool IsViewing(NetworkConnectionToClient conn, string brickId, out Brick storage) {
            storage = null;
            if (!opened.TryGetValue(brickId, out Opened entry) || !entry.viewers.Contains(conn)) {
                return false;
            }
            storage = entry.brick;
            return true;
        }

        /// <summary>What it keeps changed (on the server): it's saved, and everyone looking inside sees it.</summary>
        public static void ServerChanged(Brick storage, List<InventoryItem> items) {
            WorldNetwork.ServerSetStorage(storage, items);
            if (opened.TryGetValue(storage.id, out Opened entry)) {
                StorageContentsMessage message = new StorageContentsMessage() { brickId = storage.id, items = items.ToArray() };
                foreach (NetworkConnectionToClient viewer in entry.viewers) {
                    viewer.Send(message);
                }
            }
        }

        /// <summary>It's being broken: what it kept falls around it, and nobody looks inside anymore.</summary>
        public static void ServerBroken(Brick storage) {
            List<InventoryItem> kept = Contents(storage);
            if (opened.TryGetValue(storage.id, out Opened entry)) {
                foreach (NetworkConnectionToClient viewer in new List<NetworkConnectionToClient>(entry.viewers)) {
                    ServerClose(viewer, storage.id, true);
                }
            }
            if (kept.Count > 0) {
                DroppedItems.Spill(kept, storage.WorldBounds.center + Vector3.up * 0.3f);
                WorldNetwork.ServerSetStorage(storage, new List<InventoryItem>());
            }
        }

        /// <summary>Those looking inside who shouldn't anymore stop: too far, dead, gone, or locked out.</summary>
        public static void Update() {
            if (opened.Count == 0) {
                return;
            }
            foreach (KeyValuePair<string, Opened> entry in new List<KeyValuePair<string, Opened>>(opened)) {
                bool exists = Server.bricks.ContainsKey(entry.Key);
                foreach (NetworkConnectionToClient viewer in new List<NetworkConnectionToClient>(entry.Value.viewers)) {
                    bool keep = exists && viewer.identity != null && !viewer.identity.GetComponent<Health>().isDead
                        && Vector3.Distance(viewer.identity.transform.position, entry.Value.brick.WorldBounds.center) <= MaxDistance
                        && CanOpen(viewer, entry.Value.brick);
                    if (!keep) {
                        ServerClose(viewer, entry.Key, viewer.isReady);
                    }
                }
            }
        }

        // a player opening another (or leaving): the ones it had open close
        private static void closeAll(NetworkConnectionToClient conn, string except) {
            foreach (KeyValuePair<string, Opened> entry in new List<KeyValuePair<string, Opened>>(opened)) {
                if (entry.Key != except && entry.Value.viewers.Contains(conn)) {
                    ServerClose(conn, entry.Key, true);
                }
            }
        }

        // its lid (what's attached to it, if it's a lid) open while someone looks inside
        private static void showLid(Brick storage, bool open) {
            Brick lid = Server.attachmentOf(storage);
            if (lid != null && lid.gameObject != null && lid.gameObject.GetComponentInChildren<BrickLid>() != null) {
                WorldNetwork.ServerSetBrickState(lid, open ? BrickLid.Open : BrickLid.Closed);
            }
        }
    }
}
