using Brickcraft.Network;
using Brickcraft.Tutorials;
using Brickcraft.World;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// What the main menu's Tutorials opens, over it: the game's tutorials (see Tutorials.Tutorial), each with what it
    /// teaches and a Start that plays it in a new world of its own (see SavedWorlds.CreateTutorial).
    /// </summary>
    public class TutorialsPanel : MonoBehaviour
    {
        private static TutorialsPanel window;

        private RectTransform list;
        private Text title, closeLabel;

        public static void Open() {
            if (window == null) {
                window = create(Menu.Instance.transform);
            }
            window.gameObject.SetActive(true);
            window.transform.SetAsLastSibling();
            window.refresh();
        }

        public static void Close() {
            if (window != null) {
                window.gameObject.SetActive(false);
            }
        }

        private void Update() {
            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Menu)) {
                Close();
            }
        }

        private void refresh() {
            Font font = CodeUi.Font;
            title.text = Loc.Get(Loc.UI, "tutorials.title");
            closeLabel.text = Loc.Get(Loc.UI, "tutorials.close");
            foreach (Transform row in list) {
                Destroy(row.gameObject);
            }
            foreach (Tutorial tutorial in Tutorial.All) {
                GameObject row = new GameObject(tutorial.id, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(list, false);
                row.GetComponent<Image>().color = CodeUi.ButtonColor;
                HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(20, 20, 16, 16);
                layout.spacing = 12;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;

                GameObject texts = new GameObject("Texts", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                texts.transform.SetParent(row.transform, false);
                texts.GetComponent<LayoutElement>().flexibleWidth = 1;
                VerticalLayoutGroup column = texts.GetComponent<VerticalLayoutGroup>();
                column.spacing = 3;
                column.childControlWidth = column.childControlHeight = true;
                column.childForceExpandWidth = true;
                column.childForceExpandHeight = false;
                CodeUi.Label(texts.transform, font, Loc.Get(Loc.UI, tutorial.titleKey), 26, FontStyle.Bold, Color.white);
                Text description = CodeUi.Label(texts.transform, font, Loc.Get(Loc.UI, tutorial.descriptionKey), 19, FontStyle.Normal, CodeUi.HintColor);
                description.horizontalOverflow = HorizontalWrapMode.Wrap;

                Tutorial started = tutorial;
                GameObject start = CodeUi.Clickable(row.transform, font, Loc.Get(Loc.UI, "tutorials.start"), CodeUi.ActionColor, 170, () => play(started));
                start.GetComponent<LayoutElement>().preferredHeight = 56;
                start.GetComponentInChildren<Text>().fontSize = 22;
            }
        }

        // a new world for it, played alone
        private static void play(Tutorial tutorial) {
            string saveName;
            try {
                saveName = SavedWorlds.CreateTutorial(tutorial.id, Loc.Get(Loc.UI, tutorial.titleKey), tutorial.terrain);
            } catch (System.Exception e) {
                Debug.LogError("Couldn't make the tutorial's world: " + e);
                return;
            }
            Close();
            Menu.Instance.showPanel("LoadingPanel");
            BrickcraftNetworkManager.GetOrCreate().PlayWorld(saveName);
        }

        private static TutorialsPanel create(Transform root) {
            Font font = CodeUi.Font;
            // over the whole menu, which shows dimmed behind it
            GameObject cover = new GameObject("TutorialsPanel", typeof(RectTransform), typeof(Image));
            cover.transform.SetParent(root, false);
            RectTransform coverRect = (RectTransform)cover.transform;
            coverRect.anchorMin = Vector2.zero;
            coverRect.anchorMax = Vector2.one;
            coverRect.offsetMin = coverRect.offsetMax = Vector2.zero;
            cover.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            GameObject box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            box.transform.SetParent(cover.transform, false);
            RectTransform boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(960, 0);
            box.GetComponent<Image>().color = CodeUi.Background;
            VerticalLayoutGroup layout = box.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 22, 28);
            layout.spacing = 18;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            box.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TutorialsPanel made = cover.AddComponent<TutorialsPanel>();
            made.title = CodeUi.Label(box.transform, font, "", 34, FontStyle.Bold, Color.white);

            GameObject list = new GameObject("List", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(box.transform, false);
            VerticalLayoutGroup rows = list.GetComponent<VerticalLayoutGroup>();
            rows.spacing = 12;
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            made.list = (RectTransform)list.transform;

            GameObject close = CodeUi.Clickable(box.transform, font, Loc.Get(Loc.UI, "tutorials.close"), CodeUi.ButtonColor, 0, Close);
            close.GetComponent<LayoutElement>().preferredHeight = 50;
            made.closeLabel = close.GetComponentInChildren<Text>();
            made.closeLabel.fontSize = 21;
            return made;
        }
    }
}
