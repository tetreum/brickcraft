using Brickcraft.Events;
using Mirror;

namespace Brickcraft.Network
{
    /// <summary>Server to client: show (or change, or remove) something a mod's script asked for, see Scripting.LuaUi.</summary>
    public struct ModUiMessage : NetworkMessage
    {
        /// <summary>A ModUiKind.</summary>
        public byte kind;
        public int id;
        public string key;
        public string title;
        public string text;
        public string[] buttons;
        public bool hasInput;
        public string inputText;
        public string inputPlaceholder;
        public string[] lines;
        public float progress;
        public float seconds;
        public string position;
        public float offsetX;
        public float offsetY;
    }

    /// <summary>Client to server: how the player closed a mod's popup.</summary>
    public struct ModUiResponseMessage : NetworkMessage
    {
        public int id;
        /// <summary>The button clicked (0 is the first), -1 if it was dismissed.</summary>
        public int button;
        /// <summary>What was in its text field, if it had one.</summary>
        public string input;
    }

    /// <summary>
    /// The client side of mods' UI: messages from the server become EventManager.ModUi events (the UI
    /// shows them, see UI.ModUiPanel), and the UI answers popups through Respond.
    /// </summary>
    public static class ModUiClient
    {
        public static void Start() {
            NetworkClient.RegisterHandler<ModUiMessage>(onMessage);
        }

        public static void Stop() {
            EventManager.ModUi.Raise(new ModUiEvent() { kind = ModUiKind.Clear });
        }

        /// <summary>The player closed a popup: with a button (0 the first) or dismissing it (-1).</summary>
        public static void Respond(int popupId, int button, string input) {
            if (NetworkClient.isConnected) {
                NetworkClient.Send(new ModUiResponseMessage() { id = popupId, button = button, input = input });
            }
        }

        private static void onMessage(ModUiMessage message) {
            EventManager.ModUi.Raise(new ModUiEvent() {
                kind = (ModUiKind)message.kind,
                id = message.id,
                key = message.key,
                title = message.title,
                text = message.text,
                buttons = message.buttons,
                hasInput = message.hasInput,
                inputText = message.inputText,
                inputPlaceholder = message.inputPlaceholder,
                lines = message.lines,
                progress = message.progress,
                seconds = message.seconds,
                position = message.position,
                offsetX = message.offsetX,
                offsetY = message.offsetY,
            });
        }
    }
}
