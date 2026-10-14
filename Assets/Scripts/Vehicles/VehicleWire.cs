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
        /// <summary>A wheel steers (to the driver's right, with a positive value).</summary>
        Steer = 1,
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

        /// <summary>A part that can be wired to a seat: one that does something (a wheel, for now).</summary>
        public static bool CanWire(Brick part) {
            return part != null && part.vehicle != null && Server.items.TryGetValue(part.itemId, out Item item) && item.VehicleRole == VehicleRole.Wheel;
        }

        /// <summary>
        /// The wires a part gets when it's wired to a seat: a wheel goes forward and back with the throttle; one ahead of the
        /// vehicle's middle (for whoever sits there) also steers.
        /// </summary>
        public static List<VehicleWire> Defaults(Vehicle vehicle, Brick seat, Brick part) {
            List<VehicleWire> wires = new List<VehicleWire>() {
                new VehicleWire() { seat = seat.id, part = part.id, action = WireAction.Drive, source = WireSource.Throttle },
            };
            Vector3 forward = VehicleShape.Seat(seat, vehicle.transform).forward;
            if (Vector3.Dot(part.placement.LocalBounds.center - vehicle.grid.CentreOfMass, forward) > Server.studSize * 0.5f) {
                wires.Add(new VehicleWire() { seat = seat.id, part = part.id, action = WireAction.Steer, source = WireSource.Steering });
            }
            return wires;
        }

        /// <summary>The wire's value with what the driver asks for: -1 to 1.</summary>
        public static float Value(VehicleWire wire, VehicleInput input) {
            float value;
            switch (wire.source) {
                case WireSource.Throttle: value = input.throttle; break;
                case WireSource.Steering: value = input.steer; break;
                case WireSource.Key: value = VehicleControls.KeyHeld(input, wire.key) ? 1 : 0; break;
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
