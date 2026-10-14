using Mirror;

namespace Brickcraft.Network
{
    public enum ChatEventType : byte
    {
        Joined = 1,
        Left = 2,
        Kicked = 3,
        Banned = 4,
        /// <summary>A player died; by is who killed it, if anyone.</summary>
        Died = 5,
        /// <summary>A player drowned.</summary>
        Drowned = 6,
        /// <summary>A mod's own line (see Scripting.LuaApi's chat.event): reason is the whole text.</summary>
        Mod = 7,
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
                case ChatEventType.Died:
                    return string.IsNullOrEmpty(message.by) ? message.player + " died" : message.player + " was killed by " + message.by;
                case ChatEventType.Drowned:
                    return message.player + " drowned";
                case ChatEventType.Mod:
                    return string.IsNullOrEmpty(message.reason) ? null : message.reason;
                default:
                    return null; // from a newer server, nothing we know how to say
            }
        }

        /// <summary>One line of printable text, no longer than the chat allows ("" for null).</summary>
        public static string Clean(string text) {
            if (text == null) {
                return "";
            }
            System.Text.StringBuilder clean = new System.Text.StringBuilder(text.Length);

            foreach (char c in text) {
                clean.Append(char.IsControl(c) ? ' ' : c);
            }
            string result = clean.ToString().Trim();

            return result.Length > ChatMessage.MaxLength ? result.Substring(0, ChatMessage.MaxLength) : result;
        }

        private static string withReason(string reason) {
            return string.IsNullOrEmpty(reason) ? "" : ": " + reason;
        }
    }
}
