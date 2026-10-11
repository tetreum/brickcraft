using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// A lid of a brick model, like a chest's: its hinge turns it open (Brick.state 1) or closed (0) around
    /// its local X axis. Unlike a door it isn't clicked open: it's open while someone looks inside the brick
    /// it's on (see Network.BrickStorage).
    /// </summary>
    public class BrickLid : MonoBehaviour, IBrickState
    {
        public const int Closed = 0;
        public const int Open = 1;

        /// <summary>What turns, around its local X axis.</summary>
        public Transform hinge;
        /// <summary>How far it opens, in degrees.</summary>
        public float openAngle = 100;
        /// <summary>Degrees per second.</summary>
        public float speed = 300;

        private float angle;
        private float targetAngle;

        public void ShowState(int state, bool animate) {
            targetAngle = state == Open ? openAngle : 0;
            if (!animate) {
                angle = targetAngle;
                apply();
            }
        }

        private void Update() {
            if (angle == targetAngle) {
                return;
            }
            angle = Mathf.MoveTowards(angle, targetAngle, speed * Time.deltaTime);
            apply();
        }

        private void apply() {
            hinge.localRotation = Quaternion.Euler(angle, 0, 0);
        }
    }
}
