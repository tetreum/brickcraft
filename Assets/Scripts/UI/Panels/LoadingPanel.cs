using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>The loading screen: a progress bar and what's being loaded, see WorldNetwork.GetLoadingProgress.</summary>
    public class LoadingPanel : MonoBehaviour
    {
        public RectTransform progressFill;
        public Text status;

        private const float Speed = 3f;

        private float shown;

        private void OnEnable() {
            shown = 0f;
            refresh(true);
        }

        private void Update() {
            refresh(false);
        }

        private void refresh(bool immediately) {
            float target = WorldNetwork.GetLoadingProgress(out string text);

            // eased, and never back: stages can report a bit less than the previous one ended at
            target = Mathf.Max(target, shown);
            shown = immediately ? target : Mathf.MoveTowards(shown, target, Mathf.Max(0.05f, target - shown) * Speed * Time.unscaledDeltaTime);

            progressFill.anchorMax = new Vector2(shown, 1f);
            if (status != null) {
                status.text = text;
            }
        }
    }
}
