using Brickcraft.Bricks;
using Brickcraft.Network;
using UnityEngine;

namespace Brickcraft.Vehicles
{
    /// <summary>
    /// A player on a vehicle's seat (PlayerNetwork.seatedIn), on every peer: it's kept on the seat, facing its front.
    /// The local player doesn't walk meanwhile (see Player.FreezeReason.Driving): it looks around, drives (see
    /// Vehicle) and gets up with the interact key, out beside it (its capsule mustn't be in the vehicle when it's
    /// solid again: the vehicle would be thrown out of it). Others' hitboxes don't push the vehicle meanwhile.
    /// </summary>
    public class PlayerSeat : MonoBehaviour
    {
        // the player's position is its middle: sitting, about where it sits
        private const float SitHeight = 0.05f;
        private const float LookSensitivity = 2;
        private const float MaxPitch = 80;

        private PlayerNetwork network;
        private Player player;
        private CharacterController controller;
        private float yaw, pitch;
        private bool wasSeated;
        private Brick sittingOn;
        // where it sits on its seat, worked out once per seat and vehicle
        private Brick shapeOf;
        private Vehicle shapeIn;
        private SeatShape shape;

        private void Awake() {
            network = GetComponent<PlayerNetwork>();
            player = GetComponent<Player>();
            controller = GetComponent<CharacterController>();
        }

        public bool IsSeated {
            get { return !string.IsNullOrEmpty(network.seatedIn); }
        }

        /// <summary>The seat it's on, null if none (or it isn't here yet).</summary>
        public Brick Seat {
            get { return IsSeated && Server.bricks.TryGetValue(network.seatedIn, out Brick seat) && seat.vehicle != null ? seat : null; }
        }

        /// <summary>It sat down or got up.</summary>
        public void Changed() {
            bool seated = IsSeated;
            if (seated == wasSeated) {
                return;
            }
            wasSeated = seated;
            yaw = pitch = 0;
            Brick left = sittingOn;
            sittingOn = seated ? Seat : null;
            shapeOf = null;
            if (network.isLocalPlayer) {
                if (!seated && left != null && left.gameObject != null && left.vehicle != null) {
                    transform.position = exitSpot(left);
                    Physics.SyncTransforms();
                }
                controller.enabled = !seated;
                if (seated) {
                    player.freeze(Player.FreezeReason.Driving);
                } else {
                    player.unFreeze(Player.FreezeReason.Driving);
                    if (player.firstPersonController != null) {
                        player.firstPersonController.ResetLook();
                    }
                }
                return;
            }
            Transform hitbox = transform.Find("Hitbox");
            if (hitbox != null && hitbox.TryGetComponent(out Collider collider)) {
                collider.isTrigger = seated;
            }
        }

        // beside the seat, out of the vehicle (to its sides, further and further), else on top of it
        private Vector3 exitSpot(Brick seat) {
            Vehicle vehicle = seat.vehicle;
            Bounds all = seat.WorldBounds;
            foreach (Brick brick in vehicle.grid.Bricks) {
                all.Encapsulate(brick.WorldBounds);
            }
            Transform tr = vehicle.transform;
            SeatShape shape = VehicleShape.Seat(seat, tr);
            Vector3 sit = tr.TransformPoint(shape.sitPoint) + Vector3.up * (controller.height / 2);
            Vector3 side = Vector3.ProjectOnPlane(tr.rotation * Quaternion.LookRotation(shape.forward, Vector3.up) * Vector3.right, Vector3.up).normalized;
            float half = controller.height / 2 - controller.radius;
            for (float distance = 1; distance <= all.extents.magnitude + 1.5f; distance += 0.5f) {
                foreach (float sign in new[] { -1f, 1f }) {
                    Vector3 spot = sit + side * sign * distance;
                    if (!Physics.CheckCapsule(spot + Vector3.down * half, spot + Vector3.up * half, controller.radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) {
                        return spot;
                    }
                }
            }
            return new Vector3(sit.x, all.max.y + controller.height / 2 + 0.1f, sit.z);
        }

        // after the vehicle moved (in physics, or by the network)
        private void LateUpdate() {
            Brick seat = Seat;
            if (seat == null) {
                return;
            }
            Vehicle vehicle = seat.vehicle;
            if (shapeOf != seat || shapeIn != vehicle) {
                shapeOf = seat;
                shapeIn = vehicle;
                shape = VehicleShape.Seat(seat, vehicle.transform);
            }
            Transform tr = vehicle.transform;
            Quaternion facing = tr.rotation * Quaternion.LookRotation(shape.forward, Vector3.up);
            transform.position = tr.TransformPoint(shape.sitPoint) + tr.up * SitHeight;

            if (!network.isLocalPlayer) {
                transform.rotation = facing;
                return;
            }
            if (!GameInput.IsTyping) {
                Vector2 look = GameInput.LookDelta;
                yaw = Mathf.Repeat(yaw + look.x * LookSensitivity + 180, 360) - 180;
                pitch = Mathf.Clamp(pitch - look.y * LookSensitivity, -MaxPitch, MaxPitch);
            }
            transform.rotation = facing * Quaternion.Euler(0, yaw, 0);
            Camera eye = Camera.main;
            if (eye != null && eye.transform.IsChildOf(transform)) {
                eye.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Interact)) {
                network.CmdStandUp(vehicle.Body.velocity, vehicle.Body.angularVelocity);
            }
        }
    }
}
