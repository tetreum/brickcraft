using System.Collections.Generic;
using Brickcraft.Bricks;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>What a wired part does.</summary>
    public enum WireAction : byte
    {
        /// <summary>A wheel turns (forward for the driver, with a positive value).</summary>
        Drive = 0,
        /// <summary>A wheel or propeller steers (to the driver's right, with a positive value).</summary>
        Steer = 1,
        /// <summary>A wing lifts more (or less, with a negative value): a flap, an elevator, an aileron, a rudder.</summary>
        Tilt = 2,
    }

    /// <summary>What a wire's value comes from.</summary>
    public enum WireSource : byte
    {
        /// <summary>The driver's forward and back keys: 1 to -1.</summary>
        Throttle = 0,
        /// <summary>The driver's right and left keys: 1 to -1.</summary>
        Steering = 1,
        /// <summary>A key of the wire's own: 1 while it's held.</summary>
        Key = 2,
        /// <summary>The driver's up and down keys (see GameInput.VehicleUp): 1 to -1.</summary>
        UpDown = 3,
    }

    /// <summary>
    /// A seat wired to one of its vehicle's parts with the wiring tool (see WiringTool): the part does the action with the
    /// value of the source (inverted, if so), for whoever drives from that seat. "W turns this wheel forward" is a wire
    /// with the Drive action and the W key.
    /// </summary>
    public struct VehicleWire
    {
        /// <summary>The seat's brick id.</summary>
        public string seat;
        /// <summary>The part's brick id.</summary>
        public string part;
        public WireAction action;
        public WireSource source;
        /// <summary>The source key (a KeyCode), for WireSource.Key.</summary>
        public int key;
        public bool invert;
    }

    public static class VehicleWires
    {
        /// <summary>A vehicle has no more than this many wires.</summary>
        public const int MaxPerVehicle = 256;

        /// <summary>A part that can be wired to a seat: one that does something (a wheel, a propeller, a wing).</summary>
        public static bool CanWire(Brick part) {
            if (part == null || part.vehicle == null || !Server.items.TryGetValue(part.itemId, out Item item)) {
                return false;
            }
            VehicleRole role = item.VehicleRole;
            return role == VehicleRole.Wheel || role == VehicleRole.Propeller || role == VehicleRole.Wing;
        }

        /// <summary>
        /// The wires of a seat that has none of its own: every part as it'd be wired to it (see Defaults), so a vehicle
        /// works as it's built.
        /// </summary>
        public static List<VehicleWire> AllDefaults(Vehicle vehicle, Brick seat) {
            List<VehicleWire> wires = new List<VehicleWire>();
            foreach (Brick part in vehicle.grid.Bricks) {
                if (CanWire(part)) {
                    wires.AddRange(Defaults(vehicle, seat, part));
                }
            }
            return wires;
        }

        /// <summary>
        /// The wires a part gets when it's wired to a seat: it goes forward and back with the throttle; a wheel ahead of the
        /// vehicle's middle (for whoever sits there) also steers, and so does an upright propeller. A wing is a control
        /// surface: behind the middle, it pitches with the up and down keys (an elevator); to a side, it rolls the vehicle
        /// with left and right (an aileron); upright, it turns it with them (a rudder).
        /// </summary>
        public static List<VehicleWire> Defaults(Vehicle vehicle, Brick seat, Brick part) {
            Vector3 forward = VehicleShape.Seat(seat, vehicle.transform).forward;
            if (part.item.VehicleRole == VehicleRole.Wing) {
                return new List<VehicleWire>() { wingDefault(vehicle, seat, part, forward) };
            }
            List<VehicleWire> wires = new List<VehicleWire>() {
                new VehicleWire() { seat = seat.id, part = part.id, action = WireAction.Drive, source = WireSource.Throttle },
            };
            bool steers = part.item.VehicleRole == VehicleRole.Propeller
                ? VehicleDrive.PropellerAxis(VehicleShape.MeshBounds(part, vehicle.transform), forward) != Vector3.up
                : Vector3.Dot(part.LocalBounds.center - vehicle.grid.CentreOfMass, forward) > Server.studSize * 0.5f;
            if (steers) {
                wires.Add(new VehicleWire() { seat = seat.id, part = part.id, action = WireAction.Steer, source = WireSource.Steering });
            }
            return wires;
        }

        private static VehicleWire wingDefault(Vehicle vehicle, Brick seat, Brick part, Vector3 forward) {
            Bounds mesh = VehicleShape.MeshBounds(part, vehicle.transform);
            Vector3 normal = VehicleDrive.PropellerAxis(mesh, forward);
            Vector3 at = mesh.center - vehicle.grid.CentreOfMass;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float ahead = Vector3.Dot(at, forward), side = Vector3.Dot(at, right);
            VehicleWire wire = new VehicleWire() { seat = seat.id, part = part.id, action = WireAction.Tilt };
            if (normal != Vector3.up) {
                // a rudder: turning right, the tail goes left (pushed the other way from its normal, the right)
                wire.source = WireSource.Steering;
                wire.invert = ahead < 0;
            } else if (Mathf.Abs(side) > Mathf.Max(Server.studSize, Mathf.Abs(ahead))) {
                // an aileron: rolling right, the right wing lifts less
                wire.source = WireSource.Steering;
                wire.invert = side > 0;
            } else {
                // an elevator: nose up, the tail lifts less (a canard ahead, more)
                wire.source = WireSource.UpDown;
                wire.invert = ahead < 0;
            }
            return wire;
        }

        /// <summary>The wire's value with what the driver asks for: -1 to 1.</summary>
        public static float Value(VehicleWire wire, VehicleInput input) {
            float value;
            switch (wire.source) {
                case WireSource.Throttle: value = input.throttle; break;
                case WireSource.Steering: value = input.steer; break;
                case WireSource.Key: value = VehicleControls.KeyHeld(input, wire.key) ? 1 : 0; break;
                case WireSource.UpDown: value = input.lift; break;
                default: value = 0; break;
            }
            return wire.invert ? -value : value;
        }

        /// <summary>A wire as it'd be stored for that vehicle: its ends are its bricks, its values are known ones.</summary>
        public static bool IsValid(Vehicle vehicle, VehicleWire wire) {
            return wire.seat != null && wire.part != null
                && Server.bricks.TryGetValue(wire.seat, out Brick seat) && seat.vehicle == vehicle && seat.item.VehicleRole == VehicleRole.Seat
                && Server.bricks.TryGetValue(wire.part, out Brick part) && part.vehicle == vehicle && CanWire(part)
                && System.Enum.IsDefined(typeof(WireAction), wire.action) && System.Enum.IsDefined(typeof(WireSource), wire.source)
                && (wire.source != WireSource.Key || VehicleControls.IsWireKey(wire.key));
        }
    }
}
