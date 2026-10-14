using System;
using System.Globalization;
using Brickcraft.Bricks;
using Mirror;

namespace Brickcraft.Network
{
    /// <summary>
    /// A text for players that each one reads in its own language: a key of the Game string table (see Loc) and its
    /// arguments. The server sends these instead of finished sentences, so a Spanish player on an English host reads
    /// Spanish. Arguments are kept as text, tagged when the client words them itself: numbers (for plurals), items
    /// and colours (their names in the reader's language) and other texts (on/off, roles).
    /// </summary>
    public struct GameText
    {
        // tags of arguments the client resolves: \u0001 can't be typed in the chat (see PlayerNetwork's cleaning)
        private const char Tag = '\u0001';
        private const string Number = "\u0001n:";
        private const string ItemTag = "\u0001i:";
        private const string ColorTag = "\u0001c:";
        private const string TextTag = "\u0001t:";

        public string key;
        public string[] args;

        public bool IsEmpty {
            get { return string.IsNullOrEmpty(key); }
        }

        /// <summary>
        /// A text with its arguments: numbers, Items, BrickColors and GameTexts (without arguments of their own) are
        /// worded by the reader; anything else is shown as it is (names, what players typed).
        /// </summary>
        public static GameText Of(string key, params object[] arguments) {
            string[] args = new string[arguments != null ? arguments.Length : 0];
            for (int i = 0; i < args.Length; i++) {
                args[i] = argument(arguments[i]);
            }
            return new GameText() { key = key, args = args };
        }

        private static string argument(object value) {
            switch (value) {
                case null:
                    return "";
                case Item item:
                    return ItemTag + item.id;
                case BrickColor color:
                    return ColorTag + color.id.ToString(CultureInfo.InvariantCulture);
                case GameText text:
                    return TextTag + text.key;
                case int _:
                case long _:
                case float _:
                case double _:
                    return Number + Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    // what players typed can't pass for a tag
                    return value.ToString().Replace(Tag, ' ');
            }
        }

        /// <summary>The text in the player's language (main thread).</summary>
        public string Say() {
            if (IsEmpty) {
                return "";
            }
            object[] words = new object[args != null ? args.Length : 0];
            for (int i = 0; i < words.Length; i++) {
                words[i] = word(args[i]);
            }
            return Loc.Get(Loc.Game, key, words);
        }

        public override string ToString() {
            return key + (args != null && args.Length > 0 ? "(" + string.Join(", ", args).Replace(Tag, '#') + ")" : "");
        }

        private static object word(string arg) {
            if (string.IsNullOrEmpty(arg) || arg[0] != Tag) {
                return arg ?? "";
            }
            string value = arg.Substring(Number.Length);
            if (arg.StartsWith(Number)) {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole)) {
                    return whole;
                }
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? (object)number : value;
            }
            if (arg.StartsWith(ItemTag)) {
                return Server.items.TryGetValue(value, out Item item) ? item.DisplayName : value;
            }
            if (arg.StartsWith(ColorTag)) {
                BrickColor color = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? BrickColorPalette.Get(id) : null;
                return color != null ? color.name : value;
            }
            if (arg.StartsWith(TextTag)) {
                return Loc.Get(Loc.Game, value);
            }
            return value;
        }
    }

    /// <summary>Server to client: a chat line it words in its language, from the server or as something that happened.</summary>
    public struct GameTextMessage : NetworkMessage
    {
        public GameText text;
        /// <summary>Shown like players joining (no sender), not as the server's message.</summary>
        public bool isEvent;
    }

    /// <summary>Sending GameTexts to players' chats (server).</summary>
    public static class GameTexts
    {
        /// <summary>A line from the server in that player's chat.</summary>
        public static void Send(NetworkConnectionToClient conn, string key, params object[] arguments) {
            Send(conn, GameText.Of(key, arguments));
        }

        public static void Send(NetworkConnectionToClient conn, GameText text) {
            if (conn != null) {
                conn.Send(new GameTextMessage() { text = text });
            }
        }

        /// <summary>A line from the server in everybody's chat.</summary>
        public static void SendToAll(string key, params object[] arguments) {
            NetworkServer.SendToReady(new GameTextMessage() { text = GameText.Of(key, arguments) });
        }

        /// <summary>Something that happened, in everybody's chat (like players joining).</summary>
        public static void EventToAll(string key, params object[] arguments) {
            NetworkServer.SendToReady(new GameTextMessage() { text = GameText.Of(key, arguments), isEvent = true });
        }
    }
}
