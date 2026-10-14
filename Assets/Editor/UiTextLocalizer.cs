using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Brickcraft.UI;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace Brickcraft.Editor
{
    /// <summary>
    /// Brickcraft > Localize UI texts: the static texts of the UI prefabs (Canvas and Prefabs/UI) get a key of the UI
    /// string table, with their English, and a LocalizedText that says it in the player's language. It can be run
    /// again: texts that have one keep their key, new ones get theirs.
    ///
    /// Static: no script refers to the Text (scripts fill in theirs: titles, counts, values) but an InputField's
    /// placeholder, it isn't in a part scripts copy and fill in (Excluded), and it isn't a placeholder value ("1000",
    /// "100%"...). Keys are [panel].[words of the English], so one sentence is one key within a panel.
    /// </summary>
    public static class UiTextLocalizer
    {
        public static readonly string[] Prefabs = { "Assets/Prefabs/Canvas.prefab" };
        public const string PrefabFolder = "Assets/Prefabs/UI";

        // parts filled in by code that finds them by name: WorldsPanel's rows, the sliders' values... (whole names)
        public static readonly string[] ExcludedNames = { "Value", "Name", "Details", "Delete" };
        // and whole parts: popups' buttons and input (Popup.Show), ControlsTab's list, WorldsPanel's mods
        public static readonly string[] ExcludedParts = { "Buttons/ButtonTemplate", "Input/Placeholder", "Controls/List", "ModTemplate" };

        // what's only there to see the layout in the editor
        private static readonly Regex PlaceholderValue = new Regex(@"^(\d+([.,]\d+)?\s*%?|Option [A-Z]|English|localhost|Mod name \d.*|Page \d+ / \d+|New Text|Text)$");

        public class Found
        {
            public string prefab;
            public string path;
            public string text;
            public string key;
            public string skipped; // why it's left alone, null if it's localized
        }

        [MenuItem("Brickcraft/Localize UI texts")]
        private static void menu() {
            List<Found> found = Run(false);
            int localized = found.FindAll(f => f.skipped == null).Count;
            Debug.Log("Localized " + localized + " UI texts (" + (found.Count - localized) + " left alone)");
        }

        /// <summary>Every text found, localized unless dryRun.</summary>
        public static List<Found> Run(bool dryRun) {
            List<string> prefabs = new List<string>(Prefabs);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })) {
                prefabs.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(Loc.UI);
            StringTable english = (StringTable)collection.GetTable(new LocaleIdentifier("en"));

            List<Found> found = new List<Found>();
            foreach (string prefab in prefabs) {
                GameObject root = PrefabUtility.LoadPrefabContents(prefab);
                bool changed = false;
                try {
                    HashSet<Text> referenced = referencedTexts(root);
                    foreach (Text text in root.GetComponentsInChildren<Text>(true)) {
                        Found f = new Found() { prefab = Path.GetFileNameWithoutExtension(prefab), path = pathOf(text.transform, root.transform), text = text.text };
                        found.Add(f);
                        LocalizedText existing = text.GetComponent<LocalizedText>();
                        if (existing != null && !string.IsNullOrEmpty(existing.key)) {
                            f.key = existing.key;
                            continue;
                        }
                        f.skipped = whySkipped(text, f.path, referenced);
                        if (f.skipped != null) {
                            continue;
                        }
                        f.key = keyFor(panelOf(text.transform, root.transform), text.text);
                        if (dryRun) {
                            continue;
                        }
                        if (collection.SharedData.GetEntry(f.key) == null) {
                            collection.SharedData.AddKey(f.key);
                        }
                        if (english.GetEntry(f.key) == null) {
                            english.AddEntry(f.key, text.text);
                        }
                        LocalizedText localized = existing != null ? existing : text.gameObject.AddComponent<LocalizedText>();
                        localized.table = Loc.UI;
                        localized.key = f.key;
                        changed = true;
                    }
                    // menu buttons say their label (their own text is theirs to fill in)
                    foreach (MenuButton button in root.GetComponentsInChildren<MenuButton>(true)) {
                        if (button.transform == root.transform) {
                            continue; // the prefab itself: its instances have their labels
                        }
                        Found f = new Found() { prefab = Path.GetFileNameWithoutExtension(prefab), path = pathOf(button.transform, root.transform) + " (MenuButton)", text = button.label };
                        found.Add(f);
                        if (!string.IsNullOrEmpty(button.labelKey)) {
                            f.key = button.labelKey;
                            continue;
                        }
                        f.key = keyFor(panelOf(button.transform, root.transform), button.label);
                        if (dryRun) {
                            continue;
                        }
                        if (collection.SharedData.GetEntry(f.key) == null) {
                            collection.SharedData.AddKey(f.key);
                        }
                        if (english.GetEntry(f.key) == null) {
                            english.AddEntry(f.key, button.label);
                        }
                        button.labelKey = f.key;
                        changed = true;
                    }
                    if (changed) {
                        PrefabUtility.SaveAsPrefabAsset(root, prefab);
                    }
                } finally {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            if (!dryRun) {
                EditorUtility.SetDirty(collection.SharedData);
                EditorUtility.SetDirty(english);
                AssetDatabase.SaveAssets();
            }
            return found;
        }

        private static string whySkipped(Text text, string path, HashSet<Text> referenced) {
            string value = text.text.Trim();
            if (value.Length == 0) {
                return "empty";
            }
            if (PlaceholderValue.IsMatch(value) || Regex.Matches(value, "[A-Za-z]").Count < 2) {
                return "placeholder value or symbol";
            }
            if (referenced.Contains(text)) {
                return "a script fills it in";
            }
            string[] names = path.Split('/');
            foreach (string name in ExcludedNames) {
                if (System.Array.IndexOf(names, name) >= 0) {
                    return "filled in by code (" + name + ")";
                }
            }
            foreach (string part in ExcludedParts) {
                if (("/" + path + "/").Contains("/" + part + "/")) {
                    return "filled in by code (" + part + ")";
                }
            }
            if (path.Contains("Popup/") && path.EndsWith("Input/Placeholder") || path == "Input/Placeholder") {
                return "filled in by code (popup input)";
            }
            return null;
        }

        // the Texts the prefab's scripts refer to, but InputFields' placeholders (shown as they are)
        private static HashSet<Text> referencedTexts(GameObject root) {
            HashSet<Text> texts = new HashSet<Text>();
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (behaviour == null || behaviour is Text || behaviour is LocalizedText) {
                    continue;
                }
                SerializedProperty property = new SerializedObject(behaviour).GetIterator();
                while (property.Next(true)) {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) {
                        continue;
                    }
                    if (behaviour is InputField && property.name == "m_Placeholder") {
                        continue;
                    }
                    Text text = property.objectReferenceValue as Text;
                    if (text != null) {
                        texts.Add(text);
                    }
                }
            }
            return texts;
        }

        private static string pathOf(Transform t, Transform root) {
            StringBuilder path = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root; p = p.parent) {
                path.Insert(0, p.name + "/");
            }
            return path.ToString();
        }

        // the panel it's in (its first ancestor named ...Panel or ...Tab), or the prefab
        private static string panelOf(Transform t, Transform root) {
            for (Transform p = t; p != null && p != root; p = p.parent) {
                if ((p.name.EndsWith("Panel") || p.name.EndsWith("Tab")) && p.name != "Panel" && p.name != "Tab") {
                    return slug(p.name.Replace("Panel", "").Replace("Tab", ""));
                }
            }
            return slug(root.name);
        }

        public static string keyFor(string panel, string english) {
            string words = slug(english);
            if (words.Length > 40) {
                words = words.Substring(0, 40).TrimEnd('_');
            }
            return panel + "." + words;
        }

        private static string slug(string value) {
            string s = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            return s.Length > 0 ? s : "text";
        }
    }
}
