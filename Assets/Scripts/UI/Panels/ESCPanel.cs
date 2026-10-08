using UnityEngine;

namespace Brickcraft.UI {
    public class ESCPanel : MonoBehaviour
    {
        void OnEnable() {
            Game.unlockMouse();
            Player.Instance.freeze(Player.FreezeReason.ESCMenu);
        }

        void OnDisable() {
            Game.lockMouse();

            // the player is already gone when leaving the game
            if (Player.Instance != null) {
                Player.Instance.unFreeze(Player.FreezeReason.ESCMenu);
            }
        }

        // back to the main menu
        public void exit() {
            Network.BrickcraftNetworkManager.Instance.Leave();
        }
    }
}