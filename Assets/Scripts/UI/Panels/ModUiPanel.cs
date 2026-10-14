using System.Collections.Generic;
using Brickcraft.Events;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// Shows what mods' scripts ask for (EventManager.ModUi, see Scripting.LuaUi): popups (one at a time,
    /// the others wait), toasts, a title and HUD panels. It's always over the other panels, and isn't
    /// one of the Menu's: it stays while panels come and go.
    ///
    /// Each can go to a position of the screen (ModUiPositions): popups and the title move there (plus
    /// their offset), toasts and HUD panels stack in an area per position, made the first time it's used
    /// like the default ones (toasts at the top, HUD panels top right).
    /// </summary>
    public class ModUiPanel : MonoBehaviour
    {
        [Header("Popups")]
        /// <summary>Darkens the screen behind the popup, its parent.</summary>
        public GameObject popupLayer;
        public Popup popup;

        [Header("Toasts")]
        public RectTransform toasts;
        public Toast toastTemplate;

        [Header("Title")]
        public CanvasGroup titleGroup;
        public Text titleText;
        public Text subtitleText;

        [Header("HUD")]
        public RectTransform huds;
        public HudPanel hudTemplate;

        private readonly Queue<ModUiEvent> waitingPopups = new Queue<ModUiEvent>();
        private int shownPopup;
        private bool isShowingPopup;
        private readonly Dictionary<string, HudPanel> hudPanels = new Dictionary<string, HudPanel>();
        private float titleShownAt;
        private float titleDuration;

        public const string DefaultToastPosition = "top";
        public const string DefaultHudPosition = "topRight";
        /// <summary>Space between the edges of the screen and what's put at them.</summary>
        public const float Margin = 40;
        // the title's usual place: a bit above the middle
        private static readonly Vector2 DefaultTitleOffset = new Vector2(0, 170);

        private readonly Dictionary<string, RectTransform> toastAreas = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, RectTransform> hudAreas = new Dictionary<string, RectTransform>();

        private void Awake() {
            toastTemplate.gameObject.SetActive(false);
            hudTemplate.gameObject.SetActive(false);
            popupLayer.SetActive(false);
            titleGroup.alpha = 0;
            titleGroup.blocksRaycasts = false;
            // the default stacks are placed like the ones made for other positions
            place(toasts, DefaultToastPosition, Vector2.zero);
            place(huds, DefaultHudPosition, Vector2.zero);
            toastAreas[DefaultToastPosition] = toasts;
            hudAreas[DefaultHudPosition] = huds;
            EventManager.ModUi.Subscribe(onModUi);
        }

        private void OnDestroy() {
            EventManager.ModUi.Unsubscribe(onModUi);
        }

        private void Update() {
            if (titleGroup.alpha > 0 || titleDuration > 0) {
                float age = Time.unscaledTime - titleShownAt;
                titleGroup.alpha = Toast.fade(age, titleDuration);
                if (age >= titleDuration) {
                    titleDuration = 0;
                }
            }
        }

        private void onModUi(ModUiEvent e) {
            switch (e.kind) {
                case ModUiKind.Popup:
                    waitingPopups.Enqueue(e);
                    if (!isShowingPopup) {
                        showNextPopup();
                    }
                    break;
                case ModUiKind.ClosePopup:
                    closePopup(e.id);
                    break;
                case ModUiKind.Toast:
                    Toast toast = Instantiate(toastTemplate, areaFor(toastAreas, toasts, e.position ?? DefaultToastPosition));
                    toast.Show(e.text, e.seconds);
                    break;
                case ModUiKind.Title:
                    // without a position it's at its usual place, a bit above the middle
                    place((RectTransform)titleGroup.transform, e.position ?? "center",
                        e.position == null ? DefaultTitleOffset : new Vector2(e.offsetX, e.offsetY));
                    titleText.text = e.title;
                    subtitleText.text = e.text;
                    subtitleText.gameObject.SetActive(!string.IsNullOrEmpty(e.text));
                    titleShownAt = Time.unscaledTime;
                    titleDuration = e.seconds;
                    break;
                case ModUiKind.Hud:
                    HudPanel panel;
                    RectTransform area = areaFor(hudAreas, huds, e.position ?? DefaultHudPosition);
                    if (!hudPanels.TryGetValue(e.key, out panel)) {
                        panel = Instantiate(hudTemplate, area);
                        panel.name = e.key;
                        panel.gameObject.SetActive(true);
                        hudPanels[e.key] = panel;
                    } else if (panel.transform.parent != area) {
                        panel.transform.SetParent(area, false); // updated with another position
                    }
                    panel.Set(e.title, e.lines, e.progress);
                    break;
                case ModUiKind.RemoveHud:
                    if (hudPanels.TryGetValue(e.key, out panel)) {
                        Destroy(panel.gameObject);
                        hudPanels.Remove(e.key);
                    }
                    break;
                case ModUiKind.Clear:
                    clear();
                    break;
            }
        }

        // -------- positions --------

        // moves something to a position of the screen: its pivot on that point, Margin away from the edges, plus the offset
        private static void place(RectTransform rect, string position, Vector2 offset) {
            Vector2 anchor;
            if (!ModUiPositions.TryGetAnchor(position, out anchor)) {
                anchor = new Vector2(0.5f, 0.5f);
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            Vector2 inward = new Vector2(anchor.x == 0 ? Margin : anchor.x == 1 ? -Margin : 0, anchor.y == 0 ? Margin : anchor.y == 1 ? -Margin : 0);
            rect.anchoredPosition = inward + offset;
        }

        // the stack of toasts or HUD panels at a position, made like the default one the first time
        private RectTransform areaFor(Dictionary<string, RectTransform> areas, RectTransform defaultArea, string position) {
            RectTransform area;
            if (areas.TryGetValue(position, out area)) {
                return area;
            }
            GameObject created = new GameObject(defaultArea.name + " (" + position + ")", typeof(RectTransform));
            created.layer = defaultArea.gameObject.layer;
            area = (RectTransform)created.transform;
            area.SetParent(defaultArea.parent, false);
            area.SetSiblingIndex(defaultArea.GetSiblingIndex() + 1);
            area.sizeDelta = new Vector2(defaultArea.sizeDelta.x, 0);
            place(area, position, Vector2.zero);

            VerticalLayoutGroup source = defaultArea.GetComponent<VerticalLayoutGroup>();
            VerticalLayoutGroup layout = created.AddComponent<VerticalLayoutGroup>();
            layout.spacing = source.spacing;
            layout.childControlWidth = source.childControlWidth;
            layout.childControlHeight = source.childControlHeight;
            layout.childForceExpandWidth = source.childForceExpandWidth;
            layout.childForceExpandHeight = source.childForceExpandHeight;
            layout.childAlignment = alignmentOf(area.pivot);
            ContentSizeFitter sourceFitter = defaultArea.GetComponent<ContentSizeFitter>();
            ContentSizeFitter fitter = created.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = sourceFitter.horizontalFit;
            fitter.verticalFit = sourceFitter.verticalFit;

            areas[position] = area;
            return area;
        }

        // stacks line up with their side of the screen
        private static TextAnchor alignmentOf(Vector2 pivot) {
            int column = pivot.x == 0 ? 0 : pivot.x == 1 ? 2 : 1;
            int row = pivot.y == 1 ? 0 : pivot.y == 0 ? 2 : 1;
            return (TextAnchor)(row * 3 + column);
        }

        // -------- popups --------

        private void showNextPopup() {
            ModUiEvent next = waitingPopups.Dequeue();
            if (!isShowingPopup) {
                DialogMode.Enter();
            }
            isShowingPopup = true;
            shownPopup = next.id;
            place((RectTransform)popup.transform, next.position ?? "center", new Vector2(next.offsetX, next.offsetY));
            popupLayer.SetActive(true);
            popupLayer.transform.SetAsLastSibling();
            popup.Show(next.title, next.text, next.buttons, next.hasInput, next.inputText, next.inputPlaceholder,
                (button, input) => onPopupClosed(next.id, button, input));
        }

        private void onPopupClosed(int id, int button, string input) {
            // closed by the player: the server tells the script (closed by the script: it knows)
            if (id == shownPopup) {
                ModUiClient.Respond(id, button, input);
            }
            afterPopup();
        }

        // the script closed it: no answer
        private void closePopup(int id) {
            if (isShowingPopup && shownPopup == id) {
                shownPopup = 0;
                popup.Close(-1);
                return;
            }
            List<ModUiEvent> left = new List<ModUiEvent>(waitingPopups);
            left.RemoveAll(p => p.id == id);
            waitingPopups.Clear();
            foreach (ModUiEvent p in left) {
                waitingPopups.Enqueue(p);
            }
        }

        private void afterPopup() {
            if (waitingPopups.Count > 0) {
                showNextPopup();
                return;
            }
            isShowingPopup = false;
            shownPopup = 0;
            popupLayer.SetActive(false);
            DialogMode.Exit();
        }

        // the game is over: everything mods showed goes, popups unanswered
        private void clear() {
            waitingPopups.Clear();
            if (isShowingPopup) {
                shownPopup = 0;
                popup.Close(-1);
            }
            foreach (RectTransform area in toastAreas.Values) {
                foreach (Transform toast in area) {
                    if (toast != toastTemplate.transform) {
                        Destroy(toast.gameObject);
                    }
                }
            }
            foreach (HudPanel panel in hudPanels.Values) {
                Destroy(panel.gameObject);
            }
            hudPanels.Clear();
            titleDuration = 0;
            titleGroup.alpha = 0;
        }
    }
}
