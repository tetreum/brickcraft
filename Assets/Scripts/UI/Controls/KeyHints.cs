using System.Collections.Generic;
using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The keys that do something with what's in hand, at the bottom right while playing: R turns a brick; a part that
    /// connects the technic way (see Bricks.BrickJoints) also has T (the other way round) and G (another of its
    /// connectors). Each with the player's own key (see GameInput) and what it does, in their language; hidden while
    /// nothing in hand has any, or a panel is open. Made the first time the local player starts.
    /// </summary>
    public class KeyHints : MonoBehaviour
    {
        private static KeyHints hints;

        private RectTransform rows;
        // what they show now, to rebuild only when it changes
        private readonly List<string> shown = new List<string>();
        private Player player;

        /// <summary>Makes the hints (once), for the local player.</summary>
        public static void Show(Transform root) {
            if (hints == null) {
                hints = create(root);
            }
            hints.refresh();
        }

        // for as long as it's there (hidden too: it shows again when there's something to show)
        private void Awake() {
            EventManager.SelectedSlotChanged.Subscribe(onSelectedSlotChanged);
            EventManager.InventoryChanged.Subscribe(onInventoryChanged);
            EventManager.LanguageChanged.Subscribe(onLanguageChanged);
            EventManager.SettingChanged.Subscribe(onSettingChanged);
        }

        private void OnDestroy() {
            EventManager.SelectedSlotChanged.Unsubscribe(onSelectedSlotChanged);
            EventManager.InventoryChanged.Unsubscribe(onInventoryChanged);
            EventManager.LanguageChanged.Unsubscribe(onLanguageChanged);
            EventManager.SettingChanged.Unsubscribe(onSettingChanged);
            watch(null);
        }

        private void onSelectedSlotChanged(SelectedSlotChangedEvent e) { refresh(); }
        private void onInventoryChanged(InventoryChangedEvent e) { refresh(); }
        private void onLanguageChanged(LanguageChangedEvent e) { shown.Clear(); refresh(); }

        // a key remapped
        private void onSettingChanged(SettingChangedEvent e) {
            if (e.setting != null && e.setting.StartsWith("binding.")) {
                shown.Clear();
                refresh();
            }
        }

        // the player it follows (its freezing: panels open, chatting...)
        private void watch(Player now) {
            if (player == now) {
                return;
            }
            if (player != null) {
                player.FrozenChanged -= refresh;
            }
            player = now;
            if (player != null) {
                player.FrozenChanged += refresh;
            }
        }

        // the game left (the player went): nothing to show
        private void LateUpdate() {
            if (Player.Instance == null && shown.Count > 0) {
                refresh();
            }
        }

        private void refresh() {
            watch(Player.Instance);
            List<string> buttons = new List<string>();
            UserItem held = player != null && !player.isFrozen ? player.SelectedItem : null;
            BrickModel model = held != null && held.item.type == Item.Type.Brick ? held.item.brickModel : null;
            if (model != null && !model.IsAttachment) {
                buttons.Add(GameInput.Rotate);
                if (model.connectors.Length > 0) {
                    buttons.Add(GameInput.Flip);
                    buttons.Add(GameInput.NextConnector);
                }
            }
            bool visible = buttons.Count > 0;
            if (buttons.Count == shown.Count && buttons.TrueForAll(shown.Contains) && gameObject.activeSelf == visible) {
                return;
            }
            shown.Clear();
            shown.AddRange(buttons);
            foreach (Transform row in rows) {
                Destroy(row.gameObject);
            }
            foreach (string button in buttons) {
                addRow(button);
            }
            gameObject.SetActive(visible);
        }

        private void addRow(string button) {
            Font font = CodeUi.Font;
            GameObject row = new GameObject(button, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(rows, false);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            // the label takes what the row has spare, the keys line up on the right
            Text label = CodeUi.Label(row.transform, font, labelOf(button), 14, FontStyle.Normal, CodeUi.TextColor);
            label.alignment = TextAnchor.MiddleRight;
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // the key, as a key cap
            GameObject cap = new GameObject("Key", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            cap.transform.SetParent(row.transform, false);
            cap.GetComponent<Image>().color = CodeUi.ButtonColor;
            LayoutElement capSize = cap.GetComponent<LayoutElement>();
            capSize.minWidth = 26;
            capSize.flexibleWidth = 0;
            HorizontalLayoutGroup padding = cap.GetComponent<HorizontalLayoutGroup>();
            padding.padding = new RectOffset(7, 7, 3, 3);
            padding.childAlignment = TextAnchor.MiddleCenter;
            padding.childControlWidth = padding.childControlHeight = true;
            Text key = CodeUi.Label(cap.transform, font, keyOf(button), 14, FontStyle.Bold, Color.white);
            key.alignment = TextAnchor.MiddleCenter;
        }

        // the player's key for it (the first one)
        private static string keyOf(string button) {
            string[] paths = GameInput.GetPaths(button);
            return paths.Length > 0 ? GameInput.PathName(paths[0]) : "?";
        }

        private static string labelOf(string button) {
            foreach (GameInput.Binding binding in GameInput.Bindings) {
                if (binding.button == button) {
                    return binding.Label;
                }
            }
            return button;
        }

        private static KeyHints create(Transform root) {
            GameObject panel = new GameObject("KeyHints", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            // at the bottom right, clear of the hotbar
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-16, 16);
            Color background = CodeUi.Background;
            background.a = 0.6f;
            panel.GetComponent<Image>().color = background;
            panel.GetComponent<Image>().raycastTarget = false;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.LowerRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fit = panel.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            KeyHints hints = panel.AddComponent<KeyHints>();
            hints.rows = rect;
            return hints;
        }
    }
}
