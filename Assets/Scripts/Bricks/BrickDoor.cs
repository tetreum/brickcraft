using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// A door of a brick model: its hinge turns it open (Brick.state 1) or closed (0). Left clicking a
    /// brick with one toggles it (see Player and PlayerNetwork.CmdToggleDoor), holding the click breaks it.
    /// Its collider turns with it, so an open door lets players through.
    /// </summary>
    public class BrickDoor : MonoBehaviour, IBrickState
    {
        public const int Closed = 0;
        public const int Open = 1;

        /// <summary>What turns, around its local Y axis: the door's pivot is on it.</summary>
        public Transform hinge;
        /// <summary>How far it opens, in degrees around the hinge.</summary>
        public float openAngle = 90;
        /// <summary>Degrees per second.</summary>
        public float speed = 360;

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
            hinge.localRotation = Quaternion.Euler(0, angle, 0);
        }
    }
}
