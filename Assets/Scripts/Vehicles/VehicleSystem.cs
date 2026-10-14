using System.Collections.Generic;
using Brickcraft.Bricks;
using Mirror;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// Vehicles on the server (see Vehicle): a wheel placed on the ground starts one, bricks placed on its bricks join it.
    /// A brick removed that leaves its bricks in separate groups (see VehicleGrid.Components) splits it: the biggest group
    /// stays, the others become vehicles of their own where they are. A brick placed that also touches another vehicle
    /// lined up with this one (a whole number of cells away, quarter turns apart: like the halves of a split one) merges
    /// them: the other's bricks join this one.
    /// </summary>
    public static class VehicleSystem
    {
        /// <summary>A vehicle started by a part (a wheel) placed on the world's grid, there.</summary>
        public static Vehicle ServerCreate(Item item, int color, BrickPlacement placed) {
            Vehicle vehicle = create(BrickGrid.CellToWorld(placed.origin), Quaternion.identity);
            vehicle.ServerAdd(item, color, new BrickPlacement(item.brickModel, Vector3Int.zero, placed.rotation));
            NetworkServer.Spawn(vehicle.gameObject);
            return vehicle;
        }

        /// <summary>A brick placed on a vehicle, in its cells; the vehicles it also touches join it.</summary>
        public static Brick ServerPlace(Vehicle vehicle, Item item, int color, BrickPlacement placement) {
            Brick brick = vehicle.ServerAdd(item, color, placement);
            if (brick != null) {
                mergeTouching(vehicle, placement);
            }
            return brick;
        }

        /// <summary>Removes a vehicle's brick; what it held together comes apart. Returns the bricks removed (it).</summary>
        public static List<Brick> ServerRemove(Brick brick) {
            Vehicle vehicle = brick.vehicle;
            vehicle.ServerRemove(brick);
            if (vehicle.grid.Count == 0) {
                NetworkServer.Destroy(vehicle.gameObject);
            } else {
                split(vehicle);
            }
            return new List<Brick>() { brick };
        }

        /// <summary>Paints a vehicle's brick (a colour its item can have).</summary>
        public static void ServerSetColor(Brick brick, int color) {
            brick.vehicle.ServerSetColor(brick, color);
        }

        /// <summary>
        /// Whether a placement on a vehicle would go into something that isn't the vehicle (the terrain, the world's
        /// bricks, players, other vehicles).
        /// </summary>
        public static bool OverlapsOthers(Vehicle vehicle, BrickPlacement placement) {
            Bounds local = placement.LocalBounds;
            Transform tr = vehicle.transform;
            Vector3 halfSize = local.extents - Vector3.one * 0.02f; // touching is fine
            foreach (Collider collider in Physics.OverlapBox(tr.TransformPoint(local.center), halfSize, tr.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore)) {
                if (collider.GetComponentInParent<Vehicle>() != vehicle) {
                    return true;
                }
            }
            return false;
        }

        // a vehicle not spawned yet: its parts go in first, so the spawn message has them (a host applies that message a
        // frame later, over what changed since; see Vehicle.OnStartClient)
        private static Vehicle create(Vector3 position, Quaternion rotation) {
            GameObject go = Object.Instantiate(Resources.Load<GameObject>(Vehicle.PrefabPath), position, rotation);
            go.name = "Vehicle";
            Vehicle vehicle = go.GetComponent<Vehicle>();
            vehicle.vehicleId = System.Guid.NewGuid().ToString();
            return vehicle;
        }

        // the groups that aren't connected to the biggest one any more become vehicles of their own, where they are
        private static void split(Vehicle vehicle) {
            List<List<Brick>> groups = vehicle.grid.Components();
            for (int i = 1; i < groups.Count; i++) {
                Vehicle piece = create(vehicle.transform.position, vehicle.transform.rotation);
                move(groups[i], vehicle, piece, 0, Vector3Int.zero);
                NetworkServer.Spawn(piece.gameObject);
            }
        }

        // other vehicles lined up with this one that the placement touches join it
        private static void mergeTouching(Vehicle vehicle, BrickPlacement placement) {
            foreach (Vehicle other in new List<Vehicle>(Vehicle.All)) {
                if (other == vehicle || other.grid.Count == 0
                        || !VehicleGrid.LinesUp(vehicle.transform, other.transform, out int turns, out Vector3Int offset)) {
                    continue;
                }
                // its cells in this one's
                VehicleGrid mapped = new VehicleGrid();
                List<Brick> theirs = new List<Brick>(other.grid.Bricks);
                foreach (Brick brick in theirs) {
                    mapped.Register(new Brick() { id = brick.id, placement = VehicleGrid.Map(brick.placement, turns, offset) });
                }
                if (mapped.Touching(placement).Count == 0) {
                    continue;
                }
                // they mustn't go into this one's bricks
                bool fits = true;
                foreach (Brick brick in mapped.Bricks) {
                    fits &= vehicle.grid.IsFree(brick.placement);
                }
                if (!fits) {
                    continue;
                }
                move(theirs, other, vehicle, turns, offset);
                NetworkServer.Destroy(other.gameObject);
            }
        }

        // bricks from one vehicle to another (keeping their ids), their placements turned and moved into its cells
        private static void move(List<Brick> bricks, Vehicle from, Vehicle to, int turns, Vector3Int offset) {
            foreach (Brick brick in bricks) {
                Item item = brick.item;
                int color = brick.color, state = brick.state;
                string id = brick.id;
                BrickPlacement placement = VehicleGrid.Map(brick.placement, turns, offset);
                from.ServerRemove(brick);
                to.ServerAdd(item, color, placement, id, state);
            }
        }
    }
}
