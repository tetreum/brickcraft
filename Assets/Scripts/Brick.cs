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
        /// For connected bricks (an axle through a hole, see Bricks.BrickJoints), how they're held by attachedTo; null for
        /// others. Their placement is their holder's, their place jointPosition and jointRotation (their parent's space).
        /// </summary>
        public BrickJoint? joint;
        public Vector3 jointPosition;
        public Quaternion jointRotation = Quaternion.identity;
        /// <summary>
        /// The vehicle it's part of (see Vehicles.Vehicle), null for bricks of the world: its placement is then in the
        /// vehicle's own cells, and it isn't on the BrickGrid.
        /// </summary>
        public Vehicles.Vehicle vehicle;

        /// <summary>
        /// Its box in the world: its placement's, or for a vehicle's brick where the vehicle has it now, or for a connected
        /// brick its model's around where it is (axis aligned).
        /// </summary>
        public Bounds WorldBounds {
            get {
                if (joint.HasValue && model != null) {
                    Bounds mesh = BrickJoints.MeshBounds(model);
                    Transform parent = vehicle != null ? vehicle.transform : null;
                    Bounds around = new Bounds(point(parent, jointPosition + jointRotation * mesh.center), Vector3.zero);
                    for (int corner = 0; corner < 8; corner++) {
                        Vector3 offset = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                        around.Encapsulate(point(parent, jointPosition + jointRotation * (mesh.center + Vector3.Scale(mesh.extents, offset))));
                    }
                    return around;
                }
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

        /// <summary>
        /// Its box in its parent's space (a vehicle's, see Vehicles.Vehicle): its placement's, or for a connected brick
        /// its model's around where it's held.
        /// </summary>
        public Bounds LocalBounds {
            get {
                if (!joint.HasValue || model == null) {
                    return placement.LocalBounds;
                }
                Bounds mesh = BrickJoints.MeshBounds(model);
                Bounds around = new Bounds(jointPosition + jointRotation * mesh.center, Vector3.zero);
                for (int corner = 0; corner < 8; corner++) {
                    Vector3 offset = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    around.Encapsulate(jointPosition + jointRotation * (mesh.center + Vector3.Scale(mesh.extents, offset)));
                }
                return around;
            }
        }

        private static Vector3 point(Transform parent, Vector3 local) {
            return parent != null ? parent.TransformPoint(local) : local;
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
