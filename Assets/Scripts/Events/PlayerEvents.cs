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

    public sealed class LocalPlayerStartedEvent
    {
    }

    public sealed class SelectedSlotChangedEvent
    {
        /// <summary>The inventory slot now selected, -1 for none.</summary>
        public int slot;
    }
}
