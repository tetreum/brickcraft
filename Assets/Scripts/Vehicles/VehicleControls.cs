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
        /// <summary>Up (1) or down (-1).</summary>
        public float lift;
        /// <summary>The wires' keys held (KeyCodes), for tests; null reads the keyboard.</summary>
        public int[] keys;
        /// <summary>Nobody drives it: every wheel brakes (a handbrake), wired or not.</summary>
        public bool parked;

        public static readonly VehicleInput Parked = new VehicleInput() { brake = 1, parked = true };
    }

    /// <summary>
    /// The driver's keys: the player's own (see GameInput), forward and back for the throttle, left and right to steer,
    /// jump to brake, and up and down (R and F: a plane's nose); wires can have keys of their own (see VehicleWire). Not
    /// while typing.
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
                lift = axis(GameInput.VehicleUp, GameInput.VehicleDown),
            };
        }

        /// <summary>The keys a wire can have: letters, digits, the keypad's, arrows, F keys and the modifiers.</summary>
        public static readonly KeyCode[] WireKeys = wireKeys();

        private static KeyCode[] wireKeys() {
            System.Collections.Generic.List<KeyCode> keys = new System.Collections.Generic.List<KeyCode>();
            for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++) {
                keys.Add(key);
            }
            for (KeyCode key = KeyCode.Alpha0; key <= KeyCode.Alpha9; key++) {
                keys.Add(key);
            }
            for (KeyCode key = KeyCode.Keypad0; key <= KeyCode.Keypad9; key++) {
                keys.Add(key);
            }
            for (KeyCode key = KeyCode.F1; key <= KeyCode.F12; key++) {
                keys.Add(key);
            }
            keys.AddRange(new[] {
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Space, KeyCode.Tab,
                KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt,
            });
            return keys.ToArray();
        }

        public static bool IsWireKey(int key) {
            return System.Array.IndexOf(WireKeys, (KeyCode)key) >= 0;
        }

        /// <summary>The wire key is held, for that input.</summary>
        public static bool KeyHeld(VehicleInput input, int key) {
            if (input.keys != null) {
                return System.Array.IndexOf(input.keys, key) >= 0;
            }
            return !GameInput.IsTyping && IsWireKey(key) && GameInput.KeyHeld((KeyCode)key);
        }

        private static float axis(string positive, string negative) {
            return (GameInput.GetButton(positive) ? 1 : 0) - (GameInput.GetButton(negative) ? 1 : 0);
        }
    }
}
