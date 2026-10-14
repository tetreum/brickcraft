using UnityEngine;

namespace Brickcraft.Combat
{
    /// <summary>
    /// How worn armor takes damage: each armor point (see Item.armor, added up over what's worn) takes 4% of a
    /// hit, up to 80%. A hit always takes at least 1 health.
    /// </summary>
    public static class Armor
    {
        public const float PerPoint = 0.04f;
        public const float MaxReduction = 0.8f;

        /// <summary>What's left of a hit of that much with those armor points.</summary>
        public static int Reduce(int amount, int points) {
            if (points <= 0 || amount <= 0) {
                return amount;
            }
            float reduction = Mathf.Min(MaxReduction, points * PerPoint);
            return Mathf.Max(1, Mathf.RoundToInt(amount * (1 - reduction)));
        }
    }
}
