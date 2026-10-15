using UnityEngine;

namespace Brickcraft
{
    /// <summary>A brick's shape, what items' "brickModel" names (see Bricks.BrickModels).</summary>
    public class BrickModel
    {
        public enum Category {
            Brick = 1,
            Plate = 2
        };
        /// <summary>Its id: a part number like "3009", "[mod]:[name]" for mods' models.</summary>
        public string id;
        public Category category;

        // Dimensions on the brick grid (unrotated), see Bricks.BrickGrid.
        // Width runs along the prefab's local X axis and depth along its local Z axis.
        public int width;
        public int depth;
        public int heightInPlates;

        /// <summary>
        /// The columns of its box it fills (x + z * width, unrotated; see Bricks.BrickFootprint), null if it fills them
        /// all: a corner brick leaves one free, another part can go there.
        /// </summary>
        public bool[] footprint;

        /// <summary>Whether it fills that column of its box (unrotated).</summary>
        public bool Fills(int x, int z) {
            return footprint == null || footprint[x + z * width];
        }

        /// <summary>What bricks of it are copies of: its pivot at the center of its bottom.</summary>
        public GameObject prefab;

        /// <summary>
        /// Not null for attachments (see Bricks.BrickAttachment): what slots they go in. Their bricks
        /// aren't on the grid, their size means nothing.
        /// </summary>
        public string attachesTo;

        public bool IsAttachment {
            get { return attachesTo != null; }
        }

        public float hardness = 4; //seconds with bare hands

        /// <summary>Where it connects besides its studs: technic axles, pins and holes... (see Bricks.Connector), none if nowhere.</summary>
        public Bricks.Connector[] connectors = new Bricks.Connector[0];

        /// <summary>LDraw parts' models: where the part's LDraw origin is in the model (see LDraw.LDrawWriter), null for others.</summary>
        public Vector3? ldrawOrigin;

        /// <summary>
        /// Where its LDraw part's origin is, in its own space but LDraw's units (LDU, Y still up): an LDraw part's
        /// exactly; others' the top of their middle (LDraw's bricks and plates have theirs there).
        /// </summary>
        public Vector3 LDrawOrigin {
            get { return ldrawOrigin.HasValue ? ldrawOrigin.Value / Bricks.LDraw.LDrawModelBuilder.Scale : new Vector3(0, heightInPlates * 8, 0); }
        }
    }
}
