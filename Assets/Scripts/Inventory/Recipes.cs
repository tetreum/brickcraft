using System.Collections.Generic;

namespace Brickcraft
{
    /// <summary>Every crafting recipe, gathered from the items' info.json by ItemDatabase.</summary>
    public static class Recipes
    {
        public static readonly List<Recipe> All = new List<Recipe>();

        /// <summary>The recipe with the id, null if there's none.</summary>
        public static Recipe Find(string id) {
            foreach (Recipe recipe in All) {
                if (recipe.id == id) {
                    return recipe;
                }
            }
            return null;
        }
    }
}
