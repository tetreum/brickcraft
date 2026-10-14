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

        /// <summary>The chat line of an event, in the player's language (main thread).</summary>
        public static string Describe(ChatEventMessage message) {
            switch (message.type) {
                case ChatEventType.Joined:
                    return Loc.Get(Loc.Game, "event.joined", message.player);
                case ChatEventType.Left:
                    return Loc.Get(Loc.Game, "event.left", message.player);
                case ChatEventType.Kicked:
                    return string.IsNullOrEmpty(message.reason)
                        ? Loc.Get(Loc.Game, "event.kicked", message.player, message.by)
                        : Loc.Get(Loc.Game, "event.kicked_reason", message.player, message.by, message.reason);
                case ChatEventType.Banned:
                    return string.IsNullOrEmpty(message.reason)
                        ? Loc.Get(Loc.Game, "event.banned", message.player, message.by)
                        : Loc.Get(Loc.Game, "event.banned_reason", message.player, message.by, message.reason);
                case ChatEventType.Died:
                    return string.IsNullOrEmpty(message.by)
                        ? Loc.Get(Loc.Game, "event.died", message.player)
                        : Loc.Get(Loc.Game, "event.killed", message.player, message.by);
                case ChatEventType.Drowned:
                    return Loc.Get(Loc.Game, "event.drowned", message.player);
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
    }
}
