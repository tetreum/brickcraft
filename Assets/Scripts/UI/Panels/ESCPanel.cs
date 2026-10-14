using UnityEngine;

namespace Brickcraft.UI {
    /// <summary>The game menu (Escape). In singleplayer the game's time waits while it's open (see WorldTime.IsGameMenuOpen).</summary>
    public class ESCPanel : MonoBehaviour
    {
        void OnEnable() {
            World.WorldTime.IsGameMenuOpen = true;
            Game.unlockMouse();
            Player.Instance.freeze(Player.FreezeReason.ESCMenu);
        }

        void OnDisable() {
            World.WorldTime.IsGameMenuOpen = false;
            Game.lockMouse();

            // the player is already gone when leaving the game
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.ESCMenu);
            }
        }

        public void openSettings() {
            SettingsPanel.Open();
        }

        // back to the main menu
        public void exit() {
            Network.BrickcraftNetworkManager.Instance.Leave();
        }
    }
}