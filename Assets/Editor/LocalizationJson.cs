using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace Brickcraft.Editor
{
    /// <summary>
    /// String tables (see Loc) to and from JSON files of { "key": "text" }, one per table and language, for writing or
    /// translating many texts at once: Brickcraft > Localization > Export / Import JSON use Localization/[table].[language].json
    /// in the project's folder. Importing adds the keys it hasn't and replaces the texts it has; texts with Smart String
    /// formats ({0:plural:...}, {name}) are made Smart.
    /// </summary>
    public static class LocalizationJson
    {
        public const string Folder = "Localization";
        public static readonly string[] Tables = { Loc.UI, Loc.Game, Loc.Controls, Loc.Items };

        // {0:plural:...}, {count}: Smart; {0} alone is string.Format's
        private static readonly Regex SmartFormat = new Regex(@"\{(\d+:|[A-Za-z_][A-Za-z0-9_.]*[}:])");

        [MenuItem("Brickcraft/Localization/Export JSON")]
        private static void exportAll() {
            foreach (string table in Tables) {
                foreach (string language in GameSettings.LanguageCodes) {
                    Export(table, language, Path.Combine(Folder, table + "." + language + ".json"));
                }
            }
            Debug.Log("Exported the string tables to " + Path.GetFullPath(Folder));
        }

        [MenuItem("Brickcraft/Localization/Import JSON")]
        private static void importAll() {
            foreach (string table in Tables) {
                foreach (string language in GameSettings.LanguageCodes) {
                    string path = Path.Combine(Folder, table + "." + language + ".json");
                    if (File.Exists(path)) {
                        Debug.Log("Imported " + Import(table, language, path) + " texts from " + path);
                    }
                }
            }
        }

        /// <summary>A table's texts in that language, by key.</summary>
        public static void Export(string table, string language, string path) {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(table);
            StringTable strings = (StringTable)collection.GetTable(new LocaleIdentifier(language));
            SortedDictionary<string, string> texts = new SortedDictionary<string, string>();
            foreach (SharedTableData.SharedTableEntry shared in collection.SharedData.Entries) {
                StringTableEntry entry = strings.GetEntry(shared.Id);
                texts[shared.Key] = entry != null ? entry.Value : "";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonConvert.SerializeObject(texts, Formatting.Indented) + "\n", new UTF8Encoding(false));
        }

        /// <summary>The texts of a file into a table's language. How many there were.</summary>
        public static int Import(string table, string language, string path) {
            Dictionary<string, string> texts = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
            return Set(table, language, texts);
        }

        /// <summary>Texts into a table's language, adding the keys it hasn't. How many there were.</summary>
        public static int Set(string table, string language, IDictionary<string, string> texts) {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(table);
            StringTable strings = (StringTable)collection.GetTable(new LocaleIdentifier(language));
            foreach (KeyValuePair<string, string> text in texts) {
                if (collection.SharedData.GetEntry(text.Key) == null) {
                    collection.SharedData.AddKey(text.Key);
                }
                StringTableEntry entry = strings.AddEntry(text.Key, text.Value);
                entry.IsSmart = SmartFormat.IsMatch(text.Value);
            }
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(strings);
            AssetDatabase.SaveAssets();
            return texts.Count;
        }
    }
}
