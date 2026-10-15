using Brickcraft.Events;
using Brickcraft.Network;
using Brickcraft.Tutorials;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// In a tutorial's world (see Tutorials.Tutorial), at the top of the screen: the step to do, moving on to the next
    /// once the world shows it's done (bricks placed on the car, a seat taken; driving, checked as it goes), and at
    /// the end that it's all done. Hidden while a panel is open (not while driving). Made the first time the local
    /// player starts; hidden in other worlds.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        private static TutorialGuide guide;

        private Tutorial tutorial;
        private int step = -1;
        private Player player;
        private GameObject panel;
        private Text title, progress, text;

        /// <summary>Shows the guide if the world played is a tutorial's (from its first step), hides it otherwise.</summary>
        public static void Show(Transform root) {
            Tutorial now = WorldNetwork.Storage != null ? Tutorial.Find(WorldNetwork.Storage.Tutorial) : null;
            if (now == null) {
                if (guide != null) {
                    guide.gameObject.SetActive(false);
                }
                return;
            }
            if (guide == null) {
                guide = create(root);
            }
            guide.begin(now);
        }

        /// <summary>The step being done (its index), the steps' count once they're all done; -1 outside a tutorial.</summary>
        public static int Step {
            get { return guide != null && guide.gameObject.activeSelf ? guide.step : -1; }
        }

        // for as long as it's there (hidden too)
        private void Awake() {
            Vehicles.Vehicle.AnyChanged += check;
            EventManager.LanguageChanged.Subscribe(onLanguageChanged);
        }

        private void OnDestroy() {
            Vehicles.Vehicle.AnyChanged -= check;
            EventManager.LanguageChanged.Unsubscribe(onLanguageChanged);
            watch(null);
        }

        private void onLanguageChanged(LanguageChangedEvent e) {
            show();
        }

        private void begin(Tutorial now) {
            tutorial = now;
            step = 0;
            gameObject.SetActive(true);
            tutorial.steps[0].begin?.Invoke();
            show();
            check();
        }

        // the player it follows: frozen by a panel, it hides; seated, a step may be done
        private void watch(Player now) {
            if (player == now) {
                return;
            }
            if (player != null) {
                player.FrozenChanged -= check;
            }
            player = now;
            if (player != null) {
                player.FrozenChanged += check;
            }
        }

        private void Update() {
            if (Player.Instance == null) {
                gameObject.SetActive(false); // the game was left
                return;
            }
            if (step < tutorial.steps.Length && tutorial.steps[step].overTime) {
                check();
            }
        }

        // on to the next steps, as far as they're done
        private void check() {
            if (tutorial == null || !gameObject.activeSelf) {
                return;
            }
            watch(Player.Instance);
            int was = step;
            while (step < tutorial.steps.Length && tutorial.steps[step].isDone()) {
                step++;
                if (step < tutorial.steps.Length) {
                    tutorial.steps[step].begin?.Invoke();
                }
            }
            if (step != was) {
                show();
            }
            panel.SetActive(player == null || !player.isFrozen || player.freezeReason == Player.FreezeReason.Driving);
        }

        private void show() {
            if (tutorial == null) {
                return;
            }
            bool done = step >= tutorial.steps.Length;
            title.text = Loc.Get(Loc.UI, tutorial.titleKey);
            progress.text = done ? Loc.Get(Loc.UI, "tutorial.all_done") : Loc.Get(Loc.UI, "tutorial.step", step + 1, tutorial.steps.Length);
            text.text = Loc.Get(Loc.UI, done ? "tutorial.done" : tutorial.steps[step].textKey);
        }

        private static TutorialGuide create(Transform root) {
            Font font = CodeUi.Font;
            GameObject holder = new GameObject("TutorialGuide", typeof(RectTransform));
            holder.transform.SetParent(root, false);
            RectTransform holderRect = (RectTransform)holder.transform;
            holderRect.anchorMin = holderRect.anchorMax = holderRect.pivot = new Vector2(0.5f, 1);
            holderRect.anchoredPosition = new Vector2(0, -16);
            holderRect.sizeDelta = new Vector2(880, 0);

            // at the top, as tall as its text
            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(holder.transform, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = Vector2.zero;
            Color background = CodeUi.Background;
            background.a = 0.8f;
            Image image = panel.GetComponent<Image>();
            image.color = background;
            image.raycastTarget = false;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 14, 18);
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TutorialGuide made = holder.AddComponent<TutorialGuide>();
            made.panel = panel;
            made.title = CodeUi.Label(panel.transform, font, "", 21, FontStyle.Bold, CodeUi.HintColor);
            made.progress = CodeUi.Label(panel.transform, font, "", 18, FontStyle.Normal, CodeUi.HintColor);
            made.text = CodeUi.Label(panel.transform, font, "", 25, FontStyle.Normal, Color.white);
            made.text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return made;
        }
    }
}
