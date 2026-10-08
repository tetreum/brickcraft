using Mirror;

namespace Brickcraft.Network
{
    public enum ChatEventType : byte
    {
        Joined = 1,
        Left = 2,
        Kicked = 3,
        Banned = 4,
    }

    /// <summary>
    /// Server to clients: something happened that the chat tells everybody about. The server sends
    /// what happened, each client words it (see ChatEvents.Describe).
    /// </summary>
    public struct ChatEventMessage : NetworkMessage
    {
        public ChatEventType type;
        /// <summary>The player it happened to.</summary>
        public string player;
        /// <summary>Who did it, like the admin who kicked the player, if anyone.</summary>
        public string by;
        public string reason;
    }

    public static class ChatEvents
    {
        /// <summary>Sends the event to every player in the game but <paramref name="except"/>.</summary>
        public static void Send(ChatEventMessage message, NetworkConnectionToClient except = null) {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values) {
                if (conn != except && conn.isReady) {
                    conn.Send(message);
                }
            }
        }

        /// <summary>The chat line of an event.</summary>
        public static string Describe(ChatEventMessage message) {
            switch (message.type) {
                case ChatEventType.Joined:
                    return message.player + " joined the game";
                case ChatEventType.Left:
                    return message.player + " left the game";
                case ChatEventType.Kicked:
                    return message.player + " was kicked by " + message.by + withReason(message.reason);
                case ChatEventType.Banned:
                    return message.player + " was banned by " + message.by + withReason(message.reason);
                default:
                    return null; // from a newer server, nothing we know how to say
            }
        }

        private static string withReason(string reason) {
            return string.IsNullOrEmpty(reason) ? "" : ": " + reason;
        }
    }
}
