using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A Text that says its key of a string table (the UI's by default, see Loc) in the player's language: when it's
    /// shown and whenever the language changes. Put on the static texts of the UI prefabs by the editor's
    /// Brickcraft > Localize UI texts; texts that scripts fill in are localized by those scripts.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        public string table = Loc.UI;
        public string key;

        private Text text;

        private void Awake() {
            text = GetComponent<Text>();
        }

        private void OnEnable() {
            EventManager.LanguageChanged.Subscribe(onLanguageChanged);
            refresh();
        }

        private void OnDisable() {
            EventManager.LanguageChanged.Unsubscribe(onLanguageChanged);
        }

        private void onLanguageChanged(LanguageChangedEvent e) {
            refresh();
        }

        public void refresh() {
            if (!string.IsNullOrEmpty(key)) {
                text.text = Loc.Get(table, key);
            }
        }
    }
}
