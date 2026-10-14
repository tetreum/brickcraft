using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The parts lister's panel on the right (see PartsListerView): how to move around, the box's size, a − and +
    /// for each of its faces, the list's name (needed: it names its files) and the buttons that save the parts list
    /// or cancel. Made the first time it's needed.
    /// </summary>
    public class PartsListerPanel : MonoBehaviour
    {
        private static PartsListerPanel instance;
        // the faces' names, in PartsListerView.FaceNames' order
        private static readonly string[] FaceKeys = {
            "partslister.west", "partslister.east", "partslister.bottom", "partslister.top", "partslister.south", "partslister.north"
        };

        private Text size;
        private InputField listName;
        private Button save;
        private Text heading;
        private Text help;
        private readonly Text[] faceLabels = new Text[FaceKeys.Length];
        private Text saveLabel;
        private Text cancelLabel;

        public static void Show() {
            if (instance == null) {
                if (Menu.Instance == null) {
                    return;
                }
                instance = create(Menu.Instance.transform);
            }
            instance.transform.SetAsLastSibling();
            instance.gameObject.SetActive(true);
            instance.listName.text = "";
            instance.relabel();
            instance.refresh();
        }

        public static void Hide() {
            if (instance != null) {
                instance.gameObject.SetActive(false);
            }
        }

        private void OnEnable() {
            PartsListerView.BoxChanged += refresh;
            EventManager.LanguageChanged.Subscribe(onLanguageChanged);
        }

        private void OnDisable() {
            PartsListerView.BoxChanged -= refresh;
            EventManager.LanguageChanged.Unsubscribe(onLanguageChanged);
        }

        private void onLanguageChanged(LanguageChangedEvent e) {
            relabel();
            refresh();
        }

        // its texts in the player's language
        private void relabel() {
            heading.text = Loc.Get(Loc.UI, "partslister.parts_list");
            help.text = Loc.Get(Loc.UI, "partslister.help");
            for (int face = 0; face < faceLabels.Length; face++) {
                faceLabels[face].text = Loc.Get(Loc.UI, FaceKeys[face]);
            }
            ((Text)listName.placeholder).text = Loc.Get(Loc.UI, "partslister.name_of_the_list_required");
            saveLabel.text = Loc.Get(Loc.UI, "partslister.save_parts_list");
            cancelLabel.text = Loc.Get(Loc.UI, "partslister.cancel");
        }

        private void refresh() {
            PartsListerView view = PartsListerView.Current;
            if (view != null) {
                Vector3Int cells = view.Size;
                size.text = Loc.Get(Loc.UI, "partslister.size", cells.x, cells.z, cells.y);
            }
            save.interactable = !string.IsNullOrWhiteSpace(listName.text);
        }

        private static PartsListerPanel create(Transform root) {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject panel = new GameObject("PartsListerPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-24, 0);
            rect.sizeDelta = new Vector2(300, 0);
            panel.GetComponent<Image>().color = CodeUi.Background;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            PartsListerPanel parts = panel.AddComponent<PartsListerPanel>();
            parts.heading = CodeUi.Label(panel.transform, font, "", 20, FontStyle.Bold, Color.white);
            parts.help = CodeUi.Label(panel.transform, font, "", 14, FontStyle.Normal, CodeUi.HintColor);
            parts.size = CodeUi.Label(panel.transform, font, "", 15, FontStyle.Normal, CodeUi.TextColor);

            for (int face = 0; face < PartsListerView.FaceNames.Length; face++) {
                int which = face;
                GameObject row = new GameObject(PartsListerView.FaceNames[face], typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(panel.transform, false);
                row.GetComponent<LayoutElement>().preferredHeight = 30;
                HorizontalLayoutGroup line = row.GetComponent<HorizontalLayoutGroup>();
                line.spacing = 6;
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = false;
                line.childForceExpandHeight = true;
                Text label = parts.faceLabels[face] = CodeUi.Label(row.transform, font, "", 15, FontStyle.Normal, CodeUi.TextColor);
                label.alignment = TextAnchor.MiddleLeft;
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                CodeUi.Clickable(row.transform, font, "−", CodeUi.ButtonColor, 40, () => grow(which, -1));
                CodeUi.Clickable(row.transform, font, "+", CodeUi.ButtonColor, 40, () => grow(which, 1));
            }

            parts.listName = CodeUi.Field(panel.transform, font, "");
            parts.listName.characterLimit = Network.PartsLister.MaxNameLength;
            parts.listName.onValueChanged.AddListener(value => parts.refresh());
            GameObject saveButton = CodeUi.Clickable(panel.transform, font, "Save parts list", CodeUi.ActionColor, 0, () => {
                if (PartsListerView.Current != null) {
                    PartsListerView.Current.Save(parts.listName.text);
                }
            });
            saveButton.GetComponent<LayoutElement>().preferredHeight = 40;
            parts.save = saveButton.GetComponent<Button>();
            parts.saveLabel = saveButton.GetComponentInChildren<Text>();
            GameObject cancelButton = CodeUi.Clickable(panel.transform, font, "Cancel", CodeUi.ButtonColor, 0, () => {
                if (PartsListerView.Current != null) {
                    PartsListerView.Current.Cancel();
                }
            });
            cancelButton.GetComponent<LayoutElement>().preferredHeight = 36;
            parts.cancelLabel = cancelButton.GetComponentInChildren<Text>();
            panel.SetActive(false);
            return parts;
        }

        // the panel's buttons: Shift moves it 4 at a time, like the wheel
        private static void grow(int face, int steps) {
            if (PartsListerView.Current == null) {
                return;
            }
            bool fast = GameInput.ShiftHeld;
            PartsListerView.Current.Grow(face, fast ? steps * 4 : steps);
        }
    }
}
