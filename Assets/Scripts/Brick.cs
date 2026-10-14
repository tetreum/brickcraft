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
        /// <summary>
        /// The vehicle it's part of (see Vehicles.Vehicle), null for bricks of the world: its placement is then in the
        /// vehicle's own cells, and it isn't on the BrickGrid.
        /// </summary>
        public Vehicles.Vehicle vehicle;

        /// <summary>Its box in the world: its placement's, or for a vehicle's brick where the vehicle has it now (axis aligned).</summary>
        public Bounds WorldBounds {
            get {
                if (vehicle == null) {
                    return placement.WorldBounds;
                }
                Bounds local = placement.LocalBounds;
                Bounds world = new Bounds(vehicle.transform.TransformPoint(local.center), Vector3.zero);
                for (int corner = 0; corner < 8; corner++) {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    world.Encapsulate(vehicle.transform.TransformPoint(local.center + Vector3.Scale(local.extents, offset)));
                }
                return world;
            }
        }

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
