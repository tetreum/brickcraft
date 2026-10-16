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

        /// <summary>
        /// For glass, the depths (along its Z, in its own space) of the grooves the frame takes it in, often one either
        /// side (see LDraw.LDrawGlass): the player picks one, kept as the glass brick's state. None if the frame doesn't say.
        /// </summary>
        public float[] depths = new float[0];

        /// <summary>The places an attachment can go in it: a groove each, else just one.</summary>
        public int Seats {
            get { return isGlass ? Mathf.Max(1, depths.Length) : 1; }
        }

        private bool isGlass {
            get { return fits != null && fits.StartsWith(LDraw.LDrawGlass.FitsPrefix); }
        }

        /// <summary>
        /// Where an attachment's pivot goes, in the world: the point, but glass is centred across its frame, at the depth
        /// of that groove (seat, see Seats), as frames' LDraw origin isn't where their glass's is (a 1x4x3's is at its top).
        /// </summary>
        public Vector3 PositionFor(BrickModel attachment, int seat = 0) {
            return transform.TransformPoint(LocalPositionFor(attachment, seat));
        }

        /// <summary>PositionFor, in the brick's own space.</summary>
        public Vector3 LocalPositionFor(BrickModel attachment, int seat = 0) {
            Vector3 at = point.localPosition;
            MeshFilter frame = GetComponent<MeshFilter>();
            MeshFilter glass = attachment != null && attachment.prefab != null ? attachment.prefab.GetComponent<MeshFilter>() : null;
            if (!isGlass || frame == null || frame.sharedMesh == null || glass == null || glass.sharedMesh == null) {
                return at;
            }
            Vector3 glassMiddle = at + point.localRotation * attachment.prefab.transform.localRotation * glass.sharedMesh.bounds.center;
            Vector3 off = frame.sharedMesh.bounds.center - glassMiddle;
            // frames are thin along their Z: in a groove, else where LDraw has it
            off.z = depths.Length > 0 ? depths[SeatOf(seat)] - glassMiddle.z : 0;
            return at + off;
        }

        /// <summary>A seat that's there (a glass's state may be anything: one past the last is the first).</summary>
        public int SeatOf(int seat) {
            int seats = Mathf.Max(1, depths.Length);
            return ((seat % seats) + seats) % seats;
        }
    }
}
