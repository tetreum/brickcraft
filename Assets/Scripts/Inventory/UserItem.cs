namespace Brickcraft
{
    public class UserItem
    {
        public string id;
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