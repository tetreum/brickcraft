namespace Brickcraft.Events
{
    public sealed class ClientStartedEvent
    {
    }

    public sealed class ChatLineReceivedEvent
    {
        /// <summary>Who wrote it, null for things that happened (a player joined, left...).</summary>
        public string sender;
        public string text;

        public bool IsEvent {
            get { return sender == null; }
        }
    }

    public sealed class DisconnectedEvent
    {
        /// <summary>Why, to show in the menu.</summary>
        public string message;
        /// <summary>The player is already in the menu (a join that failed), otherwise the menu is about to load.</summary>
        public bool isInMenu;
    }
}
