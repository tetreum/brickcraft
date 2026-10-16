using System.Collections.Generic;
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
        public const string PanelName = "TutorialsPanel";

        public Text title;
        public Text subtitle;
        public Button closeButton;
        /// <summary>Where the rows go, one a tutorial, made from the template (its Name, Details and Play).</summary>
        public RectTransform list;
        public GameObject rowTemplate;

        private readonly List<GameObject> rows = new List<GameObject>();

        private void Awake() {
            rowTemplate.SetActive(false);
            closeButton.onClick.AddListener(close);
        }

        private void OnEnable() {
            Game.unlockMouse();
            refresh();
        }

        private void Update() {
            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Menu)) {
                close();
            }
        }

        /// <summary>Opens over the main menu, which stays behind it.</summary>
        public static void Open() {
            Menu.Instance.showPanel(PanelName, false);
            Menu.Instance.getPanel(PanelName).transform.SetAsLastSibling();
        }

        public void close() {
            Menu.Instance.hidePanel(PanelName);
        }

        private void refresh() {
            title.text = Loc.Get(Loc.UI, "tutorials.title");
            subtitle.text = Loc.Get(Loc.UI, "tutorials.subtitle");
            // hidden right away, Destroy only happens at the end of the frame
            foreach (GameObject row in rows) {
                row.SetActive(false);
                Destroy(row);
            }
            rows.Clear();
            foreach (Tutorial tutorial in Tutorial.All) {
                if (tutorial.hidden) {
                    continue;
                }
                GameObject row = Instantiate(rowTemplate, list);
                row.name = tutorial.id;
                row.transform.Find("Name").GetComponent<Text>().text = Loc.Get(Loc.UI, tutorial.titleKey);
                row.transform.Find("Details").GetComponent<Text>().text = Loc.Get(Loc.UI, tutorial.descriptionKey);
                Tutorial started = tutorial;
                row.transform.Find("Play").GetComponent<Button>().onClick.AddListener(() => play(started));
                row.SetActive(true);
                rows.Add(row);
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
            Menu.Instance.hidePanel(PanelName);
            Menu.Instance.showPanel("LoadingPanel");
            BrickcraftNetworkManager.GetOrCreate().PlayWorld(saveName);
        }
    }
}
