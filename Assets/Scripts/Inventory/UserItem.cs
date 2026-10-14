namespace Brickcraft
{
    public class UserItem
    {
        public string id;
        /// <summary>Its colour, see BrickColorPalette.</summary>
        public int color;
        public int quantity;
        public int health;
        public int slot;

        public Item item {
            get {
                return Server.items[id];
            }
        }
    }
}