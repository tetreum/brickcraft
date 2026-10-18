using Brickcraft.Events;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The parts lister's panel on the right (see PartsListerView), in the Canvas prefab: how to move around, the box's
    /// size, a − and + for each of its faces, the list's name (needed: it names its files) and the buttons that save the
    /// parts list or cancel. A folder by its heading opens the parts lists' folder, and two buttons at its bottom open
    /// where its files go next: BrickLink's wanted list upload (the .xml) and LDraw's model viewer (the .ldr). Two
    /// switches the player sets (kept for next time) decide what saving makes besides: its building instructions (see
    /// InstructionBook, shown once made in InstructionsPanel) and its creation's optimized model (see CreationModel).
    /// Its fixed texts are LocalizedText.
    /// </summary>
    public class PartsListerPanel : MonoBehaviour
    {
        public const string PanelName = "PartsListerPanel";
        /// <summary>Where a BrickLink wanted list (the .xml) is uploaded.</summary>
        public const string BrickLinkUpload = "https://www.bricklink.com/v2/wanted/upload.page";
        /// <summary>Where an LDraw model (the .ldr) is looked at in 3D.</summary>
        public const string LDrawViewer = "https://library.ldraw.org/model-viewer";

        public Text size;
        /// <summary>Each face's − and +, in PartsListerView.FaceNames' order.</summary>
        public Button[] shrinkButtons;
        public Button[] growButtons;
        public InputField listName;
        public Button saveButton;
        public Button cancelButton;
        public Button folderButton;
        public Button brickLinkButton;
        public Button ldrawButton;
        /// <summary>Whether saving makes the building instructions too.</summary>
        public Toggle instructionsToggle;
        /// <summary>Whether saving makes the creation's optimized model (an item of its own) too.</summary>
        public Toggle modelToggle;

        private const string InstructionsPref = "PartsLister.Instructions";
        private const string ModelPref = "PartsLister.Model";

        private void Awake() {
            for (int face = 0; face < growButtons.Length; face++) {
                int which = face;
                shrinkButtons[face].onClick.AddListener(() => grow(which, -1));
                growButtons[face].onClick.AddListener(() => grow(which, 1));
            }
            listName.characterLimit = Network.PartsLister.MaxNameLength;
            listName.onValueChanged.AddListener(value => refresh());
            saveButton.onClick.AddListener(() => {
                if (PartsListerView.Current != null) {
                    PartsListerView.Current.Save(listName.text, modelToggle.isOn);
                }
            });
            instructionsToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(InstructionsPref, 0) == 1);
            modelToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(ModelPref, 1) == 1);
            instructionsToggle.onValueChanged.AddListener(on => PlayerPrefs.SetInt(InstructionsPref, on ? 1 : 0));
            modelToggle.onValueChanged.AddListener(on => PlayerPrefs.SetInt(ModelPref, on ? 1 : 0));
            Network.PartsLister.ListSaved += onListSaved;
            cancelButton.onClick.AddListener(() => {
                if (PartsListerView.Current != null) {
                    PartsListerView.Current.Cancel();
                }
            });
            folderButton.onClick.AddListener(openFolder);
            brickLinkButton.onClick.AddListener(() => Application.OpenURL(BrickLinkUpload));
            ldrawButton.onClick.AddListener(() => Application.OpenURL(LDrawViewer));
        }

        private void OnDestroy() {
            Network.PartsLister.ListSaved -= onListSaved;
        }

        // a list saved: its instructions made, if wanted, then shown (how far it got meanwhile, while it's open)
        private void onListSaved(string model, string name) {
            if (!instructionsToggle.isOn || Menu.Instance == null) {
                return;
            }
            InstructionsPanel.ShowProgress(name, 0);
            Menu.Instance.StartCoroutine(InstructionBook.Make(model, name,
                progress => {
                    if (InstructionsPanel.IsOpen) {
                        InstructionsPanel.ShowProgress(name, progress);
                    }
                },
                folder => {
                    if (folder != null) {
                        InstructionsPanel.Open(name, folder);
                    }
                }));
        }

        public static void Show() {
            if (Menu.Instance == null) {
                return;
            }
            GameObject panel = Menu.Instance.showPanel(PanelName, false);
            panel.transform.SetAsLastSibling();
            PartsListerPanel parts = panel.GetComponent<PartsListerPanel>();
            parts.listName.text = "";
            parts.refresh();
        }

        public static void Hide() {
            if (Menu.Instance != null) {
                Menu.Instance.hidePanel(PanelName);
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
            refresh();
        }

        private void refresh() {
            PartsListerView view = PartsListerView.Current;
            if (view != null) {
                Vector3Int cells = view.Size;
                size.text = Loc.Get(Loc.UI, "partslister.size", cells.x, cells.z, cells.y);
            }
            saveButton.interactable = !string.IsNullOrWhiteSpace(listName.text);
        }

        // the parts lists' folder, in the system's file explorer (made if there's none yet)
        private static void openFolder() {
            string folder = Network.PartsLister.Folder;
            try {
                System.IO.Directory.CreateDirectory(folder);
            } catch (System.Exception e) {
                Debug.LogWarning("Can't make " + folder + ": " + e.Message);
            }
            Application.OpenURL(new System.Uri(folder).AbsoluteUri);
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
