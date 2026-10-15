using System.Collections.Generic;
using Brickcraft.Bricks;
using Brickcraft.Network;
using Brickcraft.Vehicles;
using Brickcraft.World;
using UnityEngine;

namespace Brickcraft.Tutorials
{
    /// <summary>What the vehicle tutorials' steps look at: the vehicle being built and its parts, how they're placed.</summary>
    public static class TutorialChecks
    {
        /// <summary>The vehicle being built: the one with the most bricks, null if there's none yet.</summary>
        public static Vehicle Built() {
            Vehicle built = null;
            foreach (Vehicle vehicle in Vehicle.All) {
                if (vehicle != null && (built == null || vehicle.grid.Count > built.grid.Count)) {
                    built = vehicle;
                }
            }
            return built;
        }

        /// <summary>Its bricks of that item.</summary>
        public static List<Brick> Parts(string itemId) {
            List<Brick> parts = new List<Brick>();
            Vehicle built = Built();
            if (built != null) {
                foreach (Brick brick in built.grid.Bricks) {
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

        /// <summary>A seat facing the way the wheels roll: across the axles (those of that item).</summary>
        public static bool FacesAcross(Brick seat, string axleId) {
            List<Brick> axles = Parts(axleId);
            if (axles.Count == 0 || seat.vehicle == null) {
                return false;
            }
            Vector3 along = axles[0].jointRotation * axles[0].model.connectors[0].axis;
            return Mathf.Abs(Vector3.Dot(along.normalized, Facing(seat).normalized)) < 0.5f;
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
