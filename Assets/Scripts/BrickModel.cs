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
    }
}
