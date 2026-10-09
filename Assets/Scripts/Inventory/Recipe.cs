namespace Brickcraft
{
    public class Recipe
    {
        public string itemId;
        public int quantity = 1;
        public Ingredient[] ingredients;

        public Item item {
            get {
                return Server.items[itemId];
            }
        }
    }
}
