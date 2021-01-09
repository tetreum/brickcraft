namespace Brickcraft
{
    public class Recipe
    {
        public int itemId;
        public int quantity = 1;
        public Ingredient[] ingredients;

        public Item item {
            get {
                return Server.items[itemId];
            }
        }
    }
}
