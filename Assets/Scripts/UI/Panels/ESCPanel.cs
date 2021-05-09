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
            Player.Instance.unFreeze(Player.FreezeReason.ESCMenu);
        }

        public void exit() {
            Application.Quit();
        }
    }
}