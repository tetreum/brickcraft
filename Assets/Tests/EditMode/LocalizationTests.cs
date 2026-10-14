using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Brickcraft.Events;
using Brickcraft.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brickcraft.Tests
{
    /// <summary>The game's texts in the player's language (Loc): the language setting picks them.</summary>
    public class LocalizationTests
    {
        private const string LanguageKey = "settings." + GameSettings.LanguageSetting;
        private string saved;

        [SetUp]
        public void SetUp() {
            saved = PlayerPrefs.HasKey(LanguageKey) ? PlayerPrefs.GetString(LanguageKey) : null;
        }

        // the player's own choice back, or none
        [TearDown]
        public void TearDown() {
            GameSettings.Language = saved ?? GameSettings.SystemLanguage;
            if (saved == null) {
                PlayerPrefs.DeleteKey(LanguageKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void EveryLanguageOfTheSettingsHasALocale() {
            Assert.AreEqual(GameSettings.LanguageCodes.Length, GameSettings.LanguageNames.Length);
            foreach (string language in GameSettings.LanguageCodes) {
                Assert.IsTrue(Loc.HasLanguage(language), language);
            }
        }

        [Test]
        public void TheLanguageSettingPicksTheTexts() {
            string announced = null;
            System.Action<LanguageChangedEvent> changed = e => announced = e.language;
            EventManager.LanguageChanged.Subscribe(changed);
            try {
                GameSettings.Language = "en";
                Assert.AreEqual("en", Loc.Language);
                Assert.AreEqual("Language", Loc.Get(Loc.UI, "settings.language"));

                GameSettings.Language = "es";
                Assert.AreEqual("es", Loc.Language);
                Assert.AreEqual("es", announced, "told once its texts are loaded");
                Assert.AreEqual("Idioma", Loc.Get(Loc.UI, "settings.language"));
            } finally {
                EventManager.LanguageChanged.Unsubscribe(changed);
            }
        }

        // the keys the UI prefabs say their texts with: LocalizedTexts and menu buttons
        private static List<string> prefabKeys() {
            List<string> keys = new List<string>();
            List<string> prefabs = new List<string>() { "Assets/Prefabs/Canvas.prefab" };
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/UI" })) {
                prefabs.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            foreach (string path in prefabs) {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (LocalizedText text in prefab.GetComponentsInChildren<LocalizedText>(true)) {
                    keys.Add(text.key);
                }
                foreach (MenuButton button in prefab.GetComponentsInChildren<MenuButton>(true)) {
                    if (!string.IsNullOrEmpty(button.labelKey)) {
                        keys.Add(button.labelKey);
                    }
                }
            }
            return keys;
        }

        [Test]
        public void EveryUiTextIsInEnglishAndSpanish() {
            List<string> keys = prefabKeys();
            Assert.Greater(keys.Count, 50, "the menus are localized");
            foreach (string key in keys) {
                foreach (string language in GameSettings.LanguageCodes) {
                    Assert.IsTrue(Loc.Has(Loc.UI, key, language), key + " in " + language);
                }
            }
            GameSettings.Language = "es";
            Assert.AreEqual("Ajustes", Loc.Get(Loc.UI, "main.settings"));
        }

        private static readonly string[] Tables = { Loc.UI, Loc.Game, Loc.Controls, Loc.Items };

        // a key in a table: its area (the part before the dot) is one of a table's
        private static string tableOf(string key) {
            foreach (string table in Tables) {
                if (Loc.Has(table, key)) {
                    return table;
                }
            }
            return null;
        }

        [Test]
        public void EveryKeyInTheScriptsIsInEveryLanguage() {
            // the areas keys use, from the tables' keys
            HashSet<string> areas = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:SharedTableData", new[] { "Assets/Localization" })) {
                string text = File.ReadAllText(AssetDatabase.GUIDToAssetPath(guid));
                foreach (Match key in Regex.Matches(text, @"m_Key: ([a-z]+)\.")) {
                    areas.Add(key.Groups[1].Value);
                }
            }
            Assert.Greater(areas.Count, 5);

            // every literal like "area.words" of those areas, wherever it's used (Loc.Get, arrays of keys...)
            int found = 0;
            foreach (string script in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories)) {
                foreach (Match literal in Regex.Matches(File.ReadAllText(script), "\"([a-z]+)\\.([a-z0-9_]+)\"")) {
                    if (!areas.Contains(literal.Groups[1].Value)) {
                        continue;
                    }
                    string key = literal.Groups[1].Value + "." + literal.Groups[2].Value;
                    string table = tableOf(key);
                    Assert.IsNotNull(table, key + " (" + Path.GetFileName(script) + ") isn't in any table");
                    foreach (string language in GameSettings.LanguageCodes) {
                        Assert.IsTrue(Loc.Has(table, key, language), key + " in " + language);
                    }
                    found++;
                }
            }
            Assert.Greater(found, 100, "the scripts' texts are localized");
        }

        [Test]
        public void EveryControlHasItsNamesInEveryLanguage() {
            foreach (string language in GameSettings.LanguageCodes) {
                GameSettings.Language = language;
                foreach (GameInput.Binding binding in GameInput.Bindings) {
                    Assert.IsFalse(binding.Label.Contains(".") && !binding.Label.Contains(" "), binding.button + "'s label in " + language + ": " + binding.Label);
                    Assert.IsFalse(binding.CategoryName.StartsWith("category."), binding.button + "'s category in " + language);
                }
            }
            GameSettings.Language = "es";
            Assert.AreEqual("Clic izquierdo", GameInput.KeyName(KeyCode.Mouse0));
        }

        [Test]
        public void AMissingKeyShowsItself() {
            LogAssert.Expect(LogType.Warning, "The UI table has no no.such.key");
            Assert.AreEqual("no.such.key", Loc.Get(Loc.UI, "no.such.key"));
            Assert.IsFalse(Loc.Has(Loc.UI, "no.such.key"));
        }
    }
}
