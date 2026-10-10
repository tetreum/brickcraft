using UnityEngine;

namespace Brickcraft.UI
{
    /// <summary>
    /// While a dialog (a popup) is open the player stands still, the mouse is free and keys aren't shortcuts.
    /// Dialogs can overlap (a mod's popup and a confirmation), the game goes back to playing when the last closes.
    /// </summary>
    public static class DialogMode
    {
        private static int open;

        public static bool IsOpen {
            get { return open > 0; }
        }

        public static void Enter() {
            open++;
            GameInput.SetTyping(true);
            Game.unlockMouse();
            if (Player.Instance != null) {
                Player.Instance.freeze(Player.FreezeReason.Dialog);
            }
        }

        public static void Exit() {
            if (open == 0) {
                return;
            }
            open--;
            if (open > 0) {
                return;
            }
            GameInput.SetTyping(false);
            if (Player.Instance == null) {
                return;
            }
            Player.Instance.unFreeze(Player.FreezeReason.Dialog);
            // back to playing, unless another panel (the inventory...) still needs the mouse
            foreach (GameObject panel in Menu.Instance.Menus) {
                if (panel.activeSelf && panel.GetComponent<PlayerPanel>() == null) {
                    return;
                }
            }
            Game.lockMouse();
        }
    }
}
