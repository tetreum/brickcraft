namespace Brickcraft
{
    public class BrickModel
    {
        public enum Category {
            Brick = 1,
            Plate = 2
        };
        public int type;
        public Category category;

        // Dimensions on the brick grid (unrotated), see Bricks.BrickGrid.
        // Width runs along the prefab's local X axis and depth along its local Z axis.
        public int width;
        public int depth;
        public int heightInPlates;

        public float hardness = 4; //seconds with bare hands
    }
}
