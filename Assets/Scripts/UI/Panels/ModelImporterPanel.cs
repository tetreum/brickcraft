using System.Collections.Generic;
using System.IO;
using Brickcraft.Bricks.LDraw;
using Brickcraft.Network;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// The model importer's window (see Network.ModelImporter): the LDraw models in the imports folder, one to pick,
    /// read and then placed with its ghost (ModelImporterView); and while placing, a bar telling how. Made the first
    /// time they're needed.
    /// </summary>
    public class ModelImporterPanel : MonoBehaviour
    {
        private static ModelImporterPanel window;
        private static GameObject hint;

        private RectTransform list;
        private Text status;
        private int slot;
        private bool isOpen;

        /// <summary>Opens the window, for the model importer in that slot.</summary>
        public static void Open(int slot) {
            if (window == null) {
                if (Menu.Instance == null) {
                    return;
                }
                window = create(Menu.Instance.transform);
            }
            window.slot = slot;
            window.transform.SetAsLastSibling();
            window.gameObject.SetActive(true);
            window.status.text = "";
            window.refresh();
            if (!window.isOpen) {
                window.isOpen = true;
                DialogMode.Enter();
            }
        }

        public static bool IsOpen {
            get { return window != null && window.isOpen; }
        }

        public static void Close() {
            if (window == null || !window.isOpen) {
                return;
            }
            window.isOpen = false;
            window.gameObject.SetActive(false);
            DialogMode.Exit();
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.Escape)) {
                Close();
            }
        }

        private void OnDestroy() {
            if (isOpen) {
                isOpen = false;
                DialogMode.Exit();
            }
        }

        // the models in the folder, a button each
        private void refresh() {
            foreach (Transform row in list) {
                Destroy(row.gameObject);
            }
            List<string> models = ModelImporter.Models();
            if (models.Count == 0) {
                Text empty = CodeUi.Label(list, CodeUi.Font, "No LDraw models (.ldr, .mpd) in the folder yet: put some there and refresh.", 15, FontStyle.Italic, CodeUi.HintColor);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            foreach (string file in models) {
                string path = file;
                GameObject row = CodeUi.Clickable(list, CodeUi.Font, Path.GetFileName(file), CodeUi.ButtonColor, 0, () => pick(path));
                row.GetComponent<LayoutElement>().preferredHeight = 34;
                Text label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = FontStyle.Normal;
                ((RectTransform)label.transform).offsetMin = new Vector2(10, 0);
            }
        }

        // reads the model and, if any of it can be placed, hands it to its ghost
        private void pick(string path) {
            string name = Path.GetFileNameWithoutExtension(path);
            LDrawImport model;
            try {
                model = LDrawImport.Load(path);
            } catch (System.Exception e) {
                status.text = "Can't read " + Path.GetFileName(path) + ": " + e.Message;
                return;
            }
            if (model.bricks.Count == 0) {
                status.text = "None of " + Path.GetFileName(path) + "'s parts can be placed" + skipped(model);
                return;
            }
            if (model.bricks.Count > ModelImporter.MaxBricks) {
                status.text = Path.GetFileName(path) + " has " + model.bricks.Count + " bricks, the most is " + ModelImporter.MaxBricks;
                return;
            }
            Close();
            ModelImporter.Chat(name + ": " + model.bricks.Count + " bricks, " + model.Size.x + " x " + model.Size.z + " studs, "
                + model.Size.y + " plates high" + skipped(model));
            ModelImporterView.Open(model, name, slot);
        }

        // what wasn't taken, if anything
        private static string skipped(LDrawImport model) {
            if (model.Skipped == 0) {
                return "";
            }
            List<string> why = new List<string>();
            if (model.unknown > 0) {
                List<string> some = model.unknownParts.GetRange(0, Mathf.Min(5, model.unknownParts.Count));
                why.Add(model.unknown + " the game hasn't (" + string.Join(", ", some) + (model.unknownParts.Count > some.Count ? "..." : "") + ")");
            }
            if (model.attachments > 0) {
                why.Add(model.attachments + " doors or glass");
            }
            if (model.offGrid > 0) {
                why.Add(model.offGrid + " turned or moved off the studs");
            }
            if (model.overlapping > 0) {
                why.Add(model.overlapping + " where another part already is");
            }
            return " (" + model.Skipped + " parts left out: " + string.Join(", ", why) + ")";
        }

        /// <summary>The bar telling how to place the model, while its ghost is out.</summary>
        public static void ShowHint() {
            if (hint == null) {
                if (Menu.Instance == null) {
                    return;
                }
                createHint(Menu.Instance.transform);
            }
            hint.transform.SetAsLastSibling();
            hint.SetActive(true);
        }

        public static void HideHint() {
            if (hint != null) {
                hint.SetActive(false);
            }
        }

        private static ModelImporterPanel create(Transform root) {
            Font font = CodeUi.Font;
            GameObject panel = new GameObject("ModelImporterPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(520, 480);
            panel.GetComponent<Image>().color = CodeUi.Background;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ModelImporterPanel importer = panel.AddComponent<ModelImporterPanel>();
            CodeUi.Label(panel.transform, font, "MODEL IMPORTER", 20, FontStyle.Bold, Color.white);
            Text folder = CodeUi.Label(panel.transform, font, "LDraw models (.ldr, .mpd) in " + ModelImporter.Folder, 14, FontStyle.Normal, CodeUi.HintColor);
            folder.horizontalOverflow = HorizontalWrapMode.Wrap;

            // the list, scrolling
            GameObject scroll = new GameObject("Models", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D), typeof(LayoutElement));
            scroll.transform.SetParent(panel.transform, false);
            scroll.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f);
            scroll.GetComponent<LayoutElement>().flexibleHeight = 1;
            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scroll.transform, false);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup rows = content.GetComponent<VerticalLayoutGroup>();
            rows.padding = new RectOffset(6, 6, 6, 6);
            rows.spacing = 4;
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = (RectTransform)scroll.transform;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30;
            importer.list = contentRect;

            importer.status = CodeUi.Label(panel.transform, font, "", 14, FontStyle.Normal, new Color(1f, 0.6f, 0.5f));
            importer.status.horizontalOverflow = HorizontalWrapMode.Wrap;

            GameObject buttons = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(panel.transform, false);
            LayoutElement buttonsSize = buttons.GetComponent<LayoutElement>();
            buttonsSize.preferredHeight = 36;
            buttonsSize.flexibleHeight = 0; // the list takes the room left
            HorizontalLayoutGroup line = buttons.GetComponent<HorizontalLayoutGroup>();
            line.spacing = 8;
            line.childControlWidth = line.childControlHeight = true;
            line.childForceExpandWidth = true;
            line.childForceExpandHeight = true;
            CodeUi.Clickable(buttons.transform, font, "Open folder", CodeUi.ButtonColor, 0, () => {
                ModelImporter.Models(); // makes it if it isn't there
                Application.OpenURL("file:///" + ModelImporter.Folder.Replace('\\', '/'));
            });
            CodeUi.Clickable(buttons.transform, font, "Refresh", CodeUi.ButtonColor, 0, () => importer.refresh());
            CodeUi.Clickable(buttons.transform, font, "Close", CodeUi.ButtonColor, 0, Close);
            panel.SetActive(false);
            return importer;
        }

        private static void createHint(Transform root) {
            hint = new GameObject("ModelImporterHint", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            hint.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)hint.transform;
            // at the top, clear of the hotbar and health
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -60);
            hint.GetComponent<Image>().color = CodeUi.Background;
            HorizontalLayoutGroup layout = hint.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 8, 8);
            layout.childControlWidth = layout.childControlHeight = true;
            ContentSizeFitter fitter = hint.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            CodeUi.Label(hint.transform, CodeUi.Font, "Right click: place   R: turn   Shift: any stud   Esc: cancel", 15, FontStyle.Normal, CodeUi.TextColor);
            hint.SetActive(false);
        }
    }
}
