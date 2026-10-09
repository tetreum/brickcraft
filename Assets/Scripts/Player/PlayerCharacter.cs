using UnityEngine;

namespace Brickcraft
{
    /// <summary>
    /// The animated body other players see (Prefabs/PlayerCharacter, the skin of Models/Character/Idle).
    /// It's a child of the player, whose position comes from the network, so it animates from how that
    /// moves: idle, walking (faster when running) or jumping while in the air.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerCharacter : MonoBehaviour
    {
        // speed the walking animation was made for, in meters per second
        private const float AnimationWalkSpeed = 2f;
        private const float MinWalkSpeed = 0.3f;
        // a step down a block isn't a jump: in the air this long first
        private const float AirTimeToJump = 0.15f;
        private const float GroundDistance = 0.25f;
        // more than this in one frame is a teleport (or the floating origin moving), not walking
        private const float MaxStep = 3f;

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int WalkSpeedParameter = Animator.StringToHash("WalkSpeed");
        private static readonly int GroundedParameter = Animator.StringToHash("Grounded");

        private Animator animator;
        private CharacterController controller;
        private Vector3 lastPosition;
        private float speed;
        private float airTime;

        private void Awake() {
            animator = GetComponent<Animator>();
            controller = GetComponentInParent<CharacterController>();
        }

        private void OnEnable() {
            lastPosition = transform.parent != null ? transform.parent.position : transform.position;
            speed = 0;
            airTime = 0;
        }

        private void Update() {
            Transform player = transform.parent != null ? transform.parent : transform;
            Vector3 position = player.position;
            Vector3 moved = position - lastPosition;
            lastPosition = position;
            moved.y = 0;

            if (Time.deltaTime > 0 && moved.magnitude < MaxStep) {
                // smoothed, network updates don't come every frame
                speed = Mathf.Lerp(speed, moved.magnitude / Time.deltaTime, 1 - Mathf.Exp(-10 * Time.deltaTime));
            }
            airTime = isOnGround(position) ? 0 : airTime + Time.deltaTime;

            animator.SetFloat(SpeedParameter, speed < MinWalkSpeed ? 0 : speed);
            animator.SetFloat(WalkSpeedParameter, Mathf.Clamp(speed / AnimationWalkSpeed, 0.6f, 2.5f));
            animator.SetBool(GroundedParameter, airTime < AirTimeToJump);
        }

        // from just above the feet, so the player's own collider (which the ray starts inside) isn't hit
        private bool isOnGround(Vector3 position) {
            float feet = controller != null ? controller.center.y - controller.height / 2 : 0;
            Vector3 origin = position + Vector3.up * (feet + 0.1f);

            return Physics.Raycast(origin, Vector3.down, 0.1f + GroundDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
