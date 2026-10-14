using System.Text.RegularExpressions;

namespace Brickcraft
{
    /// <summary>
    /// How blocks and items are identified, so ones added by different people don't clash: lowercase
    /// letters, digits and "_", like "dirt_2x4", optionally prefixed by who made them, like "mymod:marble".
    /// </summary>
    public static class Slugs
    {
        private static readonly Regex Pattern = new Regex("^[a-z0-9_]+(:[a-z0-9_]+)?$");

        public const string Rules = "lowercase letters, digits and _, optionally prefixed like mymod:marble";

        public static bool IsValid(string slug) {
            return slug != null && Pattern.IsMatch(slug);
        }
    }
}
