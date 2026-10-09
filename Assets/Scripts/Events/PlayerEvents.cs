namespace Brickcraft.Events
{
    public sealed class PlayerRoleChangedEvent
    {
        public string playerName;
        /// <summary>Null when the role is first known.</summary>
        public string oldRole;
        public string newRole;
        /// <summary>It's the player of this client.</summary>
        public bool isLocalPlayer;
    }

    public sealed class InventoryChangedEvent
    {
    }
}
