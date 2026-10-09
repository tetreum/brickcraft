namespace Brickcraft
{
    /// <summary>A way to craft an item, from the "recipes" of its info.json (see ItemDatabase).</summary>
    public class Recipe
    {
        /// <summary>Its item's id and its number among the item's recipes, like "dirt_2x4#1".</summary>
        public string id;
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
