using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>The local player's health above the bottom bar: a fill and "health / max". It flashes when hurt.</summary>
    public class HealthBar : MonoBehaviour
    {
        /// <summary>Stretched from the left, its right anchor follows the health.</summary>
        public RectTransform fill;
        public Image fillImage;
        public Text label;
        public Color color = new Color(0.85f, 0.26f, 0.23f);
        public Color hurtColor = Color.white;

        private const float FlashTime = 0.25f;
        private int shownHealth = -1;
        private float flashUntil;

        private void Awake() {
            EventManager.LocalPlayerHealthChanged.Subscribe(onHealthChanged);
        }

        private void OnDestroy() {
            EventManager.LocalPlayerHealthChanged.Unsubscribe(onHealthChanged);
        }

        private void onHealthChanged(LocalPlayerHealthChangedEvent e) {
            if (shownHealth >= 0 && e.health < shownHealth) {
                flashUntil = Time.unscaledTime + FlashTime;
            }
            shownHealth = e.health;
            fill.anchorMax = new Vector2(e.maxHealth > 0 ? (float)e.health / e.maxHealth : 0, 1);
            label.text = e.health + " / " + e.maxHealth;
        }

        private void Update() {
            fillImage.color = Time.unscaledTime < flashUntil ? hurtColor : color;
        }
    }
}
