namespace Brickcraft.Events
{
    public sealed class LocalPlayerHealthChangedEvent
    {
        public int health;
        public int maxHealth;
    }

    /// <summary>Seconds of air the local player has left under water, of maxAir (see Network.PlayerNetwork.air).</summary>
    public sealed class LocalPlayerAirChangedEvent
    {
        public float air;
        public float maxAir;
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
