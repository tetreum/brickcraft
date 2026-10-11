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

    /// <summary>The local player used one of their pets: its orders, to show and change.</summary>
    public sealed class PetMenuRequestedEvent
    {
        public uint npcId;
        public string name;
        public bool following;
        /// <summary>What it attacks (Npcs.NpcBrain.PetAttack).</summary>
        public byte attacks;
    }

    public sealed class SelectedSlotChangedEvent
    {
        /// <summary>The inventory slot now selected, -1 for none.</summary>
        public int slot;
    }
}
