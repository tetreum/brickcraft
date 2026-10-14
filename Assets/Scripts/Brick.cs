using UnityEngine;
using Brickcraft.Bricks;

namespace Brickcraft
{
    public class Brick {
        public string id;
        public string itemId;
        /// <summary>Its colour, see BrickColorPalette.</summary>
        public int color;
        public GameObject gameObject;
        public BrickPlacement placement;
        /// <summary>What its model shows of it, like whether a door is open (see IBrickState). Saved with it, 0 at first.</summary>
        public int state;
        /// <summary>
        /// For attachments (a door...), the id of the brick they're in (see Bricks.BrickSlot), null for bricks
        /// on the grid. Its placement is that brick's.
        /// </summary>
        public string attachedTo;

        public Item item {
            get {
                return Server.items[itemId];
            }
        }

        public BrickModel model {
            get {
                return BrickModels.Get(item.brickModelId);
            }
        }
    }
}
