using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A model's building instructions, a page at a time (see InstructionBook), in the Canvas prefab: its name, the page,
    /// Previous and Next, which page of how many, the folder its pages are in, and closing it (Esc too). While they're
    /// being made, how far it got. While it's open the game waits (see DialogMode).
    /// </summary>
    public class InstructionsPanel : MonoBehaviour
    {
        public const string PanelName = "InstructionsPanel";

        public Text title;
        public Text subtitle;
        public RawImage page;
        public Button closeButton;
        public Button previousButton;
        public Button nextButton;
        public Button folderButton;

        private readonly List<string> pages = new List<string>();
        private int index;
        private string folder;
        private Texture2D shown;
        private bool inDialog;

        private void Awake() {
            closeButton.onClick.AddListener(Close);
            previousButton.onClick.AddListener(() => go(index - 1));
            nextButton.onClick.AddListener(() => go(index + 1));
            folderButton.onClick.AddListener(() => {
                if (folder != null) {
                    Application.OpenURL(new System.Uri(folder).AbsoluteUri);
                }
            });
        }

        private void OnEnable() {
            if (!inDialog) {
                DialogMode.Enter();
                inDialog = true;
            }
        }

        private void OnDisable() {
            if (inDialog) {
                DialogMode.Exit();
                inDialog = false;
            }
        }

        private void OnDestroy() {
            if (shown != null) {
                Destroy(shown);
            }
        }

        private void Update() {
            if (!GameInput.IsTyping && GameInput.GetButtonDown(GameInput.Menu)) {
                Close();
            }
        }

        private static InstructionsPanel panel {
            get {
                if (Menu.Instance == null) {
                    return null;
                }
                GameObject shown = Menu.Instance.showPanel(PanelName, false);
                shown.transform.SetAsLastSibling();
                return shown.GetComponent<InstructionsPanel>();
            }
        }

        /// <summary>Shows a model's instructions (its name, and the folder of its pages) from its first page.</summary>
        public static void Open(string name, string folder) {
            InstructionsPanel instructions = panel;
            if (instructions == null) {
                return;
            }
            instructions.title.text = name;
            instructions.folder = folder;
            instructions.pages.Clear();
            instructions.pages.AddRange(InstructionBook.Pages(folder));
            instructions.go(0);
        }

        /// <summary>Shows how far the instructions of a model being made got (0 to 1).</summary>
        public static void ShowProgress(string name, float progress) {
            InstructionsPanel instructions = panel;
            if (instructions == null) {
                return;
            }
            instructions.title.text = name;
            instructions.folder = null;
            instructions.pages.Clear();
            instructions.show(null);
            instructions.subtitle.text = Loc.Get(Loc.UI, "instructions.making", Mathf.RoundToInt(progress * 100));
            instructions.previousButton.interactable = instructions.nextButton.interactable = instructions.folderButton.interactable = false;
        }

        /// <summary>Whether it's showing (or making) a model's instructions.</summary>
        public static bool IsOpen {
            get { return Menu.Instance != null && Menu.Instance.getPanel(PanelName).activeSelf; }
        }

        public void Close() {
            Menu.Instance.hidePanel(PanelName);
        }

        private void go(int to) {
            if (pages.Count == 0) {
                show(null);
                subtitle.text = Loc.Get(Loc.UI, "instructions.none");
                previousButton.interactable = nextButton.interactable = false;
                folderButton.interactable = folder != null;
                return;
            }
            index = Mathf.Clamp(to, 0, pages.Count - 1);
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try {
                image.LoadImage(File.ReadAllBytes(pages[index]));
            } catch (IOException e) {
                Debug.LogWarning("Can't read " + pages[index] + ": " + e.Message);
            }
            show(image);
            subtitle.text = Loc.Get(Loc.UI, "instructions.page", index + 1, pages.Count);
            previousButton.interactable = index > 0;
            nextButton.interactable = index < pages.Count - 1;
            folderButton.interactable = true;
        }

        // that page (none: nothing), the one before gone
        private void show(Texture2D image) {
            if (shown != null) {
                Destroy(shown);
            }
            shown = image;
            page.texture = image;
            page.enabled = image != null;
        }
    }
}
