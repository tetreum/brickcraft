using Brickcraft.Events;
using UnityEngine;

namespace Brickcraft.UI
{
    /// <summary>When the local player dies: a popup saying who killed it, closing it respawns (see PlayerNetwork.CmdRespawn).</summary>
    public class DeathScreen : MonoBehaviour
    {
        /// <summary>The Prefabs/UI/Popup prefab.</summary>
        public Popup popupPrefab;

        private Popup popup;

        private void Awake() {
            EventManager.LocalPlayerDied.Subscribe(onDied);
        }

        private void OnDestroy() {
            EventManager.LocalPlayerDied.Unsubscribe(onDied);
        }

        private void onDied(LocalPlayerDiedEvent e) {
            if (popup == null) {
                popup = Instantiate(popupPrefab, GetComponentInParent<Canvas>().rootCanvas.transform, false);
                popup.name = "DeathPopup";
            }
            popup.transform.SetAsLastSibling();
            DialogMode.Enter();
            popup.Show("You died", string.IsNullOrEmpty(e.killedBy) ? null : e.killedBy + " killed you.", new[] { "Respawn" }, false, null, null, (button, input) => {
                DialogMode.Exit();
                if (Player.Instance != null) {
                    Player.Instance.network.CmdRespawn();
                }
            });
        }
    }
}
