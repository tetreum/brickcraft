using System.Collections.Generic;
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

        [Test]
        public void AMissingKeyShowsItself() {
            LogAssert.Expect(LogType.Warning, "The UI table has no no.such.key");
            Assert.AreEqual("no.such.key", Loc.Get(Loc.UI, "no.such.key"));
            Assert.IsFalse(Loc.Has(Loc.UI, "no.such.key"));
        }
    }
}
