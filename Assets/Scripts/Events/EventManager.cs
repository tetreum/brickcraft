using UnityEngine;

namespace Brickcraft.Events
{
    /// <summary>
    /// Every event of the game. Parts of the game talk through them instead of calling each other,
    /// so each part (the UI, the network, the world...) only depends on this assembly:
    ///
    ///   EventManager.PlayerRoleChanged.Subscribe(onRoleChanged);    // usually in OnEnable
    ///   EventManager.PlayerRoleChanged.Unsubscribe(onRoleChanged);  // and in OnDisable
    ///   EventManager.PlayerRoleChanged.Raise(new PlayerRoleChangedEvent() { ... });
    ///
    /// Events are raised on the main thread. To add one, add its data class to this namespace and
    /// a field here.
    /// </summary>
    public static class EventManager
    {
        /// <summary>A player's role changed (and when it's first known on this client).</summary>
        public static readonly GameEvent<PlayerRoleChangedEvent> PlayerRoleChanged = new GameEvent<PlayerRoleChangedEvent>();

        /// <summary>The local player's inventory changed.</summary>
        public static readonly GameEvent<InventoryChangedEvent> InventoryChanged = new GameEvent<InventoryChangedEvent>();

        /// <summary>This client connected to a server, before anything else arrives from it.</summary>
        public static readonly GameEvent<ClientStartedEvent> ClientStarted = new GameEvent<ClientStartedEvent>();

        /// <summary>A line for the chat arrived: a player's message or something that happened.</summary>
        public static readonly GameEvent<ChatLineReceivedEvent> ChatLineReceived = new GameEvent<ChatLineReceivedEvent>();

        // subscribers of a previous play session, when entering play mode doesn't reload the domain
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void clearAll() {
            PlayerRoleChanged.Clear();
            InventoryChanged.Clear();
            ClientStarted.Clear();
            ChatLineReceived.Clear();
        }
    }
}
