using System.Collections.Generic;
using Brickcraft.Bricks;
using Mirror;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>A brick of a vehicle as it travels: where it is in the vehicle's cells (see VehicleGrid).</summary>
    public struct VehiclePart
    {
        public string id;
        public string itemId;
        public int color;
        public Vector3Int origin;
        public byte rotation;
        public int state;
    }

    /// <summary>
    /// Something built of bricks that isn't part of the world: a car, a boat, a plane. It starts with a wheel placed on the
    /// ground, and bricks placed on its bricks are its own (see VehicleSystem). They're placed like the world's, studs
    /// and plates, but on its own grid (VehicleGrid), so it can move and turn. Its parts are synced with it: every peer
    /// makes their bricks under it (Server.spawnBrick with the vehicle).
    /// </summary>
    public class Vehicle : NetworkBehaviour
    {
        public const string PrefabPath = "Vehicle";

        /// <summary>Every vehicle of this peer.</summary>
        public static readonly HashSet<Vehicle> All = new HashSet<Vehicle>();

        /// <summary>Identifies it in saves.</summary>
        [SyncVar] public string vehicleId;

        public readonly SyncList<VehiclePart> parts = new SyncList<VehiclePart>();

        public readonly VehicleGrid grid = new VehicleGrid();

        public Rigidbody Body { get; private set; }

        private void Awake() {
            Body = GetComponent<Rigidbody>();
            All.Add(this);
        }

        private void OnDestroy() {
            All.Remove(this);
            // its bricks go with it (none on the server once moved to another vehicle)
            if (Server.Instance != null) {
                foreach (Brick brick in new List<Brick>(grid.Bricks)) {
                    Server.Instance.removeBrick(brick);
                }
            }
        }

        // clients make the bricks the server sends (the host has them already)
        public override void OnStartClient() {
            if (isServer) {
                repair();
                return;
            }
            foreach (VehiclePart part in parts) {
                spawn(part);
            }
            parts.OnAdd += index => spawn(parts[index]);
            parts.OnRemove += (index, old) => remove(old.id);
            parts.OnSet += (index, old) => changed(old, parts[index]);
            parts.OnClear += () => {
                foreach (Brick brick in new List<Brick>(grid.Bricks)) {
                    Server.Instance.removeBrick(brick);
                }
            };
        }

        // a host applies its spawn message a frame after it was made, over its parts: they're its bricks again
        [Server]
        private void repair() {
            bool same = parts.Count == grid.Count;
            foreach (VehiclePart part in parts) {
                same &= BrickOf(part) != null;
            }
            if (same) {
                return;
            }
            parts.Clear();
            foreach (Brick brick in grid.Bricks) {
                parts.Add(new VehiclePart() {
                    id = brick.id,
                    itemId = brick.itemId,
                    color = brick.color,
                    origin = brick.placement.origin,
                    rotation = (byte)brick.placement.rotation,
                    state = brick.state,
                });
            }
        }

        public Brick BrickOf(VehiclePart part) {
            return Server.bricks.TryGetValue(part.id, out Brick brick) && brick.vehicle == this ? brick : null;
        }

        private Brick spawn(VehiclePart part) {
            if (!Server.items.TryGetValue(part.itemId, out Item item)) {
                Debug.LogWarning("A vehicle's brick is of the unknown item " + part.itemId);
                return null;
            }
            // moving here from another vehicle (split or merged): its message may come first
            if (Server.bricks.TryGetValue(part.id, out Brick moving)) {
                Server.Instance.removeBrick(moving);
            }
            BrickPlacement placement = new BrickPlacement(item.brickModel, part.origin, part.rotation);
            return Server.Instance.spawnBrick(item, part.color, placement, part.id, part.state, null, this);
        }

        private void remove(string id) {
            if (Server.bricks.TryGetValue(id, out Brick brick) && brick.vehicle == this) {
                Server.Instance.removeBrick(brick);
            }
        }

        private void changed(VehiclePart old, VehiclePart now) {
            Brick brick = BrickOf(now);
            if (brick == null) {
                return;
            }
            if (old.color != now.color) {
                Server.Instance.setBrickColor(brick, now.color);
            }
            if (old.state != now.state) {
                Server.Instance.setBrickState(brick, now.state);
            }
        }

        // -------- the server changes it (see VehicleSystem), its parts tell the clients --------

        [Server]
        public Brick ServerAdd(Item item, int color, BrickPlacement placement, string id = null, int state = 0) {
            VehiclePart part = new VehiclePart() {
                id = id ?? System.Guid.NewGuid().ToString(),
                itemId = item.id,
                color = item.ValidColor(color),
                origin = placement.origin,
                rotation = (byte)placement.rotation,
                state = state,
            };
            Brick brick = spawn(part);
            if (brick != null) {
                parts.Add(part);
            }
            return brick;
        }

        [Server]
        public void ServerRemove(Brick brick) {
            int index = indexOf(brick.id);
            if (index >= 0) {
                parts.RemoveAt(index);
            }
            Server.Instance.removeBrick(brick);
        }

        [Server]
        public void ServerSetColor(Brick brick, int color) {
            int index = indexOf(brick.id);
            if (index < 0) {
                return;
            }
            Server.Instance.setBrickColor(brick, color);
            VehiclePart part = parts[index];
            part.color = brick.color;
            parts[index] = part;
        }

        private int indexOf(string id) {
            for (int i = 0; i < parts.Count; i++) {
                if (parts[i].id == id) {
                    return i;
                }
            }
            return -1;
        }
    }
}
