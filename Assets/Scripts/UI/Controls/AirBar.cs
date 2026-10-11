using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>The local player's air under water, above its health: shown only while it isn't full (see Network.PlayerNetwork.air).</summary>
    public class AirBar : MonoBehaviour
    {
        /// <summary>Stretched from the left, its right anchor follows the air.</summary>
        public RectTransform fill;
        public Text label;
        /// <summary>What's hidden while the air is full (the bar itself).</summary>
        public GameObject shown;

        private void Awake() {
            EventManager.LocalPlayerAirChanged.Subscribe(onAirChanged);
            shown.SetActive(false);
        }

        private void OnDestroy() {
            EventManager.LocalPlayerAirChanged.Unsubscribe(onAirChanged);
        }

        private void onAirChanged(LocalPlayerAirChangedEvent e) {
            bool full = e.air >= e.maxAir;
            shown.SetActive(!full);
            fill.anchorMax = new Vector2(e.maxAir > 0 ? Mathf.Clamp01(e.air / e.maxAir) : 0, 1);
            label.text = e.air > 0 ? "Air " + Mathf.CeilToInt(e.air) : "Drowning!";
        }
    }
}
