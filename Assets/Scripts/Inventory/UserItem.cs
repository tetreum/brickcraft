public class UserItem
{
    public int id;
    public int quantity;
    public int health;
    public int slot;

    public Item item {
        get {
            return Server.items[id];
        }
    }
}