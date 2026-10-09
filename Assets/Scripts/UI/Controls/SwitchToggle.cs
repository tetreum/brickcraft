using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>A Toggle drawn as a switch: a pill whose knob slides to the right and turns green when on.</summary>
    [RequireComponent(typeof(Toggle))]
    public class SwitchToggle : MonoBehaviour
    {
        public Image track;
        public RectTransform knob;
        public Color onColor = new Color(0.17f, 0.68f, 0.17f);
        public Color offColor = new Color(0.30f, 0.36f, 0.44f);
        /// <summary>Space between the knob and the track's ends.</summary>
        public float padding = 5f;

        private const float SlideSpeed = 12f;

        private Toggle toggle;
        private float position; // 0 off, 1 on
        private bool isSliding;

        public Toggle Toggle {
            get {
                if (toggle == null) {
                    toggle = GetComponent<Toggle>();
                }
                return toggle;
            }
        }

        private void Awake() {
            Toggle.onValueChanged.AddListener(on => isSliding = true);
        }

        private void OnEnable() {
            Snap();
        }

        /// <summary>Jumps to the current state, the slide is only for changes seen on screen.</summary>
        public void Snap() {
            position = Toggle.isOn ? 1f : 0f;
            isSliding = false;
            apply();
        }

        private void Update() {
            if (!isSliding) {
                return;
            }
            float target = Toggle.isOn ? 1f : 0f;
            position = Mathf.MoveTowards(position, target, SlideSpeed * Time.unscaledDeltaTime);
            apply();

            if (position == target) {
                isSliding = false;
            }
        }

        // the track's size is only known once laid out
        private void OnRectTransformDimensionsChange() {
            if (track != null && knob != null) {
                apply();
            }
        }

        private void apply() {
            RectTransform rect = (RectTransform)track.transform;
            float travel = rect.rect.width - knob.rect.width - padding * 2;

            knob.anchoredPosition = new Vector2(padding + travel * position, 0);
            track.color = Color.Lerp(offColor, onColor, position);
        }
    }
}
