using System;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>A row of buttons where one is selected, like a set of radio buttons.</summary>
    public class SegmentedControl : MonoBehaviour
    {
        public Button[] segments;
        public Color selectedColor = new Color(0.18f, 0.71f, 0.13f);
        public Color normalColor = new Color(1f, 1f, 1f, 0f);
        public Color selectedTextColor = Color.white;
        public Color normalTextColor = new Color(0.85f, 0.89f, 0.94f);

        /// <summary>The player picked another segment.</summary>
        public event Action<int> Changed;

        public int Selected { get; private set; } = -1;

        private void Awake() {
            for (int i = 0; i < segments.Length; i++) {
                int index = i;
                segments[i].onClick.AddListener(() => {
                    if (index != Selected) {
                        Select(index);
                        Changed?.Invoke(index);
                    }
                });
            }
        }

        /// <summary>Shows the segment as selected, without telling Changed.</summary>
        public void Select(int index) {
            Selected = index;

            for (int i = 0; i < segments.Length; i++) {
                bool isSelected = i == index;
                segments[i].GetComponent<Image>().color = isSelected ? selectedColor : normalColor;

                Text label = segments[i].GetComponentInChildren<Text>();
                if (label != null) {
                    label.color = isSelected ? selectedTextColor : normalTextColor;
                }
            }
        }
    }
}
