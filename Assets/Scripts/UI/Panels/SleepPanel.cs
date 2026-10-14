using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// While the local player sleeps (see Network.PlayerSleep), the screen dims and says how many players are
    /// asleep and how many it takes to skip the night, with a button to get up. It's in the player panel under
    /// the chat and the player list, which still work; the inventory doesn't open (see Menu).
    /// </summary>
    public class SleepPanel : MonoBehaviour
    {
        public GameObject overlay;
        public Text status;
        public Button stopButton;

        private void Awake() {
            overlay.SetActive(false);
            stopButton.onClick.AddListener(() => {
                if (Player.Instance != null) {
                    Player.Instance.network.CmdStopSleeping();
                }
            });
        }

        private void Update() {
            bool asleep = Player.Instance != null && Player.Instance.IsSleeping;
            if (asleep != overlay.activeSelf) {
                overlay.SetActive(asleep);
                if (!asleep && !GameInput.IsTyping) {
                    Game.lockMouse(); // back to looking around
                }
            }
            if (!asleep) {
                return;
            }
            // the button needs the mouse
            Game.unlockMouse();

            SleepStatusMessage sleep = WorldNetwork.SleepStatus;
            bool skipping = sleep.isSkipping;
            stopButton.gameObject.SetActive(!skipping);
            if (skipping) {
                status.text = Loc.Get(Loc.UI, "sleep.sleeping_through_the_night");
            } else {
                int needed = PlayerSleep.Needed(sleep.players);
                status.text = Loc.Get(Loc.UI, "sleep.status", sleep.sleeping, sleep.players, needed);
            }
        }
    }
}
