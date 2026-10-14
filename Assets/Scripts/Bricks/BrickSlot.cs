using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Where a brick model takes an attachment, like a door frame's door (see BrickAttachment): placing
    /// an attachment whose fits is the same while looking at the brick puts it there, if it's free.
    /// One per model.
    /// </summary>
    public class BrickSlot : MonoBehaviour
    {
        /// <summary>The attachment's pivot goes here (for a door, its hinge).</summary>
        public Transform point;
        /// <summary>What fits in it, like "door 4x6": attachments with the same fits.</summary>
        public string fits;
    }
}
