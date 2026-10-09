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
}
