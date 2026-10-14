using UnityEngine;

namespace Brickcraft.Bricks
{
    /// <summary>
    /// Makes a brick model an attachment: instead of going on the grid, its bricks go in another brick's
    /// slot (see BrickSlot), like a door in its frame. They take no cells, move and go with that brick,
    /// and are removed on their own. Its pivot is the point it's attached by.
    /// </summary>
    public class BrickAttachment : MonoBehaviour
    {
        /// <summary>The slots it goes in, see BrickSlot.fits.</summary>
        public string fits;
    }
}
