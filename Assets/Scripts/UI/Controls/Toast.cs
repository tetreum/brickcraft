using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A short message that shows for a few seconds and fades away (the Prefabs/UI/Toast prefab). Make a
    /// copy of the prefab for each message and call <see cref="Show"/>: it destroys itself once gone.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class Toast : MonoBehaviour
    {
        public Text text;
        public const float FadeSeconds = 0.4f;

        private CanvasGroup group;
        private float shownAt;
        private float duration;

        private void Awake() {
            group = GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
        }

        public void Show(string message, float seconds) {
            gameObject.SetActive(true); // copies of an inactive template wake up here
            text.text = message;
            duration = Mathf.Max(seconds, FadeSeconds * 2);
            shownAt = Time.unscaledTime;
            group.alpha = 0;
        }

        private void Update() {
            if (duration <= 0) {
                return; // not shown yet (a template)
            }
            float age = Time.unscaledTime - shownAt;
            group.alpha = fade(age, duration);
            if (age >= duration) {
                Destroy(gameObject);
            }
        }

        /// <summary>Alpha of something shown for duration seconds: fades in, stays, fades out.</summary>
        public static float fade(float age, float duration) {
            return Mathf.Clamp01(Mathf.Min(age / FadeSeconds, (duration - age) / FadeSeconds));
        }
    }
}
