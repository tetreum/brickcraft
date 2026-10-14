using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A small panel on the side of the screen while playing (the Prefabs/UI/HudPanel prefab): a title,
    /// lines of text (a scoreboard, a quest...) and an optional progress bar. Update it with <see cref="Set"/>.
    /// </summary>
    public class HudPanel : MonoBehaviour
    {
        public Text title;
        public Text lines;
        /// <summary>The bar, hidden without progress; its fill stretches from the left.</summary>
        public GameObject progressBar;
        public RectTransform progressFill;

        /// <summary>Shows a title, lines and a progress from 0 to 1 (below 0: no bar). Empty parts are hidden.</summary>
        public void Set(string titleText, IList<string> lineTexts, float progress) {
            title.text = titleText ?? "";
            title.gameObject.SetActive(!string.IsNullOrEmpty(titleText));

            string joined = lineTexts != null ? string.Join("\n", lineTexts) : "";
            lines.text = joined;
            lines.gameObject.SetActive(joined.Length > 0);

            progressBar.SetActive(progress >= 0);
            if (progress >= 0) {
                progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
            }
        }
    }
}
