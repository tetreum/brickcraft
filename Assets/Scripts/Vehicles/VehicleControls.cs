using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>What the driver asks of a vehicle: -1 to 1 each (brake 0 to 1).</summary>
    public struct VehicleInput
    {
        /// <summary>Forward (1) or back (-1).</summary>
        public float throttle;
        /// <summary>Right (1) or left (-1).</summary>
        public float steer;
        public float brake;

        public static readonly VehicleInput Parked = new VehicleInput() { brake = 1 };
    }

    /// <summary>
    /// The driver's keys: the player's own (see GameInput), forward and back for the throttle, left and right to steer,
    /// jump to brake. Not while typing.
    /// </summary>
    public static class VehicleControls
    {
        /// <summary>Tests drive with this instead of the keys, when set.</summary>
        public static VehicleInput? Override;

        public static VehicleInput Read() {
            if (Override.HasValue) {
                return Override.Value;
            }
            if (GameInput.IsTyping) {
                return new VehicleInput();
            }
            return new VehicleInput() {
                throttle = axis(GameInput.MoveForward, GameInput.MoveBack),
                steer = axis(GameInput.MoveRight, GameInput.MoveLeft),
                brake = GameInput.GetButton(GameInput.Jump) ? 1 : 0,
            };
        }

        private static float axis(string positive, string negative) {
            return (GameInput.GetButton(positive) ? 1 : 0) - (GameInput.GetButton(negative) ? 1 : 0);
        }
    }
}
