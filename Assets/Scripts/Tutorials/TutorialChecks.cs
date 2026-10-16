using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.Vehicles;
using Brickcraft.World;
using UnityEngine;

namespace Brickcraft.Tutorials
{
    /// <summary>
    /// What the vehicle tutorials' steps look at: the vehicle being built and its parts, how they're placed. The
    /// tutorial's finished one shown beside it (see TutorialDemos) isn't the one being built.
    /// </summary>
    public static class TutorialChecks
    {
        // the finished vehicles shown (on the server, this game)
        private static readonly HashSet<Vehicle> demos = new HashSet<Vehicle>();

        /// <summary>That vehicle is a tutorial's finished one, shown: it isn't the one being built.</summary>
        public static void MarkDemo(Vehicle vehicle) {
            demos.RemoveWhere(v => v == null);
            demos.Add(vehicle);
        }

        public static bool IsDemo(Vehicle vehicle) {
            return vehicle != null && demos.Contains(vehicle);
        }

        // the bricks of the world shown finished (a house: see FirstHouse), by id
        private static readonly HashSet<string> demoBricks = new HashSet<string>();

        /// <summary>That brick of the world is part of a tutorial's finished build, shown: it isn't one the player placed.</summary>
        public static void MarkDemo(Brick brick) {
            demoBricks.Add(brick.id);
        }

        /// <summary>The player's bricks of the world of that item (not a vehicle's, not a demo's).</summary>
        public static List<Brick> Placed(string itemId) {
            List<Brick> placed = new List<Brick>();
            foreach (Brick brick in Server.bricks.Values) {
                if (brick.itemId == itemId && brick.vehicle == null && !demoBricks.Contains(brick.id)) {
                    placed.Add(brick);
                }
            }
            return placed;
        }

        /// <summary>One of the player's bricks of that item is in the slot of one of theirs of the other (a door in its frame).</summary>
        public static bool IsIn(string itemId, string holderItemId) {
            return Placed(itemId).Exists(b => b.attachedTo != null && Server.bricks.TryGetValue(b.attachedTo, out Brick holder)
                && holder.itemId == holderItemId && !demoBricks.Contains(holder.id));
        }

        /// <summary>The vehicle being built: the one with the most bricks (not a demo), null if there's none yet.</summary>
        public static Vehicle Built() {
            Vehicle built = null;
            foreach (Vehicle vehicle in Vehicle.All) {
                if (vehicle != null && !IsDemo(vehicle) && (built == null || vehicle.grid.Count > built.grid.Count)) {
                    built = vehicle;
                }
            }
            return built;
        }

        /// <summary>The vehicle being built's bricks of that item.</summary>
        public static List<Brick> Parts(string itemId) {
            return Parts(Built(), itemId);
        }

        /// <summary>A vehicle's bricks of that item, none if there's no vehicle.</summary>
        public static List<Brick> Parts(Vehicle vehicle, string itemId) {
            List<Brick> parts = new List<Brick>();
            if (vehicle != null) {
                foreach (Brick brick in vehicle.grid.Bricks) {
                    if (brick.itemId == itemId) {
                        parts.Add(brick);
                    }
                }
            }
            return parts;
        }

        public static int Count(string itemId) {
            return Parts(itemId).Count;
        }

        /// <summary>Held by a brick of that item (an axle through it, see BrickJoints).</summary>
        public static bool HeldBy(Brick brick, string itemId) {
            return brick.joint.HasValue && brick.attachedTo != null && Server.bricks.TryGetValue(brick.attachedTo, out Brick holder) && holder.itemId == itemId;
        }

        /// <summary>Its studs up, on its vehicle.</summary>
        public static bool IsUpright(Brick brick) {
            BrickJoints.GridOf(brick, out _, out Quaternion turned);
            return Vector3.Dot(turned * Vector3.up, Vector3.up) > 0.9f;
        }

        /// <summary>Where a seat faces, in its vehicle's space.</summary>
        public static Vector3 Facing(Brick seat) {
            return VehicleShape.Seat(seat, seat.vehicle.transform).forward;
        }

        /// <summary>The way its axles (those of that item) roll it, in its space: across them; forward if it has none.</summary>
        public static Vector3 AcrossAxles(Vehicle vehicle, string axleId) {
            foreach (Brick axle in Parts(vehicle, axleId)) {
                Vector3 along = axle.jointRotation * axle.model.connectors[0].axis;
                return Vector3.Cross(Vector3.up, along).normalized;
            }
            return Vector3.forward;
        }

        /// <summary>A seat facing the way the wheels roll: across its vehicle's axles (those of that item).</summary>
        public static bool FacesAcross(Brick seat, string axleId) {
            if (seat.vehicle == null || Parts(seat.vehicle, axleId).Count == 0) {
                return false;
            }
            return Mathf.Abs(Vector3.Dot(AcrossAxles(seat.vehicle, axleId), Facing(seat).normalized)) > 0.85f;
        }

        /// <summary>A propeller that pushes along that way (in its vehicle's space): its blades across it.</summary>
        public static bool PushesAlong(Brick propeller, Vector3 way) {
            Vector3 axis = VehicleDrive.PropellerAxis(VehicleShape.MeshBounds(propeller, propeller.vehicle.transform), way);
            return Mathf.Abs(Vector3.Dot(axis, way.normalized)) > 0.9f;
        }

        /// <summary>The local player in a seat of the vehicle being built.</summary>
        public static bool IsSeated() {
            PlayerNetwork network = Player.Instance != null ? Player.Instance.network : null;
            Vehicle built = Built();
            return network != null && built != null && !string.IsNullOrEmpty(network.seatedIn)
                && Server.bricks.TryGetValue(network.seatedIn, out Brick seat) && seat.vehicle == built;
        }

        /// <summary>Where the vehicle being built is (absolute: the floating origin moves), zero if there's none.</summary>
        public static Vector3 Where() {
            Vehicle built = Built();
            return built != null ? FloatingOrigin.ToAbsolute(built.transform.position) : Vector3.zero;
        }
    }
}
