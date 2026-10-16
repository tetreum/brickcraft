using System.Collections.Generic;
using Brickcraft.World;
using Mirror;
using UnityEngine;

namespace Brickcraft.Network
{
    /// <summary>
    /// Items on the ground (DroppedItem), on the server: dropping them (ServerDrop, Spill), players picking them
    /// up by walking onto them (as much as fits in their inventory, after PickupDelay), drops of the same item
    /// close together merging, and each going away Lifetime seconds after it dropped. They're saved with their
    /// chunk (World.SavedDrop, with the time they have left) when it unloads, on autosave and when the server
    /// stops, and come back with it (see Npcs.NpcSystem, which knows when chunks are ready).
    /// </summary>
    public static class DroppedItems
    {
        public const string PrefabPath = "DroppedItem";
        /// <summary>Seconds before a drop goes away.</summary>
        public const float Lifetime = 300;
        /// <summary>Seconds after dropping before it can be picked up.</summary>
        public const float PickupDelay = 1;
        private const float PickupRadius = 1.0f;
        private const float PickupHeight = 1.4f;
        private const float MergeRadius = 0.8f;
        private const float CheckInterval = 0.5f;

        private static readonly List<DroppedItem> drops = new List<DroppedItem>();
        private static float nextCheck;

        public static IReadOnlyList<DroppedItem> All {
            get { return drops; }
        }

        internal static void Register(DroppedItem drop) {
            drops.Add(drop);
        }

        internal static void Unregister(DroppedItem drop) {
            drops.Remove(drop);
        }

        public static void Clear() {
            drops.Clear();
            nextCheck = 0; // Time.unscaledTime starts over in a new play mode
        }

        /// <summary>Puts a stack on the ground at a local position, moving at that velocity until it lands.</summary>
        public static DroppedItem ServerDrop(string itemId, int color, int quantity, int health, Vector3 position, Vector3 velocity,
                float pickupDelay = PickupDelay, float lifetime = Lifetime) {
            if (quantity <= 0 || !Server.items.ContainsKey(itemId)) {
                return null;
            }
            GameObject go = Object.Instantiate(Resources.Load<GameObject>(PrefabPath), position, Quaternion.identity);
            DroppedItem drop = go.GetComponent<DroppedItem>();
            drop.itemId = itemId;
            drop.color = color;
            drop.quantity = quantity;
            drop.health = health;
            drop.Velocity = velocity;
            drop.PickableAt = NetworkTime.time + pickupDelay;
            drop.DespawnAt = NetworkTime.time + lifetime;
            go.name = "Drop " + itemId;
            NetworkServer.Spawn(go);
            return drop;
        }

        /// <summary>Throws stacks out around a point (what was in a broken chest), each its own way.</summary>
        public static void Spill(IEnumerable<InventoryItem> items, Vector3 center) {
            foreach (InventoryItem stack in items) {
                Vector2 away = Random.insideUnitCircle * 2.5f;
                ServerDrop(stack.itemId, stack.color, stack.quantity, stack.health, center, new Vector3(away.x, Random.Range(2.5f, 4f), away.y));
            }
        }

        public static void Update() {
            double now = NetworkTime.time;
            foreach (DroppedItem drop in new List<DroppedItem>(drops)) {
                if (drop == null) {
                    continue;
                }
                if (now >= drop.DespawnAt) {
                    NetworkServer.Destroy(drop.gameObject);
                    continue;
                }
                if (now >= drop.PickableAt) {
                    pickUp(drop);
                }
            }
            if (Time.unscaledTime < nextCheck) {
                return;
            }
            nextCheck = Time.unscaledTime + CheckInterval;
            merge();
            // what held them up may be gone
            foreach (DroppedItem drop in drops) {
                if (drop != null && !Physics.Raycast(drop.transform.position + Vector3.up * 0.05f, Vector3.down, 0.15f,
                        Physics.DefaultRaycastLayers & ~(1 << (int)Game.Layers.Water), QueryTriggerInteraction.Ignore)) {
                    drop.Unsettle();
                }
            }
        }

        // players standing on it get as much as fits
        private static void pickUp(DroppedItem drop) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn.identity == null || conn.identity.GetComponent<Combat.Health>().isDead) {
                    continue;
                }
                Vector3 offset = conn.identity.transform.position - drop.transform.position;
                // the player's position is its middle
                if (new Vector2(offset.x, offset.z).magnitude > PickupRadius || offset.y < -0.5f || offset.y > PickupHeight + 0.9f) {
                    continue;
                }
                PlayerInventory inventory = conn.identity.GetComponent<PlayerInventory>();
                int room = Inventory.Room(inventory.items, drop.itemId, drop.color, drop.health);
                int taken = Mathf.Min(room, drop.quantity);
                if (taken <= 0 || !inventory.ServerAdd(drop.itemId, drop.color, taken, drop.health)) {
                    continue;
                }
                drop.quantity -= taken;
                conn.identity.GetComponent<PlayerNetwork>().ServerPickedUp();
                if (drop.quantity <= 0) {
                    NetworkServer.Destroy(drop.gameObject);
                    return;
                }
            }
        }

        // drops of the same stack close together become one (the older one keeps it)
        private static void merge() {
            for (int i = 0; i < drops.Count; i++) {
                DroppedItem a = drops[i];
                if (a == null || a.quantity <= 0) {
                    continue;
                }
                for (int j = i + 1; j < drops.Count; j++) {
                    DroppedItem b = drops[j];
                    if (b == null || b.quantity <= 0 || b.itemId != a.itemId || b.color != a.color || b.health != a.health
                            || Vector3.Distance(a.transform.position, b.transform.position) > MergeRadius) {
                        continue;
                    }
                    a.quantity += b.quantity;
                    a.DespawnAt = System.Math.Max(a.DespawnAt, b.DespawnAt);
                    b.quantity = 0;
                    NetworkServer.Destroy(b.gameObject);
                }
            }
        }

        // -------- saving (called by Npcs.NpcSystem with its chunks) --------

        /// <summary>The drops in that chunk, saved (and gone from the world if remove).</summary>
        public static List<SavedDrop> SavedIn(Vector2Int coords, bool remove) {
            List<SavedDrop> saved = new List<SavedDrop>();
            double now = NetworkTime.time;
            foreach (DroppedItem drop in new List<DroppedItem>(drops)) {
                if (drop == null || drop.quantity <= 0 || WorldBehaviour.ChunkAt(drop.transform.position) != coords) {
                    continue;
                }
                FloatingOrigin.ToAbsolute(drop.transform.position, out double x, out double y, out double z);
                saved.Add(new SavedDrop() {
                    itemId = drop.itemId, color = drop.color, quantity = drop.quantity, health = drop.health,
                    x = x, y = y, z = z, lifetime = (float)System.Math.Max(1, drop.DespawnAt - now),
                });
                if (remove) {
                    NetworkServer.Destroy(drop.gameObject);
                }
            }
            return saved;
        }

        /// <summary>Puts back the drops saved in a chunk.</summary>
        public static void Restore(IEnumerable<SavedDrop> saved) {
            foreach (SavedDrop drop in saved) {
                ServerDrop(drop.itemId, drop.color, drop.quantity, drop.health, FloatingOrigin.ToLocal(drop.x, drop.y, drop.z) + Vector3.up * 0.05f,
                    Vector3.zero, 0, drop.lifetime);
            }
        }
    }
}
