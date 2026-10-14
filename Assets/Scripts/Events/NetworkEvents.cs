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
namespace Brickcraft.Events
{
    /// <summary>What a mod asked to show (see ModUiEvent).</summary>
    public enum ModUiKind
    {
        /// <summary>A window with a text, buttons and maybe a text field; answered with ModUiClient.Respond.</summary>
        Popup = 1,
        /// <summary>Closes the popup with that id, as if dismissed.</summary>
        ClosePopup = 2,
        /// <summary>A short message that fades away.</summary>
        Toast = 3,
        /// <summary>A big text in the middle of the screen, with an optional subtitle, that fades away.</summary>
        Title = 4,
        /// <summary>Shows or updates the HUD panel with that key: a title, lines and an optional progress bar.</summary>
        Hud = 5,
        /// <summary>Removes the HUD panel with that key.</summary>
        RemoveHud = 6,
        /// <summary>The client left the game: everything mods showed goes.</summary>
        Clear = 7,
    }

    /// <summary>A mod's script asked the server to show something to this player.</summary>
    public sealed class ModUiEvent
    {
        public ModUiKind kind;
        /// <summary>Popups: their id, to answer or close them.</summary>
        public int id;
        /// <summary>HUD panels: which one (mods can have several).</summary>
        public string key;
        public string title;
        public string text;
        public string[] buttons;
        public bool hasInput;
        public string inputText;
        public string inputPlaceholder;
        public string[] lines;
        /// <summary>HUD panels: 0 to 1, below 0 for no progress bar.</summary>
        public float progress;
        /// <summary>Toasts and titles: how long they're shown.</summary>
        public float seconds;
        /// <summary>Where on the screen (see ModUiPositions), null for the element's usual place.</summary>
        public string position;
        /// <summary>Popups and titles: moved this much from their position, in pixels of a 1920x1080 screen (y up).</summary>
        public float offsetX;
        public float offsetY;
    }

    /// <summary>The places of the screen mods can put things in, by name.</summary>
    public static class ModUiPositions
    {
        public static readonly string[] Names = { "center", "top", "bottom", "left", "right", "topLeft", "topRight", "bottomLeft", "bottomRight" };

        /// <summary>The point of the screen of a position, (0, 0) bottom left to (1, 1) top right; false if it isn't one.</summary>
        public static bool TryGetAnchor(string name, out UnityEngine.Vector2 anchor) {
            switch (name) {
                case "center": anchor = new UnityEngine.Vector2(0.5f, 0.5f); return true;
                case "top": anchor = new UnityEngine.Vector2(0.5f, 1); return true;
                case "bottom": anchor = new UnityEngine.Vector2(0.5f, 0); return true;
                case "left": anchor = new UnityEngine.Vector2(0, 0.5f); return true;
                case "right": anchor = new UnityEngine.Vector2(1, 0.5f); return true;
                case "topLeft": anchor = new UnityEngine.Vector2(0, 1); return true;
                case "topRight": anchor = new UnityEngine.Vector2(1, 1); return true;
                case "bottomLeft": anchor = new UnityEngine.Vector2(0, 0); return true;
                case "bottomRight": anchor = new UnityEngine.Vector2(1, 0); return true;
            }
            anchor = UnityEngine.Vector2.zero;
            return false;
        }
    }
}
