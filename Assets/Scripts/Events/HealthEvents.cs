namespace Brickcraft.Events
{
    public sealed class LocalPlayerHealthChangedEvent
    {
        public int health;
        public int maxHealth;
    }

    public sealed class LocalPlayerDiedEvent
    {
        /// <summary>Who killed the player, null for nobody.</summary>
        public string killedBy;
    }

    public sealed class LocalPlayerRespawnedEvent
    {
    }
}
